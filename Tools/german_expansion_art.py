"""Hand-shaped sprite-stack sections for German expansion vehicles."""

from dataclasses import dataclass, replace
from math import sin, sqrt

from expansion_four_view import finish_part
from expansion_vehicle_fittings import draw_fittings, draw_wheel, shade


STEEL = (85, 79, 66)
TRACK = (61, 58, 51)
TRACK_EDGE = (104, 98, 81)
RUBBER = (47, 46, 41)
RECESS = (45, 43, 38)
EXHAUST = (91, 74, 58)
GLASS = (65, 88, 79)


# Ordered horizontal sections from one pixel above the ground. A section is
# the rear, front, half-width, rear bevel, and front bevel in local pixels.
PANZER_III_BELLY = (
    (-14, 14, 6, 2, 2), (-16, 15, 7, 2, 2), (-17, 17, 7, 2, 2),
    (-17, 17, 7, 2, 2), (-17, 16, 8, 1, 1),
)
PANTHER_BELLY = (
    (-18, 17, 7, 2, 3), (-19, 19, 8, 2, 3), (-21, 21, 8, 3, 3),
    (-21, 21, 9, 3, 2), (-21, 20, 9, 2, 2), (-20, 19, 11, 2, 1),
)

HULLS = {
    "de_pz2c": (
        (-12, 12, 4, 2, 3), (-13, 13, 5, 1, 3), (-14, 14, 5, 1, 2),
        (-14, 14, 6, 1, 2), (-14, 12, 7, 1, 2), (-13, 10, 6, 1, 2),
        (-13, 8, 6, 1, 1), (-11, 7, 5, 1, 1),
    ),
    "de_pz3j": PANZER_III_BELLY + (
        (-16, 13, 8, 1, 1), (-15, 12, 7, 1, 1), (-14, 12, 7, 1, 1),
    ),
    "de_pz4f2": (
        (-14, 14, 6, 1, 1), (-15, 16, 7, 1, 1), (-16, 17, 7, 1, 1),
        (-17, 18, 7, 1, 1), (-17, 18, 7, 1, 1), (-17, 17, 8, 1, 1),
        (-17, 15, 8, 1, 1), (-16, 11, 8, 1, 1), (-8, 11, 8, 1, 1),
    ),
    "de_stug3g": PANZER_III_BELLY + (
        (-16, 13, 8, 1, 2), (-16, 12, 8, 2, 2), (-8, 11, 8, 2, 2),
        (-7, 9, 7, 2, 3), (-6, 8, 6, 2, 3),
    ),
    "de_panther_g": PANTHER_BELLY + (
        (-20, 17, 11, 1, 1), (-20, 14, 11, 1, 1), (-19, 11, 10, 1, 1),
    ),
    "de_tiger_i": (
        (-17, 17, 8, 2, 2), (-18, 19, 9, 2, 2), (-19, 20, 9, 2, 1),
        (-19, 20, 9, 2, 1), (-19, 19, 9, 1, 0), (-19, 18, 12, 1, 1),
        (-19, 17, 12, 1, 0), (-18, 16, 11, 1, 0), (-17, 14, 10, 1, 1),
    ),
    "de_jagdpanther": PANTHER_BELLY + (
        (-20, 17, 11, 2, 1), (-19, 15, 10, 2, 1), (-19, 13, 10, 2, 1),
        (-13, 12, 9, 2, 1), (-11, 11, 9, 2, 2), (-10, 10, 8, 2, 2),
        (-9, 9, 7, 2, 2), (-8, 7, 6, 2, 2),
    ),
    "de_kanjpz": (
        (-16, 16, 6, 2, 4), (-18, 18, 7, 2, 4), (-18, 18, 7, 2, 3),
        (-18, 18, 8, 2, 3), (-18, 17, 8, 2, 3), (-17, 15, 8, 2, 3),
        (-17, 12, 7, 2, 3), (-15, 10, 7, 2, 2), (-13, 7, 6, 1, 2),
    ),
    "de_ru251": (
        (-15, 14, 6, 2, 3), (-17, 17, 7, 1, 3), (-19, 20, 8, 1, 2),
        (-19, 19, 9, 1, 1), (-19, 16, 9, 1, 2), (-18, 12, 9, 1, 2),
    ),
}

