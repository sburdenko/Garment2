from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
SOURCE_GLB = Path("/Users/oleksandrburdenko/Downloads/ClothSamples/redfitsapphire.glb")
GENERATED_COLLECTION = "RedFit Sapphire"
ORIGINAL_COLLECTION = "RedFit Sapphire Original"


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))

for collection_name in (GENERATED_COLLECTION, ORIGINAL_COLLECTION):
    collection = bpy.data.collections.get(collection_name)
    if collection is None:
        continue
    for obj in list(collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(collection)

objects_before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(SOURCE_GLB))
imported_objects = [obj for obj in bpy.data.objects if obj not in objects_before]

source_collection = bpy.data.collections.new(ORIGINAL_COLLECTION)
bpy.context.scene.collection.children.link(source_collection)

for obj in imported_objects:
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    source_collection.objects.link(obj)
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.hide_render = False

bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

print(
    "REDFIT_ORIGINAL",
    [(obj.name, obj.type, tuple(round(value, 6) for value in obj.location),
      tuple(round(value, 6) for value in obj.rotation_euler),
      tuple(round(value, 6) for value in obj.scale)) for obj in imported_objects],
)
