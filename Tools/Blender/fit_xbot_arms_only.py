"""Clean arms-only fitting scene for X Bot and the unrigged puffer jacket.

Scope is intentionally narrow: straighten and skin both sleeves, bind the cuff ends
to the Mixamo hands, test T-pose plus downloaded motions, and ignore torso clipping.
"""

import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2]
BODY = ROOT / "Assets/Garment/Models/XBot/XBot.fbx"
JACKET = ROOT / "Assets/Garment/Models/PufferJacket/PufferJacket_Unrigged.fbx"
POSES = ROOT / "Assets/Garment/Models/XBot/Poses"
OUT = ROOT / "Tools/Blender/ArmsOnly"
BLEND = OUT / "puffer_xbot_arms_only.blend"
RIGGED_FBX = (ROOT / "Assets/Garment/Models/PufferJacket/Rigged_XBot" /
              "PufferJacket_XBot_Rigged.fbx")
PREFIX = "mixamorig:"
ANGLE = math.radians(50.9)

TORSO_CHAIN = ("Hips", "Spine", "Spine1", "Spine2", "Neck")
ARM_CHAIN = ("Shoulder", "Arm", "ForeArm", "Hand")


def smoothstep(low, high, value):
    factor = max(0.0, min(1.0, (value - low) / (high - low)))
    return factor * factor * (3.0 - 2.0 * factor)


def rotate_xz(point, pivot, angle):
    offset = point - pivot
    cosine, sine = math.cos(angle), math.sin(angle)
    return pivot + Vector((
        cosine * offset.x - sine * offset.z,
        offset.y,
        sine * offset.x + cosine * offset.z,
    ))


def drop_loose_vertices(obj):
    used = set()
    for polygon in obj.data.polygons:
        used.update(polygon.vertices)

    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    mesh.verts.ensure_lookup_table()
    loose = [vertex for vertex in mesh.verts if vertex.index not in used]
    bmesh.ops.delete(mesh, geom=loose, context="VERTS")
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.data.update()
    return len(loose)


def connected_components(obj):
    parent = list(range(len(obj.data.vertices)))

    def root(index):
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    for edge in obj.data.edges:
        first, second = root(edge.vertices[0]), root(edge.vertices[1])
        if first != second:
            parent[second] = first

    components = {}
    for vertex in obj.data.vertices:
        components.setdefault(root(vertex.index), []).append(vertex.index)
    return list(components.values())


def sleeve_mask(obj):
    mask = [False] * len(obj.data.vertices)
    count = 0
    for indices in connected_components(obj):
        xs = [obj.data.vertices[index].co.x for index in indices]
        low, high = min(xs), max(xs)
        is_sleeve = ((low > 0.04 and high > 0.32) or
                     (high < -0.04 and low < -0.32))
        if not is_sleeve:
            continue
        count += 1
        for index in indices:
            mask[index] = True
    return mask, count


def load_body():
    bpy.ops.import_scene.fbx(filepath=str(BODY))
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    body_meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    rig.name = "XBotRig"
    rig.data.name = "XBotRig"
    rig.show_in_front = True
    positions = {
        bone.name.replace(PREFIX, ""): rig.matrix_world @ bone.head_local
        for bone in rig.data.bones
    }
    return rig, body_meshes, positions


def load_jacket():
    bpy.ops.import_scene.fbx(filepath=str(JACKET))
    meshes = [obj for obj in bpy.context.scene.objects
              if obj.type == "MESH" and obj.name.startswith("Puffer")]
    if len(meshes) != 1:
        raise RuntimeError(f"Expected one unrigged Puffer mesh, found {len(meshes)}")
    jacket = meshes[0]
    jacket.data.transform(jacket.matrix_world)
    jacket.matrix_world.identity()
    jacket.name = "PufferJacket_ArmsOnly"
    jacket.data.name = "PufferJacket_ArmsOnly"
    return jacket


def a_pose_positions(t_positions):
    posed = dict(t_positions)
    for side, sign in (("Left", -1.0), ("Right", 1.0)):
        pivot = t_positions[f"{side}Arm"]
        for suffix in ARM_CHAIN:
            name = f"{side}{suffix}"
            posed[name] = rotate_xz(t_positions[name], pivot, sign * ANGLE)
    return posed


def torso_weights(point, positions):
    heights = [positions[name].z for name in TORSO_CHAIN]
    if point.z <= heights[0]:
        return {"Hips": 1.0}
    for index in range(len(TORSO_CHAIN) - 1):
        low_name, high_name = TORSO_CHAIN[index], TORSO_CHAIN[index + 1]
        low, high = heights[index], heights[index + 1]
        if point.z <= high:
            factor = smoothstep(low, high, point.z)
            return {low_name: 1.0 - factor, high_name: factor}
    return {"Neck": 1.0}


