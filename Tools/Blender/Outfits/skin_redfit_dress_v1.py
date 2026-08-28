from pathlib import Path
import math

import bmesh
import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
EXPORT_PATH = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Versions/V1/RedFit_Dress_V1.fbx"
PREVIEW_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_dress_v1_skinned.png"
TOP_PREVIEW_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_dress_v1_skinned_top.png"
SOURCE_NAME = "RedFit_Top_XBot_Fitted"
SKINNED_NAME = "RedFit_Dress_V1_Skinned"


def smoothstep(edge_a, edge_b, value):
    value = max(0.0, min(1.0, (value - edge_a) / (edge_b - edge_a)))
    return value * value * (3.0 - 2.0 * value)


def apply_modifier(obj, modifier):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def center_sleeves_on_arms(dress):
    sleeve_vertices = {}
    for vertex in dress.data.vertices:
        position = vertex.co
        distance_from_center = abs(position.x)
        if distance_from_center <= 0.16 or position.z <= 1.15:
            continue

        outward_influence = smoothstep(0.16, 0.32, distance_from_center)
        shoulder_influence = 1.0 - smoothstep(0.25, 0.45, distance_from_center)
        depth_influence = smoothstep(0.24, 0.58, distance_from_center)
        front_y = -0.06
        depth_scale = 1.0 + 0.75 * depth_influence
        position.y = front_y + (position.y - front_y) * depth_scale
        position.z += 0.085 * outward_influence * shoulder_influence
        if distance_from_center >= 0.24:
            shoulder_overlap = 1.0 - smoothstep(0.24, 0.38, distance_from_center)
            position.x -= math.copysign(0.09 * shoulder_overlap, position.x)
            sleeve_vertices[vertex.index] = distance_from_center

    dress.data.update()
    return sleeve_vertices


def transfer_body_weights(dress, body):
    for group in list(dress.vertex_groups):
        dress.vertex_groups.remove(group)
    for group in body.vertex_groups:
        dress.vertex_groups.new(name=group.name)

    transfer = dress.modifiers.new("XBot body weights", "DATA_TRANSFER")
    transfer.object = body
    transfer.use_vert_data = True
    transfer.data_types_verts = {"VGROUP_WEIGHTS"}
    transfer.vert_mapping = "POLYINTERP_NEAREST"
    transfer.layers_vgroup_select_src = "ALL"
    transfer.layers_vgroup_select_dst = "NAME"
    transfer.mix_mode = "REPLACE"
    transfer.mix_factor = 1.0
    apply_modifier(dress, transfer)


def weight_sleeves(dress, sleeve_vertices):
    group_indices = {
        name: dress.vertex_groups[name].index
        for name in (
            "mixamorig:LeftArm",
            "mixamorig:LeftForeArm",
            "mixamorig:LeftHand",
            "mixamorig:RightArm",
            "mixamorig:RightForeArm",
            "mixamorig:RightHand",
        )
    }

    bm = bmesh.new()
    bm.from_mesh(dress.data)
    bm.verts.ensure_lookup_table()
    deform = bm.verts.layers.deform.verify()
    for vertex_index, original_distance in sleeve_vertices.items():
        vertex = bm.verts[vertex_index]
        side = "Left" if vertex.co.x > 0.0 else "Right"
        weights = vertex[deform]
        weights.clear()

        if original_distance < 0.40:
            weights[group_indices[f"mixamorig:{side}Arm"]] = 1.0
        elif original_distance < 0.50:
            forearm_weight = smoothstep(0.40, 0.50, original_distance)
            weights[group_indices[f"mixamorig:{side}Arm"]] = 1.0 - forearm_weight
            weights[group_indices[f"mixamorig:{side}ForeArm"]] = forearm_weight
        else:
            hand_weight = 0.30 * smoothstep(0.64, 0.73, original_distance)
            weights[group_indices[f"mixamorig:{side}ForeArm"]] = 1.0 - hand_weight
            weights[group_indices[f"mixamorig:{side}Hand"]] = hand_weight

    bm.to_mesh(dress.data)
    bm.free()
    dress.data.update()


def stabilize_long_skirt(dress):
    hips_index = dress.vertex_groups["mixamorig:Hips"].index
    left_thigh_index = dress.vertex_groups["mixamorig:LeftUpLeg"].index
    right_thigh_index = dress.vertex_groups["mixamorig:RightUpLeg"].index

    mesh = dress.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    deform = bm.verts.layers.deform.verify()

    stabilized = 0
    for vertex in bm.verts:
        world_position = dress.matrix_world @ vertex.co
        if world_position.z >= 1.05:
            continue

        lower_influence = 1.0 - smoothstep(0.45, 1.05, world_position.z)
        thigh_weight = 0.45 * lower_influence
        left_share = smoothstep(-0.08, 0.08, world_position.x)

        weights = vertex[deform]
        weights.clear()
        weights[hips_index] = 1.0 - thigh_weight
        weights[left_thigh_index] = thigh_weight * left_share
        weights[right_thigh_index] = thigh_weight * (1.0 - left_share)
        stabilized += 1

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return stabilized


