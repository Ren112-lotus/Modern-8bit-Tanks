"""Sprite-stack profiles for the historical and Japanese vehicles.

Dimensions below are game pixels. Each vehicle has an independent silhouette
trace and running gear profile. The four-view plates constrain the envelopes
and the local artwork supplies the fittings. See VehicleArt/expanded-references.md.
"""

from dataclasses import dataclass
from pathlib import Path
from math import hypot

from PIL import Image, ImageFilter

from expansion_four_view import PLATE_DIR, Plate, finish_part, fit_mount, register_hull, register_turret
from expansion_vehicle_fittings import draw_fittings, suspension
from expansion_vehicle_shapes import hull_section, shoulder_width, turret_section
from german_expansion_art import HULLS as GERMAN_SCULPTS, sculpt_vehicle as sculpt_german_vehicle
from us_light_tank_art import SCULPTS as US_LIGHT_SCULPTS


@dataclass(frozen=True)
class Trace:
    width: int
    length: int
    deck: int
    wheels: int
    hull: str
    pivot: int
    turret_width: int
    turret_depth: int
    turret_height: int
    turret: str
    muzzle: int
    markings: str = ""
    mount_height: int | None = None
    muzzle_height: int | None = None


T = Trace
CHAFFEE = T(21, 35, 8, 5, "slope", 0, 13, 16, 5, "weld", 21, muzzle_height=2)
BULLDOG = T(23, 39, 9, 5, "slope", 1, 17, 20, 6, "weld", 28, muzzle_height=2)
TRACES = {
    "us_m1917": T(13, 27, 7, 9, "tail", 3, 8, 8, 5, "rivet", 9),
    "us_m2a4_light": T(16, 27, 7, 4, "rivets", 1, 10, 11, 5, "rivet", 12),
    "us_m3_stuart": T(16, 30, 8, 4, "rivets", 2, 11, 12, 5, "rivet", 13, "bins"),
    "us_m3_lee": T(18, 35, 11, 6, "lee", 1, 10, 11, 6, "cast", 14),
    "us_m4a1": T(19, 36, 9, 6, "sherman", 1, 13, 12, 5, "cast", 19, "cupola"),
    "us_m4a3e8": T(21, 41, 9, 6, "slope", 1, 15, 16, 6, "cast", 26, "brake"),
    "us_m24": CHAFFEE,
    "us_m36": T(19, 38, 8, 6, "slope", 0, 15, 17, 5, "open", 23, "brake"),
    "us_m26": T(22, 39, 9, 6, "slope", 1, 16, 16, 6, "cast", 26, "brake"),
    "us_m46": T(22, 41, 9, 6, "slope", 1, 17, 16, 6, "cast", 27, "cupola brake"),
    "us_m41": BULLDOG,
    "us_m56": T(17, 31, 5, 4, "gunbed", 2, 9, 6, 3, "shield", 21, "brake"),
    "us_m50": T(17, 26, 7, 4, "gunbed", 0, 9, 10, 4, "twin", 12, "six"),
    "us_m103": T(27, 48, 10, 7, "slope", 1, 24, 26, 8, "cast", 40, "cupola"),
    "de_a7v": T(23, 53, 17, 12, "landship", 22, 9, 6, 2, "fixed", 7, mount_height=8),
    "de_pz2c": T(16, 31, 9, 5, "rivets", 0, 10, 10, 4, "rivet", 12),
    "de_pz3j": T(20, 37, 9, 6, "box", 1, 13, 14, 5, "weld", 19),
    "de_pz4f2": T(22, 39, 10, 8, "box", 1, 15, 18, 6, "weld", 25, muzzle_height=3),
    "de_stug3g": T(20, 37, 9, 6, "casemate", 9, 9, 8, 4, "fixed", 19, mount_height=7, muzzle_height=2),
    "de_panther_g": T(26, 45, 10, 8, "slope", 2, 17, 20, 6, "weld", 34, muzzle_height=3),
    "de_tiger_i": T(28, 44, 10, 8, "tiger", -1, 21, 20, 7, "cast", 31, muzzle_height=3),
    "de_jagdpanther": T(26, 45, 13, 8, "casemate", 11, 11, 9, 5, "fixed", 26, mount_height=9, muzzle_height=2),
    "de_m41g": BULLDOG,
    "de_kanjpz": T(20, 39, 9, 5, "casemate", 13, 9, 8, 4, "fixed", 22, mount_height=5, muzzle_height=2),
    "de_ru251": T(23, 42, 7, 6, "slope", 0, 17, 18, 5, "cast", 27, muzzle_height=2),
    "de_wiesel_mk": T(13, 23, 6, 3, "slope", -2, 7, 7, 4, "weld", 10),
    "de_wiesel_tow": T(13, 23, 6, 3, "slope", -2, 7, 7, 4, "fixed", 7, muzzle_height=4),
    "ru_ms1": T(13, 23, 7, 6, "tail", 1, 8, 8, 5, "rivet", 8),
    "ru_t26": T(16, 28, 8, 8, "rivets", 3, 10, 11, 5, "rivet", 14),
    "ru_bt7": T(17, 34, 8, 4, "fast", 4, 11, 11, 5, "rivet", 15),
    "ru_t28": T(20, 50, 10, 12, "multi", -4, 16, 15, 6, "weld", 17),
    "ru_t34_1941": T(20, 36, 8, 5, "sloped", 4, 13, 14, 5, "cast", 21),
    "ru_kv1": T(24, 41, 10, 6, "box", 2, 17, 18, 7, "weld", 21, "bins"),
    "ru_t3485": T(20, 36, 8, 5, "sloped", 3, 15, 17, 6, "cast", 25, "cupola"),
    "ru_is2": T(24, 42, 10, 6, "slope", 4, 18, 20, 7, "cast", 32, "brake"),
    "ru_su100": T(21, 37, 9, 5, "casemate", 6, 11, 8, 5, "fixed", 27),
    "ru_t44": T(22, 37, 8, 5, "sloped", 0, 15, 16, 5, "cast", 25, "cupola"),
    "ru_asu57": T(15, 25, 5, 4, "gunbed", 3, 7, 7, 2, "shield", 14, "brake"),
    "ru_asu85": T(19, 36, 8, 6, "casemate", 6, 9, 8, 4, "fixed", 23, "brake"),
    "ru_it1": T(24, 42, 8, 5, "slope", 1, 18, 15, 5, "launcher", 10),
    "ru_obj906": T(20, 43, 8, 6, "amph", 2, 15, 15, 5, "cast", 24),
    "jp_igo": T(15, 34, 9, 9, "tail", 3, 10, 10, 6, "rivet", 10, "cupola"),
    "jp_hago": T(15, 30, 7, 4, "rivets", 0, 8, 9, 5, "rivet", 10, "cupola"),
    "jp_chiha": T(17, 33, 8, 6, "rivets", 1, 11, 11, 5, "rivet", 11, "rail"),
    "jp_shinhoto": T(17, 33, 8, 6, "rivets", 1, 13, 14, 6, "weld", 17, "cupola"),
    "jp_chihe": T(18, 34, 8, 6, "box", 1, 13, 14, 6, "weld", 17, "cupola"),
    "jp_chinu": T(19, 35, 8, 6, "box", 1, 15, 16, 6, "weld", 23),
    "jp_chito": T(21, 40, 9, 7, "slope", 0, 17, 17, 6, "weld", 27),
    "jp_chiri": T(24, 45, 10, 8, "box", 0, 18, 21, 7, "weld", 30, "bins"),
    "jp_kami": T(19, 47, 8, 4, "amph", -2, 10, 10, 5, "rivet", 16, "float"),
    "jp_honi3": T(18, 35, 8, 6, "casemate", 5, 10, 9, 5, "fixed", 20),
    "jp_m24": CHAFFEE,
    "jp_type60": T(16, 29, 5, 5, "gunbed", 2, 8, 9, 3, "twin", 13),
    "jp_type61": T(22, 40, 9, 6, "slope", 1, 17, 17, 6, "cast", 26, "cupola rack brake"),
    "jp_type74": T(22, 41, 8, 5, "sloped", 2, 19, 17, 5, "cast", 30, "optic"),
    "jp_type90": T(24, 43, 9, 6, "modern", 1, 23, 22, 7, "flat", 32, "rack optic"),
    "jp_type10": T(23, 42, 9, 5, "modern", 1, 21, 22, 6, "wedge", 33, "rack optic"),
    "jp_type16": T(21, 46, 9, 4, "wheeled", -3, 18, 19, 6, "wedge", 28, "optic brake"),
    "jp_type89ifv": T(23, 41, 9, 6, "ifv", -3, 15, 16, 6, "weld", 22, "missiles optic"),
}