TURRETS = {
    "de_pz2c": ((-4, 4, 3, 1, 2), (-5, 5, 4, 2, 2), (-4, 4, 4, 2, 2), (-3, 2, 3, 2, 1)),
    "de_pz3j": ((-6, 5, 5, 2, 3), (-7, 6, 6, 2, 3), (-7, 6, 6, 2, 3),
                (-6, 5, 5, 1, 3), (-5, 3, 5, 1, 2)),
    "de_pz4f2": ((-6, 6, 6, 2, 2), (-8, 7, 7, 3, 3), (-8, 7, 7, 3, 3),
                 (-8, 7, 7, 3, 3), (-7, 6, 6, 2, 2), (-7, 5, 6, 2, 2)),
    "de_panther_g": ((-8, 7, 6, 2, 1), (-10, 8, 7, 2, 2), (-10, 8, 8, 3, 3),
                    (-9, 8, 8, 3, 3), (-8, 7, 7, 2, 2), (-7, 6, 6, 2, 1)),
    "de_ru251": ((-7, 6, 6, 3, 3), (-9, 8, 8, 4, 4), (-9, 8, 8, 4, 4),
                 (-8, 7, 7, 4, 3), (-6, 5, 6, 3, 3)),
}


@dataclass(frozen=True)
class RunningGear:
    ends: tuple
    wheels: tuple
    height: int
    band: int
    wheel_size: tuple
    rollers: tuple = ()
    overlap: bool = False
    sprocket_at_front: bool = True
    wheel_depth: int = 1


PANZER_III_TRACKS = RunningGear((-17, 17), (-11, -7, -3, 1, 5, 9), 5, 2, (1.85, 1.8), (-8, -1, 6))
PANTHER_TRACKS = RunningGear((-20, 21), (-14, -10, -6, -2, 2, 6, 10, 14), 6, 3, (2.7, 2.4), overlap=True)
TRACKS = {
    "de_pz2c": RunningGear((-14, 14), (-9, -5, -1, 3, 7), 5, 2, (1.9, 1.8), (-6, 1, 8)),
    "de_pz3j": PANZER_III_TRACKS,
    "de_pz4f2": RunningGear((-18, 18), (-12, -9, -5, -2, 2, 5, 9, 12), 6, 3, (1.56, 1.85), (-11, -4, 4, 11)),
    "de_stug3g": PANZER_III_TRACKS,
    "de_panther_g": replace(PANTHER_TRACKS, wheel_size=(3.45, 2.95), band=4, wheel_depth=2),
    "de_tiger_i": RunningGear((-19, 20), (-13, -9, -5, -1, 3, 7, 11, 15), 7, 4, (2.75, 2.8), overlap=True),
    "de_jagdpanther": PANTHER_TRACKS,
    "de_kanjpz": RunningGear((-18, 18), (-12, -7, -2, 3, 8), 5, 2, (2.25, 2.0), (-7, 0, 8), sprocket_at_front=False),
    "de_ru251": RunningGear((-19, 19), (-13, -8, -3, 2, 7, 12), 5, 3, (2.45, 2.30),
                            (-10, -2, 7), sprocket_at_front=False, wheel_depth=3),
}


