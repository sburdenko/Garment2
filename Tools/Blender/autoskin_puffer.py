import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/PufferJacket/PufferJacket_Unrigged.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/PufferJacket/PufferJacket_Rigged.fbx"
TEXTURE_FILES = {
    "_diffuse_": "PufferJacket_BaseColor.png",
    "_displacement_": "PufferJacket_Height.png",
    "_metalness_": "PufferJacket_Metallic.png",
    "_normal_": "PufferJacket_Normal.png",
    "_roughness_": "PufferJacket_Roughness.png",
}
ARM_ANGLE = math.radians(35.0)
SHOULDER_LIFT = 0.0
CUFF_LIFT = 0.06
CUFF_EXTENSION = 0.015
BODY_WIDTH_SCALE = 1.06
BODY_LENGTH = 0.04
DEPTH_SCALE = 1.5
ARM_BONES = {
    "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "LeftHandEnd",
    "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "RightHandEnd",
}


def to_blender(position):
    return Vector((position["x"], -position["z"], position["y"]))


def arm_pose(position, side, shoulders):
    point = to_blender(position)
    if position["name"] not in ARM_BONES:
        return point

    pivot = shoulders[side]
    offset = point - pivot
    point.x = pivot.x + math.cos(ARM_ANGLE) * offset.x
    point.z = pivot.z - math.sin(ARM_ANGLE) * abs(offset.x)
    return point


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


def move_armature_to_t_pose(armature_object, bones, t_positions):
    bpy.context.view_layer.objects.active = armature_object
    bpy.ops.object.mode_set(mode="EDIT")
    children = {}
    for bone in bones:
        children.setdefault(bone["parent"], []).append(bone)

    for bone in bones:
        edit_bone = armature_object.data.edit_bones[bone["name"]]
        head = t_positions[bone["name"]]
        child_bones = children.get(bone["name"], [])
        if child_bones:
            tail = sum((t_positions[child["name"]] for child in child_bones), Vector()) / len(child_bones)
        else:
            parent = t_positions.get(bone["parent"])
            direction = (head - parent).normalized() if parent is not None else Vector((0, 0, 1))
            tail = head + direction * 0.06
        if (tail - head).length < 0.01:
            tail = head + Vector((0, 0, 0.05))
        edit_bone.head = head
        edit_bone.tail = tail

    bpy.ops.object.mode_set(mode="OBJECT")


def raise_weighted_sleeves(mesh_object, shoulders):
    group_indices = {
        side: {
            group.index
            for group in mesh_object.vertex_groups
            if group.name.startswith(side) and group.name in ARM_BONES
        }
        for side in ("Left", "Right")
    }

    moved = 0
    for vertex in mesh_object.data.vertices:
        side = "Left" if vertex.co.x >= 0 else "Right"
        influence = sum(
            assignment.weight
            for assignment in vertex.groups
            if assignment.group in group_indices[side]
        )
        influence = min(influence, 1.0)
        if influence <= 0.0:
            continue

        pivot = shoulders[side]
        offset = vertex.co - pivot
        angle = ARM_ANGLE if side == "Left" else -ARM_ANGLE
        rotated = Vector((
            math.cos(angle) * offset.x - math.sin(angle) * offset.z,
            offset.y,
            math.sin(angle) * offset.x + math.cos(angle) * offset.z,
        )) + pivot
        reach = smoothstep(0.20, 0.48, abs(vertex.co.x))
        lift = SHOULDER_LIFT + (CUFF_LIFT - SHOULDER_LIFT) * reach
        vertex.co = vertex.co.lerp(rotated, influence)
        vertex.co.z += lift * influence
        vertex.co.x += (CUFF_EXTENSION if side == "Left" else -CUFF_EXTENSION) * reach * influence
        moved += 1

    mesh_object.data.update()
    print("SLEEVE_VERTICES", moved)


def segment_distance(point, start, end):
    segment = end - start
    length_squared = segment.length_squared
    if length_squared <= 1e-8:
        return (point - start).length
    factor = max(0.0, min(1.0, (point - start).dot(segment) / length_squared))
    return (point - (start + segment * factor)).length


def smoothstep(edge0, edge1, value):
    factor = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return factor * factor * (3.0 - 2.0 * factor)


def normalized_inverse_distances(point, segments):
    weighted = []
    for name, start, end in segments:
        distance = segment_distance(point, start, end)
        weighted.append((name, 1.0 / ((distance + 0.015) ** 2)))
    total = sum(weight for _, weight in weighted)
    return [(name, weight / total) for name, weight in weighted]


def sleeve_vertex_mask(mesh_object):
    vertex_count = len(mesh_object.data.vertices)
    parent = list(range(vertex_count))

    def root(index):
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    def union(first, second):
        first_root = root(first)
        second_root = root(second)
        if first_root != second_root:
            parent[second_root] = first_root

    for edge in mesh_object.data.edges:
        union(edge.vertices[0], edge.vertices[1])

    components = {}
    for index in range(vertex_count):
        components.setdefault(root(index), []).append(index)

    mask = [False] * vertex_count
    sleeve_components = 0
    for indices in components.values():
        minimum_x = min(mesh_object.data.vertices[index].co.x for index in indices)
        maximum_x = max(mesh_object.data.vertices[index].co.x for index in indices)
        is_sleeve = ((minimum_x > 0.04 and maximum_x > 0.32) or
                     (maximum_x < -0.04 and minimum_x < -0.32))
        if not is_sleeve:
            continue
        sleeve_components += 1
        for index in indices:
            mask[index] = True

    print("SLEEVE_COMPONENTS", sleeve_components)
    return mask


