import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/BunnyHoodie/Source/Bunny Hoodie_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/BunnyHoodie/BunnyHoodie_Rigged.fbx"

# Same CLO avatar family as the sweater: the same drop seats it on the mannequin.
GLOBAL_DROP = 0.07
# A shell is a sleeve when it lives out past the shoulder and reaches the flare.
SLEEVE_MIN_INNER = 0.10
SLEEVE_MIN_OUTER = 0.26


def to_blender(position):
    return Vector((position["x"], -position["z"], position["y"]))


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


def smoothstep(edge0, edge1, value):
    factor = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return factor * factor * (3.0 - 2.0 * factor)


def segment_distance(point, start, end):
    span = end - start
    length_squared = span.length_squared
    if length_squared < 1e-9:
        return (point - start).length
    t = max(0.0, min(1.0, (point - start).dot(span) / length_squared))
    return (point - (start + span * t)).length


def normalized_inverse_distances(point, segments):
    weighted = []
    for name, start, end in segments:
        distance = segment_distance(point, start, end)
        weighted.append((name, 1.0 / ((distance + 0.015) ** 2)))
    total = sum(weight for _, weight in weighted)
    return [(name, weight / total) for name, weight in weighted]


def connected_components(mesh_object):
    parent = list(range(len(mesh_object.data.vertices)))

    def find(index):
        while parent[index] != index:
            parent[index] = parent[parent[index]]
            index = parent[index]
        return index

    for edge in mesh_object.data.edges:
        first, second = find(edge.vertices[0]), find(edge.vertices[1])
        if first != second:
            parent[second] = first

    components = {}
    for index in range(len(mesh_object.data.vertices)):
        components.setdefault(find(index), []).append(index)
    return list(components.values())


def sleeve_shells(mesh_object):
    """Vertex indices of each sleeve, keyed by side. The knit is built from many
    separate shells; a sleeve is any shell living out past the shoulder."""
    shells = {"Left": [], "Right": []}
    for indices in connected_components(mesh_object):
        xs = [mesh_object.data.vertices[index].co.x for index in indices]
        inner = min(abs(x) for x in xs)
        outer = max(abs(x) for x in xs)
        if inner < SLEEVE_MIN_INNER or outer < SLEEVE_MIN_OUTER:
            continue
        side = "Left" if sum(xs) >= 0.0 else "Right"
        shells[side].extend(indices)
    return shells


def raise_sleeves(mesh_object, shells, shoulders):
    """One rigid turn per side: the sleeve's armhole-to-cuff axis onto horizontal.

    The flare drapes almost vertically (~78 deg) off CLO's relaxed arms; the
    tracked bind is a T-pose, so the tube must run level along the arm. Rigid,
    never blended — a blend shears a shell (the vest's caps proved it again).
    """
    for side, indices in shells.items():
        if not indices:
            raise RuntimeError(f"No sleeve shell found on the {side} side")
        pivot = shoulders[side]
        verts = [mesh_object.data.vertices[index] for index in indices]

        by_distance = sorted(verts, key=lambda v: (Vector((abs(v.co.x), v.co.y, v.co.z))
                                                   - Vector((abs(pivot.x), pivot.y, pivot.z))).length)
        count = max(1, len(by_distance) // 10)
        near = sum((Vector((abs(v.co.x), v.co.y, v.co.z)) for v in by_distance[:count]), Vector()) / count
        far = sum((Vector((abs(v.co.x), v.co.y, v.co.z)) for v in by_distance[-count:]), Vector()) / count

        axis = far - near
        droop = math.atan2(-axis.z, axis.x)
        print(f"SLEEVE_{side.upper()}_DROOP_DEG", round(math.degrees(droop), 1))

        # Turn about the ARMHOLE centre, not the shoulder joint: an 80-degree turn
        # about the joint sweeps the whole armhole ring up over the yoke and the
        # sleeves end up crossed at the collar. About its own ring, the sleeve
        # stays sewn where it is and only the tube swings level.
        cos_a, sin_a = math.cos(droop), math.sin(droop)
        for vertex in verts:
            dx = abs(vertex.co.x) - near.x
            dz = vertex.co.z - near.z
            rx = dx * cos_a - dz * sin_a
            rz = dx * sin_a + dz * cos_a
            vertex.co = Vector((math.copysign(near.x + rx, vertex.co.x), vertex.co.y, near.z + rz))
    mesh_object.data.update()


def assign_weights(mesh_object, positions, shells):
    torso_segments = [
        ("Hips", positions["Hips"], positions["Spine"]),
        ("Spine", positions["Spine"], positions["Chest"]),
        ("Chest", positions["Chest"], positions["Neck"]),
        ("Neck", positions["Neck"], positions["Head"]),
    ]
    arm_segments = {
        side: [
            (f"{side}Shoulder", positions[f"{side}Shoulder"], positions[f"{side}UpperArm"]),
            (f"{side}UpperArm", positions[f"{side}UpperArm"], positions[f"{side}LowerArm"]),
            (f"{side}LowerArm", positions[f"{side}LowerArm"], positions[f"{side}Hand"]),
        ]
        for side in ("Left", "Right")
    }

    group_names = [name for name, _, _ in torso_segments]
    for side in ("Left", "Right"):
        group_names.extend(name for name, _, _ in arm_segments[side])
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    sleeve_of = {}
    for side, indices in shells.items():
        for index in indices:
            sleeve_of[index] = side

    for vertex in mesh_object.data.vertices:
        side = sleeve_of.get(vertex.index)
        segments = arm_segments[side] if side else torso_segments
        weights = sorted(normalized_inverse_distances(vertex.co, segments),
                         key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _, weight in weights)
        for name, weight in weights:
            if weight > 0.0:
                groups[name].add([vertex.index], weight / total, "REPLACE")
    mesh_object.data.update()


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one hoodie mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "BunnyHoodie_Rigged"
mesh_object.data.name = "BunnyHoodie_Rigged"

for vertex in mesh_object.data.vertices:
    vertex.co.z -= GLOBAL_DROP
mesh_object.data.update()

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)
positions = {bone["name"]: to_blender(bone) for bone in bones}

shells = sleeve_shells(mesh_object)
print("SLEEVE_VERTS", {side: len(indices) for side, indices in shells.items()})
raise_sleeves(mesh_object, shells,
              {"Left": positions["LeftShoulder"], "Right": positions["RightShoulder"]})

armature_object = create_armature(bones, positions, "MannequinRig")
assign_weights(mesh_object, positions, shells)

mesh_object.parent = armature_object
modifier = mesh_object.modifiers.new(name="Armature", type="ARMATURE")
modifier.object = armature_object

# GarmentBinder consumes vertices directly and replaces FBX bind poses with the
# live avatar's bind poses, so store mesh data in Unity's Y-up coordinates.
mesh_object.data.transform(Matrix.Rotation(math.radians(-90.0), 4, "X"))

unweighted = sum(1 for vertex in mesh_object.data.vertices if not vertex.groups)
print("VERTICES", len(mesh_object.data.vertices))
print("UNWEIGHTED", unweighted)
if unweighted:
    raise RuntimeError(f"Hoodie skinning left {unweighted} vertices unweighted")

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
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