def running_gear(hull, gear):
    rear, front = gear.ends
    road_y, road_z = gear.wheel_size
    reach = hull.width // 2 - 1
    wheel_face = shade(hull.base, .90)
    hub = shade(hull.base, 1.14)
    for side in (-1, 1):
        outer = side * reach
        inside = side * (reach - gear.band + 1)
        left, right = sorted((inside, outer))
        hull.box(2, 2, inside, inside, rear + 1, front - 1, RECESS)
        for i, y in enumerate(gear.wheels):
            if gear.overlap:
                draw_wheel(hull, outer - gear.wheel_depth * side, y, gear.height * .43, road_y, road_z, hull.dark, STEEL)
                if i % 2:
                    continue
            for dx in range(gear.wheel_depth):
                draw_wheel(hull, outer - dx * side, y, gear.height * .43, road_y, road_z, wheel_face, hub)
            hull.pixel(round(gear.height * .43) - 1, outer, y, hull.dark)
        for y in gear.rollers:
            draw_wheel(hull, outer - side, y, gear.height - 1, 1.2, 1.15, hull.dark, STEEL)
        for end, drive in ((rear + 1, not gear.sprocket_at_front), (front - 1, gear.sprocket_at_front)):
            draw_wheel(hull, outer, end, gear.height * .52, 1.6, gear.height * .39, wheel_face,
                       TRACK_EDGE if drive else STEEL, TRACK)
        for z, rear_inset, front_inset in ((0, 3, 3), (gear.height, 2, 2)):
            a, b = rear + rear_inset, front - front_inset
            hull.box(z, z, left, right, a, b, TRACK)
            for y in range(a + 1, b + 1, 2):
                hull.box(z, z, left, right, y, y, STEEL)
                hull.pixel(z, outer, y, TRACK_EDGE)
        for z in range(1, gear.height):
            inset = 1 if z in (1, gear.height - 1) else 0
            hull.box(z, z, left, right, rear + inset, rear + inset, TRACK_EDGE if z % 2 else TRACK)
            hull.box(z, z, left, right, front - inset, front - inset, TRACK_EDGE if z % 2 else TRACK)


# Rear of the collar, contour of each horizontal collar section, and tip treatment.
# The movable piece on a casemate is just the exterior gun and its collar.
GUNS = {
    "de_pz2c": (3, (-1, 1, 1, 0), "plain"),
    "de_pz3j": (5, (1, 2, 3, 3, 2, 1), "plain"),
    "de_pz4f2": (6, (1, 2, 3, 3, 3, 2), "single_baffle"),
    "de_stug3g": (-1, (1, 2, 3, 2, 1), "double"),
    "de_panther_g": (7, (4, 5, 5, 5, 5, 4), "double"),
    "de_tiger_i": (8, (2, 4, 5, 5, 5, 4, 2), "double"),
    "de_jagdpanther": (-1, (2, 3, 4, 4, 3, 2, 1), "double"),
    "de_kanjpz": (-1, (1, 2, 2, 2, 1), "round"),
    "de_ru251": (5, (2, 3, 4, 3, 2), "plain"),
}


def gun_armor(turret, profile, gun_z, muzzle, *, thin_tube=False):
    start, section, brake = profile
    for z, span in enumerate(section):
        if span < 0:
            continue
        face = start + 2 + (1 if z == gun_z else 0)
        if span >= 3:
            face -= 1
        turret.box(z, z, -span, span, start, face, shade(turret.base, .76 + z * .065))
        if z > gun_z and span > 1:
            turret.box(z, z, -span + 1, span - 1, face + 1, face + 1, turret.base)
    collar = start + 4
    turret.box(gun_z, gun_z, -1, 1, collar - 1, collar + 2, turret.base)
    turret.box(gun_z - 1, gun_z - 1, 0, 0, collar, collar + 3 if thin_tube else muzzle - 2, STEEL)
    turret.box(gun_z, gun_z, 0, 0, collar, muzzle, shade(turret.base, 1.10))
    if brake in {"round", "double", "tee"}:
        turret.box(gun_z, gun_z, -1, 1, muzzle - 1, muzzle, turret.dark)
        turret.box(gun_z - 1, gun_z - 1, 0, 0, muzzle - 1, muzzle, turret.dark)
        turret.pixel(gun_z + 1, 0, muzzle - 1, turret.base)
    if brake == "double":
        turret.box(gun_z, gun_z, -1, 1, muzzle - 3, muzzle - 3, turret.base)
    elif brake == "tee":
        turret.box(gun_z, gun_z, -2, 2, muzzle, muzzle, turret.dark)
    elif brake == "single_baffle":
        turret.box(gun_z, gun_z, -1, 1, muzzle - 2, muzzle - 1, turret.base)
        for x in (-1, 1):
            turret.pixel(gun_z, x, muzzle - 1, STEEL)
        turret.pixel(gun_z - 1, 0, muzzle - 1, turret.dark)
        turret.pixel(gun_z + 1, 0, muzzle - 1, turret.light)
    turret.pixel(gun_z, 0, muzzle, RECESS)