PALETTES = {
    "USA": ((113, 115, 84),),
    "GER": ((107, 107, 96),),
    "USSR": ((116, 124, 82),),
    "JPN": ((130, 116, 84), (129, 98, 70), (99, 111, 73)),
}
JAPAN_MODERN = ((105, 115, 88), (118, 97, 76), (86, 99, 78))
GER_LATE = ((139, 123, 86), (116, 92, 71), (101, 109, 78))
GER_POSTWAR = ((106, 115, 84),)
MODEL_PALETTES = {
    "de_panther_g": ((147, 134, 104),),
    "de_ru251": ((111, 109, 89),),
    "jp_m24": ((96, 102, 91),),
    "de_m41g": ((102, 100, 75),),
    "us_m24": ((119, 115, 91),),
    "us_m41": ((107, 113, 91),),
    "de_pz2c": ((99, 105, 104),),
    "de_pz3j": ((104, 107, 105),),
    "de_pz4f2": ((157, 139, 103),),
    "de_tiger_i": ((143, 128, 96), (131, 118, 85), (126, 119, 86)),
}
DARK_STEEL = (66, 64, 54)
EDGE_STEEL = (83, 81, 67)
GLASS = (59, 76, 74)
REFERENCE_FILES = {
    "jp_igo": "type-89-igo-four-views.png",
    "jp_type10": "type-10-four-views.png",
}
FLEET_PLATES = {model: PLATE_DIR.parent / "Fleet" / f"{model}.jpg"
                for model in ("de_wiesel_mk", "de_wiesel_tow")}

