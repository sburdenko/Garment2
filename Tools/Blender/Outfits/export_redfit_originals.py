from pathlib import Path

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
EXPORT_DIRECTORY = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Original"
EXPORTS = {
    "RedFit_Top_Original": "RedFit_Top_Original.fbx",
    "RedFit_Pants_Original": "RedFit_Pants_Original.fbx",
}


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        tuple(round(min(corner[axis] for corner in corners), 6) for axis in range(3)),
        tuple(round(max(corner[axis] for corner in corners), 6) for axis in range(3)),
    )


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))
EXPORT_DIRECTORY.mkdir(parents=True, exist_ok=True)

for object_name, filename in EXPORTS.items():
    bpy.ops.object.select_all(action="DESELECT")
    garment = bpy.data.objects[object_name]
    garment.hide_set(False)
    garment.hide_viewport = False
    garment.select_set(True)
    bpy.context.view_layer.objects.active = garment

    export_path = EXPORT_DIRECTORY / filename
    bpy.ops.export_scene.fbx(
        filepath=str(export_path),
        use_selection=True,
        object_types={"MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_space_transform=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )
    print("REDFIT_EXPORT", object_name, export_path, world_bounds(garment))
