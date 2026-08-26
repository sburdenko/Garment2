import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/Skirt/Source/skirt 1_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/Skirt/Skirt_Rigged.fbx"

# The skirt hangs from the waistband and flares; unlike trousers it has fabric
# crossing the centreline, so the left/right leg split must blend softly there
# or the front panel tears in half the moment the legs part.
BAND_TOP = 1.05      # fully hips above this height
HEM_TOP = 0.62       # leg influence fades in from here down
HEM_LEG_WEIGHT = 0.55  # at the hem: this much leg, the rest stays with the pelvis
CENTRE_BLEND = 0.06  # metres over which left and right leg influence crossfades


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


def assign_skirt_weights(mesh_object):
    group_names = ["Hips", "LeftUpperLeg", "RightUpperLeg"]
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    for vertex in mesh_object.data.vertices:
        band = smoothstep(HEM_TOP, BAND_TOP, vertex.co.z)
        hip_weight = 1.0 - HEM_LEG_WEIGHT * (1.0 - band)
        leg_weight = 1.0 - hip_weight

        left_factor = smoothstep(-CENTRE_BLEND, CENTRE_BLEND, vertex.co.x)
        weights = {
            "Hips": hip_weight,
            "LeftUpperLeg": leg_weight * left_factor,
            "RightUpperLeg": leg_weight * (1.0 - left_factor),
        }
        for name, weight in weights.items():
            if weight > 0.0001:
                groups[name].add([vertex.index], weight, "REPLACE")


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one skirt mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "Skirt_Rigged"
mesh_object.data.name = "Skirt_Rigged"

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)

positions = {bone["name"]: to_blender(bone) for bone in bones}
armature_object = create_armature(bones, positions, "MannequinRig")
assign_skirt_weights(mesh_object)

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
    raise RuntimeError(f"Skirt skinning left {unweighted} vertices unweighted")

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
