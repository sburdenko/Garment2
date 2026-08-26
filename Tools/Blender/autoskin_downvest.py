import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/DownVest/Source/1_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/DownVest/DownVest_Rigged.fbx"

# CLO exported the vest together with its avatar. The garment's materials carry
# fabric/trim names; the avatar's are anonymous "MaterialXXXX" — that is the split.
GARMENT_MATERIAL_PREFIXES = ("Default Fabric", "Knit_Terry", "FABRIC", "Trim_Hardware")
# Measured against the mannequin (shoulder 1.394, head base 1.57): the garment's
# shoulder seam sits at 1.42, and its funnel collar ran to 1.70 — over the face.
# The puffer's collar ends near 1.55 and reads right, so this one gets the same.
GLOBAL_DROP = 0.03
COLLAR_BASE = 1.46
COLLAR_TOP = 1.54
SLEEVE_BLEND_INNER = 0.17
SLEEVE_BLEND_OUTER = 0.20


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


def strip_avatar(mesh_object):
    garment_slots = set()
    for index, slot in enumerate(mesh_object.material_slots):
        name = slot.material.name if slot.material else ""
        if name.startswith(GARMENT_MATERIAL_PREFIXES):
            garment_slots.add(index)
    if not garment_slots:
        raise RuntimeError("No garment materials recognised")

    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    doomed = [face for face in mesh.faces if face.material_index not in garment_slots]
    bmesh.ops.delete(mesh, geom=doomed, context="FACES")
    lonely = [vertex for vertex in mesh.verts if not vertex.link_faces]
    bmesh.ops.delete(mesh, geom=lonely, context="VERTS")
    mesh.to_mesh(mesh_object.data)
    mesh.free()

    # drop the now-empty avatar slots so submesh order is garment-only
    bpy.context.view_layer.objects.active = mesh_object
    for index in reversed(range(len(mesh_object.material_slots))):
        if index not in garment_slots:
            mesh_object.active_material_index = index
            bpy.ops.object.material_slot_remove()
    mesh_object.data.update()


def fit_collar(mesh_object):
    source_top = max(vertex.co.z for vertex in mesh_object.data.vertices)
    compression = (COLLAR_TOP - COLLAR_BASE) / (source_top - COLLAR_BASE)
    for vertex in mesh_object.data.vertices:
        if vertex.co.z > COLLAR_BASE:
            vertex.co.z = COLLAR_BASE + (vertex.co.z - COLLAR_BASE) * compression
    mesh_object.data.update()


def smoothstep(edge0, edge1, value):
    factor = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return factor * factor * (3.0 - 2.0 * factor)


def segment_distance(point, start, end):
    span = end - start
    length_squared = span.length_squared
    if length_squared < 1e-9:
        return (point - start).length
    t = max(0.0, min(1.0, (point - start).dot(span) / length_squared))
    return (point - (start + span * t)).length


def normalized_inverse_distances(point, segments):
    weighted = []
    for name, start, end in segments:
        distance = segment_distance(point, start, end)
        weighted.append((name, 1.0 / ((distance + 0.015) ** 2)))
    total = sum(weight for _, weight in weighted)
    return [(name, weight / total) for name, weight in weighted]


def sleeve_vertex_mask(mesh_object):
    vertex_count = len(mesh_object.data.vertices)
    parent = list(range(vertex_count))

    def root(index):
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    def union(first, second):
        first_root = root(first)
        second_root = root(second)
        if first_root != second_root:
            parent[second_root] = first_root

    for edge in mesh_object.data.edges:
        union(edge.vertices[0], edge.vertices[1])

    components = {}
    for index in range(vertex_count):
        components.setdefault(root(index), []).append(index)

    mask = [False] * vertex_count
    sleeve_components = 0
    for indices in components.values():
        minimum_x = min(mesh_object.data.vertices[index].co.x for index in indices)
        maximum_x = max(mesh_object.data.vertices[index].co.x for index in indices)
        is_sleeve = ((minimum_x > 0.10 and maximum_x > 0.30) or
                     (maximum_x < -0.10 and minimum_x < -0.30))
        if not is_sleeve:
            continue
        sleeve_components += 1
        for index in indices:
            mask[index] = True

    print("SLEEVE_COMPONENTS", sleeve_components)
    return mask


def assign_weights(mesh_object, positions):
    torso_segments = [
        ("Hips", positions["Hips"], positions["Spine"]),
        ("Spine", positions["Spine"], positions["Chest"]),
        ("Chest", positions["Chest"], positions["Neck"]),
        ("Neck", positions["Neck"], positions["Head"]),
    ]
    group_names = [name for name, _, _ in torso_segments] + ["LeftUpperArm", "RightUpperArm"]
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    mesh.verts.ensure_lookup_table()
    deform = mesh.verts.layers.deform.verify()
    sleeve_mask = sleeve_vertex_mask(mesh_object)

    for vertex in mesh.verts:
        arm_factor = (smoothstep(SLEEVE_BLEND_INNER, SLEEVE_BLEND_OUTER, abs(vertex.co.x))
                      if sleeve_mask[vertex.index] else 0.0)
        weights = {
            name: weight * (1.0 - arm_factor)
            for name, weight in normalized_inverse_distances(vertex.co, torso_segments)
        }
        side = "Left" if vertex.co.x >= 0.0 else "Right"
        weights[f"{side}UpperArm"] = arm_factor

        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _, weight in strongest)
        for name, weight in strongest:
            if weight > 0.0:
                vertex[deform][groups[name].index] = weight / total

    mesh.to_mesh(mesh_object.data)
    mesh.free()
    mesh_object.data.update()


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "DownVest_Rigged"
mesh_object.data.name = "DownVest_Rigged"

strip_avatar(mesh_object)
fit_collar(mesh_object)
for vertex in mesh_object.data.vertices:
    vertex.co.z -= GLOBAL_DROP
mesh_object.data.update()
zs = [v.co.z for v in mesh_object.data.vertices]
print("GARMENT_Z", round(min(zs), 3), "..", round(max(zs), 3))
print("MATERIALS", [slot.material.name if slot.material else "?" for slot in mesh_object.material_slots])

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)

positions = {bone["name"]: to_blender(bone) for bone in bones}
armature_object = create_armature(bones, positions, "MannequinRig")
assign_weights(mesh_object, positions)

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
    raise RuntimeError(f"Vest skinning left {unweighted} vertices unweighted")

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