def arm_weights(point, side, positions):
    arm = positions[f"{side}Arm"]
    elbow = positions[f"{side}ForeArm"]
    hand = positions[f"{side}Hand"]
    direction = (hand - arm).normalized()
    reach = max(0.0, (point - arm).dot(direction))
    elbow_reach = (elbow - arm).length
    hand_reach = (hand - arm).length

    if reach <= elbow_reach:
        forearm = smoothstep(elbow_reach - 0.08, elbow_reach + 0.05, reach)
        return {f"{side}Arm": 1.0 - forearm, f"{side}ForeArm": forearm}

    hand_weight = smoothstep(hand_reach - 0.13, hand_reach - 0.015, reach)
    return {f"{side}ForeArm": 1.0 - hand_weight, f"{side}Hand": hand_weight}


def assign_weights(jacket, mask, positions):
    names = list(TORSO_CHAIN)
    names += [f"{side}{suffix}" for side in ("Left", "Right") for suffix in ARM_CHAIN]
    groups = {name: jacket.vertex_groups.new(name=PREFIX + name) for name in names}

    mesh = bmesh.new()
    mesh.from_mesh(jacket.data)
    mesh.verts.ensure_lookup_table()
    deform = mesh.verts.layers.deform.verify()

    arm_vertices = 0
    hand_vertices = 0
    for vertex in mesh.verts:
        point = vertex.co
        side = "Left" if point.x >= 0.0 else "Right"
        shoulder = positions[f"{side}Arm"]
        hand = positions[f"{side}Hand"]
        direction = (hand - shoulder).normalized()
        offset = point - shoulder
        along = offset.dot(direction)
        radial = (offset - direction * along).length

        arm_factor = 0.0
        if mask[vertex.index]:
            arm_factor = smoothstep(-0.03, 0.10, along)
            arm_factor *= 1.0 - smoothstep(0.19, 0.30, radial)
        if arm_factor > 0.01:
            arm_vertices += 1

        weights = {}
        for name, value in torso_weights(point, positions).items():
            weights[name] = value * (1.0 - arm_factor)
        for name, value in arm_weights(point, side, positions).items():
            weights[name] = weights.get(name, 0.0) + value * arm_factor

        hand_name = f"{side}Hand"
        if weights.get(hand_name, 0.0) >= 0.5:
            hand_vertices += 1

        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(value for _, value in strongest)
        for name, value in strongest:
            vertex[deform][groups[name].index] = value / total

    mesh.to_mesh(jacket.data)
    mesh.free()
    jacket.data.update()
    return arm_vertices, hand_vertices


def straighten_weighted_sleeves(jacket, t_positions):
    arm_group_indices = {
        group.index for group in jacket.vertex_groups
        if any(group.name == f"{PREFIX}{side}{suffix}"
               for side in ("Left", "Right") for suffix in ARM_CHAIN)
    }
    moved = 0
    for vertex in jacket.data.vertices:
        influence = min(sum(assignment.weight for assignment in vertex.groups
                            if assignment.group in arm_group_indices), 1.0)
        if influence <= 0.01:
            continue
        side = "Left" if vertex.co.x >= 0.0 else "Right"
        pivot = t_positions[f"{side}Arm"]
        sign = 1.0 if side == "Left" else -1.0
        target = rotate_xz(vertex.co, pivot, sign * ANGLE)
        vertex.co = vertex.co.lerp(target, influence)
        moved += 1
    jacket.data.update()
    return moved


def arm_influence(vertex, arm_group_indices):
    return min(sum(assignment.weight for assignment in vertex.groups
                   if assignment.group in arm_group_indices), 1.0)


def arm_axis_at_reach(side, reach, t_positions):
    """Return the centre line of the T-pose arm at a signed X reach."""
    sign = 1.0 if side == "Left" else -1.0
    arm = t_positions[f"{side}Arm"]
    elbow = t_positions[f"{side}ForeArm"]
    hand = t_positions[f"{side}Hand"]
    arm_reach = sign * arm.x
    elbow_reach = sign * elbow.x
    hand_reach = sign * hand.x

    if reach <= elbow_reach:
        factor = max(0.0, min(1.0, (reach - arm_reach) /
                              (elbow_reach - arm_reach)))
        return arm.lerp(elbow, factor)
    factor = max(0.0, min(1.0, (reach - elbow_reach) /
                          (hand_reach - elbow_reach)))
    return elbow.lerp(hand, factor)


