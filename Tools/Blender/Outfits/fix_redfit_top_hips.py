from pathlib import Path
import sys

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
PREVIEW_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/Previews/redfit_top_hips_fixed.png"
SAVE_RESULT = "--save" in sys.argv


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(corner[axis] for corner in corners) for axis in range(3))),
        Vector(tuple(max(corner[axis] for corner in corners) for axis in range(3))),
    )


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))

top = bpy.data.objects["RedFit_Top_XBot_Fitted"]
body = bpy.data.objects["Beta_Surface"]

bin_size = 0.02
body_widths = {}
for vertex in body.data.vertices:
    position = body.matrix_world @ vertex.co
    key = round(position.z / bin_size)
    body_widths[key] = max(body_widths.get(key, 0.0), abs(position.x))

width_vertices_changed = 0
depth_vertices_changed = 0
for vertex in top.data.vertices:
    z = vertex.co.z
    lower_blend = smoothstep((z - 0.72) / 0.12)
    upper_blend = 1.0 - smoothstep((z - 0.96) / 0.12)
    influence = lower_blend * upper_blend
    if influence <= 0.0:
        continue

    current_half_width = abs(vertex.co.x)
    side_influence = smoothstep((current_half_width - 0.11) / 0.09)
    if side_influence > 0.0:
        vertex.co.y *= 1.0 + 0.72 * influence * side_influence
        vertex.co.y -= 0.04 * influence * side_influence
        depth_vertices_changed += 1

    key = round(z / bin_size)
    body_half_width = max(body_widths.get(key + offset, 0.0) for offset in range(-2, 3))
    target_half_width = body_half_width + 0.025
    if current_half_width <= target_half_width:
        continue

    corrected_half_width = current_half_width + (target_half_width - current_half_width) * 0.92 * influence
    vertex.co.x = corrected_half_width if vertex.co.x > 0.0 else -corrected_half_width
    width_vertices_changed += 1

top.data.update()

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

if SAVE_RESULT:
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

minimum, maximum = world_bounds(top)
print(
    "REDFIT_HIPS_FIXED",
    f"width_vertices_changed={width_vertices_changed}",
    f"depth_vertices_changed={depth_vertices_changed}",
    f"bounds={tuple(round(value, 3) for value in minimum)}..{tuple(round(value, 3) for value in maximum)}",
    f"saved={SAVE_RESULT}",
)
