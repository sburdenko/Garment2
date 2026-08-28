"""Exports the skinned RedFit Sapphire top, with the XBot rig, for Unity.

Run after skin_redfit_top.py, on the blend it saved:
  blender xbot_skirt_pants.blend --background --python export_redfit_top.py
"""

from pathlib import Path

import bpy

PROJECT_ROOT = Path(__file__).resolve().parents[3]
EXPORT_PATH = (PROJECT_ROOT
               / "Assets/Garment/Models/RedFitSapphire/Rigged_XBot/RedFit_Top_XBot_Rigged.fbx")
DRESS_NAME = "RedFit_Top_Skinned"
RIG_NAME = "XBotRig"


def main():
    rig = bpy.data.objects[RIG_NAME]
    dress = bpy.data.objects[DRESS_NAME]
    EXPORT_PATH.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in (rig, dress):
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig

    bpy.ops.export_scene.fbx(
        filepath=str(EXPORT_PATH),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
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
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )
    size = EXPORT_PATH.stat().st_size / 1e6
    print(f"[export] {EXPORT_PATH} ({size:.1f} MB)")

    weighted = sum(1 for v in dress.data.vertices if any(g.weight > 1e-4 for g in v.groups))
    print(f"[export] {len(dress.data.vertices)} верш, с весами {weighted}, "
          f"материалов {len(dress.data.materials)}, костей {len(rig.data.bones)}")


main()
