"""Build exterior sprite stacks from the registered four-view artwork.

The plan stations and roof fittings are traced at sprite scale. For each part,
the side profile and the two end views constrain the depth of that outline.
Colors are sampled from the exposed faces, with separate materials for rubber,
tracks, glass and shadowed fittings.
"""

from dataclasses import dataclass
from math import hypot
from statistics import median

from PIL import Image


@dataclass(frozen=True)
class TurretTrace:
    form: str
    aft: float
    nose: float
    breadth: float
    levels: int
    deck_row: float
    fittings: tuple


# Aft and nose are positions along the overhead hull, measured from its rear.
# Breadth is relative to the hull. The remaining positions are in sprite pixels
# about the swivel, with positive longitudinal positions toward the front.
TRACES = {
    "us_m60a3": TurretTrace("cast", .28, .84, .81, 5, .49,
                            (("cupola", -4, -2), ("sight", 4, 1), ("basket", 0, -10))),
    "us_xm1": TurretTrace("welded", .23, .83, .86, 4, .51,
                          (("hatch", -4, -3), ("sight", 4, 0), ("bin", 4, -9))),
    "us_m1": TurretTrace("welded", .23, .81, .95, 5, .49,
                         (("hatch", -4, -4), ("periscope", 4, 4), ("mg", -3, -4), ("basket", 0, -11))),
    "us_m1a1": TurretTrace("wedge", .22, .83, .91, 5, .51,
                           (("hatch", -4, -3), ("sight", 4, 1), ("mg", -3, -3), ("basket", 0, -11))),
    "us_m1a2_sep2": TurretTrace("wedge", .19, .84, .92, 5, .54,
                               (("cupola", -4, -2), ("sight", 5, 3), ("basket", 0, -13), ("optic", -6, 5))),
    "us_m1a2_sep3": TurretTrace("wedge", .18, .83, .94, 5, .53,
                               (("hatch", -5, -2), ("rws", 5, 0), ("basket", 0, -14), ("bin", -5, -9))),
    "us_m551": TurretTrace("cast", .27, .85, .84, 4, .44,
                           (("cupola", -1, -2), ("hatch", 4, 0), ("basket", 0, -7))),
    "us_m551a1": TurretTrace("cast", .26, .84, .86, 4, .48,
                             (("cupola", -2, -2), ("mg", -2, -2), ("searchlight", 6, 5), ("basket", 0, -7))),
    "us_m8_ags": TurretTrace("welded", .30, .79, .76, 5, .48,
                             (("hatch", 3, -1), ("mg", -3, -3), ("cheek", -6, 3))),
    "us_m1128": TurretTrace("remote", .26, .72, .49, 3, .47,
                            (("sight", -3, 0), ("bin", 2, -4), ("bin", -3, -6))),
    "us_m10_booker": TurretTrace("wedge", .20, .78, .91, 5, .62,
                                 (("optic", -4, -2), ("sight", 4, 2), ("basket", 0, -10), ("bin", 7, -5))),
    "us_m901": TurretTrace("itv", .20, .62, .53, 5, .40, ()),
    "us_m2a2": TurretTrace("ifv", .24, .75, .71, 4, .42,
                           (("hatch", -3, -2), ("periscope", 3, -1), ("sight", -4, 3), ("pod", 7, -1))),
    "us_m2a4": TurretTrace("ifv", .27, .77, .73, 4, .42,
                           (("hatch", 1, -3), ("optic", -4, 2), ("pod", 7, -1), ("bin", -5, -5))),
    "de_leo2a5": TurretTrace("wedge", .22, .89, .96, 5, .50,
                             (("cupola", -4, -4), ("hatch", 4, -3), ("sight", 5, 1), ("basket", 0, -12))),
    "de_leo2a7v": TurretTrace("wedge", .19, .90, .98, 5, .58,
                              (("hatch", -4, -3), ("optic", 4, -1), ("sight", -4, 3), ("basket", 0, -13))),
    "de_leo2a8": TurretTrace("wedge", .23, .88, .99, 5, .49,
                             (("hatch", -4, -3), ("optic", 1, -3), ("sensor", -8, 2), ("sensor", 8, 2))),
    "de_kf51": TurretTrace("wedge", .20, .86, .97, 5, .57,
                           (("optic", 0, -3), ("bin", -5, -7), ("sight", 6, 2), ("bin", 6, -7))),
    "de_luchs": TurretTrace("ifv", .37, .69, .58, 3, .44,
                            (("hatch", -2, -1), ("sight", 2, 2), ("cheek", -5, 1))),
    "de_wiesel_mk": TurretTrace("remote", .30, .65, .53, 3, .36,
                                (("optic", -2, -2), ("bin", 2, -3), ("cheek", 3, 1))),
    "de_puma": TurretTrace("remote", .29, .73, .69, 4, .44,
                           (("optic", -4, -3), ("sight", 3, 1), ("pod", 5, -2))),
    "de_rakjpz2": TurretTrace("twin_rails", .40, .76, .58, 4, .33, ()),
    "ru_t55a": TurretTrace("cast", .29, .81, .85, 4, .52,
                           (("cupola", -4, -2), ("hatch", 3, -4), ("searchlight", 6, 4))),
    "ru_t90a": TurretTrace("welded", .27, .82, .89, 4, .49,
                           (("cupola", -5, -2), ("hatch", 4, -3), ("sensor", -5, 5), ("sensor", 5, 5))),
    "ru_t14": TurretTrace("remote", .22, .72, .70, 5, .51,
                          (("bin", -5, -5), ("optic", 4, -3), ("sensor", -5, 4), ("sensor", 5, 4))),
    "ru_bmp3m": TurretTrace("cast", .30, .73, .83, 4, .44,
                            (("hatch", -4, -2), ("optic", 2, -1), ("sensor", -6, 2), ("cheek", 6, 2))),
    "ru_9p149": TurretTrace("single_rail", .10, .38, .32, 5, .37, ()),
    "ru_2s25": TurretTrace("cast", .29, .81, .86, 4, .63,
                           (("cupola", -3, -2), ("hatch", 3, -3), ("cheek", -6, 2), ("cheek", 6, 2))),
    "ru_bmpt": TurretTrace("remote", .24, .70, .62, 5, .49,
                           (("optic", -1, -4), ("sight", 0, 2), ("tubes", -7, 0), ("tubes", 7, 0))),
}