def centre_sleeves_on_arm_axis(jacket, mask, t_positions):
    """Centre every sleeve cross-section on the matching T-pose arm bone."""
    arm_group_indices = {
        group.index for group in jacket.vertex_groups
        if any(group.name == f"{PREFIX}{side}{suffix}"
               for side in ("Left", "Right") for suffix in ARM_CHAIN)
    }
    bin_size = 0.025
    moved = 0

    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        arm_reach = sign * t_positions[f"{side}Arm"].x
        hand_reach = sign * t_positions[f"{side}Hand"].x
        bins = {}
        for vertex in jacket.data.vertices:
            if not mask[vertex.index] or sign * vertex.co.x <= 0.0:
                continue
            influence = arm_influence(vertex, arm_group_indices)
            if influence <= 0.2:
                continue
            reach = sign * vertex.co.x
            if reach < arm_reach - 0.03 or reach > hand_reach + 0.06:
                continue
            key = round((reach - arm_reach) / bin_size)
            bins.setdefault(key, []).append(vertex)

        for vertices in bins.values():
            if len(vertices) < 8:
                continue
            reach = sum(sign * vertex.co.x for vertex in vertices) / len(vertices)
            sleeve_y = (min(vertex.co.y for vertex in vertices) +
                        max(vertex.co.y for vertex in vertices)) * 0.5
            sleeve_z = (min(vertex.co.z for vertex in vertices) +
                        max(vertex.co.z for vertex in vertices)) * 0.5
            axis = arm_axis_at_reach(side, reach, t_positions)
            along = smoothstep(arm_reach, hand_reach, reach)
            blend = 0.55 + 0.45 * along
            offset = Vector((0.0, axis.y - sleeve_y, axis.z - sleeve_z)) * blend
            for vertex in vertices:
                vertex.co += offset
                moved += 1

    jacket.data.update()
    return moved


def overlap_sleeve_roots(jacket, mask, t_positions):
    """Move only the shoulder end inward so the sleeve overlaps the jacket body."""
    moved = 0
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        arm_axis = t_positions[f"{side}Arm"]
        arm_reach = sign * t_positions[f"{side}Arm"].x
        root_end = arm_reach + 0.23
        for vertex in jacket.data.vertices:
            reach = sign * vertex.co.x
            if (not mask[vertex.index] or sign * vertex.co.x <= 0.0 or
                    reach < arm_reach - 0.04 or reach > root_end):
                continue
            root_factor = 1.0 - smoothstep(arm_reach - 0.02, root_end, reach)
            lower_factor = 1.0 - smoothstep(arm_axis.z - 0.01,
                                            arm_axis.z + 0.04,
                                            vertex.co.z)
            vertex.co.x -= sign * 0.20 * root_factor
            vertex.co.z -= 0.13 * root_factor * lower_factor
            moved += 1

    jacket.data.update()
    return moved


def inflate_sleeves_around_arms(jacket, t_positions):
    """Keep the X Bot arm surface inside the puffer sleeve tube.

    Only vertices already carrying arm weights are touched.  Torso and lower body are
    deliberately outside the scope of this pass.
    """
    arm_group_indices = {
        group.index for group in jacket.vertex_groups
        if any(group.name == f"{PREFIX}{side}{suffix}"
               for side in ("Left", "Right") for suffix in ARM_CHAIN)
    }
    moved = 0
    for vertex in jacket.data.vertices:
        influence = arm_influence(vertex, arm_group_indices)
        if influence <= 0.2:
            continue

        side = "Left" if vertex.co.x >= 0.0 else "Right"
        sign = 1.0 if side == "Left" else -1.0
        arm = t_positions[f"{side}Arm"]
        hand = t_positions[f"{side}Hand"]
        reach = sign * vertex.co.x
        arm_reach = sign * arm.x
        hand_reach = sign * hand.x
        if reach < arm_reach - 0.03 or reach > hand_reach + 0.05:
            continue

        along = smoothstep(arm_reach, hand_reach, reach)
        target_radius = 0.120 + (0.078 - 0.120) * along
        axis = arm_axis_at_reach(side, reach, t_positions)
        radial = Vector((0.0, vertex.co.y - axis.y, vertex.co.z - axis.z))
        radius = radial.length
        if radius >= target_radius or radius < 1e-5:
            continue

        correction = radial.normalized() * (target_radius - radius) * influence * 0.65
        vertex.co += correction
        moved += 1
    jacket.data.update()
    return moved


def attach_to_rig(jacket, rig):
    jacket.data.transform(rig.matrix_world.inverted())
    jacket.parent = rig
    jacket.parent_type = "OBJECT"
    jacket.matrix_parent_inverse.identity()
    jacket.matrix_basis.identity()
    modifier = jacket.modifiers.new(name="XBot Armature", type="ARMATURE")
    modifier.object = rig
    modifier.use_deform_preserve_volume = True


