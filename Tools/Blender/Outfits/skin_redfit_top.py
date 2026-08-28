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

import bpy
from mathutils import Quaternion, Vector

PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SOURCE_FBX = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Original/RedFit_Top_Original.fbx"
PREVIEW_DIR = PROJECT_ROOT / "Tools/Blender/Outfits/Previews"
SKINNED_NAME = "RedFit_Top_Skinned"
BODY_NAME = "Beta_Surface"
RIG_NAME = "XBotRig"
CUFF_REACH = 0.50
SEED_REACH = 0.40
ARMHOLE = 0.16
SLEEVE_FULL = 0.26
# The kameez has side slits, so past the armhole the bodice stays wide all the way down and a
# width test alone lets the flood walk the side seam into the skirt. Distance from the sleeve's
# own axis separates them: the cuff sits on the axis, the side seam is a quarter metre off it.
SLEEVE_RADIUS = 0.20
SLEEVE_FADE = 0.06
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
        point = world[index]
        if sign * point.x <= ARMHOLE:
            return False
        offset = point - shoulder
        along = offset.dot(direction)
        return along > 0.0 and (offset - direction * along).length < SLEEVE_RADIUS
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
    """Rotates each sleeve onto the rig's horizontal arm, pivoting at the shoulder.

    The turn fades in across the armhole, so fabric at or inside ARMHOLE is left exactly where it
    was and nothing on the torso is disturbed.
    """
    report = {}
    for side, sign in (("Left", 1.0), ("Right", -1.0)):
        shoulder, direction = sleeve_direction(dress, rig, side, sign)
        region, world = sleeve_region(dress, sign, shoulder, direction)
        target = Vector((sign, 0.0, 0.0))
        turn = direction.rotation_difference(target)
        heights = [world[i].z for i in region]
        widths = [abs(world[i].x) for i in region]
        for index in region:
            offset = world[index] - shoulder
            off_axis = (offset - direction * offset.dot(direction)).length
            # Fade at both edges of the region: across the armhole, and out at the radius the
            # flood stopped at. A hard edge in either leaves a vertex flung away from neighbours
            # that stayed put, which shows up as a spike.
            share = (smoothstep(ARMHOLE, SLEEVE_FULL, abs(world[index].x))
                     * (1.0 - smoothstep(SLEEVE_RADIUS - SLEEVE_FADE, SLEEVE_RADIUS, off_axis)))
            if share <= 0.0:
                continue
            partial = Quaternion().slerp(turn, share)
            moved = shoulder + partial @ (world[index] - shoulder)
            dress.data.vertices[index].co = dress.matrix_world.inverted() @ moved
        report[side] = (len(region), min(heights), max(heights), max(widths), turn.angle)
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


def bind_to_rig(dress, rig):
    armature = dress.modifiers.new("XBot Armature", "ARMATURE")
    armature.object = rig
    dress.parent = rig
    dress.matrix_parent_inverse = rig.matrix_world.inverted()


def torso_check(dress, before):
    """The promise was that only the sleeves move; this counts what actually held still."""
    untouched = 0
    moved = 0
    worst = 0.0
    for index, vertex in enumerate(dress.data.vertices):
        shift = ((dress.matrix_world @ vertex.co) - before[index]).length
        if shift == 0.0:
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

    bind_to_rig(dress, rig)

    untouched, moved, worst = torso_check(dress, before)
    print(f"[проверка] не сдвинулось ни на микрон: {untouched} вершин")
    print(f"[проверка] сдвинуто {moved}, максимум {worst*1000:.1f} мм")

    render_previews(dress)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    print(f"[saved] {BLEND_PATH}")


if __name__ == "__main__":
    main()
