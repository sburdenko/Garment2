import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
REGULAR_SOURCE = PROJECT_ROOT / "Assets/t-shirt/shirt_skinned.fbx"
OVERSIZED_SOURCE = PROJECT_ROOT / "Assets/girl-tshirt-oversized/model/outfit_yuna.obj"
REGULAR_OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/TShirtRegular/TShirtRegular_Rigged.fbx"
OVERSIZED_OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/TShirtOversized/TShirtOversized_Rigged.fbx"


def to_blender(position):
    return Vector((position["x"], -position["z"], position["y"]))


def smoothstep(edge0, edge1, value):
    factor = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return factor * factor * (3.0 - 2.0 * factor)


def segment_distance(point, start, end):
    segment = end - start
    length_squared = segment.length_squared
    if length_squared <= 1e-8:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(segment) / length_squared))
    return (point - (start + segment * factor)).length


def normalized_inverse_distances(point, segments):
    weighted = []
    for name, start, end in segments:
        distance = segment_distance(point, start, end)
        weighted.append((name, 1.0 / ((distance + 0.015) ** 2)))
    total = sum(weight for _, weight in weighted)
    return [(name, weight / total) for name, weight in weighted]


def create_armature(bones, positions):
    children = {}
    for bone in bones:
        children.setdefault(bone["parent"], []).append(bone)

    armature = bpy.data.armatures.new("MannequinRig")
    armature_object = bpy.data.objects.new("MannequinRig", armature)
    bpy.context.collection.objects.link(armature_object)
    bpy.context.view_layer.objects.active = armature_object
    bpy.ops.object.mode_set(mode="EDIT")

    edit_bones = {}
    for bone in bones:
        edit_bone = armature.edit_bones.new(bone["name"])
        head = positions[bone["name"]]
        children_of_bone = children.get(bone["name"], [])
        if children_of_bone:
            tail = sum((positions[child["name"]] for child in children_of_bone), Vector()) / len(children_of_bone)
        else:
            parent = positions.get(bone["parent"])
            direction = (head - parent).normalized() if parent is not None else Vector((0, 0, 1))
            tail = head + direction * 0.06
        if (tail - head).length < 0.01:
            tail = head + Vector((0, 0, 0.05))
        edit_bone.head = head
        edit_bone.tail = tail
        edit_bones[bone["name"]] = edit_bone

    for bone in bones:
        if bone["parent"] in edit_bones:
            edit_bones[bone["name"]].parent = edit_bones[bone["parent"]]

    bpy.ops.object.mode_set(mode="OBJECT")
    return armature_object


def prepare_regular():
    bpy.ops.import_scene.fbx(filepath=str(REGULAR_SOURCE))
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(mesh_objects) != 1:
        raise RuntimeError(f"Expected one regular T-shirt mesh, found {len(mesh_objects)}")

    mesh_object = mesh_objects[0]
    mesh_object.data.transform(mesh_object.matrix_world)
    mesh_object.matrix_world.identity()
    mesh_object.parent = None
    for modifier in list(mesh_object.modifiers):
        mesh_object.modifiers.remove(modifier)
    mesh_object.vertex_groups.clear()
    for obj in list(bpy.context.scene.objects):
        if obj != mesh_object:
            bpy.data.objects.remove(obj, do_unlink=True)
    return mesh_object


def prepare_oversized():
    bpy.ops.wm.obj_import(filepath=str(OVERSIZED_SOURCE))
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(mesh_objects) != 1:
        raise RuntimeError(f"Expected one oversized outfit mesh, found {len(mesh_objects)}")

    mesh_object = mesh_objects[0]
    mesh_object.data.transform(mesh_object.matrix_world)
    mesh_object.data.transform(Matrix.Scale(0.001, 4))
    mesh_object.matrix_world.identity()

    top_material_indices = (1, 2)
    top_materials = [mesh_object.data.materials[index] for index in top_material_indices]
    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    unwanted = [face for face in mesh.faces if face.material_index not in top_material_indices]
    bmesh.ops.delete(mesh, geom=unwanted, context="FACES")
    mesh.to_mesh(mesh_object.data)
    mesh.free()

    material_indices = [top_material_indices.index(polygon.material_index) for polygon in mesh_object.data.polygons]
    mesh_object.data.materials.clear()
    for material in top_materials:
        mesh_object.data.materials.append(material)
    for polygon, material_index in zip(mesh_object.data.polygons, material_indices):
        polygon.material_index = material_index
    mesh_object.data.update()
    return mesh_object


