"""Procedural tileable textures for the city models and OSM districts.

Run: python3 AssetSources/Textures/make_textures.py
Writes PNGs to PeninsulaTimeUnity/Assets/Resources/Textures/City.
Detail textures are near-white so Unity tints them with each material colour;
facade, road, paving and flat-roof textures carry their own colours.
One texture repeat = 2 m on Blender city models (see make_city_models.py).
"""
import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "PeninsulaTimeUnity", "Assets", "Resources", "Textures", "City")
os.makedirs(OUT, exist_ok=True)
RNG = np.random.default_rng(7)
SIZE = 256


def noise(scale=8, amount=.12):
    """Tileable value noise centred a little below 1.0."""
    grid = RNG.random((scale, scale))
    tiled = np.tile(grid, (3, 3))
    img = Image.fromarray((tiled * 255).astype(np.uint8)).resize((SIZE * 3, SIZE * 3), Image.BICUBIC)
    a = np.asarray(img, dtype=np.float32)[SIZE:SIZE * 2, SIZE:SIZE * 2] / 255
    fine = RNG.random((SIZE, SIZE))
    return 1 - amount + amount * (a * .7 + fine * .3)


def save(name, arr):
    arr = np.clip(arr, 0, 1)
    if arr.ndim == 2:
        arr = np.stack([arr] * 3, -1)
    Image.fromarray((arr * 255).astype(np.uint8)).save(os.path.join(OUT, name + ".png"))


def grey_draw(fill):
    img = Image.new("L", (SIZE, SIZE), fill)
    return img, ImageDraw.Draw(img)