# These traces fit the known body envelope at the game's common pixel scale.
# Photo masking still shapes the armor, but it can't change the sheet bounds.
DIMENSIONED_HULLS = {
    "us_m4a1", "us_m4a3e8", "us_m56", "us_m103", "de_a7v", *FLEET_PLATES,
    "ru_t28", "ru_asu57", "jp_hago", "jp_kami", "jp_type60", "jp_type16",
}


def tint(color, gain):
    return tuple(max(0, min(255, round(channel * gain))) for channel in color)


def read_reference(id):
    if id not in REFERENCE_FILES:
        return None
    filename = Path(__file__).with_name("VehicleArt") / REFERENCE_FILES[id]
    with Image.open(filename) as source:
        return source.convert("RGB").filter(ImageFilter.BoxBlur(5))


def armor_paint(stack, x, y, gain, photo, turret=False):
    base = stack.paint(x + stack.width % 5, y + stack.length % 7)
    if photo is not None:
        if turret:
            origin = (400, 247)
            pitch = (15, 12)
        else:
            origin = (315, 247)
            pitch = (510 / stack.length, 250 / stack.width)
        r, g, b = photo.getpixel((round(origin[0] + y * pitch[0]), round(origin[1] - x * pitch[1])))
        if max(r, g, b) - min(r, g, b) > 6 and r + g + b < 480:
            base = tuple(round(.62 * c + .38 * p) for c, p in zip(base, (r, g, b)))
    grain = (0, -3, 0, 2, 0, -1, 1)[(x * 73 + y * 53) % 7]
    return tuple(max(0, min(255, c + grain)) for c in tint(base, gain))


