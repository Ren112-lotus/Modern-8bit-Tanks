"""Register photoreal vehicle plates to the expansion's sprite-stack scale."""

from dataclasses import replace
from pathlib import Path
from statistics import median

from PIL import Image

from fleet_four_views import View


PLATE_DIR = Path(__file__).with_name("VehicleArt") / "Expansion"
TRACK_DARK = (58, 55, 47)
TRACK_EDGE = (77, 74, 63)

# Rear, front, half-width at successive casemate levels above the deck.
CASEMATES = {
    "ru_su100": ((-9, 14, 8), (-8, 11, 7), (-6, 9, 6)),
    "ru_asu85": ((-10, 14, 7), (-9, 10, 6), (-8, 6, 6)),
    "jp_honi3": ((-8, 11, 7), (-7, 9, 6), (-6, 8, 5)),
}

# Exterior collars, gun cradles and shields read through rotation. The positions
# are on the sprite grid relative to the existing aiming pivot.
COLLARS = {
    "de_a7v": (2, 1, 2),
    "ru_su100": (3, 2, 4), "ru_asu85": (2, 2, 3),
    "jp_honi3": (2, 2, 3), "us_m36": (3, 3, 5),
    "us_m56": (2, 2, 3),
}



def clamp(value, low, high):
    return max(low, min(high, value))


def brightness(rgb):
    return (rgb[0] + rgb[1] * 2 + rgb[2]) / 4


def projection_bounds(view, along_x, fraction):
    length, cross = (view.width, view.height) if along_x else (view.height, view.width)
    counts = [sum(view.occupied(i, j) if along_x else view.occupied(j, i) for j in range(cross))
              for i in range(length)]
    bulk = [i for i, count in enumerate(counts) if count >= max(counts) * fraction]
    return bulk[0], bulk[-1]


def profile_width(view, y, left, right):
    return sum(view.occupied(x, y) for x in range(left, right + 1))


def find_deck(view, left, right, ground, expected):
    # The first broad row below the turret is the deck. Use a neighborhood
    # here so that an antenna, gun or thin fender never defines its height.
    radius = round((ground - expected) * .40)
    for y in range(max(view.top + 2, round(expected) - radius), min(ground - 3, round(expected) + radius)):
        if all(profile_width(view, row, left, right) > (right - left) * .70 for row in (y, y + 1, y + 2)):
            return y
    return expected


