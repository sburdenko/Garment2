import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "Assets/Garment/Models/DownVest/Source/1_fbx_thick.fbx"
SKELETON = PROJECT_ROOT / "Tools/Blender/skeleton.json"
OUTPUT = PROJECT_ROOT / "Assets/Garment/Models/DownVest/DownVest_Rigged.fbx"

# CLO exported the vest together with its avatar. The garment's materials carry
# fabric/trim names; the avatar's are anonymous "MaterialXXXX" — that is the split.
GARMENT_MATERIAL_PREFIXES = ("Default Fabric", "Knit_Terry", "FABRIC", "Trim_Hardware")
# Measured against the mannequin (shoulder 1.394, head base 1.57): the garment's
# shoulder seam sits at 1.42, and its funnel collar ran to 1.70 — over the face.
# The puffer's collar ends near 1.55 and reads right, so this one gets the same.
# The whole garment rode above the shoulders; it comes down in one piece.
GLOBAL_DROP = 0.07
COLLAR_BASE = 1.46
COLLAR_TOP = 1.54
SLEEVE_BLEND_INNER = 0.17
SLEEVE_BLEND_OUTER = 0.20


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


def strip_avatar(mesh_object):
    garment_slots = set()
    for index, slot in enumerate(mesh_object.material_slots):
        name = slot.material.name if slot.material else ""
        if name.startswith(GARMENT_MATERIAL_PREFIXES):
            garment_slots.add(index)
    if not garment_slots:
        raise RuntimeError("No garment materials recognised")

    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    doomed = [face for face in mesh.faces if face.material_index not in garment_slots]
    bmesh.ops.delete(mesh, geom=doomed, context="FACES")
    lonely = [vertex for vertex in mesh.verts if not vertex.link_faces]
    bmesh.ops.delete(mesh, geom=lonely, context="VERTS")
    mesh.to_mesh(mesh_object.data)
    mesh.free()

    # drop the now-empty avatar slots so submesh order is garment-only
    bpy.context.view_layer.objects.active = mesh_object
    for index in reversed(range(len(mesh_object.material_slots))):
        if index not in garment_slots:
            mesh_object.active_material_index = index
            bpy.ops.object.material_slot_remove()
    mesh_object.data.update()


def fit_collar(mesh_object):
    source_top = max(vertex.co.z for vertex in mesh_object.data.vertices)
    compression = (COLLAR_TOP - COLLAR_BASE) / (source_top - COLLAR_BASE)
    for vertex in mesh_object.data.vertices:
        if vertex.co.z > COLLAR_BASE:
            vertex.co.z = COLLAR_BASE + (vertex.co.z - COLLAR_BASE) * compression
    mesh_object.data.update()


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


def measured_cap_droop(mesh_object, sleeve_mask):
    """Slope of the cap sleeves' upper edge, radians below horizontal.

    Measured from the mesh AFTER collar shaping and the global drop, because both
    move the very vertices the angle is read from — a constant tuned on the raw
    export would rectify by the wrong amount.
    """
    # The FULL cap, yoke included. Restricting to the outer bands reads a steeper
    # slope (34 deg) and overshoots — the caps end up pointing above the shoulders
    # like epaulettes. The upper edge across the whole cap (12.4 deg) sits sleeves
    # level along a T-pose arm; the leftover visual droop is the fabric's own drape.
    bands = {}
    for vertex in mesh_object.data.vertices:
        if not sleeve_mask[vertex.index]:
            continue
        band = round(abs(vertex.co.x) * 50.0) / 50.0
        bands.setdefault(band, []).append(vertex.co.z)

    points = []
    for band, zs in sorted(bands.items()):
        zs.sort()
        top = zs[int(len(zs) * 0.9):]
        points.append((band, sum(top) / len(top)))
    if len(points) < 3:
        raise RuntimeError("Not enough sleeve bands to measure the cap angle")

    n = len(points)
    mean_x = sum(x for x, _ in points) / n
    mean_z = sum(z for _, z in points) / n
    slope = (sum((x - mean_x) * (z - mean_z) for x, z in points)
             / sum((x - mean_x) ** 2 for x, _ in points))
    return math.atan(-slope)