def rear_pipes(hull, x, y, bottom, top, shield):
    for sign in (-1, 1):
        center = sign * x
        radius = 1 if shield else 0
        hull.box(bottom, top - 1, center - radius, center + radius, y, y + 1, EXHAUST)
        hull.box(bottom, top - 1, center, center, y + 1, y + 1, hull.dark)
        hull.pixel(top, center, y, STEEL)
        hull.pixel(top, center, y + 1, RECESS)


def cooling_fan(hull, z, x, y, radius=2):
    hull.ellipse(z, x, y, radius, radius, hull.dark)
    hull.ellipse(z + 1, x, y, radius, radius, STEEL)
    for dx in range(1 - radius, radius):
        hull.box(z + 1, z + 1, x + dx, x + dx, y - radius + 1, y + radius - 1,
                 hull.dark if dx % 2 else RECESS)


def muffler(hull, y, z, reach):
    hull.box(z, z + 1, -reach, reach, y, y + 1, EXHAUST)
    hull.box(z + 1, z + 1, -reach + 1, reach - 1, y + 1, y + 1, STEEL)
    for x in (-reach + 1, reach - 1):
        hull.box(z, z + 1, x, x, y - 1, y, hull.dark)


def side_hatches(turret, edge, y, z):
    for side in (-1, 1):
        x = edge * side
        turret.box(z, z + 2, x, x, y - 2, y + 1, turret.dark)
        turret.box(z + 1, z + 2, x, x, y - 1, y, shade(turret.base, 1.12))
        turret.pixel(z + 1, x, y + 1, STEEL)


def bow_guard(hull, x, y, z):
    for side in (-1, 1):
        point = side * x
        hull.box(z, z + 1, point - 1, point + 1, y - 1, y, hull.dark)
        hull.pixel(z + 1, point, y, (148, 150, 122))


def towing_eyes(hull, x, y, z):
    for side in (-1, 1):
        point = side * x
        hull.box(z, z + 1, point - 1, point + 1, y, y, hull.dark)
        hull.pixel(z + 1, point - side, y, STEEL)
        hull.pixel(z + 1, point, y, RECESS)


def panzer_ii(hull, turret):
    hull.box(6, 7, -4, -1, 8, 9, hull.dark)
    hull.box(7, 7, -4, -1, 9, 9, GLASS)
    hull.box(6, 6, 2, 6, -13, -12, EXHAUST)
    towing_eyes(hull, 3, 13, 3)
    turret.box(2, 2, 1, 1, 5, 7, STEEL)


def panzer_iii(hull, turret):
    hull.box(6, 7, -5, -2, 13, 13, hull.dark)
    hull.box(7, 7, -4, -3, 14, 14, GLASS)
    hull.box(6, 7, 3, 4, 13, 13, hull.base)
    hull.pixel(7, 4, 14, RECESS)
    muffler(hull, -17, 4, 5)
    towing_eyes(hull, 5, 17, 4)
    side_hatches(turret, 5, -1, 2)
    turret.box(3, 4, -3, 3, -8, -7, turret.dark)
    turret.box(4, 4, -2, 2, -8, -7, turret.base)


