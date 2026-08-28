from pathlib import Path
import math
import sys

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SOURCE_FBX = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Original/RedFit_Top_Original.fbx"
PREVIEW_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_top_fbx_xbot.png"
SOURCE_COLLECTION = "RedFit Sapphire Original"
FITTED_COLLECTION = "RedFit Sapphire Top XBot"
SAVE_RESULT = "--save" in sys.argv


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector(tuple(min(corner[axis] for corner in corners) for axis in range(3)))
    maximum = Vector(tuple(max(corner[axis] for corner in corners) for axis in range(3)))
    return minimum, maximum


def apply_transform(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def fit_sleeve(top, pivot, target, side):
    side_vertices = [
        vertex for vertex in top.data.vertices
        if side * vertex.co.x > side * pivot.x and vertex.co.z > 0.82
    ]
    extreme = max(side * vertex.co.x for vertex in side_vertices)
    cuff = [vertex.co for vertex in side_vertices if side * vertex.co.x > extreme - 0.035]
    current = sum(cuff, Vector()) / len(cuff)

    current_vector = Vector((current.x - pivot.x, current.z - pivot.z))
    target_vector = Vector((target.x - pivot.x, target.z - pivot.z))
    current_angle = math.atan2(current_vector.y, current_vector.x)
    target_angle = math.atan2(target_vector.y, target_vector.x)
    angle = target_angle - current_angle
    length_scale = target_vector.length / current_vector.length
    cosine = math.cos(angle)
    sine = math.sin(angle)

    for vertex in side_vertices:
        outward = (side * vertex.co.x - side * pivot.x) / (extreme - side * pivot.x)
        influence = smoothstep(outward / 0.45)
        relative_x = vertex.co.x - pivot.x
        relative_z = vertex.co.z - pivot.z
        fitted_x = pivot.x + length_scale * (cosine * relative_x - sine * relative_z)
        fitted_z = pivot.z + length_scale * (sine * relative_x + cosine * relative_z)
        vertex.co.x += (fitted_x - vertex.co.x) * influence
        vertex.co.y -= 0.06 * influence
        vertex.co.z += (fitted_z - vertex.co.z + 0.08) * influence


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))

source_collection = bpy.data.collections[SOURCE_COLLECTION]
source_body = bpy.data.objects["body"]
source_minimum, source_maximum = world_bounds(source_body)

old_fitted_collection = bpy.data.collections.get(FITTED_COLLECTION)
if old_fitted_collection:
    for obj in list(old_fitted_collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(old_fitted_collection)

objects_before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(SOURCE_FBX))
imported_objects = [obj for obj in bpy.data.objects if obj not in objects_before]
top = next(obj for obj in imported_objects if obj.type == "MESH")
top.name = "RedFit_Top_XBot_Fitted"

fitted_collection = bpy.data.collections.new(FITTED_COLLECTION)
bpy.context.scene.collection.children.link(fitted_collection)
for collection in list(top.users_collection):
    collection.objects.unlink(top)
fitted_collection.objects.link(top)

target_body = bpy.data.objects["Beta_Surface"]
target_minimum, target_maximum = world_bounds(target_body)
scale = (target_maximum.z - target_minimum.z) / (source_maximum.z - source_minimum.z)
source_center_after_scale = (source_minimum + source_maximum) * 0.5 * scale
target_center = (target_minimum + target_maximum) * 0.5

top.scale *= scale
top.location += target_center - source_center_after_scale
apply_transform(top)

for vertex in top.data.vertices:
    vertex.co.y *= 1.18

rig = bpy.data.objects["XBotRig"]
left_shoulder = rig.matrix_world @ rig.data.bones["mixamorig:LeftArm"].head_local
right_shoulder = rig.matrix_world @ rig.data.bones["mixamorig:RightArm"].head_local
left_wrist = rig.matrix_world @ rig.data.bones["mixamorig:LeftHand"].head_local
right_wrist = rig.matrix_world @ rig.data.bones["mixamorig:RightHand"].head_local
fit_sleeve(top, left_shoulder, left_wrist, 1.0)
fit_sleeve(top, right_shoulder, right_wrist, -1.0)
top.data.update()

for obj in list(source_collection.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.data.collections.remove(source_collection)

for name in ("PufferJacket_ArmsOnly", "Skirt_XBot_Rigged", "PufferPants_XBot_Rigged"):
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_viewport = True
        obj.hide_render = True

top.hide_viewport = False
top.hide_render = False

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x = 1100
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
camera = bpy.data.objects["Camera"]
camera.data.type = "ORTHO"
camera.data.ortho_scale = 2.15
camera.location = (0.0, -4.0, 1.02)
camera.rotation_euler = (Vector((0.0, 0.0, 1.02)) - camera.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = camera
scene.render.filepath = str(PREVIEW_PATH)
bpy.ops.render.render(write_still=True)

if SAVE_RESULT:
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

minimum, maximum = world_bounds(top)
print(
    "REDFIT_TOP_FBX",
    f"polys={len(top.data.polygons)}",
    f"bounds={tuple(round(value, 3) for value in minimum)}..{tuple(round(value, 3) for value in maximum)}",
    f"old_glb_removed={bpy.data.collections.get(SOURCE_COLLECTION) is None}",
    f"saved={SAVE_RESULT}",
)
