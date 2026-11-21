"""Carve the M48A1 and M48A3 exterior sprites from their registered four views."""

from functools import partial
from pathlib import Path

from PIL import Image

from four_view_carving import intersect, register


ART = Path(__file__).with_name("VehicleArt")
MUZZLE_Z = 3
MUZZLE_Y = 32
TRACK_TOP = (72, 72, 61)
TRACK_SIDE = (56, 56, 49)
TRACK_PAD = (43, 45, 43)
CAST_SHADOW = (56, 57, 49)
GLASS = (69, 84, 79)
DARK = (47, 49, 45)

# The two sheets have different framing. Coordinates refer to the cast hull,
# running gear, and gun at the same locations in the shared sprite grid.
REGISTRATION = {
    "a1": ((276, 264, 11.56, 12.9), (1042, 402, 14.4, 17.5),
           (389, 952, 22.4, 29.0), (1126, 952, 22.7, 30.1),
           ((155, 425, 713), (874, 1190, 1516), 253, 719, 706)),
    "a3": ((290, 241, 13.08, 12.5), (1106, 405, 16.3, 18.0),
           (393, 979, 21.0, 33.0), (1147, 979, 21.8, 32.5),
           ((218, 482, 747), (949, 1262, 1527), 229, 719, 711)),
}

HULL_TOP = (
    (-7, -21), (7, -21), (10, -18), (11, -12), (11, 14), (10, 18),
    (7, 20), (3, 21), (-3, 21), (-7, 20), (-10, 18), (-11, 14),
    (-11, -12), (-10, -18),
)
HULL_SIDE = (
    (-21, 5), (-20, 7), (-17, 8), (-9, 8), (-7, 7), (9, 7), (10, 8),
    (13, 8), (20, 6), (21, 5), (20, 4), (16, 3), (12, 2), (-14, 2),
    (-19, 3),
)
HULL_END = (
    (-4, 2), (-7, 3), (-8, 5), (-9, 6), (-11, 6), (-11, 7), (-8, 7),
    (-6, 8), (6, 8), (8, 7), (11, 7), (11, 6), (9, 6), (8, 5),
    (7, 3), (4, 2),
)
TURRET_TOP = (
    (-5, -12), (5, -12), (8, -9), (9, -5), (9, 2), (8, 6),
    (5, 9), (2, 10), (-2, 10), (-5, 9), (-8, 6), (-9, 2), (-9, -5), (-8, -9),
)
TURRET_SIDE = (
    (-10, 0), (-12, 1), (-12, 3), (-10, 5), (-5, 6), (3, 6),
    (7, 5), (10, 3), (10, 1), (8, 0),
)
TURRET_END = (
    (-7, 0), (-9, 1), (-9, 3), (-8, 4), (-6, 5), (-4, 6),
    (4, 6), (6, 5), (8, 4), (9, 3), (9, 1), (7, 0),
)


def painted_steel(rgb, base):
    r, g, b = rgb
    value = (r + 2 * g + b) / 4
    gain = max(.68, min(1.16, round((.51 + value / 215) / .06) * .06))
    return tuple(round(channel * gain) for channel in base)


def turret_longitudinal(y, rear, mantlet, tip):
    if y <= 9:
        return rear + (y + 12) * (mantlet - rear) / 21
    return mantlet + (y - 9) * (tip - mantlet) / (MUZZLE_Y - 9)


def register_hull(photo, hull, registration):
    top, left, front, rear, _ = registration
    tx, ty, dx, dy = top
    sx, sy, step_y, step_z = left
    fx, fy, front_dx, front_dz = front
    rx, ry, rear_dx, rear_dz = rear
    trace = partial(register, photo, colorize=partial(painted_steel, base=hull.base))
    return (
        trace((hull.width, hull.length), HULL_TOP, lambda x, y: (tx + y * dx, ty - x * dy)),
        trace((hull.length, len(hull.layers)), HULL_SIDE, lambda y, z: (sx + y * step_y, sy - z * step_z), profile=True),
        trace((hull.width, len(hull.layers)), HULL_END, lambda x, z: (fx + x * front_dx, fy - z * front_dz), profile=True),
        trace((hull.width, len(hull.layers)), HULL_END, lambda x, z: (rx - x * rear_dx, ry - z * rear_dz), profile=True),
    )


