"""Binds the merged RedFit Sapphire top to the XBot rig and brings its sleeves into T-pose.

The garment is exported from CLO3D with the arms down about 51 degrees, while the rig rests in
T-pose. Rather than pushing sleeve vertices around by hand, the rig is posed to match the garment,
weights are transferred from the body in that pose, and the mesh is then carried back to the rest
pose through its own skinning, inverted. Vertices weighted to bones that were never posed — the
whole torso and skirt — come through the inversion untouched, exactly rather than approximately.

Run: blender xbot_skirt_pants.blend --background --python skin_redfit_top.py
Nothing is exported; the blend is saved for inspection.
"""

from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SOURCE_FBX = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Original/RedFit_Top_Original.fbx"
PREVIEW_DIR = PROJECT_ROOT / "Tools/Blender/Outfits/Previews"
CLOTH_SPLIT_DIR = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/ClothSplit"
SKINNED_NAME = "RedFit_Top_Skinned"
BODY_NAME = "Beta_Surface"
RIG_NAME = "XBotRig"
CUFF_REACH = 0.50
SEED_REACH = 0.40
ARMHOLE = 0.20
SLEEVE_FULL = 0.28
# The kameez has side slits, so past the armhole the bodice stays wide all the way down and a
# width test alone lets the flood walk the side seam into the skirt. Distance from the sleeve's
# own axis separates them: the cuff sits on the axis, the side seam is a quarter metre off it.
# How far below the sleeve's top seam the arm ran in the pose the garment was cut for: the
# mannequin's upper arm measures 0.055 in radius, plus a little ease.
ARM_CLEARANCE = 0.06
AXIS_SPAN = (0.24, 0.44)
# The garment was cut on a 1.755m avatar; on XBot its shoulder seam lands 9cm below the shoulder.
LIFT = 0.12
# "hang" keeps the cross section upright against gravity, so the bell still falls downward but
# fabric under the armhole stays behind as a curtain. "rigid" turns the sleeve whole, which
# clears the armpit but swings the bell out sideways.
SLEEVE_MODE = "hang"
# When the arm rises the armhole takes up the fabric that hung under it, so the cross section is
# pulled in towards the axis near the shoulder and released to its full bell out at the cuff.
TUCK_AT_SHOULDER = 0.35
TUCK_REACH = 0.30 
OTHER_GARMENTS = ("PufferJacket_ArmsOnly", "Skirt_XBot_Rigged", "PufferPants_XBot_Rigged",
                  "RedFit_Dress_V1_Skinned", "RedFit_Top_XBot_Fitted")


def reset_rig_to_rest(rig):
    """The blend keeps a walking preview action on the rig; skinning has to see the T-pose."""
    previous = None
    if rig.animation_data and rig.animation_data.action:
        previous = rig.animation_data.action.name
        rig.animation_data.action = None
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.transforms_clear()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    hand = rig.matrix_world @ rig.pose.bones["mixamorig:LeftHand"].head
    print(f"[rest] снят экшен {previous}; кисть в ({hand.x:.3f},{hand.y:.3f},{hand.z:.3f})")
    return previous


def replace_previous():
    existing = bpy.data.objects.get(SKINNED_NAME)
    if existing:
        bpy.data.objects.remove(existing, do_unlink=True)


def import_dress():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE_FBX))
    imported = [o for o in bpy.data.objects if o not in before]
    dress = next(o for o in imported if o.type == "MESH")
    for extra in imported:
        if extra is not dress and extra.type == "EMPTY":
            bpy.data.objects.remove(extra, do_unlink=True)
    dress.name = SKINNED_NAME
    dress.data.name = SKINNED_NAME
    dress.parent = None
    select_only(dress)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for vertex in dress.data.vertices:
        vertex.co.z += LIFT
    dress.data.update()
    return dress


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def sleeve_direction(dress, rig, side, sign):
    """Where the sleeve actually points: from the rig's shoulder to the centre of the cuff."""
    shoulder = rig.matrix_world @ rig.data.bones[f"mixamorig:{side}Arm"].head_local
    cuff = [dress.matrix_world @ v.co for v in dress.data.vertices
            if sign * (dress.matrix_world @ v.co).x > CUFF_REACH]
    if not cuff:
        raise RuntimeError(f"{side}: no cuff vertices past {CUFF_REACH}")
    centre = sum(cuff, Vector()) / len(cuff)
    return shoulder, (centre - shoulder).normalized()


def sleeve_axis(region, world, sign, shoulder_x):
    """Where the arm ran inside the sleeve: the top seam, dropped by the arm's own radius.

    Anchoring on the rig's shoulder joint instead puts the axis a couple of centimetres under
    the seam, which is the fabric surface rather than the arm, and the arm ends up lying on top
    of the sleeve instead of inside it.
    """
    seam = {}
    for index in region:
        point = world[index]
        step = round(abs(point.x), 2)
        if not AXIS_SPAN[0] <= step <= AXIS_SPAN[1]:
            continue
        if step not in seam or point.z > seam[step].z:
            seam[step] = point
    steps = sorted(seam)
    if len(steps) < 2:
        raise RuntimeError("sleeve too short to fit an axis")

    first, last = seam[steps[0]], seam[steps[-1]]
    direction = (last - first).normalized()
    up = Vector((0.0, 0.0, 1.0))
    up_perp = (up - direction * up.dot(direction)).normalized()
    reach = (sign * shoulder_x - first.x) / direction.x
    anchor = first + direction * reach - up_perp * ARM_CLEARANCE
    return anchor, direction