def assign_weights(mesh_object, positions, lateral_start, lateral_end, sleeve_bottom, sleeve_full):
    torso_segments = [
        ("Hips", positions["Hips"], positions["Spine"]),
        ("Spine", positions["Spine"], positions["Chest"]),
        ("Chest", positions["Chest"], positions["Neck"]),
        ("Neck", positions["Neck"], positions["Head"]),
    ]
    arm_segments = {
        "Left": [
            ("LeftShoulder", positions["LeftShoulder"], positions["LeftUpperArm"]),
            ("LeftUpperArm", positions["LeftUpperArm"], positions["LeftLowerArm"]),
            ("LeftLowerArm", positions["LeftLowerArm"], positions["LeftHand"]),
        ],
        "Right": [
            ("RightShoulder", positions["RightShoulder"], positions["RightUpperArm"]),
            ("RightUpperArm", positions["RightUpperArm"], positions["RightLowerArm"]),
            ("RightLowerArm", positions["RightLowerArm"], positions["RightHand"]),
        ],
    }
    group_names = [name for name, _, _ in torso_segments]
    group_names.extend(name for side in ("Left", "Right") for name, _, _ in arm_segments[side])
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    for vertex in mesh_object.data.vertices:
        point = vertex.co
        side = "Left" if point.x >= 0.0 else "Right"
        lateral = smoothstep(lateral_start, lateral_end, abs(point.x))
        vertical = smoothstep(sleeve_bottom, sleeve_full, point.z)
        arm_factor = lateral * vertical

        weights = {}
        for name, weight in normalized_inverse_distances(point, torso_segments):
            weights[name] = weight * (1.0 - arm_factor)
        for name, weight in normalized_inverse_distances(point, arm_segments[side]):
            weights[name] = weight * arm_factor

        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _, weight in strongest)
        for name, weight in strongest:
            if weight > 0.0001:
                groups[name].add([vertex.index], weight / total, "REPLACE")


def export_tshirt(mesh_object, bones, output, name, weight_settings):
    positions = {bone["name"]: to_blender(bone) for bone in bones}
    armature_object = create_armature(bones, positions)
    assign_weights(mesh_object, positions, **weight_settings)

    mesh_object.name = name
    mesh_object.data.name = name
    mesh_object.parent = armature_object
    modifier = mesh_object.modifiers.new(name="Armature", type="ARMATURE")
    modifier.object = armature_object

    mesh_object.data.transform(Matrix.Rotation(math.radians(-90.0), 4, "X"))
    unweighted = sum(1 for vertex in mesh_object.data.vertices if not vertex.groups)
    print(name, "VERTICES", len(mesh_object.data.vertices), "UNWEIGHTED", unweighted)
    if unweighted:
        raise RuntimeError(f"{name} has {unweighted} unweighted vertices")

    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    mesh_object.select_set(True)
    armature_object.select_set(True)
    bpy.context.view_layer.objects.active = armature_object
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        path_mode="STRIP",
        embed_textures=False,
    )
    print("EXPORTED", output)


with SKELETON.open(encoding="utf-8") as stream:
    skeleton = json.load(stream)

bpy.ops.wm.read_factory_settings(use_empty=True)
export_tshirt(
    prepare_regular(), skeleton, REGULAR_OUTPUT, "TShirtRegular_Rigged",
    dict(lateral_start=0.20, lateral_end=0.34, sleeve_bottom=1.10, sleeve_full=1.28),
)

bpy.ops.wm.read_factory_settings(use_empty=True)
export_tshirt(
    prepare_oversized(), skeleton, OVERSIZED_OUTPUT, "TShirtOversized_Rigged",
    dict(lateral_start=0.18, lateral_end=0.30, sleeve_bottom=1.12, sleeve_full=1.28),
)