def assign_distance_weights(mesh_object, positions):
    torso_segments = [
        ("Hips", positions["Hips"], positions["Spine"]),
        ("Spine", positions["Spine"], positions["Chest"]),
        ("Chest", positions["Chest"], positions["Neck"]),
        ("Neck", positions["Neck"], positions["Head"]),
    ]
    arm_segments = {
        "Left": [
            ("LeftShoulder", positions["LeftShoulder"], positions["LeftUpperArm"]),
            ("LeftUpperArm", positions["LeftUpperArm"], positions["LeftLowerArm"]),
            ("LeftLowerArm", positions["LeftLowerArm"], positions["LeftHand"]),
            ("LeftHand", positions["LeftHand"], positions["LeftHandEnd"]),
        ],
        "Right": [
            ("RightShoulder", positions["RightShoulder"], positions["RightUpperArm"]),
            ("RightUpperArm", positions["RightUpperArm"], positions["RightLowerArm"]),
            ("RightLowerArm", positions["RightLowerArm"], positions["RightHand"]),
            ("RightHand", positions["RightHand"], positions["RightHandEnd"]),
        ],
    }

    group_names = [name for name, _, _ in torso_segments]
    group_names.extend(name for side in ("Left", "Right") for name, _, _ in arm_segments[side])
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    mesh.verts.ensure_lookup_table()
    deform = mesh.verts.layers.deform.verify()
    sleeve_mask = sleeve_vertex_mask(mesh_object)

    arm_vertices = 0
    for vertex in mesh.verts:
        point = vertex.co
        side = "Left" if point.x >= 0 else "Right"
        lateral = smoothstep(0.12, 0.28, abs(point.x))
        shoulder = positions[f"{side}Shoulder"]
        sleeve_center_z = shoulder.z - math.tan(ARM_ANGLE) * (abs(point.x) - abs(shoulder.x))
        sleeve_lower_z = sleeve_center_z - 0.25
        vertical = smoothstep(sleeve_lower_z - 0.02, sleeve_lower_z + 0.04, point.z)
        arm_factor = lateral * vertical if sleeve_mask[vertex.index] else 0.0
        if arm_factor > 0.01:
            arm_vertices += 1

        weights = {}
        for name, weight in normalized_inverse_distances(point, torso_segments):
            weights[name] = weight * (1.0 - arm_factor)
        for name, weight in normalized_inverse_distances(point, arm_segments[side]):
            weights[name] = weight * arm_factor

        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _, weight in strongest)
        for name, weight in strongest:
            if weight > 0.0:
                vertex[deform][groups[name].index] = weight / total

    mesh.to_mesh(mesh_object.data)
    mesh.free()
    mesh_object.data.update()
    print("ARM_WEIGHTED_VERTICES", arm_vertices)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

for image in bpy.data.images:
    for token, filename in TEXTURE_FILES.items():
        if token in image.name.lower():
            image.name = Path(filename).stem
            image.filepath = str(SOURCE.parent / filename)
            break

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one Puffer mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "PufferJacket_Rigged"
mesh_object.data.name = "PufferJacket_Rigged"

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)

t_positions = {bone["name"]: to_blender(bone) for bone in bones}
shoulders = {
    "Left": t_positions["LeftShoulder"],
    "Right": t_positions["RightShoulder"],
}
a_positions = {}
for bone in bones:
    if bone["name"].startswith("Left"):
        side = "Left"
    elif bone["name"].startswith("Right"):
        side = "Right"
    else:
        side = ""
    a_positions[bone["name"]] = arm_pose(bone, side, shoulders) if side else to_blender(bone)

armature_object = create_armature(bones, a_positions, "MannequinRig")

assign_distance_weights(mesh_object, a_positions)
raise_weighted_sleeves(mesh_object, shoulders)
move_armature_to_t_pose(armature_object, bones, t_positions)

mesh_object.parent = armature_object
modifier = mesh_object.modifiers.new(name="Armature", type="ARMATURE")
modifier.object = armature_object

# GarmentBinder consumes the imported mesh data directly and replaces FBX bind poses
# with the live avatar's bind poses. Store vertices in Unity's Y-up coordinates so
# the FBX node rotation is not needed when the renderer is rebuilt at runtime.
mesh_object.data.transform(Matrix.Rotation(math.radians(-90.0), 4, "X"))
arm_group_indices = {
    group.index for group in mesh_object.vertex_groups if group.name in ARM_BONES
}
for vertex in mesh_object.data.vertices:
    arm_influence = min(sum(
        assignment.weight for assignment in vertex.groups
        if assignment.group in arm_group_indices
    ), 1.0)
    torso_influence = 1.0 - arm_influence
    vertex.co.x *= 1.0 + (BODY_WIDTH_SCALE - 1.0) * torso_influence
    hem = 1.0 - smoothstep(1.0, 1.25, vertex.co.y)
    vertex.co.y -= BODY_LENGTH * hem * torso_influence
    vertex.co.z *= DEPTH_SCALE

unweighted = sum(1 for vertex in mesh_object.data.vertices if not vertex.groups)
print("VERTICES", len(mesh_object.data.vertices))
print("GROUPS", len(mesh_object.vertex_groups), sorted(group.name for group in mesh_object.vertex_groups))
print("UNWEIGHTED", unweighted)
if unweighted:
    raise RuntimeError(f"Automatic weights left {unweighted} vertices unweighted")

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
