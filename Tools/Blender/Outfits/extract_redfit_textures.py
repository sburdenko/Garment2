from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLEND_PATH = PROJECT_ROOT / "Tools/Blender/Outfits/xbot_skirt_pants.blend"
OUTPUT_DIR = PROJECT_ROOT / "Assets/Garment/Models/RedFitSapphire/Versions/V1/Textures"


bpy.ops.wm.open_mainfile(filepath=str(BLEND_PATH))
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

scene = bpy.context.scene
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.image_settings.color_depth = "8"

textures = {
    "Image_0.002": "RedFit_Fabric.png",
    "Image_1.002": "RedFit_Flower.png",
    "Image_2.002": "RedFit_SleevePatch.png",
}

for image_name, filename in textures.items():
    image = bpy.data.images[image_name]
    output_path = OUTPUT_DIR / filename
    image.save_render(filepath=str(output_path), scene=scene)
    print("EXTRACTED_TEXTURE", image_name, output_path, tuple(image.size))
