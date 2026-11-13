"""Rebuild the original RetailCore storefront icon (developer dependency: Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

# Draw at 4x for smooth edges at every Windows icon size. No downloaded artwork.
scale = 4
image = Image.new('RGBA', (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
def rect(box, color, radius=0):
    box = tuple(v * scale for v in box)
    if radius:
        draw.rounded_rectangle(box, radius=radius * scale, fill=color)
    else:
        draw.rectangle(box, fill=color)
rect((4, 4, 252, 252), '#1D4ED8', 48)
rect((47, 104, 209, 208), '#FFFFFF', 8)
rect((64, 126, 121, 166), '#1D4ED8', 5)
rect((140, 126, 188, 208), '#1D4ED8', 5)
rect((151, 137, 177, 177), '#93C5FD', 3)
# Bold awning remains recognizable in the 16/20px launcher sizes.
draw.polygon([(v * scale, y * scale) for v, y in [(42, 101), (59, 62), (197, 62), (214, 101)]], fill='#FFFFFF')
for left in (42, 99, 157):
    rect((left, 95, left + 56, 116), '#FFFFFF', 8)
for left in (72, 130):
    rect((left, 95, left + 26, 112), '#93C5FD', 5)
rect((40, 207, 216, 218), '#FFFFFF', 5)
path = Path(__file__).resolve().parents[1] / 'src/RetailCore.Desktop/Assets/RetailCore.ico'
image.save(path, format='ICO', sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
