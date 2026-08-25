import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/FV2 Straight-Leg Jeans_fbx_thick/FV2 Straight-Leg Jeans_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/StraightLegJeans/StraightLegJeans_Rigged.fbx"


def to_blender(position):
    return Vector((position["x"], -position["z"], position["y"]))


def create_armature(bones, positions, name):
    children = {}
    for bone in bones:
        children.setdefault(bone["parent"], []).append(bone)

    armature = bpy.data.armatures.new(name)
    armature_object = bpy.data.objects.new(name, armature)
    bpy.context.collection.objects.link(armature_object)
    bpy.context.view_layer.objects.active = armature_object
    bpy.ops.object.mode_set(mode="EDIT")

    edit_bones = {}
    for bone in bones:
        edit_bone = armature.edit_bones.new(bone["name"])
        head = positions[bone["name"]]
        child_bones = children.get(bone["name"], [])
        if child_bones:
            tail = sum((positions[child["name"]] for child in child_bones), Vector()) / len(child_bones)
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


def smoothstep(edge0, edge1, value):
    factor = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return factor * factor * (3.0 - 2.0 * factor)


def hardware_vertices(mesh_object):
    indices = set()
    for polygon in mesh_object.data.polygons:
        if polygon.material_index > 0:
            indices.update(polygon.vertices)
    return indices


def assign_leg_weights(mesh_object):
    group_names = [
        "Hips",
        "LeftUpperLeg", "LeftLowerLeg",
        "RightUpperLeg", "RightLowerLeg",
    ]
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}
    rigid = hardware_vertices(mesh_object)

    for vertex in mesh_object.data.vertices:
        if vertex.index in rigid:
            weights = {"Hips": 1.0}
        else:
            side = "Left" if vertex.co.x >= 0.0 else "Right"
            height = vertex.co.z
            centre = 1.0 - smoothstep(0.025, 0.065, abs(vertex.co.x))

            hip_weight = smoothstep(0.80, 1.01, height)
            crotch_weight = centre * (1.0 - smoothstep(0.70, 0.93, height)) * smoothstep(0.58, 0.76, height) * 0.65
            hip_weight = max(hip_weight, crotch_weight)

            lower_leg_weight = 1.0 - smoothstep(0.39, 0.57, height)
            leg_weight = 1.0 - hip_weight
            weights = {
                "Hips": hip_weight,
                f"{side}UpperLeg": leg_weight * (1.0 - lower_leg_weight),
                f"{side}LowerLeg": leg_weight * lower_leg_weight,
            }

        for name, weight in weights.items():
            if weight > 0.0001:
                groups[name].add([vertex.index], weight, "REPLACE")

    print("RIGID_VERTICES", len(rigid))


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one jeans mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "StraightLegJeans_Rigged"
mesh_object.data.name = "StraightLegJeans_Rigged"

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)

positions = {bone["name"]: to_blender(bone) for bone in bones}
armature_object = create_armature(bones, positions, "MannequinRig")
assign_leg_weights(mesh_object)

mesh_object.parent = armature_object
modifier = mesh_object.modifiers.new(name="Armature", type="ARMATURE")
modifier.object = armature_object

# GarmentBinder consumes vertices directly and replaces FBX bind poses with the
# live avatar's bind poses, so store mesh data in Unity's Y-up coordinates.
mesh_object.data.transform(Matrix.Rotation(math.radians(-90.0), 4, "X"))

unweighted = sum(1 for vertex in mesh_object.data.vertices if not vertex.groups)
print("VERTICES", len(mesh_object.data.vertices))
print("GROUPS", len(mesh_object.vertex_groups), sorted(group.name for group in mesh_object.vertex_groups))
print("UNWEIGHTED", unweighted)
if unweighted:
    raise RuntimeError(f"Jeans skinning left {unweighted} vertices unweighted")

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
mesh_object.select_set(True)
armature_object.select_set(True)
bpy.context.view_layer.objects.active = armature_object
bpy.ops.export_scene.fbx(
    filepath=str(OUTPUT),
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=False,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    path_mode="STRIP",
    embed_textures=False,
)
print("EXPORTED", OUTPUT)