def bind_to_rig(dress, rig):
    armature = dress.modifiers.new("XBot Armature", "ARMATURE")
    armature.object = rig
    dress.parent = rig
    dress.matrix_parent_inverse = rig.matrix_world.inverted()


def vertex_weight_report(dress):
    weighted = 0
    maximum_influences = 0
    for vertex in dress.data.vertices:
        influences = [membership.weight for membership in vertex.groups if membership.weight > 0.0001]
        if influences:
            weighted += 1
            maximum_influences = max(maximum_influences, len(influences))
    return weighted, maximum_influences


def render_preview(dress):
    for name in ("PufferJacket_ArmsOnly", "Skirt_XBot_Rigged", "PufferPants_XBot_Rigged"):
        obj = bpy.data.objects.get(name)
        if obj:
            obj.hide_render = True
            obj.hide_viewport = True

    dress.hide_render = False
    dress.hide_viewport = False
    body = bpy.data.objects["Beta_Surface"]
    body.hide_render = False
    body.hide_viewport = False

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

    camera.data.ortho_scale = 1.85
    camera.location = (0.0, 0.0, 4.0)
    camera.rotation_euler = (Vector((0.0, 0.0, 1.3)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(TOP_PREVIEW_PATH)
    bpy.ops.render.render(write_still=True)


def export_fbx(rig, dress):
    EXPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    rig.hide_set(False)
    dress.hide_set(False)
    rig.select_set(True)
    dress.select_set(True)
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


def validate_export():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(EXPORT_PATH))
    armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    if len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(f"Expected one armature and one mesh, got {len(armatures)} and {len(meshes)}")

    rig = armatures[0]
    dress = meshes[0]
    armature_modifiers = [modifier for modifier in dress.modifiers if modifier.type == "ARMATURE"]
    weighted_vertices, maximum_influences = vertex_weight_report(dress)
    if not armature_modifiers or weighted_vertices != len(dress.data.vertices):
        raise RuntimeError(
            f"Invalid skin: modifiers={len(armature_modifiers)} weighted={weighted_vertices}/{len(dress.data.vertices)}"
        )

    depsgraph = bpy.context.evaluated_depsgraph_get()
    sample_step = max(1, len(dress.data.vertices) // 2000)

    def sample_positions():
        evaluated = dress.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        positions = [
            evaluated.matrix_world @ evaluated_mesh.vertices[index].co
            for index in range(0, len(evaluated_mesh.vertices), sample_step)
        ]
        evaluated.to_mesh_clear()
        return positions

    rest_positions = sample_positions()
    forearm = rig.pose.bones.get("mixamorig:LeftForeArm")
    if forearm is None:
        raise RuntimeError("Imported rig has no mixamorig:LeftForeArm bone")
    forearm.rotation_mode = "XYZ"
    forearm.rotation_euler.z = math.radians(35.0)
    bpy.context.view_layer.update()
    posed_positions = sample_positions()
    maximum_motion = max((posed - rest).length for rest, posed in zip(rest_positions, posed_positions))
    if maximum_motion < 0.01:
        raise RuntimeError(f"Skin deformation validation failed: max motion {maximum_motion:.6f}")

    print(
        "REDFIT_V1_SKIN_VALID",
        f"mesh={dress.name}",
        f"vertices={len(dress.data.vertices)}",
        f"groups={len(dress.vertex_groups)}",
        f"weighted={weighted_vertices}",
        f"max_influences={maximum_influences}",
        f"pose_motion={maximum_motion:.4f}",
    )


def main():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))
    source = bpy.data.objects[SOURCE_NAME]
    rig = bpy.data.objects["XBotRig"]
    body = bpy.data.objects["Beta_Surface"]
    if rig.animation_data:
        rig.animation_data.action = None
    rig.data.pose_position = "REST"
    bpy.context.scene.frame_set(1)

    previous = bpy.data.objects.get(SKINNED_NAME)
    if previous:
        bpy.data.objects.remove(previous, do_unlink=True)

    dress = source.copy()
    dress.data = source.data.copy()
    dress.name = SKINNED_NAME
    source.users_collection[0].objects.link(dress)
    source.hide_render = True
    source.hide_viewport = True

    sleeve_vertices = center_sleeves_on_arms(dress)
    transfer_body_weights(dress, body)
    weight_sleeves(dress, sleeve_vertices)
    stabilized = stabilize_long_skirt(dress)
    bind_to_rig(dress, rig)
    weighted_vertices, maximum_influences = vertex_weight_report(dress)
    if weighted_vertices != len(dress.data.vertices):
        raise RuntimeError(f"Unweighted vertices: {len(dress.data.vertices) - weighted_vertices}")

    render_preview(dress)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    export_fbx(rig, dress)
    print(
        "REDFIT_V1_SKINNED",
        EXPORT_PATH,
        f"vertices={len(dress.data.vertices)}",
        f"groups={len(dress.vertex_groups)}",
        f"max_influences={maximum_influences}",
        f"skirt_vertices={stabilized}",
        f"sleeve_vertices={len(sleeve_vertices)}",
    )
    validate_export()


main()
