from pathlib import Path
import math

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BASE_BLEND = PROJECT_ROOT / "Tools/Blender/ArmsOnly/puffer_xbot_arms_only.blend"
SKIRT_SOURCE = PROJECT_ROOT / "Assets/Garment/Models/Skirt/Source/skirt 1_fbx_thick.fbx"
PANTS_SOURCE = PROJECT_ROOT / "Assets/Garment/Models/PufferPants/Source/Freebie Pants_fbx_thick.fbx"
OUTPUT_BLEND = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SKIRT_FBX = PROJECT_ROOT / "Assets/Garment/Models/Skirt/Skirt_XBot_Rigged.fbx"
PANTS_FBX = PROJECT_ROOT / "Assets/Garment/Models/PufferPants/PufferPants_XBot_Rigged.fbx"
PREVIEW_DIR = PROJECT_ROOT / "Tools/Blender/Outfits/Previews"


def import_single_mesh(path):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=str(path))
    imported = [obj for obj in bpy.data.objects if obj not in before and obj.type == "MESH"]
    return imported[0]


def apply_modifier(obj, modifier):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def prepare_garment(obj, name, scale_xy, height_offset, target_triangles=None, uniform_scale=None):
    obj.name = name
    if uniform_scale is None:
        obj.scale.x *= scale_xy[0]
        obj.scale.y *= scale_xy[1]
    else:
        obj.scale = (uniform_scale, uniform_scale, uniform_scale)
    obj.location.z += height_offset

    if target_triangles is not None:
        current_triangles = sum(len(face.vertices) - 2 for face in obj.data.polygons)
        decimate = obj.modifiers.new("Unity triangle budget", "DECIMATE")
        decimate.ratio = min(1.0, target_triangles / current_triangles)
        decimate.use_collapse_triangulate = True
        apply_modifier(obj, decimate)

    return obj


def add_weight(group, vertex_index, weight):
    if weight > 0.0001:
        group.add([vertex_index], weight, "REPLACE")


def smoothstep(edge_a, edge_b, value):
    value = max(0.0, min(1.0, (value - edge_a) / (edge_b - edge_a)))
    return value * value * (3.0 - 2.0 * value)


def bind_to_rig(obj, rig):
    armature = obj.modifiers.new("XBot Armature", "ARMATURE")
    armature.object = rig
    obj.parent = rig
    obj.matrix_parent_inverse = rig.matrix_world.inverted()


def weight_skirt(obj, rig):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)

    hips = obj.vertex_groups.new(name="mixamorig:Hips")
    left_thigh = obj.vertex_groups.new(name="mixamorig:LeftUpLeg")
    right_thigh = obj.vertex_groups.new(name="mixamorig:RightUpLeg")

    for vertex in obj.data.vertices:
        position = obj.matrix_world @ vertex.co
        thigh_weight = 0.72 * (1.0 - smoothstep(0.62, 1.08, position.z))
        left_weight = smoothstep(-0.06, 0.06, position.x)
        add_weight(hips, vertex.index, 1.0 - thigh_weight)
        add_weight(left_thigh, vertex.index, thigh_weight * left_weight)
        add_weight(right_thigh, vertex.index, thigh_weight * (1.0 - left_weight))

    bind_to_rig(obj, rig)


def weight_pants(obj, rig):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)

    groups = {
        name: obj.vertex_groups.new(name=name)
        for name in (
            "mixamorig:Hips",
            "mixamorig:LeftUpLeg",
            "mixamorig:LeftLeg",
            "mixamorig:LeftFoot",
            "mixamorig:RightUpLeg",
            "mixamorig:RightLeg",
            "mixamorig:RightFoot",
        )
    }

    for vertex in obj.data.vertices:
        position = obj.matrix_world @ vertex.co
        left_weight = smoothstep(-0.006, 0.006, position.x)
        hips_weight = smoothstep(0.88, 1.15, position.z)
        remaining = 1.0 - hips_weight

        if position.z >= 0.64:
            thigh_weight = remaining
            shin_weight = 0.0
            foot_weight = 0.0
        elif position.z >= 0.43:
            thigh_weight = remaining * smoothstep(0.43, 0.64, position.z)
            shin_weight = remaining - thigh_weight
            foot_weight = 0.0
        elif position.z >= 0.10:
            thigh_weight = 0.0
            shin_weight = remaining
            foot_weight = 0.0
        else:
            foot_weight = remaining * (1.0 - smoothstep(0.02, 0.10, position.z))
            shin_weight = remaining - foot_weight
            thigh_weight = 0.0

        add_weight(groups["mixamorig:Hips"], vertex.index, hips_weight)
        add_weight(groups["mixamorig:LeftUpLeg"], vertex.index, thigh_weight * left_weight)
        add_weight(groups["mixamorig:RightUpLeg"], vertex.index, thigh_weight * (1.0 - left_weight))
        add_weight(groups["mixamorig:LeftLeg"], vertex.index, shin_weight * left_weight)
        add_weight(groups["mixamorig:RightLeg"], vertex.index, shin_weight * (1.0 - left_weight))
        add_weight(groups["mixamorig:LeftFoot"], vertex.index, foot_weight * left_weight)
        add_weight(groups["mixamorig:RightFoot"], vertex.index, foot_weight * (1.0 - left_weight))

    bind_to_rig(obj, rig)