@dataclass(frozen=True)
class HullTrace:
    # Width at bottom, width at shoulder, height of the track run, front slope.
    belly: float
    shoulder: float
    track_top: int
    glacis: float
    length: int
    mount: int


HULLS = {
    "us_xm1": HullTrace(.73, .96, 5, .18, 42, 10),
    "us_m1": HullTrace(.72, .98, 5, .21, 43, 10),
    "us_m551": HullTrace(.70, .92, 4, .19, 34, 8),
    "us_m551a1": HullTrace(.70, .94, 4, .23, 34, 8),
    "us_m8_ags": HullTrace(.69, .93, 4, .25, 34, 7),
    "us_m10_booker": HullTrace(.72, .98, 4, .21, 40, 8),
    "us_m901": HullTrace(.76, .94, 4, .13, 31, 8),
    "us_m2a2": HullTrace(.72, .99, 4, .32, 37, 9),
    "us_m2a4": HullTrace(.72, .99, 4, .31, 39, 9),
    "de_puma": HullTrace(.74, .99, 4, .29, 40, 8),
    "de_rakjpz2": HullTrace(.70, .94, 4, .26, 38, 8),
    "ru_t14": HullTrace(.76, .98, 4, .24, 46, 9),
    "ru_bmp3m": HullTrace(.71, .98, 4, .24, 38, 8),
    "ru_bmpt": HullTrace(.72, .99, 5, .23, 43, 10),
}


# Five stations (rear, rear shoulder, center, front shoulder, mantlet).
PLAN = {
    "cast": (.53, .92, 1, .85, .39),
    "welded": (.89, 1, .97, .82, .41),
    "wedge": (.95, 1, .99, .81, .34),
    "ifv": (.77, 1, .98, .70, .32),
    "remote": (.78, .97, 1, .73, .37),
}
RUBBER = (48, 48, 43, 255)
TRACK = (62, 60, 52, 255)
TRACK_RIDGE = (76, 74, 64, 255)


def clamp(value, low, high):
    return max(low, min(high, value))


def light(color):
    return (color[0] + 2 * color[1] + color[2]) / 4


def scaled_color(color, gain=1, shift=0):
    return tuple(clamp(round(c * gain + shift), 0, 255) for c in color[:3]) + (255,)