def running_gear(stack, trace):
    high = min(trace.deck - 1, 7 if trace.hull in {"tail", "landship"} else 5)
    wheel_tall = trace.wheels <= 5 or trace.hull in {"modern", "tiger", "fast", "sloped"}
    half = trace.width // 2 - 1
    rear = -trace.length // 2 + (6 if trace.hull in {"tail", "amph"} else 2)
    front = trace.length // 2 - (4 if trace.hull == "amph" else 2)
    if trace.hull == "wheeled":
        for side in (-1, 1):
            for center in (-14, -6, 5, 14):
                for z in range(7):
                    for y in range(center - 3, center + 4):
                        radius = hypot((y - center) / 3.4, (z - 3) / 3.1)
                        if radius <= 1:
                            for x in range(half - 2, half + 1):
                                face = radius < .54 and x == half
                                stack.pixel(z, x * side, y, tint(stack.base, .75) if face else DARK_STEEL)
        return

    first, last = rear + 2, front - 2
    spacing = (last - first) / (trace.wheels - 1)
    centers = [first + i * spacing for i in range(trace.wheels)]
    for side in (-1, 1):
        for z in range(high + 1):
            for y in range(rear + (2 if z == 0 else 0), front + (0 if z == 0 else 2)):
                end = y - rear <= 1 or front - y <= 1
                nearest = min(abs(y - c) for c in centers)
                radius = hypot(nearest / max(1, spacing * .55),
                               (z - high * .46) / (high * (.55 if wheel_tall else .37)))
                for x in range(half - 1, half + 1):
                    if z in (0, high) or end:
                        color = EDGE_STEEL if (y + z) % 3 == 0 else DARK_STEEL
                    elif x < half:
                        color = DARK_STEEL
                    elif radius < .66:
                        color = tint(stack.base, .96 if z > high * .45 else .77)
                    elif radius < 1.14:
                        color = tint(EDGE_STEEL, 1.05)
                    else:
                        continue
                    stack.pixel(z, side * x, y, color)


