"""Register and carve the Armata photo sheet into horizontal sprite slices."""

from functools import partial
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

from four_view_carving import intersect, register as register_view


PHOTO = Path(__file__).with_name("VehicleArt") / "t14-armata-four-views.png"

ARMOR = (115, 125, 100)
EDGE = (69, 77, 61)
HIGHLIGHT = (150, 157, 128)
TREAD = (57, 55, 44)
RIM = (115, 112, 86)
GLASS = (75, 104, 100)
MUZZLE_Y = 32

# The contours are traced on the game's grid. The nose points toward +y;
# the profiles and the two end views share the same height origin.
HULL = (
    ((-11, -22), (11, -22), (12, -19), (12, 16), (11, 20), (8, 22),
     (-8, 22), (-11, 20), (-12, 16), (-12, -19)),
    ((-22, 4), (-22, 7), (-19, 8), (-15, 9), (10, 9), (14, 8),
     (20, 6), (22, 5), (21, 4), (18, 2), (15, 0), (-16, 0), (-20, 2)),
    ((-12, 0), (-12, 7), (-9, 8), (-8, 9), (8, 9), (9, 8), (12, 7), (12, 0),
     (9, 0), (8, 3), (-8, 3), (-9, 0)),
    ((-12, 0), (-12, 7), (-10, 8), (-8, 9), (8, 9), (10, 8), (12, 7), (12, 0),
     (9, 0), (8, 3), (-8, 3), (-9, 0)),
)

TURRET = (
    ((-7, -12), (7, -12), (8, -8), (10, -3), (10, 3), (7, 7), (4, 10),
     (1, 10), (1, MUZZLE_Y), (-1, MUZZLE_Y), (-1, 10), (-4, 10), (-8, 7), (-10, 3),
     (-10, -4), (-8, -9)),
    ((-12, 1), (-12, 3), (-10, 5), (-7, 5), (-7, 8), (-3, 8), (-3, 6),
     (2, 6), (5, 5), (9, 4), (13, 3), (MUZZLE_Y, 3), (MUZZLE_Y, 2), (11, 2),
     (8, 1), (6, 0), (-8, 0)),
    ((-9, 0), (-10, 2), (-10, 4), (-7, 6), (-5, 6), (-5, 8), (-2, 8),
     (-2, 6), (6, 6), (10, 4), (10, 2), (8, 0)),
    ((-9, 0), (-10, 2), (-10, 3), (-7, 5), (-5, 6), (-5, 8), (-2, 8),
     (-2, 6), (6, 6), (10, 3), (10, 2), (8, 0)),
)


def palette_color(rgb, base=ARMOR):
    # Keep the lighting from each photo, quantized into a shared game palette.
    luminance = (rgb[0] * 2 + rgb[1] * 5 + rgb[2]) / 8
    level = max(5, min(11, round((luminance + 66) / 21)))
    return tuple(round(channel * level / 8) for channel in base)


def y_in_photo(y, tail, mantlet, tip):
    if y <= 10:
        return tail + (y + 12) * (mantlet - tail) / 22
    return mantlet + (y - 10) * (tip - mantlet) / 22


register = partial(register_view, colorize=palette_color)