def panzer_iv(hull, turret):
    # A narrow ledge separates the upright driver's plate from the transmission deck.
    hull.box(8, 9, 2, 5, 11, 12, hull.dark)
    hull.box(9, 9, 2, 5, 12, 12, hull.base)
    hull.box(8, 8, -5, -3, 12, 12, hull.dark)
    hull.pixel(8, -4, 13, STEEL)
    for x in (-5, 5):
        hull.box(7, 7, x - 1, x + 1, 14, 15, shade(hull.base, .88))
        hull.pixel(7, x, 15, hull.light)
    # Spare links laid across the nose, as in the wartime front-quarter photo.
    hull.box(5, 6, -6, 5, 18, 18, TRACK)
    for x in range(-6, 6, 2):
        hull.pixel(6, x, 18, TRACK_EDGE)
    towing_eyes(hull, 5, 18, 4)
    muffler(hull, -18, 5, 6)
    hull.box(6, 6, -6, 6, -19, -19, EXHAUST)
    for x in (-4, 4):
        hull.pixel(6, x, -19, STEEL)

    for sign in (-1, 1):
        outer = sign * 10
        inner = sign * 8
        left, right = sorted((inner, outer))
        gear = TRACKS["de_pz4f2"]
        # Give the paired wheels a full tread width. A single-pixel face
        # can disappear when the renderer foreshortens a rotated slice.
        for rear, front in zip(gear.wheels[::2], gear.wheels[1::2]):
            for center in (rear, front):
                for x in range(left, right + 1):
                    draw_wheel(hull, x, center, 2, 1.56, 1.85, hull.base, hull.light)
                hull.box(2, 2, left, right, center, center, shade(hull.base, 1.16))
            hull.box(4, 4, left, right, rear + 1, front - 1, hull.dark)
            hull.pixel(4, outer, rear + 1, hull.base)
        for end in (-16, 16):
            for x in range(left, right + 1):
                draw_wheel(hull, x, end, 3, 2, 2.3, hull.dark, hull.base, TRACK)
        hull.pixel(7, sign * 9, 15, hull.light)
    side_hatches(turret, 7, 0, 2)
    # The overhanging bin has a chamfered lid and a visible seam below the turret roof.
    turret.shell(2, -10, -6, 5, 1, 0, .73)
    turret.shell(3, -11, -6, 6, 2, 0, .87)
    turret.shell(4, -11, -6, 6, 2, 0, 1.06)
    for x in (-3, 3):
        turret.pixel(3, x, -11, STEEL)
    turret.box(5, 5, -2, 2, 7, 8, shade(turret.base, 1.08))


def stug_iii(hull, turret):
    hull.box(6, 9, -7, -6, 3, 9, hull.dark)
    hull.box(7, 8, 6, 7, 1, 7, hull.base)
    hull.box(8, 8, -6, -4, 10, 10, GLASS)
    muffler(hull, -17, 4, 5)
    towing_eyes(hull, 5, 17, 4)
    turret.box(2, 3, -1, 1, 1, 4, turret.base)


def panther_band(x, y, z):
    # Broad, pale-edged earth and olive bands on the Bovington G.
    across = (x + .48 * y + .35 * z + 1.9 * sin(y * .23)) % 15
    if 2 < across < 6:
        return (132, 102, 83)
    if 8 < across < 13.5:
        return (130, 132, 104)
    return (168, 155, 121)


def band_armor(stack):
    for z, layer in enumerate(stack.layers):
        bounds = layer.getbbox()
        if bounds is None:
            continue
        for py in range(bounds[1], bounds[3]):
            for px in range(bounds[0], bounds[2]):
                red, _, _, alpha = layer.getpixel((px, py))
                if alpha:
                    x, y = px - stack.cx, py - stack.cy
                    stack.pixel(z, x, y, shade(panther_band(x, y, z), red / stack.base[0]))