def register_turret(photo, turret, registration):
    top, _, front, rear, (plan_y, side_y, side_base, front_base, rear_base) = registration
    _, ty, _, dy = top
    fx, _, front_dx, _ = front
    rx, _, rear_dx, _ = rear
    trace = partial(register, photo, colorize=partial(painted_steel, base=turret.base))
    height = len(turret.layers)
    return (
        trace((turret.width, turret.length), TURRET_TOP,
              lambda x, y: (turret_longitudinal(y, *plan_y), ty - x * dy)),
        trace((turret.length, height), TURRET_SIDE,
              lambda y, z: (turret_longitudinal(y, *side_y), side_base - z * 16),
              profile=True),
        trace((turret.width, height), TURRET_END,
              lambda x, z: (fx + x * front_dx * .83, front_base - z * 20), profile=True),
        trace((turret.width, height), TURRET_END,
              lambda x, z: (rx - x * rear_dx * .84, rear_base - z * 20), profile=True),
    )


def carve(stack, views, turret=False):
    top, side, front, rear = (view.load() for view in views)
    width, length, height = stack.width, stack.length, len(stack.layers)
    voxels = intersect(width, length, height, (top, side, front, rear))
    if turret:
        rims = ((7.5, 9.8), (9.2, 11.5), (9.4, 12.0), (9.2, 11.8),
                (8.5, 11.2), (7.5, 9.7), (5.9, 7.6))
        voxels = {(x, y, z) for x, y, z in voxels
                  if ((x - stack.cx) / rims[z][0]) ** 2 + ((y - stack.cy + 1.3) / rims[z][1]) ** 2 <= 1}
    for x, y, z in sorted(voxels):
        row = height - 1 - z
        local_x, local_y = x - stack.cx, y - stack.cy
        out = 1 if local_x >= 0 else -1
        if (x, y, z + 1) not in voxels:
            rgb = top[x, y]
        elif (x, y + 1, z) not in voxels:
            rgb = front[x, row]
        elif (x, y - 1, z) not in voxels:
            rgb = rear[x, row]
        elif (x + out, y, z) not in voxels:
            rgb = side[y, row]
        else:
            rgb = stack.base
        # Broad matte planes with deeper rims and restrained cast texture.
        grain = (0, -1, 2, 0, 1, -2)[(x * 59 + y * 7 + z * 43) % 6]
        gain = .88 if z <= 2 and not turret else .94 if z <= 2 else 1
        stack.pixel(z, local_x, local_y, tuple(round(c * gain) + grain for c in rgb[:3]))


def running_gear(hull, diesel):
    for sign in (-1, 1):
        inside, outside = sorted((sign * 9, sign * 11))
        for z, (rear, front) in enumerate(((-16, 14), (-18, 16), (-19, 18), (-20, 19), (-20, 19), (-19, 18))):
            if z in (0, 5):
                hull.box(z, z, inside, outside, rear, front, TRACK_TOP)
                for y in range(rear + 1, front, 2):
                    hull.pixel(z, sign * 11, y, TRACK_PAD)
            else:
                hull.box(z, z, inside, outside, rear, -17, TRACK_SIDE)
                hull.box(z, z, inside, outside, 16, front, TRACK_SIDE)
                hull.box(z, z, inside, inside, -16, 15, CAST_SHADOW)

        # Dark rubber rims and shaded steel hubs keep the six wheels legible.
        rim = (98, 98, 79) if diesel else (105, 99, 81)
        for y in (-14, -9, -4, 1, 6, 11):
            hull.box(1, 3, inside, outside, y - 2, y + 2, TRACK_PAD)
            hull.box(2, 3, sign * 11, sign * 11, y - 1, y + 1, rim)
            hull.pixel(2, sign * 11, y, CAST_SHADOW)
            hull.pixel(3, sign * 11, y, rim)
        for z, y in ((3, -18), (3, 17)):
            hull.pixel(z, sign * 11, y - 1, rim)
            hull.pixel(z + 1, sign * 11, y, rim)
        if not diesel:
            hull.pixel(3, sign * 11, -16, rim)
        for y in (-13, -7, 0, 7, 13):
            hull.pixel(4, sign * 11, y, CAST_SHADOW)