def set_material_color(obj, color):
    material = obj.data.materials[0] if obj.data.materials else bpy.data.materials.new(obj.name + " Material")
    if not obj.data.materials:
        obj.data.materials.append(material)
    material.diffuse_color = (*color, 1.0)


def look_at(camera, target):
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()


def render_preview(scene, camera, skirt, pants, garment, filename, camera_position):
    skirt.hide_render = garment is not skirt
    pants.hide_render = garment is not pants
    camera.location = camera_position
    look_at(camera, (0.0, 0.0, 0.92))
    scene.render.filepath = str(PREVIEW_DIR / filename)
    bpy.ops.render.render(write_still=True)


def export_garment(rig, garment, path):
    garment.hide_viewport = False
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    garment.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
    )


def main():
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BASE_BLEND))

    rig = bpy.data.objects["XBotRig"]
    body = bpy.data.objects["Beta_Surface"]
    joints = bpy.data.objects.get("Beta_Joints")
    jacket = bpy.data.objects.get("PufferJacket_ArmsOnly")
    if joints:
        joints.hide_render = True
        joints.hide_viewport = True
    if jacket:
        jacket.hide_render = True
        jacket.hide_viewport = True

    skirt = prepare_garment(
        import_single_mesh(SKIRT_SOURCE),
        "Skirt_XBot_Rigged",
        scale_xy=(1.14, 1.20),
        height_offset=-0.38587,
        uniform_scale=0.012,
    )
    pants = prepare_garment(
        import_single_mesh(PANTS_SOURCE),
        "PufferPants_XBot_Rigged",
        scale_xy=(1.18, 1.30),
        height_offset=-0.08,
        target_triangles=70000,
    )
    pants.location = (0.0, 0.0, 0.014962)
    pants.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    pants.scale = (0.012, 0.009, 0.009)

    weight_skirt(skirt, rig)
    weight_pants(pants, rig)

    set_material_color(body, (0.30, 0.13, 0.16))
    set_material_color(skirt, (0.72, 0.22, 0.30))
    set_material_color(pants, (0.20, 0.38, 0.68))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "WORLD"
    scene.display.shading.curvature_ridge_factor = 1.5
    scene.display.shading.curvature_valley_factor = 1.0
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False

    camera = bpy.data.objects.get("Camera")
    camera.data.lens = 58
    scene.camera = camera

    render_preview(scene, camera, skirt, pants, skirt, "skirt_front.png", (0.0, -3.25, 1.05))
    render_preview(scene, camera, skirt, pants, skirt, "skirt_side.png", (3.25, 0.0, 1.05))
    render_preview(scene, camera, skirt, pants, pants, "pants_front.png", (0.0, -3.25, 1.05))
    render_preview(scene, camera, skirt, pants, pants, "pants_side.png", (3.25, 0.0, 1.05))

    skirt.hide_render = False
    pants.hide_render = True
    skirt.hide_viewport = False
    pants.hide_viewport = True
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))

    export_garment(rig, skirt, SKIRT_FBX)
    export_garment(rig, pants, PANTS_FBX)

    skirt_triangles = sum(len(face.vertices) - 2 for face in skirt.data.polygons)
    pants_triangles = sum(len(face.vertices) - 2 for face in pants.data.polygons)
    print(f"BUILT skirt={skirt_triangles} tris groups={len(skirt.vertex_groups)}")
    print(f"BUILT pants={pants_triangles} tris groups={len(pants.vertex_groups)}")
    print(f"BLEND {OUTPUT_BLEND}")


main()