def export_rigged_fbx(jacket, rig):
    RIGGED_FBX.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    jacket.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=str(RIGGED_FBX),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True,
        bake_space_transform=False,
        add_leaf_bones=False,
        use_armature_deform_only=True,
        mesh_smooth_type="FACE",
        bake_anim=False,
        path_mode="COPY",
        embed_textures=True,
    )


def load_actions(rig):
    actions = []
    for path in sorted(POSES.glob("*.fbx")):
        old_actions = set(bpy.data.actions)
        old_objects = set(bpy.context.scene.objects)
        bpy.ops.import_scene.fbx(filepath=str(path))
        imported = [action for action in bpy.data.actions if action not in old_actions]
        for obj in list(bpy.context.scene.objects):
            if obj not in old_objects:
                bpy.data.objects.remove(obj, do_unlink=True)
        if imported:
            action = imported[0]
            action.name = path.stem
            action.use_fake_user = True
            actions.append(action)
    rig.animation_data_create()
    return actions


def setup_render(body_meshes, jacket):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 700
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "OBJECT"
    scene.display.shading.show_cavity = True
    scene.world = bpy.data.worlds.new("World")
    scene.world.color = (0.08, 0.09, 0.11)
    for body in body_meshes:
        body.color = (0.25, 0.66, 0.84, 1.0)
    jacket.color = (0.88, 0.28, 0.12, 1.0)

    camera_data = bpy.data.cameras.new("Camera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 1.35
    camera = bpy.data.objects.new("Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    return camera


def render(camera, filename, position, target=(0.0, 0.0, 1.35)):
    camera.location = Vector(position)
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()
    path = OUT / filename
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    return path


def cuff_report(jacket, mask, t_positions):
    result = {}
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        candidates = [vertex.co for vertex in jacket.data.vertices
                      if mask[vertex.index] and sign * vertex.co.x > 0.0]
        furthest = sorted(candidates, key=lambda point: sign * point.x, reverse=True)[:800]
        centre = Vector((
            sum(point.x for point in furthest) / len(furthest),
            (min(point.y for point in furthest) + max(point.y for point in furthest)) * 0.5,
            (min(point.z for point in furthest) + max(point.z for point in furthest)) * 0.5,
        ))
        result[side] = (centre, (centre - t_positions[f"{side}Hand"]).length)
    return result


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    OUT.mkdir(parents=True, exist_ok=True)
    rig, body_meshes, t_positions = load_body()
    jacket = load_jacket()
    loose = drop_loose_vertices(jacket)
    mask, sleeve_components = sleeve_mask(jacket)
    posed = a_pose_positions(t_positions)
    arm_vertices, hand_vertices = assign_weights(jacket, mask, posed)
    moved = straighten_weighted_sleeves(jacket, t_positions)
    centred = centre_sleeves_on_arm_axis(jacket, mask, t_positions)
    overlapped = overlap_sleeve_roots(jacket, mask, t_positions)
    inflated = inflate_sleeves_around_arms(jacket, t_positions)
    cuffs = cuff_report(jacket, mask, t_positions)

    camera = setup_render(body_meshes, jacket)
    renders = [
        render(camera, "arms_tpose_front.png", (0.0, -3.0, 1.35)),
        render(camera, "arms_tpose_three_quarter.png", (2.0, -2.5, 1.35)),
    ]

    attach_to_rig(jacket, rig)
    actions = load_actions(rig)
    for action in actions:
        rig.animation_data.action = action
        first, last = int(action.frame_range[0]), int(action.frame_range[1])
        bpy.context.scene.frame_set(first + (last - first) // 2)
        renders.append(render(camera, f"arms_{action.name.lower()}.png", (0.0, -3.0, 1.35)))

    rig.animation_data.action = None
    bpy.context.scene.frame_set(1)
    for pose_bone in rig.pose.bones:
        pose_bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    export_rigged_fbx(jacket, rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    jacket.select_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))

    print("\n================ ARMS ONLY ================")
    print("loose vertices removed", loose)
    print("sleeve components     ", sleeve_components)
    print("arm-weighted vertices ", arm_vertices)
    print("hand-weighted vertices", hand_vertices)
    print("vertices straightened ", moved)
    print("sleeve vertices centred", centred)
    print("root vertices overlapped", overlapped)
    print("sleeve vertices inflated", inflated)
    print("unweighted vertices   ", sum(1 for vertex in jacket.data.vertices if not vertex.groups))
    for side, (centre, distance) in cuffs.items():
        print(f"{side} cuff centre      {tuple(round(v, 3) for v in centre)}; "
              f"hand distance {distance * 1000:.0f} mm")
    for path in renders:
        print("RENDER", path)
    print("SAVED", BLEND)
    print("EXPORTED", RIGGED_FBX)
    print("===========================================\n")


main()