def deck_details(hull, diesel):
    seam = (75, 76, 63) if diesel else (79, 75, 62)
    lip = (112, 116, 91) if diesel else (117, 111, 91)
    for x in (-10, 10):
        hull.box(6, 6, x - 1, x + 1, -17, 17, hull.base)
        hull.box(6, 6, x - 1, x + 1, -19, -17, seam)
        hull.box(6, 6, x - 1, x + 1, 16, 19, seam)
        hull.box(7, 7, x - 1, x + 1, -10, -5, seam)
        hull.box(7, 7, x, x + 1, -9, -6, hull.base)
        hull.pixel(7, x, 14, lip)
        hull.pixel(7, x, 15, (137, 138, 113))
        hull.pixel(6, x, -18, (103, 72, 55))
    for x in (-4, 4):
        rear = -18 if diesel else -16
        hull.box(8, 8, x - 3, x + 3, rear, -10, seam)
        for y in range(rear + 1, -10, 2):
            hull.box(8, 8, x - 2, x + 2, y, y, lip)
        if diesel:
            hull.box(5, 7, x - 3, x + 3, -20, -20, seam)
            for z in (5, 7):
                hull.box(z, z, x - 2, x + 2, -20, -20, TRACK_PAD)
    hull.box(8, 8, -2, 2, 11, 13, seam)
    hull.box(8, 8, -1, 1, 12, 13, lip)
    hull.pixel(8, 0, 14, GLASS)
    for x in (-3, 3):
        hull.pixel(5, x, 20, CAST_SHADOW)


def turret_details(turret, diesel):
    rim = (107, 114, 93) if diesel else (116, 109, 91)
    tube = (110, 115, 96) if diesel else (119, 112, 94)
    turret.ellipse(6, 3, -2, 2, 2, CAST_SHADOW)
    turret.ellipse(6, 3, -2, 1, 1, rim)
    if diesel:
        turret.ellipse(6, -4, -3, 3, 3, CAST_SHADOW)
        for x in (-6, -4, -2):
            turret.pixel(7, x, -1, GLASS)
        turret.ellipse(8, -4, -3, 2, 2, rim)
        turret.pixel(8, -4, -2, GLASS)
        # The basket is a thin frame with gaps between the rails.
        for x in (-6, 6):
            turret.box(3, 4, x, x, -13, -9, CAST_SHADOW)
        turret.box(3, 3, -6, 6, -13, -13, CAST_SHADOW)
        turret.box(4, 4, -6, 6, -13, -13, rim)
        turret.box(3, 3, -1, -1, -13, -10, CAST_SHADOW)
    else:
        turret.ellipse(6, -4, -3, 3, 3, CAST_SHADOW)
        turret.ellipse(7, -4, -3, 2, 2, rim)
        turret.pixel(7, -4, -1, GLASS)
    for sign in (-1, 1):
        turret.box(3, 3, sign * 8, sign * 8, -4, 3, CAST_SHADOW)
        turret.pixel(4, sign * 7, 3, rim)
    turret.box(2, 4, -2, 2, 7, 10, CAST_SHADOW)
    turret.box(3, 3, -1, 1, 9, 13, rim)
    turret.box(MUZZLE_Z - 1, MUZZLE_Z - 1, 0, 0, 10, MUZZLE_Y - 1, CAST_SHADOW)
    turret.box(MUZZLE_Z, MUZZLE_Z, 0, 0, 13, MUZZLE_Y, tube)
    for y in (22, 29):
        turret.pixel(MUZZLE_Z, 0, y, rim)
    turret.box(MUZZLE_Z, MUZZLE_Z, -1, 1, MUZZLE_Y - 1, MUZZLE_Y, tube)
    turret.pixel(MUZZLE_Z, 0, MUZZLE_Y, DARK)


def sculpt_patton(hull, turret, vehicle):
    diesel = vehicle["id"] == "us_m48a3"
    suffix = "a3" if diesel else "a1"
    registration = REGISTRATION[suffix]
    with Image.open(ART / f"m48{suffix}-four-views.png") as image:
        photo = image.convert("RGB")
    carve(hull, register_hull(photo, hull, registration))
    running_gear(hull, diesel)
    deck_details(hull, diesel)
    carve(turret, register_turret(photo, turret, registration), turret=True)
    turret_details(turret, diesel)
    return MUZZLE_Y
