from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))

outfit = bpy.data.objects["redfit_onGirl_fbx2"]
pants_material_index = next(
    index
    for index, material in enumerate(outfit.data.materials)
    if material.name.startswith("FABRIC 2_2711")
)

bpy.ops.object.select_all(action="DESELECT")
outfit.hide_set(False)
outfit.hide_viewport = False
outfit.select_set(True)
bpy.context.view_layer.objects.active = outfit

objects_before = set(bpy.data.objects)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="DESELECT")
bpy.ops.object.mode_set(mode="OBJECT")

for polygon in outfit.data.polygons:
    polygon.select = polygon.material_index == pants_material_index

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")

pants = next(obj for obj in bpy.data.objects if obj not in objects_before)
outfit.name = "RedFit_Top_Original"
pants.name = "RedFit_Pants_Original"

bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

print(
    "REDFIT_SPLIT",
    [(obj.name, len(obj.data.vertices), len(obj.data.polygons),
      tuple(round(value, 6) for value in obj.location),
      tuple(round(value, 6) for value in obj.rotation_euler),
      tuple(round(value, 6) for value in obj.scale))
     for obj in (outfit, pants)],
)