class Plate:
    def __init__(self, filename, trace, *, fit_dimensions=True):
        with Image.open(filename) as file:
            image = file.convert("RGB")
        w, h = image.width // 2, image.height // 2
        self.top, self.side, front, rear = [
            View(image.crop((x, y, x + w, y + h)))
            for x, y in ((0, 0), (w, 0), (0, h), (w, h))
        ]
        self.top_length = self.top.plan_hull()
        self.top_width = projection_bounds(self.top, False, .27)
        self.side_x = projection_bounds(self.side, True, .38)
        self.ground = projection_bounds(self.side, False, .22)[1]
        side_length = self.side_x[1] - self.side_x[0]
        nominal_deck = self.ground - side_length * (trace.deck - 1) / max(1, trace.length - 2)
        self.deck = find_deck(self.side, *self.side_x, self.ground, nominal_deck)
        self.ends = tuple(self.register_end(view, trace, side_length) for view in (front, rear))
        self.midtone = self.sample_midtone()
        self.trace = self.fit_trace(trace) if fit_dimensions else trace
        self.body_roof = self.turret_roof(self.side, self.side_x, self.deck, self.trace)
        self.end_roofs = tuple(self.turret_roof(view, (left, right), deck, self.trace, end=True)
                               for view, left, right, _, deck in self.ends)

    def register_end(self, view, trace, side_length):
        left, right = projection_bounds(view, True, .27)
        ground = projection_bounds(view, False, .24)[1]
        height = (self.ground - self.deck) / side_length * (trace.length - 2)
        expected = ground - (right - left) * height / max(1, trace.width - 1)
        return view, left, right, ground, find_deck(view, left, right, ground, expected)

    def sample_midtone(self):
        x1, x2 = self.top_length
        y1, y2 = self.top_width
        points = [
            self.top.color(x, y)
            for x in range(x1 + 3, x2, 6)
            for y in range(y1 + 3, y2, 5)
        ]
        points = [p for p in points if p is not None and 43 < brightness(p) < 192]
        return tuple(median(p[i] for p in points) for i in range(3)) if points else (108, 107, 90)

    def fit_trace(self, trace):
        top_length = self.top_length[1] - self.top_length[0]
        top_width = self.top_width[1] - self.top_width[0]
        # The view registration can measure the hull, but it shouldn't change
        # a model's scale because one rendering clipped a float or rear tail.
        # Use the manual outline as a bound for those cases.
        expected_length = top_length * (trace.width - 1) / max(1, top_width)
        limit = 2 if trace.hull in {"tail", "landship", "amph"} else 3
        length = trace.length + clamp(round((expected_length - trace.length) * .48), -limit, limit)
        deck = trace.deck
        if trace.hull not in {"casemate", "landship", "gunbed", "lee"}:
            estimated = (self.ground - self.deck) * length / (self.side_x[1] - self.side_x[0]) + 1
            deck += clamp(round((estimated - deck) * .56), -1, 1)
        return replace(trace, length=length, deck=deck)

    @staticmethod
    def turret_roof(view, bounds, deck, trace, end=False):
        left, right = bounds
        span = right - left
        if end:
            threshold = span * trace.turret_width / trace.width * .52
        else:
            threshold = span * trace.turret_depth / trace.length * .52
        for y in range(view.top + 1, round(deck) - 2):
            if all(profile_width(view, row, left, right) > threshold for row in (y, y + 1, y + 2)):
                return y
        return deck - 7

    def frames(self, x, y, z, turret=False):
        t = self.trace
        lateral = x / max(1, (t.width - 1) / 2)
        along = .5 + (y + (t.pivot if turret else 0)) / (t.length - 2)
        left, right = self.top_length
        near, far = self.top_width
        top = (self.top, left + (right - left) * along, (near + far) / 2 - (far - near) / 2 * lateral)
        left, right = self.side_x
        if turret:
            level = z / max(1, t.turret_height - 1)
            row = self.deck - (self.deck - self.body_roof) * level
        else:
            level = z / (t.deck - 1)
            row = self.ground - (self.ground - self.deck) * level
        side = self.side, left + (right - left) * along, row
        ends = []
        for i, (view, left, right, ground, deck) in enumerate(self.ends):
            row = (deck - (deck - self.end_roofs[i]) * level if turret
                   else ground - (ground - deck) * level)
            across = lateral if i == 0 else -lateral
            ends.append((view, (left + right) / 2 + (right - left) / 2 * across, row))
        return top, side, *ends

    def paint(self, face, basis, gain, grain):
        view, x, y = face
        color = view.color(x, y)
        if color is None:
            return tuple(clamp(round(c * gain + grain), 0, 255) for c in basis)
        level = brightness(color)
        amount = clamp((level - brightness(self.midtone)) * .27, -16, 13)
        base_level = brightness(basis)
        chroma = [clamp(((c - level) - (base - base_level)) * .34, -9, 9)
                  for c, base in zip(color, basis)]
        return tuple(clamp(round((base + amount + color_shift) * gain + grain), 0, 255)
                     for base, color_shift in zip(basis, chroma))


def covers(frame, tolerance=0):
    view, x, y = frame
    return (view.occupied(x, y) or
            (tolerance and (view.occupied(x - tolerance, y) or view.occupied(x + tolerance, y)
                            or view.occupied(x, y - tolerance) or view.occupied(x, y + tolerance))))