def sleeve_region(dress, sign, shoulder, direction):
    """The sleeve, found by flooding out from the cuff and stopping at the armhole.

    A width test alone cannot do it: low down the skirt flares wider than the armhole, and the
    hanging bell of the sleeve reaches into the same heights. The two only ever meet through the
    bodice, so a flood that refuses to cross ARMHOLE separates them exactly.
    """
    mesh = dress.data
    world = [dress.matrix_world @ v.co for v in mesh.vertices]
    neighbours = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)

    def inside(index):
        return sign * world[index].x > ARMHOLE
    region = set(i for i in range(len(world)) if sign * world[i].x > SEED_REACH)
    stack = list(region)
    while stack:
        current = stack.pop()
        for other in neighbours[current]:
            if other in region or not inside(other):
                continue
            region.add(other)
            stack.append(other)
    return region, world


def lift_sleeves(dress, rig):
    """Carries each sleeve onto the rig's arm without rotating the fabric around it.

    Turning the whole sleeve rigidly also turns its cross section, so cloth that hung under the
    arm swings out sideways into a wing and leaves the arm outside the tube. Only the length
    along the sleeve's axis is rotated onto the arm; the cross section is rebuilt in cylindrical
    coordinates measured against world up, so fabric that hung below the arm still hangs below
    it and the arm stays as deep inside the tube as it was in the pose the garment was cut for.
    """
    up = Vector((0.0, 0.0, 1.0))
    report = {}
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        shoulder, rough = sleeve_direction(dress, rig, side, sign)
        region, world = sleeve_region(dress, sign, shoulder, rough)
        anchor, direction = sleeve_axis(region, world, sign, abs(shoulder.x))
        target = Vector((sign, 0.0, 0.0))

        was_up = (up - direction * up.dot(direction)).normalized()
        was_side = direction.cross(was_up)
        now_up = (up - target * up.dot(target)).normalized()
        now_side = target.cross(now_up)
        turn = direction.rotation_difference(target)

        for index in region:
            offset = world[index] - anchor
            along = offset.dot(direction)
            across = offset - direction * along
            # Zero exactly at the armhole, which is the edge of the flooded region, so the
            # transform is continuous across the seam and nothing can tear away from a
            # neighbour that stayed put.
            share = smoothstep(ARMHOLE, SLEEVE_FULL, abs(world[index].x))
            if share <= 0.0:
                continue
            if SLEEVE_MODE == "rigid":
                rebuilt = shoulder + turn @ offset
            else:
                depth = across.dot(was_up)
                if depth < 0.0:
                    depth *= TUCK_AT_SHOULDER + (1.0 - TUCK_AT_SHOULDER) * smoothstep(
                        0.0, TUCK_REACH, along)
                rebuilt = (shoulder + target * along
                           + now_up * depth + now_side * across.dot(was_side))
            dress.data.vertices[index].co = (
                dress.matrix_world.inverted() @ world[index].lerp(rebuilt, share))

        heights = [world[i].z for i in region]
        report[side] = (len(region), min(heights), max(heights),
                        max(abs(world[i].x) for i in region), direction.angle(target))
    dress.data.update()
    return report


def smoothstep(edge_a, edge_b, value):
    value = max(0.0, min(1.0, (value - edge_a) / (edge_b - edge_a)))
    return value * value * (3.0 - 2.0 * value)


def transfer_body_weights(dress, body):
    """The Data Transfer modifier samples the evaluated source, so this reads the posed body."""
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
    select_only(dress)
    bpy.ops.object.modifier_apply(modifier=transfer.name)


def export_cloth_split(dress):
    """The cloth lab hangs the garment with no body under it, and takes it in two pieces.

    Fabric goes to the solver; the embroidery and buttons ride its surface. Both come off the
    finished garment, before it moves into the rig's space, so the lab sees the same geometry the
    mannequin wears — in metres, which is what the lab's distances are written in.
    """
    pieces = (("RedFit_Top_Fabric", {"RedFit_Fabric"}),
              ("RedFit_Top_Trim", {"RedFit_Button", "RedFit_Flower", "RedFit_SleevePatch"}))
    for name, keep in pieces:
        piece = dress.copy()
        piece.data = dress.data.copy()
        piece.name = name
        piece.data.name = name
        bpy.context.collection.objects.link(piece)

        mesh = bmesh.new()
        mesh.from_mesh(piece.data)
        drop = [f for f in mesh.faces if piece.data.materials[f.material_index].name not in keep]
        bmesh.ops.delete(mesh, geom=drop, context="FACES_ONLY")
        mesh.to_mesh(piece.data)
        mesh.free()

        select_only(piece)
        bpy.ops.object.material_slot_remove_unused()
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.delete_loose()
        bpy.ops.object.mode_set(mode="OBJECT")

        CLOTH_SPLIT_DIR.mkdir(parents=True, exist_ok=True)
        path = CLOTH_SPLIT_DIR / f"{name}.fbx"
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, apply_unit_scale=True,
                                 global_scale=1.0, axis_forward="-Z", axis_up="Y",
                                 object_types={"MESH"}, mesh_smooth_type="FACE",
                                 use_mesh_modifiers=True, bake_space_transform=True,
                                 apply_scale_options="FBX_SCALE_ALL")
        print(f"[split] {name}: {len(piece.data.vertices)} верш, {len(piece.data.polygons)} тр, "
              f"слоты {[m.name for m in piece.data.materials]}")
        bpy.data.objects.remove(piece, do_unlink=True)