def cheek_marking(turret, rear, pattern, dark, light):
    # Paint on the inclined cheek itself so the stencil follows the turret through a turn.
    for z, line in enumerate(pattern, 1):
        for y, ink in enumerate(line, rear):
            if ink == " ":
                continue
            for sign in (-1, 1):
                for reach in range(turret.cx, 0, -1):
                    x = sign * reach
                    if turret.layers[z].getpixel((turret.cx + x, turret.cy + y))[3]:
                        turret.pixel(z, x, y, light if ink == "+" else dark)
                        break


def panther(hull, turret):
    for sign in (-1, 1):
        x = sign * 6
        # Recessed fan with central hub, flanked by radiator shutters.
        hull.ellipse(9, x, -14, 3, 3, hull.dark)
        hull.ellipse(9, x, -14, 2, 2, RECESS)
        for offset in (-1, 1):
            hull.box(9, 9, x + offset, x + offset, -15, -13, STEEL)
        hull.pixel(9, x, -14, hull.base)
        for y in (-18, -10):
            hull.box(9, 9, x - 2, x + 2, y, y, hull.dark)
        # One suspended skirt panel leaves the interleaved wheels visible.
        edge = sign * 12
        rear = 1 if sign == -1 else -8
        for y in range(rear, rear + 7):
            for z in range(4, 8):
                hull.pixel(z, edge, y, shade(panther_band(edge, y, z), .77 if z == 4 else .94))
        hull.box(5, 7, sign * 8 - 2, sign * 8 + 2, -21, -20, hull.dark)
        hull.box(7, 7, sign * 8 - 2, sign * 8 + 2, -21, -20, hull.base)
    rear_pipes(hull, 4, -21, 5, 9, shield=True)
    hull.box(7, 8, 9, 10, 14, 15, hull.dark)
    hull.pixel(8, 10, 15, (152, 149, 119))
    towing_eyes(hull, 7, 20, 4)
    hull.pixel(7, -6, 16, RECESS)
    for z, nose in enumerate((9, 10, 11, 11, 10, 9)):
        for x in range(-5, 6):
            turret.box(z, z, x, x, 7, nose, shade(panther_band(x, 8, z), .78 + .045 * z))
    turret.box(3, 3, -1, 1, 11, 13, turret.base)
    turret.pixel(3, -3, 11, RECESS)
    cheek_marking(turret, -4, (" +#+ ", "++#++", "#####", "++#++", " +#+ "),
                  (63, 61, 53), (165, 158, 133))


def tiger_turret(turret):
    # The armor wraps continuously around the rear, then draws inward along
    # straight cheeks to meet the separate front mantlet.
    sections = (
        (-8, 7, 8), (-9, 8, 9), (-10, 9, 10), (-10, 9, 10),
        (-10, 8, 10), (-10, 8, 10), (-9, 7, 9),
    )
    shoulder = -3
    for z, (rear, front, width) in enumerate(sections):
        for y in range(rear, front + 1):
            if y < shoulder:
                arc = (shoulder - y) / (shoulder - rear + .5)
                reach = round(width * sqrt(1 - arc * arc))
            else:
                reach = width - round(2.8 * (y - shoulder) / (front - shoulder))
            for x in range(-reach, reach + 1):
                contour = .88 if abs(x) == reach else 1.0
                turret.pixel(z, x, y, shade(turret.paint(x, y), (.80 + .044 * z) * contour))


