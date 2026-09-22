"""Regenerate these synthetic test fixtures with Python 3 and Pillow; no remote assets."""
import json
from pathlib import Path

from PIL import GifImagePlugin, Image

ROOT = Path(__file__).parent
PALETTE = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0] + [0] * (256 * 3 - 15)


def indexed(size, color=0):
    image = Image.new("P", size, color)
    image.putpalette(PALETTE)
    return image


def gif(name, canvas_size, frames):
    # Explicit subrectangles ensure these exercise actual GIF delta/disposal handling.
    canvas = indexed(canvas_size)
    with (ROOT / name).open("wb") as output:
        for block in GifImagePlugin._get_global_header(canvas, {"transparency": 0, "loop": 0}):
            output.write(block)
        for frame, offset, duration, disposal in frames:
            GifImagePlugin._write_frame_data(output, frame, offset, {
                "duration": duration, "disposal": disposal, "transparency": 0
            })
        output.write(b";")


first = indexed((4, 4))
first.putpixel((0, 0), 1)
gif("disposal.gif", (4, 4), [
    (first, (0, 0), 100, 1),
    (indexed((1, 1), 2), (1, 0), 50, 3),
    (indexed((1, 1), 3), (2, 0), 200, 2),
    (indexed((1, 1), 4), (3, 0), 100, 1),
])

gif("timing.gif", (5, 1), [
    (indexed((1, 1), 1), (x, 0), duration, 2)
    for x, duration in enumerate([0, 10, 30, 30, 140])
])

sheet = Image.new("RGBA", (4, 2))
sheet.putpixel((0, 0), (255, 0, 0, 255))
sheet.putpixel((3, 1), (0, 255, 0, 255))
sheet.save(ROOT / "atlas.png")

frames = [
    {"filename": "0002.png", "frame": {"x": 3, "y": 1, "w": 1, "h": 1},
     "spriteSourceSize": {"x": 4, "y": 5, "w": 1, "h": 1}, "sourceSize": {"w": 8, "h": 8}},
    {"filename": "0001.png", "frame": {"x": 0, "y": 0, "w": 1, "h": 1},
     "spriteSourceSize": {"x": 1, "y": 2, "w": 1, "h": 1}, "sourceSize": {"w": 8, "h": 8}},
]
(ROOT / "atlas-array.json").write_text(json.dumps({"textures": [{"frames": frames}]}, indent=2) + "\n")
(ROOT / "atlas-hash.json").write_text(json.dumps({"frames": {
    frame["filename"]: {key: value for key, value in frame.items() if key != "filename"}
    for frame in frames
}}, indent=2) + "\n")