def raise_cap_sleeves(mesh_object, shoulders, sleeve_mask):
    """One rigid turn of each cap about its shoulder, droop up to horizontal.

    The tracked T-pose holds the arms level, so the bind mesh must too — the same
    rectification the puffer needs, scaled down to caps. Blended by each vertex's
    own arm weight so the armhole seam stretches instead of tearing.
    """
    droop = measured_cap_droop(mesh_object, sleeve_mask)
    print("CAP_DROOP_DEG", round(math.degrees(droop), 1))

    # One RIGID turn of each whole shell — a blend rotates half a cap and shears
    # it into a torn-looking flap (measured mistake, and the puffer's sleeves hit
    # the same trap once: per-vertex rectification shreds, rigid keeps the tube).
    moved = 0
    cos_a, sin_a = math.cos(droop), math.sin(droop)
    for vertex in mesh_object.data.vertices:
        if not sleeve_mask[vertex.index]:
            continue
        pivot = shoulders["Left" if vertex.co.x >= 0.0 else "Right"]
        dx = abs(vertex.co.x) - abs(pivot.x)
        dz = vertex.co.z - pivot.z
        rx = dx * cos_a - dz * sin_a
        rz = dx * sin_a + dz * cos_a
        vertex.co = Vector((math.copysign(abs(pivot.x) + rx, vertex.co.x), vertex.co.y, pivot.z + rz))
        moved += 1
    mesh_object.data.update()
    print("CAP_VERTICES_RAISED", moved)


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
        is_sleeve = ((minimum_x > 0.10 and maximum_x > 0.30) or
                     (maximum_x < -0.10 and minimum_x < -0.30))
        if not is_sleeve:
            continue
        sleeve_components += 1
        for index in indices:
            mask[index] = True

    print("SLEEVE_COMPONENTS", sleeve_components)
    return mask


def assign_weights(mesh_object, positions):
    torso_segments = [
        ("Hips", positions["Hips"], positions["Spine"]),
        ("Spine", positions["Spine"], positions["Chest"]),
        ("Chest", positions["Chest"], positions["Neck"]),
        ("Neck", positions["Neck"], positions["Head"]),
    ]
    group_names = [name for name, _, _ in torso_segments] + ["LeftUpperArm", "RightUpperArm"]
    groups = {name: mesh_object.vertex_groups.new(name=name) for name in group_names}

    mesh = bmesh.new()
    mesh.from_mesh(mesh_object.data)
    mesh.verts.ensure_lookup_table()
    deform = mesh.verts.layers.deform.verify()
    sleeve_mask = sleeve_vertex_mask(mesh_object)

    for vertex in mesh.verts:
        side = "Left" if vertex.co.x >= 0.0 else "Right"
        if sleeve_mask[vertex.index]:
            # The caps are separate shells, not fabric continuous with the body:
            # there is no seam to protect, and a cap must always point down the
            # arm. Whole shell on the arm bone — any partial blend shears it.
            vertex[deform][groups[f"{side}UpperArm"].index] = 1.0
            continue

        weights = dict(normalized_inverse_distances(vertex.co, torso_segments))
        strongest = sorted(weights.items(), key=lambda item: item[1], reverse=True)[:4]
        total = sum(weight for _, weight in strongest)
        for name, weight in strongest:
            if weight > 0.0:
                vertex[deform][groups[name].index] = weight / total

    mesh.to_mesh(mesh_object.data)
    mesh.free()
    mesh_object.data.update()


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one mesh, found {len(mesh_objects)}")

mesh_object = mesh_objects[0]
mesh_object.data.transform(mesh_object.matrix_world)
mesh_object.matrix_world.identity()
mesh_object.name = "DownVest_Rigged"
mesh_object.data.name = "DownVest_Rigged"

strip_avatar(mesh_object)
fit_collar(mesh_object)
for vertex in mesh_object.data.vertices:
    vertex.co.z -= GLOBAL_DROP
mesh_object.data.update()
zs = [v.co.z for v in mesh_object.data.vertices]
print("GARMENT_Z", round(min(zs), 3), "..", round(max(zs), 3))
print("MATERIALS", [slot.material.name if slot.material else "?" for slot in mesh_object.material_slots])

with SKELETON.open(encoding="utf-8") as stream:
    bones = json.load(stream)

positions = {bone["name"]: to_blender(bone) for bone in bones}
armature_object = create_armature(bones, positions, "MannequinRig")
assign_weights(mesh_object, positions)
raise_cap_sleeves(mesh_object, {"Left": positions["LeftShoulder"], "Right": positions["RightShoulder"]},
                  sleeve_vertex_mask(mesh_object))

mesh_object.parent = armature_object
modifier = mesh_object.modifiers.new(name="Armature", type="ARMATURE")
modifier.object = armature_object

# GarmentBinder consumes vertices directly and replaces FBX bind poses with the
# live avatar's bind poses, so store mesh data in Unity's Y-up coordinates.
mesh_object.data.transform(Matrix.Rotation(math.radians(-90.0), 4, "X"))

unweighted = sum(1 for vertex in mesh_object.data.vertices if not vertex.groups)
print("VERTICES", len(mesh_object.data.vertices))
print("GROUPS", len(mesh_object.vertex_groups), sorted(group.name for group in mesh_object.vertex_groups))
print("UNWEIGHTED", unweighted)
if unweighted:
    raise RuntimeError(f"Vest skinning left {unweighted} vertices unweighted")

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
