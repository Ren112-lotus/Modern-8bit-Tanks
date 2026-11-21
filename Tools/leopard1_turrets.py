"""Build separate Leopard 1 turret stacks from the three exterior sheets."""

from functools import partial

from PIL import Image, ImageDraw

from four_view_carving import intersect, register


LEOPARD1_TURRETS = {"de_leo1", "de_leo1a1a1", "de_leo1a5"}

# Top center, side center, front center, rear center, matte paint.
REGISTRATION = {
    "de_leo1": ((255, 212), (907, 204), (326, 649), (978, 640), (107, 111, 90)),
    "de_leo1a1a1": ((265, 210), (914, 195), (325, 613), (977, 607), (105, 111, 88)),
    "de_leo1a5": ((270, 210), (905, 209), (328, 639), (975, 632), (108, 111, 86)),
}

CAST_PLAN = (
    (-5, -10), (5, -10), (8, -7), (9, -4), (9, 3), (7, 7), (3, 9),
    (-3, 9), (-7, 7), (-9, 3), (-9, -4), (-8, -7),
)
PLATED_PLAN = (
    (-6, -11), (6, -11), (9, -8), (11, -3), (10, 3), (8, 8), (4, 10),
    (-4, 10), (-8, 8), (-10, 3), (-11, -3), (-9, -8),
)
CAST_SIDE = ((-7, 0), (-10, 1), (-10, 3), (-8, 4), (-4, 5),
             (4, 5), (8, 4), (9, 2), (8, 1), (6, 0))
PLATED_SIDE = ((-8, 0), (-11, 1), (-11, 3), (-8, 4), (-5, 5),
               (5, 5), (9, 4), (10, 2), (8, 1), (6, 0))
CAST_END = ((-7, 0), (-9, 2), (-8, 3), (-7, 4), (-4, 5),
            (4, 5), (7, 4), (8, 3), (9, 2), (7, 0))
PLATED_END = ((-8, 0), (-11, 1), (-11, 2), (-10, 3), (-8, 4), (-5, 5),
              (5, 5), (8, 4), (10, 3), (11, 2), (11, 1), (8, 0))


def steel(rgb, base):
    r, g, b = rgb
    light = (r + 2 * g + b) / 4
    gain = max(.71, min(1.13, round((.56 + light / 242) / .05) * .05))
    return tuple(round(c * gain) for c in base)


def paint_turret(part, photo, name, tip):
    plated = name != "de_leo1"
    late = name == "de_leo1a5"
    top_xy, side_xy, front_xy, rear_xy, base = REGISTRATION[name]
    width, length, height = part.width, part.length, len(part.layers)
    with Image.open(photo) as image:
        source = image.convert("RGB")
    trace = partial(register, source, colorize=partial(steel, base=base))
    end = PLATED_END if plated else CAST_END
    top = trace((width, length), PLATED_PLAN if plated else CAST_PLAN,
                lambda x, y: (top_xy[0] + y * 11.3, top_xy[1] - x * 10.0))
    side = trace((length, height), PLATED_SIDE if plated else CAST_SIDE,
                 lambda y, z: (side_xy[0] + y * 12.2, side_xy[1] - z * 10.7), profile=True)
    front = trace((width, height), end,
                  lambda x, z: (front_xy[0] + x * 12.6, front_xy[1] - z * 16.2), profile=True)
    rear = trace((width, height), end,
                 lambda x, z: (rear_xy[0] - x * 12.6, rear_xy[1] - z * 16.2), profile=True)
    occupied = intersect(width, length, height, tuple(view.load() for view in (top, side, front, rear)))
    cx, cy = width // 2, length // 2
    if not plated:
        # Round off the cast shoulders between their four registered silhouettes.
        cross = ((7.3, 8.5), (8.4, 10.3), (9.5, 11.4), (9.0, 10.7), (7.2, 9.2), (5.6, 7.0))
        occupied = {(x, y, z) for x, y, z in occupied
                    if ((x - cx) / cross[z][0]) ** 2 + ((y - cy + 1) / cross[z][1]) ** 2 <= 1}
    layers = [Image.new("RGBA", (width, length)) for _ in range(height)]
    drawers = [ImageDraw.Draw(layer) for layer in layers]
    for x, y, z in sorted(occupied):
        row = height - 1 - z
        outer = -1 if x < cx else 1
        if (x, y, z + 1) not in occupied:
            pixel = top.getpixel((x, y))
        elif (x + outer, y, z) not in occupied:
            pixel = side.getpixel((y, row))
        elif (x, y + 1, z) not in occupied:
            pixel = front.getpixel((x, row))
        else:
            pixel = rear.getpixel((x, row))
        # A stepped edge and gaps across the panel joints read through rotation.
        edge = -14 if plated and abs(x - cx) >= 7 and y - cy in (-7, -2, 4) else 0
        grain = (0, 2, -1, 1, -2)[(x * 59 + y * 17 + z * 7) % 5]
        tint = (0, -3, -4) if late and (x - cx) - (y - cy) > 8 else (0, 0, 0)
        brightness = .92 if z < 2 else 1
        color = tuple(round(pixel[i] * brightness) + edge + grain + tint[i] for i in range(3))
        drawers[z].point((x, y), fill=color)

    dark = (58, 65, 54)
    metal = (128, 132, 107)
    glass = (60, 81, 80)

    def box(z, x0, y0, x1, y1, color):
        drawers[z].rectangle((cx + x0, cy + y0, cx + x1, cy + y1), fill=color)

    for x in (-4, 4):
        drawers[5].ellipse((cx + x - 2, cy - 6, cx + x + 2, cy - 2), fill=dark)
        drawers[6].ellipse((cx + x - 1, cy - 5, cx + x + 1, cy - 3), fill=metal)
        box(5, x - 1, -2, x + 1, -2, glass)
    if plated:
        # The spaced panels step back towards the roof and leave seams visible.
        plate = (113, 118, 94)
        for sign in (-1, 1):
            for y0, y1, reach in ((-8, -5, 9), (-3, 0, 11), (2, 5, 9)):
                left, right = sorted((sign * (reach - 1), sign * reach))
                box(2, left, y0, right, y1, dark)
                box(3, left, y0 + 1, right, y1, plate)
        box(2, -6, -12, 6, -10, dark)
        box(3, -6, -13, 6, -13, metal)
        for x in (-6, -2, 2, 6):
            box(3, x, -13, x, -10, dark)
            box(4, x, -13, x, -12, metal)
    if late:
        box(4, 3, 3, 6, 6, dark)
        box(5, 3, 3, 6, 6, base)
        box(6, 3, 3, 6, 6, metal)
        box(6, 4, 5, 6, 6, glass)
    for z in (1, 2, 3):
        box(z, -2, 7, 2, 10, dark if z == 1 else base)
    box(1, 0, 10, 0, tip - 1, dark)
    box(2, 0, 10, 0, tip, metal)
    box(2, -1, 10, 1, 13, base)
    box(2, 0, tip, 0, tip, dark)
    return layers