def register_hull(plate, shape):
    """Cut the exposed slopes and widen fenders where the views agree."""
    t = plate.trace
    for z in range(3, t.deck):
        for y in range(-t.length // 2 + 2, t.length // 2):
            for x in range(2 - t.width // 2, t.width // 2 - 1):
                px, py = shape.cx + x, shape.cy + y
                layer = shape.layers[z]
                color = layer.getpixel((px, py))
                near = abs(y) > t.length * .30
                outer = abs(x) >= t.width * .34
                if not (near or outer):
                    continue
                top, side, front, rear = plate.frames(x, y, z)
                end = front if y >= 0 else rear
                top_ok = covers(top, 2)
                side_ok = covers(side, 2)
                end_ok = covers(end, 2)
                if color[3] and (not side_ok and near or not top_ok and outer and not end_ok):
                    # Do not delete the wheel band or the foundation of the turret.
                    if z >= 5 and (near or abs(y - t.pivot) > 5):
                        layer.putpixel((px, py), (0, 0, 0, 0))
                elif not color[3] and z >= 4 and top_ok and side_ok and end_ok:
                    below = shape.layers[z - 1].getpixel((px, py))[3]
                    inside_x = px - 1 if x > 0 else px + 1
                    inside_y = py - 1 if y > 0 else py + 1
                    adjacent = layer.getpixel((inside_x, py))[3] or layer.getpixel((px, inside_y))[3]
                    if below and adjacent:
                        shape.pixel(z, x, y, plate.paint(top, shape.base, .93, 0))


def register_turret(plate, stack):
    """Fit the end and side projections to the raised armored envelope."""
    t = plate.trace
    if t.turret in {"fixed", "twin", "shield", "launcher", "open"}:
        return
    reach, depth = t.turret_width // 2, t.turret_depth // 2
    for z in range(1, t.turret_height):
        layer = stack.layers[z]
        for y in range(-depth, depth + 1):
            for x in range(-reach, reach + 1):
                if abs(y) < max(2, depth - 2) and abs(x) < max(2, reach - 2):
                    continue
                if y >= depth - 2 and abs(x) < 2 and z < 4:
                    continue
                top, side, front, rear = plate.frames(x, y, z, turret=True)
                end = front if y >= 0 else rear
                px, py = stack.cx + x, stack.cy + y
                old = layer.getpixel((px, py))
                low = stack.layers[z - 1].getpixel((px, py))[3]
                support = covers(side, 3) and covers(end, 3)
                if old[3] and not support and abs(x) > 2 and z >= 2:
                    layer.putpixel((px, py), (0, 0, 0, 0))
                elif not old[3] and low and support and covers(top):
                    nx = px - 1 if x > 0 else px + 1
                    ny = py - 1 if y > 0 else py + 1
                    if layer.getpixel((nx, py))[3] and layer.getpixel((px, ny))[3]:
                        stack.pixel(z, x, y, plate.paint(top, stack.base, .91, 0))


def fit_mount(plate, hull, turret, name, gun_z):
    trace = plate.trace
    if name == "us_m3_lee":
        # The offset sponson and short visible tube belong to the hull.
        nose = trace.length // 2
        z = trace.deck - 3
        hull.box(z - 1, z + 1, 4, 7, nose - 8, nose - 5, hull.dark)
        hull.box(z, z + 1, 5, 7, nose - 6, nose - 4, hull.base)
        hull.box(z, z, 6, 6, nose - 5, nose - 1, TRACK_EDGE)
        hull.pixel(z, 6, nose - 1, TRACK_DARK)

    if name in CASEMATES:
        for z in range(trace.deck, trace.deck + 2):
            hull.layers[z].paste((0, 0, 0, 0), (0, 0, hull.width, hull.length))
        for level, (rear, front, width) in enumerate(CASEMATES[name]):
            z = trace.deck - 1 + level
            for y in range(rear, front + 1):
                bevel = max(0, y - front + 3, rear + 2 - y)
                reach = width - bevel
                for x in range(-reach, reach + 1):
                    top, side, _, _ = plate.frames(x, y, z)
                    if level >= 2 and abs(x) == reach and not covers(side, 4):
                        continue
                    hull.pixel(z, x, y, plate.paint(side if abs(x) == reach else top, hull.base,
                                                   .91 if abs(x) == reach else 1.0, 0))

    if name == "de_wiesel_tow":
        turret.ellipse(0, 0, 0, 2, 2, turret.dark)
        turret.box(1, 2, -1, 1, -2, 1, turret.base)
        turret.box(3, 3, -2, -1, -3, 1, turret.dark)
        turret.box(gun_z - 1, gun_z + 1, 0, 0, -5, 6, turret.base)
        turret.box(gun_z, gun_z, -1, 1, -5, 6, TRACK_EDGE)
        turret.pixel(gun_z, 0, trace.muzzle, TRACK_DARK)
    elif name == "us_m103":
        neck = trace.turret_depth // 2 - 4
        for z, radius in ((1, 4), (2, 5), (3, 5), (4, 4), (5, 2)):
            turret.ellipse(z, 0, neck, radius, 4, turret.base if z >= 3 else turret.dark)
        turret.box(gun_z, gun_z, -1, 1, neck + 2, neck + 6, TRACK_EDGE)
    elif name in COLLARS:
        radius, height, length = COLLARS[name]
        start = trace.turret_depth // 2 - 2 if trace.turret == "open" else 0
        for y in range(start, min(trace.muzzle, start + length) + 1):
            taper = 1 - .44 * (y - start) / length
            for z in range(max(0, gun_z - height + 1), min(len(turret.layers), gun_z + height)):
                for x in range(-radius, radius + 1):
                    if (x / radius) ** 2 + ((z - gun_z) / height) ** 2 > taper:
                        continue
                    _, side, front, _ = plate.frames(x, y, z, turret=True)
                    turret.pixel(z, x, y, plate.paint(front if y == start + length else side,
                                                     turret.base, .91 if z >= gun_z else .78, 0))
    elif trace.turret == "shield":
        z = trace.turret_height - 1
        edge = trace.turret_width // 2
        for x in (-edge, edge):
            turret.layers[z].putpixel((turret.cx + x, turret.cy + 1), (0, 0, 0, 0))
        turret.box(gun_z - 1, gun_z, -1, 1, 2, 4, TRACK_EDGE)
        turret.box(gun_z + 1, gun_z + 1, -1, 1, 0, 2, turret.base)
    elif trace.turret == "twin":
        # Open air separates each tube past the support frame.
        tubes = ((-1, 0), (1, 0))
        if "six" in trace.markings:
            tubes += ((-3, 0), (3, 0), (-2, 1), (2, 1))
        elif name == "jp_type60":
            hull.box(trace.deck - 1, trace.deck, 1, 3, -2, 5, hull.dark)
        for z in range(gun_z - 1, gun_z + 2):
            turret.layers[z].paste((0, 0, 0, 0), (0, turret.cy + 5, turret.width, turret.length))
        for x, lift in tubes:
            z = gun_z + lift
            turret.box(z, z, x, x, 2, trace.muzzle, turret.base)
            turret.box(z - 1, z - 1, x, x, 2, 6, TRACK_EDGE)
            turret.pixel(z, x, trace.muzzle, TRACK_DARK)
    elif trace.turret == "launcher":
        turret.box(gun_z, gun_z + 1, -2, 2, -2, 6, TRACK_DARK)
        for x in (-1, 1):
            turret.box(gun_z + 2, gun_z + 2, x, x, -2, 7, TRACK_EDGE)
        turret.box(gun_z + 3, gun_z + 3, 0, 0, -1, 5, turret.base)
    else:
        # Build the collar into the front armor rather than painting an
        # outline around a cylinder. The narrow center also ties the barrel
        # back to the ring if the angled cheeks recede above it.
        center = trace.turret_depth // 2 - 2
        radius = max(1, min(4, trace.turret_width // 5))
        for y in range(center - 1, center + 3):
            reach = max(1, radius - max(0, y - center))
            turret.box(gun_z - 1, gun_z + 1, -reach, reach, y, y, turret.dark)
            turret.box(gun_z + 1, gun_z + 1, -reach + 1, reach - 1, y, y, turret.base)
            turret.pixel(gun_z, 0, y, turret.light)


def finish_part(plate, stack, turret=False):
    """Transfer plate color and lighting while retaining dark mechanical fittings."""
    t = plate.trace
    masks = [layer.getchannel("A").load() for layer in stack.layers]
    for z, layer in enumerate(stack.layers):
        for py in range(1, stack.length - 1):
            for px in range(1, stack.width - 1):
                old = layer.getpixel((px, py))
                if not old[3]:
                    continue
                x, y = px - stack.cx, py - stack.cy
                if not turret and z <= 5 and abs(x) >= t.width // 2 - 2:
                    # Preserve the hand-drawn track links and wheel hubs.
                    continue
                top, side, front, rear = plate.frames(x, y, z, turret)
                topmost = z + 1 == len(masks) or not masks[z + 1][px, py]
                edge = not masks[z][px - 1, py] or not masks[z][px + 1, py]
                grain = (0, -1, 0, 1)[(x + y * 3 + z) % 4]
                if brightness(old) < 68:
                    rgb = tuple(clamp(c + grain, 0, 255) for c in old[:3])
                elif turret and y > t.turret_depth // 2:
                    rgb = plate.paint(side, old[:3], .92 if topmost else .78, grain)
                elif edge and not topmost:
                    rgb = plate.paint(side, old[:3], .94, grain)
                elif not masks[z][px, py - 1]:
                    rgb = plate.paint(rear, old[:3], .91, grain)
                elif not masks[z][px, py + 1]:
                    rgb = plate.paint(front, old[:3], .94, grain)
                else:
                    # The baked-in gun turret image must not appear on a hull deck.
                    under_ring = not turret and abs(y - t.pivot) < t.turret_depth * .40 and abs(x) < t.turret_width * .42
                    source = side if under_ring else top
                    rgb = plate.paint(source, old[:3], 1.02 if topmost else .93, grain)
                stack.pixel(z, x, y, rgb)
