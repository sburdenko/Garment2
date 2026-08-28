from pathlib import Path
import math
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
SOURCE_BLEND = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SOURCE_GLB = Path("/Users/oleksandrburdenko/Downloads/ClothSamples/redfitsapphire.glb")
PREVIEW = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_xbot_fit.png"
SAVE_RESULT = "--save" in sys.argv


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector(tuple(min(corner[axis] for corner in corners) for axis in range(3)))
    maximum = Vector(tuple(max(corner[axis] for corner in corners) for axis in range(3)))
    return minimum, maximum


def copy_faces(source, name, keep_material):
    copied = source.copy()
    copied.data = source.data.copy()
    copied.name = name

    mesh = copied.data
    material_names = [material.name for material in mesh.materials]
    bm = bmesh.new()
    bm.from_mesh(mesh)
    unwanted = [face for face in bm.faces if not keep_material(material_names[face.material_index])]
    bmesh.ops.delete(bm, geom=unwanted, context="FACES")
    orphaned = [vertex for vertex in bm.verts if not vertex.link_faces]
    if orphaned:
        bmesh.ops.delete(bm, geom=orphaned, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return copied


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
        vertex.co.x = vertex.co.x + (fitted_x - vertex.co.x) * influence
        vertex.co.y -= 0.06 * influence
        vertex.co.z = vertex.co.z + (fitted_z - vertex.co.z + 0.08) * influence


bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))

existing_collection = bpy.data.collections.get("RedFit Sapphire")
if existing_collection:
    for obj in list(existing_collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(existing_collection)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)
    for image in list(bpy.data.images):
        if image.users == 0:
            bpy.data.images.remove(image)

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(SOURCE_GLB))
imported = [obj for obj in bpy.data.objects if obj not in before]
source_body = next(obj for obj in imported if obj.name == "body")
source_outfit = next(obj for obj in imported if obj.type == "MESH" and obj is not source_body)

collection = bpy.data.collections.new("RedFit Sapphire")
bpy.context.scene.collection.children.link(collection)

top = copy_faces(source_outfit, "RedFit_Top_Fitted", lambda material: not material.startswith("FABRIC 2_2711"))
bottom = copy_faces(source_outfit, "RedFit_Pants_Fitted", lambda material: material.startswith("FABRIC 2_2711"))
collection.objects.link(top)
collection.objects.link(bottom)

rotation = Matrix.Rotation(math.radians(-90.0), 4, "X")
for obj in (source_body, top, bottom):
    obj.rotation_euler = rotation.to_euler()

bpy.context.view_layer.update()
target_body = bpy.data.objects["Beta_Surface"]
source_minimum, source_maximum = world_bounds(source_body)
target_minimum, target_maximum = world_bounds(target_body)
scale = (target_maximum.z - target_minimum.z) / (source_maximum.z - source_minimum.z)

for obj in (source_body, top, bottom):
    obj.scale *= scale

bpy.context.view_layer.update()
source_minimum, source_maximum = world_bounds(source_body)
source_center = (source_minimum + source_maximum) * 0.5
target_center = (target_minimum + target_maximum) * 0.5
offset = target_center - source_center
for obj in (source_body, top, bottom):
    obj.location += offset

apply_transform(top)
apply_transform(bottom)

for vertex in top.data.vertices:
    vertex.co.y *= 1.18
for vertex in bottom.data.vertices:
    vertex.co.y *= 1.08

rig = bpy.data.objects["XBotRig"]
left_shoulder = rig.matrix_world @ rig.data.bones["mixamorig:LeftArm"].head_local
right_shoulder = rig.matrix_world @ rig.data.bones["mixamorig:RightArm"].head_local
left_wrist = rig.matrix_world @ rig.data.bones["mixamorig:LeftHand"].head_local
right_wrist = rig.matrix_world @ rig.data.bones["mixamorig:RightHand"].head_local
fit_sleeve(top, left_shoulder, left_wrist, 1.0)
fit_sleeve(top, right_shoulder, right_wrist, -1.0)
top.data.update()

bpy.data.objects.remove(source_outfit, do_unlink=True)
bpy.data.objects.remove(source_body, do_unlink=True)

for name in ("PufferJacket_ArmsOnly", "Skirt_XBot_Rigged", "PufferPants_XBot_Rigged"):
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_viewport = True
        obj.hide_render = True

top.hide_viewport = False
top.hide_render = False
bottom.hide_viewport = False
bottom.hide_render = False

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
scene.render.filepath = str(PREVIEW)
bpy.ops.render.render(write_still=True)

if SAVE_RESULT:
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))

top_minimum, top_maximum = world_bounds(top)
bottom_minimum, bottom_maximum = world_bounds(bottom)
print(
    "REDFIT",
    f"top={len(top.data.polygons)} polys bounds={tuple(round(value, 3) for value in top_minimum)}..{tuple(round(value, 3) for value in top_maximum)}",
    f"bottom={len(bottom.data.polygons)} polys bounds={tuple(round(value, 3) for value in bottom_minimum)}..{tuple(round(value, 3) for value in bottom_maximum)}",
    f"saved={SAVE_RESULT}",
)
