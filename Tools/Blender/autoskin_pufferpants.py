import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/PufferPants/Source/Freebie Pants_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/PufferPants/PufferPants_Rigged.fbx"

# The export is 717k vertices / 1.4M polys — several times the rest of the
# wardrobe combined, and binding cost scales with vertex count. Puffer fabric
# has no detail that survives past this budget anyway.
DECIMATE_RATIO = 0.15
# Oversized cut: legs slimmed towards the centre, but the seat and waistband
# keep their width — slimming reached the seat and cinched the waist. The band
# itself opens slightly: it was sewn for a narrower avatar than the mannequin
# (halfwidth 0.163 against a pelvis of ~0.175).
SLIM = 0.72
WAIST_WIDEN = 1.08
SLIM_FADE_BOTTOM = 0.60
SLIM_FADE_TOP = 0.95
# Authored waistband tops out at 1.163 — ribcage height. Squashed vertically
# about the hem so the waist lands at 1.05 and the hem stays on the floor.
WAIST_TARGET = 1.05
# Hem raised off the floor to the ankle: pooling length has nowhere to go when a
# leg folds (yoga tree pose) and juts out past the knee instead of hanging.
HEM_Z = 0.11


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


def component_sides(mesh_object):
    """Which leg each vertex belongs to, decided per CONNECTED PANEL.

    Measured: not one face bridges the centreline — the tubes are separate
    shells. A per-vertex x test tears them anyway, because an inner wall wobbles
    across x=0 and its vertices get snapped to opposite legs (the bar between
    the shins). A crossfade melts both inner walls to the middle instead (the
    glued-together look). A panel lives on one leg; assign it whole."""
    parent = list(range(len(mesh_object.data.vertices)))

    def find(index):
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    for edge in mesh_object.data.edges:
        first, second = find(edge.vertices[0]), find(edge.vertices[1])
        if first != second:
            parent[second] = first

    totals = {}
    for index, vertex in enumerate(mesh_object.data.vertices):
        key = find(index)
        sum_x, count = totals.get(key, (0.0, 0))
        totals[key] = (sum_x + vertex.co.x, count + 1)

    return {index: ("Left" if totals[find(index)][0] >= 0.0 else "Right")
            for index in range(len(mesh_object.data.vertices))}


def assign_leg_weights(mesh_object):
    group_names = [
        "Hips",
        "LeftUpperLeg", "LeftLowerLeg",
        "RightUpperLeg", "RightLowerLeg",
    ]
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}
    sides = component_sides(mesh_object)

    for vertex in mesh_object.data.vertices:
        height = vertex.co.z
        side = sides[vertex.index]
        hip_weight = smoothstep(0.75, 1.03, height)
        lower_leg = 1.0 - smoothstep(0.38, 0.50, height)
        leg_weight = 1.0 - hip_weight
        weights = {
            "Hips": hip_weight,
            f"{side}UpperLeg": leg_weight * (1.0 - lower_leg),
            f"{side}LowerLeg": leg_weight * lower_leg,
        }
        for name, weight in weights.items():
            if weight > 0.0001:
                groups[name].add([vertex.index], weight, "REPLACE")


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one pants mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "PufferPants_Rigged"
mesh_object.data.name = "PufferPants_Rigged"

decimate = mesh_object.modifiers.new(name="Decimate", type="DECIMATE")
decimate.ratio = DECIMATE_RATIO
bpy.context.view_layer.objects.active = mesh_object
bpy.ops.object.modifier_apply(modifier=decimate.name)
print("DECIMATED_TO", len(mesh_object.data.vertices))

source_waist = max(v.co.z for v in mesh_object.data.vertices)
source_hem = min(v.co.z for v in mesh_object.data.vertices)
squash = (WAIST_TARGET - HEM_Z) / (source_waist - source_hem)
for vertex in mesh_object.data.vertices:
    vertex.co.z = HEM_Z + (vertex.co.z - source_hem) * squash

for vertex in mesh_object.data.vertices:
    shrink = SLIM + (WAIST_WIDEN - SLIM) * smoothstep(SLIM_FADE_BOTTOM, SLIM_FADE_TOP, vertex.co.z)
    vertex.co.x *= shrink
    vertex.co.y *= shrink
mesh_object.data.update()

# Linear blend skinning cuts the corner on a folded knee — the chord of the bend
# passes inside the kneecap and the knee pokes through the fabric. Thickening
# the tube around knee height keeps the chord outside the knee.
KNEE_INFLATE = 0.03
LEG_AXIS_X = 0.10
for vertex in mesh_object.data.vertices:
    taper = smoothstep(0.25, 0.38, vertex.co.z) * (1.0 - smoothstep(0.52, 0.65, vertex.co.z))
    if taper <= 0.0:
        continue
    axis_x = LEG_AXIS_X if vertex.co.x >= 0.0 else -LEG_AXIS_X
    dx = vertex.co.x - axis_x
    dy = vertex.co.y
    length = (dx * dx + dy * dy) ** 0.5
    if length < 1e-5:
        continue
    push = KNEE_INFLATE * taper / length
    vertex.co.x += dx * push
    vertex.co.y += dy * push
mesh_object.data.update()

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
    raise RuntimeError(f"Pants skinning left {unweighted} vertices unweighted")

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
