from pathlib import Path

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
WALKING_FBX = PROJECT_ROOT / "Assets/Garment/Models/XBot/Poses/Walking.fbx"
PREVIEW_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_dress_v1_walking_blender.png"
ACTION_NAME = "Walking_XBot_Preview"


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))

rig = bpy.data.objects["XBotRig"]
dress = bpy.data.objects["RedFit_Dress_V1_Skinned"]
body = bpy.data.objects["Beta_Surface"]

rig.animation_data_create()
rig.animation_data.action = None
old_action = bpy.data.actions.get(ACTION_NAME)
if old_action:
    bpy.data.actions.remove(old_action)

objects_before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(WALKING_FBX))
imported_objects = [obj for obj in bpy.data.objects if obj not in objects_before]
imported_rig = next(obj for obj in imported_objects if obj.type == "ARMATURE")
walking_action = imported_rig.animation_data.action
walking_action.name = ACTION_NAME
walking_action.use_fake_user = True

rig.animation_data.action = walking_action
if walking_action.slots:
    rig.animation_data.action_slot = walking_action.slots[0]
rig.data.pose_position = "POSE"

for obj in imported_objects:
    bpy.data.objects.remove(obj, do_unlink=True)

for name in ("RedFit_Top_XBot_Fitted", "PufferJacket_ArmsOnly", "Skirt_XBot_Rigged", "PufferPants_XBot_Rigged"):
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_render = True
        obj.hide_viewport = True

dress.hide_render = False
dress.hide_viewport = False
body.hide_render = False
body.hide_viewport = False

scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = 30
scene.render.fps = 30
scene.sync_mode = "FRAME_DROP"
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x = 900
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100

camera = bpy.data.objects["Camera"]
camera.data.type = "ORTHO"
camera.data.ortho_scale = 2.15
camera.location = (0.0, -4.0, 1.02)
camera.rotation_euler = (Vector((0.0, 0.0, 1.02)) - camera.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = camera

scene.frame_set(16)
scene.render.filepath = str(PREVIEW_PATH)
bpy.ops.render.render(write_still=True)
scene.frame_set(1)

bpy.ops.object.select_all(action="DESELECT")
dress.select_set(True)
bpy.context.view_layer.objects.active = dress
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

print(
    "WALKING_PREVIEW_READY",
    f"action={walking_action.name}",
    f"frames={scene.frame_start}-{scene.frame_end}",
    f"dress={dress.name}",
    f"preview={PREVIEW_PATH}",
)