def tiger(hull, turret):
    for x in (-7, 7):
        cooling_fan(hull, 8, x, -13, 2)
    rear_pipes(hull, 5, -20, 5, 10, shield=True)
    bow_guard(hull, 9, 14, 7)
    towing_eyes(hull, 8, 20, 4)
    for x in range(-6, 7, 2):
        hull.box(4, 5, x, x + 1, 20, 20, STEEL if x % 4 else TRACK)
    hull.box(8, 8, -7, -3, 17, 17, GLASS)
    turret.shell(2, -11, -8, 4, 1, 0, .74)
    turret.shell(3, -12, -8, 5, 1, 0, .81)
    turret.shell(4, -12, -8, 5, 1, 0, .94)
    turret.shell(5, -11, -8, 4, 1, 0, 1.07)
    turret.ellipse(6, 2, -3, 2, 2, turret.dark)


def jagdpanther(hull, turret):
    for x in (-5, 5):
        cooling_fan(hull, 9, x, -16, 2)
    rear_pipes(hull, 4, -21, 5, 9, shield=False)
    towing_eyes(hull, 7, 20, 4)
    hull.box(11, 12, -4, 3, -10, -9, hull.dark)
    hull.box(12, 12, -3, 2, -10, -9, hull.base)
    turret.box(2, 3, -3, 3, 1, 2, turret.base)


def kanonenjagdpanzer(hull, turret):
    for z in range(4, 7):
        hull.box(z, z, -7, 7, -18 + (z - 4) // 2, -18 + (z - 4) // 2, hull.dark)
        for x in range(-6, 7, 3):
            hull.pixel(z, x, -18 + (z - 4) // 2, STEEL)
    bow_guard(hull, 7, 12, 5)
    towing_eyes(hull, 5, 16, 4)
    hull.box(7, 8, 3, 5, 4, 6, hull.dark)
    turret.box(1, 2, -2, 2, -1, 2, turret.base)


def ru_251(hull, turret):
    for sign in (-1, 1):
        x = sign * 4
        hull.box(3, 6, x - 3, x + 3, -19, -18, hull.dark)
        for z in (3, 5):
            hull.box(z, z, x - 2, x + 2, -19, -19, RECESS)
        hull.box(4, 5, sign * 8, sign * 8, 16, 17, hull.dark)
        hull.pixel(5, sign * 8, 17, (166, 165, 138))
    towing_eyes(hull, 5, 19, 4)
    # Box searchlight with a circular lens on the forward face.
    turret.box(4, 4, 2, 3, 3, 4, STEEL)
    turret.box(5, 7, 1, 4, 2, 5, turret.dark)
    turret.box(7, 7, 1, 4, 2, 5, turret.base)
    turret.box(6, 6, 1, 4, 5, 5, (145, 148, 131))
    turret.box(5, 7, 2, 3, 5, 5, (168, 171, 151))
    turret.pixel(6, 2, 5, (184, 186, 166))
    cheek_marking(turret, -4, (" +#+ ", "+###+", "++#++", " +#+ "),
                  (67, 72, 68), (172, 173, 151))


DETAILS = {
    "de_pz2c": panzer_ii, "de_pz3j": panzer_iii, "de_pz4f2": panzer_iv, "de_stug3g": stug_iii,
    "de_panther_g": panther, "de_tiger_i": tiger, "de_jagdpanther": jagdpanther,
    "de_kanjpz": kanonenjagdpanzer, "de_ru251": ru_251,
}


def sculpt_vehicle(model, plate, hull, turret, gun_z):
    hull.loft(1, HULLS[model])
    if model == "de_panther_g":
        band_armor(hull)
    running_gear(hull, TRACKS[model])
    if model == "de_tiger_i":
        tiger_turret(turret)
    elif model in TURRETS:
        turret.loft(0, TURRETS[model])
    if model == "de_panther_g":
        band_armor(turret)
    gun_armor(turret, GUNS[model], gun_z, plate.trace.muzzle,
              thin_tube=model in {"de_panther_g", "de_ru251"})
    finish_part(plate, hull)
    finish_part(plate, turret, turret=True)
    draw_fittings(model, hull, turret)
    DETAILS[model](hull, turret)