def hull_art(stack, trace, photo, model=None):
    half, span = trace.width // 2, trace.length // 2
    roof = trace.deck - 1
    angular = trace.hull in {"slope", "sloped", "modern", "amph", "wheeled", "ifv", "fast"}
    shoulder = half - (2 if trace.hull in {"tail", "fast"} else 1)
    for z in range(1, trace.deck):
        f = z / roof
        width = half - 3 if z < 4 else shoulder - (1 if f > .8 else 0)
        fore = span - 2 - round(max(0, f - .38) * (11 if angular else 5))
        aft = -span + (6 if trace.hull == "tail" else 2) + round(max(0, f - .6) * 3)
        if trace.hull == "landship":
            fore -= 2 + (2 if f > .75 else 0)
            aft += 1
            width = half - 1
        if trace.hull == "gunbed" and f > .72:
            fore -= 6
        if model is not None:
            aft, fore, width, rear_clip, front_clip = hull_section(trace, model, z)
        else:
            rear_clip, front_clip = 2, 3 if angular else 1
        for y in range(aft, fore + 1):
            chamfer = max(0, aft + rear_clip - y, y - fore + front_clip)
            if model is not None and trace.hull == "sherman" and y > fore - front_clip:
                reach = shoulder_width(width, chamfer, front_clip, rounded=True)
            else:
                reach = width - chamfer
            for x in range(-reach, reach + 1):
                flare = abs(x) == reach
                surface = .76 + .26 * f - (.09 if flare else 0)
                if trace.hull == "rivets" and flare and z % 3 == 2 and y % 4 == 0:
                    surface *= 1.13
                stack.pixel(z, x, y, armor_paint(stack, x, y, surface, photo))
    if model is None:
        running_gear(stack, trace)
    else:
        suspension(stack, trace, model)
    if trace.hull in {"modern", "wheeled", "ifv", "tiger"}:
        for x in (-half + 1, half - 1):
            for y in range(-span + 3, span - 3):
                stack.pixel(6, x, y, tint(stack.paint(x, y), .74 if y % 5 == 0 else .91))
    if trace.hull == "tail":
        stack.box(4, 4, -2, 2, -span + 1, -span + 6, EDGE_STEEL)
        stack.box(5, 5, -2, 2, -span + 2, -span + 7, tint(stack.base, .88))
    if trace.hull == "casemate":
        stack.loft(roof, [
            (trace.pivot - 9, trace.pivot + 4, half - 3, 2, 2),
            (trace.pivot - 8, trace.pivot + 3, half - 4, 2, 2),
            (trace.pivot - 7, trace.pivot + 2, half - 5, 2, 1),
        ])
    if trace.hull == "landship":
        stack.loft(roof, [(-7, 5, 5, 1, 1), (-6, 5, 4, 1, 1), (-5, 4, 4, 1, 1)])
        for x in (-2, 2):
            stack.pixel(roof + 1, x, 5, GLASS)
            stack.pixel(roof + 1, x, -6, DARK_STEEL)
        for side in (-1, 1):
            x = side * (half - 3)
            for y in (-13, 9):
                stack.box(7, 10, min(x, x + side), max(x, x + side), y, y + 2, stack.dark)
                stack.pixel(9, x + side, y + 1, DARK_STEEL)
    if trace.hull == "lee":
        stack.box(roof - 3, roof - 1, 4, 7, span - 7, span - 3, tint(stack.base, .81))
        stack.box(roof - 1, roof - 1, 5, 6, span - 4, span, EDGE_STEEL)
    if trace.hull == "multi":
        for x in (-4, 4):
            stack.ellipse(roof + 1, x, 11, 3, 3, tint(stack.base, .87))
            stack.ellipse(roof + 2, x, 11, 2, 2, tint(stack.base, 1.05))
            stack.box(roof + 1, roof + 1, x, x, 13, 16, EDGE_STEEL)
        for x in (-half + 1, half - 1):
            stack.box(5, 7, x, x, -span + 4, span - 4, tint(stack.base, .83))
            for y in range(-span + 5, span - 4, 5):
                stack.box(6, 7, x, x, y, y, stack.dark)
    if "float" in trace.markings:
        # The pontoon shell extends beyond the track run and meets the deck
        # at a stepped seam. The front and stern have different profiles.
        stack.loft(3, [(span - 12, span - 4, half - 3, 0, 4),
                       (span - 12, span - 2, half - 2, 0, 5),
                       (span - 12, span - 1, half - 1, 0, 6),
                       (span - 12, span - 1, half - 1, 0, 5),
                       (span - 12, span - 2, half - 2, 0, 4)])
        stack.loft(3, [(-span + 3, -span + 11, half - 3, 3, 0),
                       (-span + 2, -span + 11, half - 2, 3, 0),
                       (-span + 1, -span + 11, half - 1, 3, 0),
                       (-span + 1, -span + 11, half - 1, 3, 0),
                       (-span + 2, -span + 11, half - 2, 3, 0)])
    if model in {"ru_asu57", "us_m56"}:
        # Leave a stepped recess in the open platform behind the shield.
        for z in range(3, roof + 1):
            stack.layers[z].paste((0, 0, 0, 0), (stack.cx - 3, stack.cy - 8, stack.cx + 4, stack.cy - 1))
        stack.box(2, 2, -3, 3, -8, -2, stack.dark)
        for x in (-4, 4):
            stack.box(3, roof, x, x, -9, -2, tint(stack.base, .83))
            stack.box(roof, roof, x, x, -9, -2, tint(stack.base, 1.05))
    if model is None and trace.hull not in {"gunbed", "landship"}:
        grilles_y = span - 11 if trace.hull in {"ifv", "wheeled"} else -span + 6
        for x in (-4, 4):
            stack.grille(roof, x - 2, x + 2, grilles_y - 2, grilles_y + 2)
        for x in (-half + 3, half - 3):
            stack.pixel(max(4, roof - 2), x, span - 4, tint(stack.base, 1.18))
        if trace.hull != "casemate":
            stack.lid(roof, 1, 4, span - 12, span - 9)
    if model is None and trace.hull in {"sherman", "slope", "sloped"}:
        lip = span - 6
        stack.box(roof - 1, roof - 1, -half + 4, half - 4, lip, lip, tint(stack.base, .83))
        if trace.hull == "sherman":
            stack.box(4, 4, -half + 4, half - 4, span - 3, span - 3, tint(stack.base, .89))


