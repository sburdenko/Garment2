import bpy, json
from mathutils import Vector

SP = "/private/tmp/claude-501/-Users-oleksandrburdenko-UP-Demo-Garment2/7bd7ecba-7193-471a-87d6-353b95c2a2c3/scratchpad"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=SP + "/shirt_fitted.obj", forward_axis='NEGATIVE_Z', up_axis='Y')
mesh_obj = [o for o in bpy.data.objects if o.type == 'MESH'][0]
mesh_obj.name = "shirt_skinned"
mesh_obj.data.materials.clear()
mesh_obj.data.materials.append(bpy.data.materials.new("shirt_Fabric"))

# Fold the oversized underarm panels into a natural armhole curve. The source
# mesh otherwise leaves a broad triangular flap below each raised sleeve.
adjusted = 0
for vertex in mesh_obj.data.vertices:
    x = abs(vertex.co.x)
    if x <= 0.232:
        continue
    t = min(max((x - 0.232) / (0.425 - 0.232), 0.0), 1.0)
    t = t * t * (3.0 - 2.0 * t)
    minimum_y = 1.08 + (1.32 - 1.08) * t
    if vertex.co.y < minimum_y:
        vertex.co.y = minimum_y
        adjusted += 1
print("UNDERARM_VERTICES", adjusted)

def to_bl(u):
    # OBJ import with -Z forward / Y up maps (x,y,z)_unity-obj -> (x,-z,y)_blender
    return Vector((u[0], -u[2], u[1]))

bones = json.load(open(SP + "/skeleton.json"))
children = {}
for b in bones:
    children.setdefault(b["parent"], []).append(b)

arm_data = bpy.data.armatures.new("MannequinRig")
arm_obj = bpy.data.objects.new("MannequinRig", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')

edit_bones = {}
for b in bones:
    eb = arm_data.edit_bones.new(b["name"])
    head = to_bl((b["x"], b["y"], b["z"]))
    kids = children.get(b["name"], [])
    if kids:
        tail = sum((to_bl((k["x"], k["y"], k["z"])) for k in kids), Vector()) / len(kids)
        if (tail - head).length < 0.01:
            tail = head + Vector((0, 0, 0.05))
    else:
        parent = next((p for p in bones if p["name"] == b["parent"]), None)
        direction = (head - to_bl((parent["x"], parent["y"], parent["z"]))).normalized() if parent else Vector((0, 0, 1))
        if direction.length < 1e-3: direction = Vector((0, 0, 1))
        tail = head + direction * 0.06
    eb.head, eb.tail = head, tail
    edit_bones[b["name"]] = eb
for b in bones:
    if b["parent"] and b["parent"] in edit_bones:
        edit_bones[b["name"]].parent = edit_bones[b["parent"]]

bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True)
arm_obj.select_set(True)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

# Smooth the weight boundaries (shoulder seams bulge otherwise) and cap influences at
# Unity's per-vertex limit of four so the engine doesn't truncate arbitrarily.
bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5, repeat=3, expand=0.5)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
bpy.ops.object.mode_set(mode='OBJECT')

groups = [g.name for g in mesh_obj.vertex_groups]
print("VGROUPS", len(groups), sorted(groups))
empty = sum(1 for v in mesh_obj.data.vertices if not v.groups)
print("UNWEIGHTED", empty, "of", len(mesh_obj.data.vertices))

bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True)
arm_obj.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=SP + "/shirt_skinned.fbx",
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=False,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL')
print("EXPORTED")