def smoothed_bounds(view, horizontal):
    # A floating antenna or the barrel tip should not set the scale of a view.
    length, depth = (view.width, view.height) if horizontal else (view.height, view.width)
    runs = [sum(view.occupied(i, j) if horizontal else view.occupied(j, i)
                for j in range(depth)) for i in range(length)]
    covered = [i for i, count in enumerate(runs) if count >= max(runs) * .31]
    return covered[0], covered[-1]


class ExteriorViews:
    def __init__(self, filename, trace, view_factory):
        with Image.open(filename) as image:
            im = image.convert("RGB")
        w, h = im.width // 2, im.height // 2
        self.plan, self.side, self.front, self.rear = [
            view_factory(im.crop((left, top, left + w, top + h)))
            for left, top in ((0, 0), (w, 0), (0, h), (w, h))
        ]
        self.plan_x = self.plan.plan_hull()
        self.plan_y = smoothed_bounds(self.plan, False)
        self.side_deck = trace.deck_row * self.side.height
        self.side_ground = smoothed_bounds(self.side, False)[1]
        self.side_x = self.side.span(self.side_deck + 1, self.side_ground)
        self.end_bounds = [smoothed_bounds(view, True) for view in (self.front, self.rear)]
        self.end_ground = [smoothed_bounds(view, False)[1] for view in (self.front, self.rear)]

    def side_roof(self, trace):
        a, b = self.side_x
        start, end = (round(a + t * (b - a)) for t in (trace.aft, trace.nose))
        low = round(self.side_deck)
        # Detect the armor roof across a band. Roof optics are added separately.
        for y in range(max(0, low - 50), low):
            if sum(self.side.occupied(x, y) for x in range(start, end + 1)) >= (end - start) * .52:
                return y
        return low - 6

    def overhead(self, lateral, longitudinal):
        a, b = self.plan_x
        near, far = self.plan_y
        return self.plan, a + (b - a) * longitudinal, (far + near) / 2 - (far - near) * lateral / 2

    def profile(self, longitudinal, y):
        a, b = self.side_x
        return self.side, a + (b - a) * longitudinal, y

    def end(self, lateral, z, deck_ratio, is_rear):
        index = int(is_rear)
        view = (self.front, self.rear)[index]
        a, b = self.end_bounds[index]
        if is_rear:
            lateral = -lateral
        return (view, (a + b) / 2 + (b - a) * lateral / 2,
                self.end_ground[index] - (b - a) * z * deck_ratio)


def sampled(surface):
    view, x, y = surface
    return view.color(x, y)


def photo_midtone(views):
    a, b = views.plan_x
    c, d = views.plan_y
    colors = [views.plan.color(x, y)
              for x in range(a + 4, b - 4, 7) for y in range(c + 4, d - 4, 7)]
    colors = [c for c in colors if c and 57 < light(c) < 185]
    if not colors:
        return (105, 106, 89)
    return tuple(median(c[i] for c in colors) for i in range(3))


def match_hull_finish(path, meta, photo_color):
    width = meta["hullWidth"]
    start = width * (meta["hullBaseHeight"] - 1)
    with Image.open(path) as sheet:
        deck = sheet.convert("RGBA").crop((start, 0, start + width, sheet.height))
    samples = [color for color in deck.getdata() if color[3] and light(color) > 65]
    if not samples:
        return photo_color
    return tuple(round(.88 * median(c[i] for c in samples) + .12 * photo_color[i]) for i in range(3))


def surface_color(reference, base, gain, grain):
    if reference:
        value = light(reference)
        tone = clamp((value - light(base)) * .68, -43, 29)
        base = [base[i] + tone + (reference[i] - value - base[i] + light(base)) * .63
                for i in range(3)]
    return scaled_color(base, gain, grain)


class Stack:
    def __init__(self, width, length, levels):
        self.width = width
        self.length = length
        self.layers = [Image.new("RGBA", (width, length)) for _ in range(levels)]
        self.pixels = [im.load() for im in self.layers]

    def put(self, x, y, z, color):
        x += self.width // 2
        y += self.length // 2
        if 0 <= x < self.width and 0 <= y < self.length and 0 <= z < len(self.layers):
            self.pixels[z][x, y] = color

    def block(self, x, y, z, width, length, height, color, rounded=False):
        for dz in range(height):
            for dy in range(length):
                for dx in range(width):
                    corner = dx in (0, width - 1) and dy in (0, length - 1)
                    if not (rounded and corner):
                        self.put(x + dx - width // 2, y + dy - length // 2, z + dz,
                                 scaled_color(color, .85 if dz + 1 < height else 1.0))

    def save(self, path):
        while self.layers and self.layers[-1].getbbox() is None:
            self.layers.pop()
        sheet = Image.new("RGBA", (self.width * len(self.layers), self.length))
        for z, im in enumerate(self.layers):
            sheet.paste(im, (z * self.width, 0))
        sheet.save(path)
        return len(self.layers)