def turret_art(stack, trace, photo, gun_z, model=None):
    kind = trace.turret
    span, depth = trace.turret_width // 2, trace.turret_depth // 2
    roof = trace.turret_height - 1
    closed = kind not in {"fixed", "shield", "twin"}
    if closed:
        for z in range(trace.turret_height):
            taper = z / max(1, roof)
            reach = span - round(taper * (span * (.35 if kind == "cast" else .13 if kind == "flat" else .22)))
            aft = -depth + (1 if z == 0 else 0) + round(taper * 2)
            fore = depth - round(taper * (3 if kind in {"cast", "wedge"} else 2))
            if model is not None:
                aft, fore, reach, rear_cut, front_cut, center, rounded = turret_section(trace, model, z)
            for y in range(aft, fore + 1):
                c = .34 if kind == "cast" else .09 if kind == "flat" else .19
                inset = round(reach * c * ((y - (fore + aft) * .5) / max(1, depth)) ** 2)
                if kind == "wedge":
                    inset += max(0, y - fore + 3)
                offset = 0
                if model is not None:
                    clip = front_cut if y >= (fore + aft) / 2 else rear_cut
                    corner = max(0, aft + rear_cut - y, y - fore + front_cut)
                    inset = reach - max(1, shoulder_width(reach, corner, clip, rounded))
                    offset = center
                for x in range(-reach + inset, reach - inset + 1):
                    if kind == "open" and z >= 2 and abs(x) < reach - inset - 1 and aft + 1 < y < fore - 1:
                        continue
                    shade = .76 + .27 * taper - (.10 if abs(x) == reach - inset else 0)
                    stack.pixel(z, x + offset, y, armor_paint(stack, x, y, shade, photo, True))
        if kind == "open":
            stack.box(2, 2, -2, 2, -depth + 3, 0, DARK_STEEL)
        elif model is None:
            stack.hatch(roof, -max(1, span // 2), -2, 2, 2)
            stack.hatch(roof, max(1, span // 2), 0, 1, 2)
    elif kind == "shield":
        stack.box(0, roof, -span, span, -1, 1, tint(stack.base, .94))
        stack.box(roof, roof, -span + 1, span - 1, -2, 1, stack.base)
    elif kind == "twin":
        stack.box(0, 1, -span + 1, span - 1, -4, 4, stack.base)

    marks = trace.markings
    if model is None and "cupola" in marks:
        x = -max(1, span // 2)
        stack.ellipse(roof + 1, x, -2, 2, 2, tint(stack.base, .88))
        stack.ellipse(roof + 2, x, -2, 1, 1, tint(stack.base, 1.12))
    if model is None and ("bins" in marks or "rack" in marks):
        stack.box(1, 2, -span + 1, span - 1, -depth - 2, -depth + 1, tint(stack.base, .80))
        if "rack" in marks:
            stack.box(3, 3, -span + 1, span - 1, -depth - 2, -depth - 2, EDGE_STEEL)
    if model is None and "rail" in marks:
        for x in (-span + 1, span - 1):
            stack.box(roof, roof, x, x, -depth + 1, 1, DARK_STEEL)
    if model is None and "optic" in marks:
        stack.sight(roof, -span // 2 + 1, 0, width=1, height=3)
        stack.sight(roof, span // 2, -3, width=1, height=2)
    if kind in {"wedge", "flat"}:
        for side in (-1, 1):
            for step in range(3):
                x = side * (span - 2 - 2 * step)
                y = depth - (3 if kind == "flat" else 4) + step
                stack.box(2, 2, x - 1, x + 1, y - 2, y, tint(stack.paint(x, y), .82))
                stack.box(3, 3, x - 1, x + 1, y - 2, y - 1, tint(stack.paint(x, y), 1.04))
    if "missiles" in marks:
        for x in (-span, span):
            stack.box(3, 4, x - 1, x + 1, -4, 3, DARK_STEEL)
    if kind == "launcher":
        stack.box(gun_z, gun_z + 1, -1, 1, -4, trace.muzzle, tint(stack.base, .94))
    else:
        start = min(depth - 1, trace.muzzle - 2)
        if kind in {"fixed", "shield", "twin"}:
            start = 0
        stack.box(max(0, gun_z - 1), gun_z, -1, 1, start, min(trace.muzzle, start + 3), tint(stack.base, .86))
        tubes = (-1, 1) if kind == "twin" else (0,)
        if "six" in marks:
            tubes = (-3, -1, 1, 3)
            for x in (-2, 2):
                stack.box(gun_z + 1, gun_z + 1, x, x, start, trace.muzzle, EDGE_STEEL)
        for x in tubes:
            stack.box(gun_z, gun_z, x, x, start, trace.muzzle, tint(stack.base, .99))
            if trace.muzzle >= 18:
                stack.box(gun_z - 1, gun_z - 1, x, x, start, trace.muzzle - 1, DARK_STEEL)
        if "brake" in marks:
            stack.box(gun_z, gun_z, -1, 1, trace.muzzle - 1, trace.muzzle, EDGE_STEEL)
        for x in tubes:
            stack.pixel(gun_z, x, trace.muzzle, DARK_STEEL)


def build_expanded_vehicle(vehicle, png_dir, stack_type):
    name = vehicle["id"]
    trace = TRACES[name]
    palette = PALETTES[vehicle["nation"]]
    historical = vehicle.get("historyColumn", 0) > 0
    if vehicle["nation"] == "JPN" and not historical:
        palette = JAPAN_MODERN
    if vehicle["nation"] == "GER" and historical and vehicle["year"] >= 1942:
        palette = GER_LATE
    if vehicle["nation"] == "GER" and not historical:
        palette = GER_POSTWAR
    palette = MODEL_PALETTES.get(name, palette)
    photo = read_reference(name)
    filename = FLEET_PLATES.get(name, PLATE_DIR / f"{name}.png")
    dedicated = name in GERMAN_SCULPTS
    fit_dimensions = not dedicated and name not in US_LIGHT_SCULPTS and name not in DIMENSIONED_HULLS
    plate = Plate(filename, trace, fit_dimensions=fit_dimensions) if name not in REFERENCE_FILES else None
    if plate is not None:
        trace = plate.trace
    length = max(trace.muzzle + 3, trace.turret_depth + 8) * 2 + 1
    # The hull owns a casemate's superstructure. Its movable sprite is the gun.
    mount = trace.mount_height if trace.mount_height is not None else trace.deck + (trace.hull == "casemate")
    gun_z = trace.muzzle_height if trace.muzzle_height is not None else (1 if trace.turret == "fixed" else 2)
    hull = stack_type(trace.width, trace.length, trace.deck + 3, palette)
    turret = stack_type(max(11, trace.turret_width + 4), length, trace.turret_height + 3, palette)
    model = name if plate is not None else None
    if name in US_LIGHT_SCULPTS:
        US_LIGHT_SCULPTS[name](plate, hull, turret, name)
    elif dedicated:
        sculpt_german_vehicle(name, plate, hull, turret, gun_z)
    else:
        hull_art(hull, trace, photo, model)
        turret_art(turret, trace, photo, gun_z, model)
        if plate is not None:
            register_hull(plate, hull)
            register_turret(plate, turret)
            fit_mount(plate, hull, turret, name, gun_z)
            finish_part(plate, hull)
            finish_part(plate, turret, turret=True)
            draw_fittings(name, hull, turret)
    vehicle.update(hullWidth=hull.width, hullLength=hull.length, hullBaseHeight=mount,
                   hullPivotOffsetY=trace.pivot, turretWidth=turret.width,
                   turretLength=turret.length, muzzleDist=trace.muzzle, muzzleHeight=gun_z)
    vehicle["hullSlices"] = hull.save(png_dir / f"{name}_hull_slices.png")
    vehicle["turretSlices"] = turret.save(png_dir / f"{name}_turret_slices.png")