def blocks(rows, cols, mortar=165, jitter=20, gap=2):
    img, d = grey_draw(mortar)
    h, w = SIZE // rows, SIZE // cols
    for r in range(rows):
        off = (w // 2) * (r % 2)
        for c in range(-1, cols + 1):
            shade = int(228 + RNG.integers(-jitter, jitter))
            d.rectangle((c * w + off + gap, r * h + gap, c * w + off + w - gap, r * h + h - gap), fill=shade)
    return np.asarray(img, np.float32) / 255 * noise(amount=.1)


def stripes(period, power, vertical=True, low=.78):
    x = np.arange(SIZE)
    wave = (np.cos(x / period * np.pi * 2) + 1) / 2
    line = low + (1 - low) * wave ** power
    arr = np.tile(line, (SIZE, 1)) if vertical else np.tile(line[:, None], (1, SIZE))
    return arr * noise(amount=.06)


def wood():
    y = np.arange(SIZE)[:, None] / SIZE
    x = np.arange(SIZE)[None, :] / SIZE
    grain = np.sin((y * 12 + np.sin(x * np.pi * 4) * .15) * np.pi * 2 + noise(scale=16, amount=1) * 2)
    return (.86 + .09 * grain) * noise(amount=.05)


def giwa():
    """Concave tile channels with horizontal courses."""
    x = np.arange(SIZE)[None, :]
    y = np.arange(SIZE)[:, None]
    channel = .7 + .3 * np.abs(np.sin(x / SIZE * np.pi * 8))
    course = np.where((y % 32) < 4, .68, 1.0)
    return channel * course * noise(amount=.06)


def tiles(n, mortar=.78):
    arr = noise(amount=.08)
    step = SIZE // n
    for k in range(3):
        arr[k::step, :] *= mortar
        arr[:, k::step] *= mortar
    return arr


def thatch():
    y = np.arange(SIZE)[:, None]
    x = np.arange(SIZE)[None, :]
    lines = .82 + .18 * np.sin(x * 1.3 + np.sin(y / 11) * 3)
    return lines * noise(scale=32, amount=.25)


def crop_rows():
    x = np.arange(SIZE)[None, :]
    rows = .72 + .28 * (np.sin(x / SIZE * np.pi * 2 * 8) > -.2)
    return rows * noise(scale=32, amount=.2)


def facade(lit=False):
    """OSM building wall: one 3 m bay by one 3.2 m floor per repeat, colours baked in."""
    img = Image.new("RGB", (SIZE, SIZE), (198, 196, 186))
    d = ImageDraw.Draw(img)
    d.rectangle((0, SIZE - 16, SIZE, SIZE), fill=(168, 168, 160))
    glass = (232, 196, 120) if lit else (52, 72, 88)
    d.rectangle((36, 52, SIZE - 36, SIZE - 46), fill=glass)
    d.rectangle((36, 52, SIZE - 36, 62), fill=(118, 136, 146))
    d.line((SIZE // 2, 52, SIZE // 2, SIZE - 46), fill=(150, 156, 156), width=6)
    d.rectangle((28, SIZE - 46, SIZE - 28, SIZE - 36), fill=(216, 214, 206))
    arr = np.asarray(img, np.float32) / 255
    return arr * noise(amount=.07)[..., None]


FACADE_STYLES = {
    # wall, glass, frame, lit share
    "grey": ((198, 196, 186), (52, 72, 88), (150, 156, 156), .18),
    "white": ((228, 228, 222), (60, 84, 100), (190, 192, 190), .15),
    "beige": ((212, 192, 156), (58, 70, 80), (170, 150, 120), .2),
    "brick": ((150, 72, 52), (48, 62, 72), (210, 200, 186), .22),
}


def facade_set(style):
    """Four bays by four floors (12 m x 12.8 m per repeat) with varied lit and shaded windows."""
    size, bays = 512, 4
    cell = size // bays
    wall, glass, frame, lit_share = FACADE_STYLES[style]
    img = Image.new("RGB", (size, size), wall)
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(len(style) * 13)
    if style == "brick":
        for r in range(0, size, 10):
            off = 12 * ((r // 10) % 2)
            for c in range(-24, size, 24):
                d.rectangle((c + off + 1, r + 1, c + off + 22, r + 8), fill=tuple(int(v * rng.uniform(.88, 1.08)) for v in wall))
    for floor in range(bays):
        y0 = floor * cell
        d.rectangle((0, y0 + cell - 8, size, y0 + cell), fill=tuple(int(v * .85) for v in wall))
        for bay in range(bays):
            x0 = bay * cell
            roll = rng.random()
            pane = (236, 200, 128) if roll < lit_share else glass
            d.rectangle((x0 + 18, y0 + 26, x0 + cell - 18, y0 + cell - 26), fill=frame)
            d.rectangle((x0 + 22, y0 + 30, x0 + cell - 22, y0 + cell - 30), fill=pane)
            if roll > .85:
                d.rectangle((x0 + 22, y0 + 30, x0 + cell - 22, y0 + 30 + int((cell - 60) * rng.uniform(.3, .7))),
                            fill=(205, 200, 186))
            d.line((x0 + cell // 2, y0 + 30, x0 + cell // 2, y0 + cell - 30), fill=frame, width=4)
    arr = np.asarray(img, np.float32) / 255
    grain = np.asarray(Image.fromarray((noise(amount=.06) * 255).astype(np.uint8)).resize((size, size)), np.float32) / 255
    return arr * grain[..., None]


def facade_glass():
    """Curtain wall: blue-green glass with mullions and floor bands, 12 m x 12.8 m per repeat."""
    size = 512
    img = Image.new("RGB", (size, size), (70, 110, 128))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(5)
    for floor in range(4):
        y0 = floor * 128
        for k in range(8):
            shade = rng.uniform(.85, 1.15)
            lit = rng.random() < .12
            color = (226, 196, 130) if lit else tuple(int(v * shade) for v in (70, 112, 132))
            d.rectangle((k * 64 + 3, y0 + 10, k * 64 + 61, y0 + 122), fill=color)
        d.rectangle((0, y0, size, y0 + 9), fill=(150, 162, 168))
    gradient = np.linspace(1.15, .85, size)[:, None, None]
    return np.clip(np.asarray(img, np.float32) / 255 * gradient, 0, 1)


def junction():
    base = noise(scale=64, amount=.35) * .22 + .04
    return np.stack([base, base, base * 1.05], -1)


def osm_road():
    """Road strip: u runs along the road (10 m per repeat), v spans the full width."""
    base = noise(scale=64, amount=.35) * .22 + .04
    arr = np.stack([base, base, base * 1.05], -1)
    for v0, v1 in ((8, 14), (SIZE - 14, SIZE - 8)):
        arr[v0:v1, :] = (.82, .82, .78)
    mid = SIZE // 2
    arr[mid - 4:mid - 1, :] = (.9, .72, .2)
    arr[mid + 1:mid + 4, :] = (.9, .72, .2)
    for v in (SIZE // 4, SIZE * 3 // 4):
        for u in range(0, SIZE, 64):
            arr[v - 2:v + 2, u:u + 34] = (.85, .85, .8)
    return arr


def paving():
    arr = tiles(8, .82) * .9
    return np.stack([arr * .66, arr * .65, arr * .6], -1)


def roof_flat():
    arr = noise(scale=48, amount=.3)
    arr = arr * np.where((np.arange(SIZE)[:, None] % 128) < 3, .82, 1)
    return np.stack([arr * .46, arr * .48, arr * .49], -1)


def main():
    textures = {
        "brick": blocks(8, 4, mortar=150, jitter=25, gap=3),
        "plaster": noise(scale=32, amount=.1),
        "white": noise(scale=32, amount=.06),
        "concrete": tiles(2, .86) * noise(scale=24, amount=.12),
        "pavement": tiles(4),
        "asphalt": noise(scale=64, amount=.35),
        "grass": noise(scale=24, amount=.35),
        "lawn": noise(scale=24, amount=.25) * stripes(64, 1, low=.92),
        "soil": noise(scale=40, amount=.3),
        "giwa": giwa(),
        "roof_grey": blocks(8, 8, mortar=150, jitter=18, gap=1),
        "thatch": thatch(),
        "timber": wood(),
        "darkwood": wood(),
        "metal": stripes(16, 2),
        "factory_blue": stripes(16, 2),
        "steel_blue": stripes(32, 2, low=.85),
        "stone": blocks(4, 3),
        "granite": blocks(3, 2),
        "crop": crop_rows(),
        "wheat": crop_rows(),
        "rust": noise(scale=32, amount=.3),
        "coal": noise(scale=64, amount=.5),
        "cooling": stripes(32, 1, vertical=False, low=.9),
        "facade": facade(),
        "facade_grey": facade_set("grey"),
        "facade_white": facade_set("white"),
        "facade_beige": facade_set("beige"),
        "facade_brick": facade_set("brick"),
        "facade_glass": facade_glass(),
        "junction": junction(),
        "facade_lit": facade(lit=True),
        "osm_road": osm_road(),
        "paving": paving(),
        "roof_flat": roof_flat(),
    }
    for name, arr in textures.items():
        save(name, arr)
    print("textures", len(textures), "->", OUT)


main()