def shade(x, y, z, occupied, top, side, front, rear, base, reference_offset=(0, 0, 0)):
    grain = (0, -2, 1, 0, 2, -1, 0)[(x * 73 + y * 37 + z * 7) % 7]
    has_side = (x + (-1 if x < 0 else 1), y, z) in occupied
    if (x, y, z + 1) not in occupied:
        face, gain = top, 1.02 if has_side else .98
    elif (x, y + 1, z) not in occupied:
        face, gain = front, .91
    elif (x, y - 1, z) not in occupied:
        face, gain = rear, .87
    else:
        face, gain = side, .89 if not has_side else .84
    reference = sampled(face)
    if reference:
        reference = tuple(c + shift for c, shift in zip(reference, reference_offset))
    return surface_color(reference, base, gain, grain)


def armor_hull(stack, views, base, meta, trace):
    stand = meta["hullBaseHeight"]
    half_width = (stack.width - 1) / 2
    deck_ratio = (stand / stack.width) * .90
    voxels = set()
    for z in range(1, stand):
        top = z / (stand - 1)
        above_tracks = clamp((z - 1) / (trace.track_top - 1), 0, 1)
        boundary = trace.belly + (trace.shoulder - trace.belly) * above_tracks
        if top > .72:
            boundary -= (top - .72) * .59
        for y in range(1 - stack.length // 2, stack.length - stack.length // 2 - 1):
            along = (y + stack.length // 2) / (stack.length - 1)
            profile = views.profile(along, views.side_ground - (views.side_ground - views.side_deck) * top)
            rear_edge = .018 + .065 * (1 - above_tracks)
            fore_edge = .98 - trace.glacis * max(0, (top - .46) / .54)
            if not rear_edge <= along <= fore_edge:
                continue
            if (along < .17 or along > .70) and not profile[0].occupied(*profile[1:]):
                continue
            taper = 1 - max(0, .12 - along, along - .84) * 1.4
            for x in range(-int(half_width * boundary * taper), int(half_width * boundary * taper) + 1):
                lateral = x / half_width
                plan = views.overhead(lateral, along)
                if abs(lateral) > .75 and not plan[0].occupied(*plan[1:]):
                    continue
                # The end views constrain the fenders and the lower nose shoulders.
                is_rear = y < 0
                end = views.end(lateral, top, deck_ratio, is_rear)
                if abs(lateral) > .69 and not end[0].occupied(*end[1:]):
                    continue
                voxels.add((x, y, z))

    for x, y, z in voxels:
        lateral = x / half_width
        along = (y + stack.length // 2) / (stack.length - 1)
        top = z / (stand - 1)
        plan = views.overhead(lateral, along)
        # Suppress any roof image of the turret that would reappear on the deck
        # when the independent turret rotates away.
        if .27 < along < .75 and abs(lateral) < .69:
            plan = (views.plan, -1, -1)
        side = views.profile(along, views.side_ground - (views.side_ground - views.side_deck) * top)
        front = views.end(lateral, top, deck_ratio, False)
        rear = views.end(lateral, top, deck_ratio, True)
        pixel = shade(x, y, z, voxels, plan, side, front, rear, base)
        if (x, y, z + 1) not in voxels and along > .69:
            # The glacis falls away from the deck break. Keep its large plane
            # distinct from the track guards in the small gameplay view.
            pixel = scaled_color(pixel, .93 if (x, y + 1, z) not in voxels else .99)
        stack.put(x, y, z, pixel)

    # Narrow turret-ring foundation and separate rear deck louvers.
    stack.block(0, round(meta["hullPivotOffsetY"]), stand - 1, 9, 9, 1, scaled_color(base, .89), True)
    deck_rear = -stack.length // 2 + 7
    stack.block(0, deck_rear, stand - 1, max(4, stack.width // 3 + 2), 5, 1, scaled_color(base, .79))
    for y in (deck_rear - 2, deck_rear, deck_rear + 2):
        stack.block(0, y, stand - 1, stack.width // 3, 1, 1, scaled_color(base, .62))

    for sign in (-1, 1):
        x = sign * (stack.width // 2 - 4)
        lamp_z = min(stand - 1, trace.track_top + 1)
        front_y = round(stack.length * .39)
        stack.block(x, front_y, lamp_z, 3, 3, 1, scaled_color(base, .72), True)
        stack.put(x, front_y + 1, lamp_z, scaled_color(base, 1.24))
        rear_y = -stack.length // 2 + 3
        stack.put(x, rear_y, lamp_z, scaled_color(base, .61))


def running_gear(stack, views, base, trace, wheel_count, skirted):
    half_width = stack.width // 2 - 1
    fore, aft = stack.length // 2 - 2, -stack.length // 2 + 2
    roof = trace.track_top
    first, last = aft + 3, fore - 3
    spacing = (last - first) / (wheel_count - 1)
    centers = tuple(first + i * spacing for i in range(wheel_count))
    radius = clamp(spacing * .52, 1.3, 3.3)
    rim = surface_color(sampled(views.profile(.35, views.side_ground - 6)), base, .90, 0)
    for z in range(roof + 2):
        for y in range(aft, fore + 1):
            end = min(y - aft, fore - y)
            for side in (-1, 1):
                for lane in range(3):
                    x = side * (half_width - lane)
                    if z <= roof and end >= (2 if z == 0 else 0 if z >= roof - 2 else 1):
                        nearest = min(abs(y - c) for c in centers)
                        wheel = hypot(nearest / radius, (z - roof * .47) / (roof * .46))
                        if z in (0, roof) or end < 2:
                            color = TRACK_RIDGE if (y + z) % 3 == 0 else TRACK
                        elif wheel < 1.12:
                            color = scaled_color(rim, .79 + .24 * (wheel < .55)) if lane == 0 else RUBBER
                        elif lane == 2:
                            color = RUBBER
                        else:
                            continue
                        stack.put(x, y, z, color)
                    if skirted and lane <= 1 and end > (1 if z > roof else 2) and z >= roof - 1:
                        seam = -16 if (y - aft) % 5 == 0 else 0
                        stack.put(x, y, z, scaled_color(base, .92 if z >= roof else .80, seam))
                    elif not skirted and z == roof + 1 and lane <= 1 and end > 1:
                        stack.put(x, y, z, scaled_color(base, 1.02))


def armor_turret(stack, views, base, meta, trace, reference_offset):
    hull_w, hull_l = meta["hullWidth"], meta["hullLength"]
    pivot = meta["hullPivotOffsetY"]
    start, end = ((p - .5) * (hull_l - 2) - pivot for p in (trace.aft, trace.nose))
    reach = min(stack.width // 2 - 1, hull_w * trace.breadth / 2)
    width_curve = PLAN[trace.form]
    deck = views.side_deck
    roof = views.side_roof(trace)
    voxels = set()
    for z in range(trace.levels):
        top = z / (trace.levels - 1)
        rounded = trace.form == "cast"
        inset = (.30 if rounded else .19) * top
        span = (.80 + .20 * min(1, z)) * (1 - inset)
        lower = start + (end - start) * (.08 if z == 0 else .02 + .10 * top)
        upper = end - (end - start) * (.08 if z == 0 else .03 + .19 * top)
        for y in range(round(lower), round(upper) + 1):
            u = clamp(4 * (y - start) / (end - start), 0, 4)
            a = min(3, int(u))
            row_span = (width_curve[a] * (1 - u + a) + width_curve[a + 1] * (u - a))
            along = .5 + (y + pivot) / (hull_l - 2)
            side = views.profile(along, deck - (deck - roof) * top * .94)
            if z > 1 and (y < lower + 2 or y > upper - 2) and not side[0].occupied(*side[1:]):
                row_span *= .77
            for x in range(-int(reach * span * row_span), int(reach * span * row_span) + 1):
                lateral = x / (hull_w / 2)
                # Allow a little registration tolerance at the corners between
                # the two independently generated end views.
                if z > 1 and abs(x) > reach * .78:
                    surface = views.end(lateral, 1 + z / meta["hullBaseHeight"], .85 * meta["hullBaseHeight"] / hull_w, y < 0)
                    if not surface[0].occupied(*surface[1:]):
                        continue
                voxels.add((x, y, z))
    stack.block(0, 0, 0, 5, 6, 1, scaled_color(base, .76), True)
    for x, y, z in voxels:
        along = .5 + (y + pivot) / (hull_l - 2)
        lateral = x / (hull_w / 2)
        top = z / (trace.levels - 1)
        depth = 1 + z / meta["hullBaseHeight"]
        ratio = .85 * meta["hullBaseHeight"] / hull_w
        stack.put(x, y, z, shade(x, y, z, voxels, views.overhead(lateral, along),
                                views.profile(along, deck - (deck - roof) * top * .94),
                                views.end(lateral, depth, ratio, False),
                                views.end(lateral, depth, ratio, True), base, reference_offset))
    return max(y for _, y, _ in voxels)


def turret_fittings(stack, base, trace):
    z = trace.levels - 1
    metal = scaled_color(base, 1.04)
    shadow = scaled_color(base, .74)
    glass = (61, 78, 75, 255)
    for kind, x, y in trace.fittings:
        if kind in ("hatch", "cupola", "vent"):
            width = 5 if kind == "cupola" else 4
            stack.block(x, y, z, width + 1, width + 1, 1, shadow, True)
            stack.block(x, y, z + 1, width, width, 1, metal, True)
            if kind == "cupola":
                stack.block(x, y, z + 2, width - 1, width - 1, 1, base, True)
                stack.block(x, y + 2, z + 1, 3, 1, 1, glass)
        elif kind in ("sight", "bin", "periscope", "optic", "sensor", "rws"):
            width, length, height = {
                "sight": (3, 3, 2), "bin": (4, 4, 1), "periscope": (3, 2, 1),
                "optic": (3, 3, 3), "sensor": (3, 2, 1), "rws": (3, 4, 3),
            }[kind]
            footing = 1 if kind == "sensor" else z
            stack.block(x, y, footing, width, length, height, metal)
            if kind != "bin":
                stack.block(x, y + (length - 1) // 2, footing + height - 1, width - 1, 1, 1, glass)
            if kind == "rws":
                stack.block(x + 1, y + 2, z + 2, 1, 4, 1, shadow)
        elif kind == "mg":
            stack.block(x, y, z + 1, 2, 3, 2, shadow)
            stack.block(x + 1, y + 3, z + 2, 1, 5, 1, shadow)
        elif kind == "basket":
            stack.block(x, y, 1, 13, 3, 1, shadow)
            stack.block(x, y - 2, 3, 13, 1, 1, metal)
            for rib in range(x - 6, x + 7, 3):
                stack.block(rib, y - 1, 2, 1, 3, 2, metal)
        elif kind == "tubes":
            for tube in (x - 1, x + 1):
                stack.block(tube, y, 2, 2, 9, 2, shadow)
                stack.block(tube, y, 4, 2, 9, 1, metal)
                stack.put(tube, y + 4, 4, shadow)
        else:
            size = (4, 5, 2) if kind == "pod" else (3, 3, 2)
            stack.block(x, y, 2, *size, metal, rounded=kind != "pod")
            if kind in ("pod", "searchlight"):
                stack.block(x, y + (size[1] - 1) // 2, 3, size[0] - 1, 1, 1, glass)


def cheek_armor(stack, base, nose, name):
    if name in {"de_leo2a5", "de_leo2a7v", "de_leo2a8", "de_kf51"}:
        for sign in (-1, 1):
            for step in range(4):
                # The arrowhead plate starts at the mantlet and sweeps back
                # into the turret sides. Thin lower seams survive the isometric view.
                x = sign * (3 + 2 * step)
                y = nose - step
                stack.block(x, y, 1, 2, 4, 1, scaled_color(base, .73))
                stack.block(x, y - 1, 2, 2, 4, 2, scaled_color(base, .97), True)
    elif name == "ru_t90a":
        for sign in (-1, 1):
            for step in range(3):
                x = sign * (3 + 2 * step)
                y = nose - 1 - step
                stack.block(x, y, 2, 2, 3, 1, scaled_color(base, .81))
                stack.block(x, y - 1, 3, 2, 2, 1, scaled_color(base, 1.07))


def barrel(stack, base, meta, body_nose, name, form):
    height = meta.get("muzzleHeight") or 2
    tip = meta["muzzleDist"]
    ribs = name not in {"us_m551", "us_m551a1", "de_luchs", "de_puma", "de_wiesel_mk",
                        "us_m2a2", "us_m2a4", "ru_bmpt"}
    narrow = form in ("remote", "ifv") and name not in {"ru_t14", "us_m1128"}
    dark, steel = scaled_color(base, .62), scaled_color(base, .94)
    start = min(body_nose - 2, tip - 4)
    centers = (-1, 1) if name in ("ru_bmp3m", "ru_bmpt") else (0,)
    stack.block(0, start + 1, max(0, height - 1), 3 if narrow else 5, 4, 2, base, True)
    for x in centers:
        tube_tip = tip - 2 if name == "ru_bmp3m" and x == 1 else tip
        for y in range(start, tube_tip + 1):
            collar = ribs and start + 4 <= y <= start + 8
            stack.put(x, y, height, steel if y < tube_tip else dark)
            if (not narrow and not (name == "ru_bmp3m" and x == 1)) or y < start + 3:
                stack.put(x, y, height - 1, dark)
            if collar:
                stack.put(x, y, height + 1, steel)
            if ribs and y < start + 5:
                stack.put(x - 1, y, height, scaled_color(base, .82))
    if name == "us_m8_ags":
        stack.block(0, tip - 1, height, 3, 3, 1, steel, True)
        stack.put(0, tip, height, dark)


def launcher(stack, base, meta, form):
    dark, edge = scaled_color(base, .58), scaled_color(base, 1.02)
    tip, height = meta["muzzleDist"], meta.get("muzzleHeight") or 2
    stack.block(0, 0, 0, 5, 6, 1, dark, True)
    if form == "itv":
        stack.block(0, -1, 1, 3, 4, height - 1, base, True)
        stack.block(0, -1, height - 2, 5, 5, 2, base)
        for x in (-4, 4):
            stack.block(x, 0, height - 1, 4, 11, 2, edge)
            stack.block(x, tip, height, 2, 1, 1, dark)
        stack.block(0, 1, height, 5, 5, 1, edge)
    elif form == "twin_rails":
        for x in (-4, 4):
            stack.block(x, 0, 0, 3, 4, 1, dark)
            stack.block(x, 1, 1, 1, 4, height - 1, base)
            stack.block(x, 1, height, 2, tip + 6, 1, edge)
    else:
        stack.block(0, -2, 1, 3, 5, 2, base)
        for z in range(2, height):
            stack.block(0, z - 3, z, 2, 3, 1, base)
            stack.put(1, 2 - z, z, dark)
        stack.block(0, tip - 6, height - 1, 3, 13, 2, edge, True)
        stack.put(0, tip, height, dark)


def build_vehicle(meta, original, png_dir, photo, view_factory, wheel_count, skirted):
    name = original["id"]
    trace = TRACES[name]
    views = ExteriorViews(photo, trace, view_factory)
    midtone = photo_midtone(views)
    lift = clamp(light(midtone), 100, 145) - light(midtone)
    base = scaled_color(midtone, 1, lift)
    hull_trace = HULLS.get(name)
    if hull_trace:
        meta["hullLength"] = hull_trace.length
        meta["hullBaseHeight"] = hull_trace.mount
        meta["hullPivotOffsetY"] = round(original["hullPivotOffsetY"] *
                                         meta["hullLength"] / original["hullLength"], 3)
        hull = Stack(meta["hullWidth"], meta["hullLength"], meta["hullBaseHeight"] + 1)
        armor_hull(hull, views, base, meta, hull_trace)
        running_gear(hull, views, base, hull_trace, wheel_count, skirted)
        meta["hullSlices"] = hull.save(png_dir / f"{name}_hull_slices.png")
    else:
        base = match_hull_finish(png_dir / f"{name}_hull_slices.png", meta, base)

    turret = Stack(meta["turretWidth"], meta["turretLength"], 12)
    if trace.form in PLAN:
        reference_offset = tuple(c - tone for c, tone in zip(base, midtone))
        nose = armor_turret(turret, views, base, meta, trace, reference_offset)
        cheek_armor(turret, base, nose, name)
        turret_fittings(turret, base, trace)
        barrel(turret, base, meta, nose, name, trace.form)
    else:
        launcher(turret, base, meta, trace.form)
    meta["turretSlices"] = turret.save(png_dir / f"{name}_turret_slices.png")