def hull_views(photo, hull):
    width, length, height = hull.width, hull.length, len(hull.layers)
    top = register(photo, (width, length), HULL[0],
                   lambda x, y: (263 + y * 9.31, 323 - x * 8.8))
    side = register(photo, (length, height), HULL[1],
                    lambda y, z: (895 + y * 11.1, 430 - z * 12.1), profile=True)
    front = register(photo, (width, height), HULL[2],
                     lambda x, z: (323 + x * 18.3, 1114 - z * 22.5), profile=True)
    rear = register(photo, (width, height), HULL[3],
                    lambda x, z: (931 - x * 18.8, 1114 - z * 22.7), profile=True)

    side_draw = ImageDraw.Draw(side)
    for y in range(-22, -16):
        px = y + length // 2
        for z in (4, 6, 8):
            if side.getpixel((px, height - 1 - z))[3]:
                side_draw.point((px, height - 1 - z), fill=EDGE)
    for y in range(-20, 21):
        px = y + length // 2
        for z in (0, 3):
            if side.getpixel((px, height - 1 - z))[3]:
                side_draw.point((px, height - 1 - z), fill=TREAD)
    for center in (-14, -9, -4, 0, 5, 10, 14):
        px = center + length // 2
        side_draw.ellipse((px - 1, height - 4, px + 1, height - 2), fill=RIM)
        side_draw.point((px, height - 3), fill=EDGE)
    for y in (-11, -5, 1, 7, 13, 18):
        px = y + length // 2
        for z in range(4, 8):
            if side.getpixel((px, height - 1 - z))[3]:
                side_draw.point((px, height - 1 - z), fill=EDGE)
    for x in range(-12, 13):
        px = x + width // 2
        for z in (5, 7):
            rear.putpixel((px, height - 1 - z), EDGE)

    top_draw = ImageDraw.Draw(top)
    for x in (-5, 0, 5):
        px, py = x + width // 2, length // 2 + 12
        top_draw.rectangle((px - 1, py - 1, px + 1, py), fill=EDGE)
        top_draw.point((px, py - 1), fill=GLASS)
    top_draw.line((width // 2 - 7, length // 2 + 18,
                   width // 2 + 7, length // 2 + 18), fill=EDGE)
    for x in (-8, 8):
        front.putpixel((x + width // 2, height - 1 - 6), HIGHLIGHT)
    return top, side, front, rear


def turret_views(photo, turret):
    width, length, height = turret.width, turret.length, len(turret.layers)
    top = register(photo, (width, length), TURRET[0],
                   lambda x, y: (y_in_photo(y, 127, 354, 594), 321 - x * 7.8))
    side = register(photo, (length, height), TURRET[1],
                    lambda y, z: (y_in_photo(y, 756, 971, 1209), 324 - z * 9.2), profile=True)
    front = register(photo, (width, height), TURRET[2],
                     lambda x, z: (323 + x * 13.2, 902 - z * 17.0), profile=True)
    rear = register(photo, (width, height), TURRET[3],
                    lambda x, z: (931 - x * 13.2, 901 - z * 16.9), profile=True)

    draw = ImageDraw.Draw(top)
    cx, cy = width // 2, length // 2
    draw.line([(cx - 9, cy + 2), (cx - 6, cy + 6), (cx - 3, cy + 8)], fill=EDGE)
    draw.line([(cx + 9, cy + 2), (cx + 6, cy + 6), (cx + 3, cy + 8)], fill=EDGE)
    draw.line([(cx - 6, cy - 10), (cx + 5, cy - 10)], fill=HIGHLIGHT)
    draw.rectangle((cx - 5, cy - 7, cx - 2, cy - 4), fill=EDGE)
    draw.rectangle((cx - 4, cy - 7, cx - 2, cy - 5), fill=HIGHLIGHT)
    draw.point((cx - 3, cy - 4), fill=GLASS)
    draw.point((cx + 3, cy + 1), fill=GLASS)
    return top, side, front, rear


def carve(stack, views, is_turret=False):
    """Intersect the four aligned silhouettes and color the exposed faces."""
    top, side, front, rear = (view.load() for view in views)
    width, length, height = stack.width, stack.length, len(stack.layers)
    occupied = intersect(width, length, height, (top, side, front, rear))
    if is_turret:
        occupied = {(x, y, z) for x, y, z in occupied
                    if not (y >= length // 2 + 15 and z == 3 and x != width // 2)}

    for x, y, z in occupied:
        row = height - 1 - z
        outer_x = x - 1 if x < width // 2 else x + 1
        if is_turret and y >= length // 2 + 15:
            at_tip = y == length // 2 + MUZZLE_Y
            color = top[x, y] if z == 3 and not at_tip else EDGE
        elif not is_turret and z < 4 and abs(x - width // 2) >= 9:
            color = side[y, row] if (outer_x, y, z) not in occupied else TREAD
        elif (x, y, z + 1) not in occupied:
            color = top[x, y]
        elif (x, y + 1, z) not in occupied:
            color = front[x, row]
        elif (x, y - 1, z) not in occupied:
            color = rear[x, row]
        elif (x - 1, y, z) not in occupied or (x + 1, y, z) not in occupied:
            color = side[y, row]
        else:
            color = ARMOR
        stack.layers[z].putpixel((x, y), color)


def sculpt_armata(hull, turret, _vehicle):
    with Image.open(PHOTO) as source:
        if source.size != (1254, 1254):
            raise ValueError("The T-14 photo sheet has changed size and must be registered again.")
        photo = source.convert("RGB").filter(ImageFilter.BoxBlur(4))
    carve(hull, hull_views(photo, hull))
    carve(turret, turret_views(photo, turret), is_turret=True)
    return MUZZLE_Y