def bind_to_rig(dress, rig):
    """Leaves the mesh in the rig's own space, where the body and the other garments live.

    The wardrobe binds a pre-skinned garment by swapping the body's bind poses onto it and
    dropping the renderer at the body's origin, so it reads the vertex data and ignores whatever
    transform the model's own node carried. Exporting the mesh in metres and Z-up, with the
    conversion parked on that node, would lay the garment on its side once rebound.
    """
    dress.data.transform(rig.matrix_world.inverted() @ dress.matrix_world)
    dress.parent = rig
    dress.matrix_parent_inverse = Matrix.Identity(4)
    dress.matrix_basis = Matrix.Identity(4)

    armature = dress.modifiers.new("XBot Armature", "ARMATURE")
    armature.object = rig
    bpy.context.view_layer.update()


def torso_check(dress, before):
    """The promise was that only the sleeves move; this counts what actually held still."""
    untouched = 0
    moved = 0
    worst = 0.0
    for index, vertex in enumerate(dress.data.vertices):
        shift = ((dress.matrix_world @ vertex.co) - before[index]).length
        if shift < 1e-6:
            untouched += 1
        else:
            moved += 1
            worst = max(worst, shift)
    return untouched, moved, worst


def render_previews(dress):
    for name in OTHER_GARMENTS:
        obj = bpy.data.objects.get(name)
        if obj:
            obj.hide_render = True
            obj.hide_viewport = True
    body = bpy.data.objects[BODY_NAME]
    for obj in (dress, body):
        obj.hide_render = False
        obj.hide_viewport = False

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    camera = bpy.data.objects["Camera"]
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.0
    scene.camera = camera

    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    shots = {
        "redfit_top_skinned_front": ((0.0, -4.0, 1.0), (0.0, 0.0, 1.0)),
        "redfit_top_skinned_side": ((4.0, 0.0, 1.0), (0.0, 0.0, 1.0)),
        "redfit_top_skinned_threequarter": ((2.8, -2.8, 1.3), (0.0, 0.0, 1.0)),
        "redfit_top_skinned_back": ((0.0, 4.0, 1.0), (0.0, 0.0, 1.0)),
    }
    for name, (location, target) in shots.items():
        camera.location = location
        camera.rotation_euler = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(PREVIEW_DIR / name)
        bpy.ops.render.render(write_still=True)
        print(f"[preview] {name}.png")


def drop_superseded():
    """The V1 dress was skinned from the unmerged triangle-soup export; this replaces it."""
    for name in ("RedFit_Dress_V1_Skinned", "RedFit_Top_XBot_Fitted"):
        obj = bpy.data.objects.get(name)
        if obj:
            bpy.data.objects.remove(obj, do_unlink=True)
            print(f"[removed] {name}")


def main():
    import math
    rig = bpy.data.objects[RIG_NAME]
    body = bpy.data.objects[BODY_NAME]
    reset_rig_to_rest(rig)
    drop_superseded()
    replace_previous()
    dress = import_dress()
    print(f"[mesh] {len(dress.data.vertices)} верш, {len(dress.data.polygons)} тр, "
          f"материалов {len(dress.data.materials)}")

    before = [dress.matrix_world @ v.co for v in dress.data.vertices]

    for side, (count, low, high, reach, angle) in lift_sleeves(dress, rig).items():
        print(f"[sleeve] {side}: {count} вершин, было z {low:.3f}..{high:.3f}, "
              f"|x| до {reach:.3f}, поворот {math.degrees(angle):.1f}°")

    transfer_body_weights(dress, body)
    weighted = sum(1 for v in dress.data.vertices if any(g.weight > 1e-4 for g in v.groups))
    print(f"[weights] с весами {weighted} из {len(dress.data.vertices)}")

    untouched, moved, worst = torso_check(dress, before)
    export_cloth_split(dress)
    bind_to_rig(dress, rig)

    print(f"[проверка] не сдвинулось ни на микрон: {untouched} вершин")
    print(f"[проверка] сдвинуто {moved}, максимум {worst*1000:.1f} мм")

    render_previews(dress)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    print(f"[saved] {BLEND_PATH}")


if __name__ == "__main__":
    main()
