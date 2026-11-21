"""Exterior fittings drawn on the sprite grid for the 55 photo-based vehicles.

Coordinates are game pixels: x across the hull, positive y toward the nose.
Roof coordinates are relative to the hull or turret pivot. The existing four
views establish the armor envelope; this pass draws the parts a photo sample
would otherwise flatten into the paint.
"""
from math import hypot


RECESS = (48, 48, 41)
TREAD = (67, 63, 52)
LINK = (98, 92, 74)
RUBBER = (47, 47, 42)
GLASS = (72, 102, 94)
LAMP = (158, 162, 127)
TAIL_LAMP = (131, 69, 54)
PIPE = (99, 85, 68)


def shade(color, factor):
    return tuple(min(255, round(c * factor)) for c in color)


# Running gear has support rollers, idlers and treads in separate layers.
# Values are the visual suspension layout and total track height.
SUSPENSION = {
    "us_m1917": ("paired", 8), "us_m2a4_light": ("paired", 5),
    "us_m3_stuart": ("trailing", 5), "us_m3_lee": ("paired", 5),
    "us_m4a1": ("paired", 5), "us_m4a3e8": ("horizontal", 5),
    "us_m36": ("paired", 5), "us_m103": ("large", 6),
    "de_a7v": ("enclosed", 5),
    "de_wiesel_mk": ("large", 4), "de_wiesel_tow": ("large", 4),
    "ru_ms1": ("paired", 6), "ru_t26": ("paired", 5),
    "ru_bt7": ("large", 6), "ru_t28": ("enclosed", 5),
    "ru_t34_1941": ("large", 6), "ru_t3485": ("large", 6),
    "ru_su100": ("large", 6), "ru_t44": ("large", 6),
    "ru_it1": ("large", 6), "ru_obj906": ("large", 5),
    "jp_hago": ("paired", 5), "jp_chiha": ("japanese", 5),
    "jp_shinhoto": ("japanese", 5), "jp_chihe": ("japanese", 5),
    "jp_chinu": ("japanese", 5), "jp_honi3": ("japanese", 5),
    "jp_kami": ("paired", 5), "jp_type74": ("large", 6),
}


def draw_wheel(stack, x, center, level, radius_y, radius_z, face, hub, rubber=RUBBER):
    for y in range(round(center - radius_y), round(center + radius_y) + 1):
        for z in range(max(1, round(level - radius_z)), round(level + radius_z) + 1):
            distance = hypot((y - center) / radius_y, (z - level) / radius_z)
            if distance > 1:
                continue
            color = rubber
            if distance < .76:
                color = shade(face, 1.08 if z > level else .77 if z < level else 1)
            if distance < .32:
                color = hub
            stack.pixel(z, x, y, color)


def suspension(stack, trace, vehicle_id):
    reach = trace.width // 2 - 1
    if trace.hull == "wheeled":
        for side in (-1, 1):
            for center in (-14, -6, 5, 14):
                for offset in (2, 1, 0):
                    draw_wheel(stack, side * (reach - offset), center, 3, 3.45, 3.2,
                               shade(stack.base, .77), shade(stack.base, 1.14))
                stack.pixel(0, reach * side, center, TREAD)
                stack.pixel(4, reach * side, center, shade(stack.base, .90))
        return

    layout, height = SUSPENSION.get(vehicle_id, ("independent", 5))
    height = min(height, trace.deck - 1)
    band = 3 if trace.width >= 20 else 2
    half_length = trace.length // 2
    rear = -half_length + (6 if trace.hull == "tail" else 7 if "float" in trace.markings else 2)
    front = half_length - (8 if "float" in trace.markings else 3)
    if layout == "trailing":
        rear -= 1
    first, last = rear + 3, front - 3
    gap = (last - first) / (trace.wheels - 1)
    centers = [first + i * gap for i in range(trace.wheels)]
    paired = layout in {"paired", "trailing", "horizontal", "japanese"}
    if paired:
        centers = [y + gap * (.19 if i % 2 == 0 else -.19) for i, y in enumerate(centers)]
    if layout == "japanese":
        centers[0], centers[-1] = first - 1, last + 1
    level = (height - 1) * .5 if layout in {"large", "interleaved"} else (height - 1) * .44
    radius_y = max(.9, gap * (.70 if layout == "interleaved" else .52 if layout == "large" else .43))
    radius_z = height * (.49 if layout in {"large", "interleaved"} else .35)
    for side in (-1, 1):
        outer = reach * side
        inner = (reach - band + 1) * side
        left, right = sorted((inner, outer))
        for z, a, b in ((0, rear + 2, front - 1), (height, rear + 1, front - 1)):
            stack.box(z, z, left, right, a, b, TREAD)
            for y in range(a, b + 1, 2):
                stack.box(z, z, left, right, y, y, LINK if (y - a) % 4 == 0 else (81, 77, 64))

        for z in range(1, height):
            heel = 1 if z == 1 else 0
            stack.box(z, z, left, right, rear + heel, rear + 1 + heel, TREAD)
            stack.box(z, z, left, right, front - 1 - heel, front - heel, TREAD)
        stack.box(2, 2, inner, inner, rear + 1, front - 1, RECESS)
        if layout == "interleaved":
            for y in centers:
                draw_wheel(stack, outer - side, y, level, radius_y + .3, radius_z, stack.dark, LINK)
        for i, y in enumerate(centers):
            inset = side if layout == "interleaved" and i % 2 else 0
            draw_wheel(stack, outer - inset, y, level, radius_y, radius_z,
                       shade(stack.base, .84), shade(stack.base, 1.12))
        for y, z in ((rear + 1, height * .51), (front - 1, height * (.66 if trace.hull == "tail" else .55))):
            draw_wheel(stack, outer, y, z, 1.55, height * .35, stack.dark, LINK, TREAD)
        if paired:
            for a, b in zip(centers[::2], centers[1::2]):
                y = round((a + b) / 2)
                stack.box(height - 1, height - 1, left, right, y - 1, y + 1, stack.dark)
                stack.pixel(height - 1, outer, y, LINK)
        if layout == "enclosed":
            z = height - 1
            stack.box(z, z + 1, outer, outer, rear + 2, front - 2, stack.dark)
            for y in range(rear + 3, front - 2, 3):
                stack.pixel(z + 1, outer, y, shade(stack.base, .94))


class Exterior:
    def __init__(self, stack):
        self.stack = stack
        self.mask = [layer.getchannel("A").load() for layer in stack.layers]

    def roof_at(self, x, y):
        px, py = self.stack.cx + x, self.stack.cy + y
        if 0 <= px < self.stack.width and 0 <= py < self.stack.length:
            return next((z for z in reversed(range(len(self.mask))) if self.mask[z][px, py]), -1)
        return -1

    def brush(self, x, y, color, lift=0):
        z = self.roof_at(x, y)
        if z < 0:
            return
        for dz in range(min(lift, len(self.mask) - z - 1) + 1):
            self.stack.pixel(z + dz, x, y, color)

    def hatch(self, x, y, rx, ry, shape="round"):
        for dy in range(-ry, ry + 1):
            for dx in range(-rx, rx + 1):
                r = hypot(dx / (rx + .16), dy / (ry + .16))
                if shape == "round" and r > 1:
                    continue
                edge = r > .70 if shape == "round" else abs(dx) == rx or abs(dy) == ry
                paint = self.stack.paint(x + dx, y + dy)
                color = shade(paint, .64 if edge else 1.15)
                if shape == "split" and dx == 0:
                    color = shade(paint, .65)
                self.brush(x + dx, y + dy, color, 0 if edge else 1)
        self.brush(x, y - ry, shade(self.stack.paint(x, y), 1.30))
        self.brush(x + max(0, rx - 1), y + max(0, ry - 1), self.stack.dark, 1)

    def grille(self, x, y, rx, ry, direction="cross"):
        for dy in range(-ry, ry + 1):
            for dx in range(-rx, rx + 1):
                lip = abs(dx) == rx or abs(dy) == ry
                slat = (dx if direction == "length" else dy) % 2
                paint = self.stack.paint(x + dx, y + dy)
                color = shade(paint, 1.08 if lip else .76) if (lip or slat) else RECESS
                self.brush(x + dx, y + dy, color)

    def locker(self, x, y, rx, ry, raised=1):
        for dy in range(-ry, ry + 1):
            for dx in range(-rx, rx + 1):
                side = abs(dx) == rx or abs(dy) == ry
                color = shade(self.stack.paint(x + dx, y + dy), .75 if side else 1.12)
                self.brush(x + dx, y + dy, color, max(0, raised - 1) if side else raised)
        self.brush(x, y + ry, shade(self.stack.paint(x, y), 1.22), max(0, raised - 1))

    def sight(self, x, y, rx=1):
        for dx in range(-rx, rx + 1):
            self.brush(x + dx, y - 1, shade(self.stack.paint(x, y), 1.20), 1)
            self.brush(x + dx, y, GLASS if dx == 0 else self.stack.dark, 1)

    def cupola(self, x, y, radius, height=2):
        z = self.roof_at(x, y)
        if z < 0:
            return
        top = min(z + height, len(self.mask) - 1)
        for layer in range(z, top):
            self.stack.ellipse(layer, x, y, radius, radius, shade(self.stack.paint(x, y), .83))
        self.stack.ellipse(top, x, y, radius, radius, shade(self.stack.paint(x, y), 1.18))
        self.stack.pixel(top, x, y + radius, GLASS)
        self.stack.pixel(top, x, y - 1, self.stack.dark)

    def seam(self, x0, y0, x1, y1, rivets=False):
        steps = max(abs(x1 - x0), abs(y1 - y0))
        for i in range(steps + 1):
            x = x0 + round((x1 - x0) * i / max(1, steps))
            y = y0 + round((y1 - y0) * i / max(1, steps))
            paint = self.stack.paint(x, y)
            self.brush(x, y, shade(paint, 1.19 if rivets and i % 3 == 0 else .74))

    def rail(self, x0, y0, x1, y1):
        steps = max(abs(x1 - x0), abs(y1 - y0))
        anchors = [self.roof_at(x, y) for x, y in ((x0, y0), (x1, y1))]
        z = min(min(anchors) + 1, len(self.mask) - 1)
        if min(anchors) < 0:
            return
        for i in range(steps + 1):
            x = x0 + round((x1 - x0) * i / max(1, steps))
            y = y0 + round((y1 - y0) * i / max(1, steps))
            self.stack.pixel(z, x, y, shade(self.stack.paint(x, y), 1.15 if i % 4 else .78))

    def pipe(self, x, y, length, cross=False):
        for i in range(length):
            px, py = (x + i, y) if cross else (x, y + i)
            self.brush(px, py, RECESS if i in (0, length - 1) else PIPE, 1)

    def lamps(self, x, y, rear=False):
        color = TAIL_LAMP if rear else LAMP
        for side in (-1, 1):
            self.brush(side * x, y, self.stack.dark, 1)
            self.brush(side * x, y + (-1 if rear else 1), color)

    def end_plate(self, x, z, rx, height, side="rear"):
        stack = self.stack
        for dx in range(-rx, rx + 1):
            for dz in range(height):
                px = stack.cx + x + dx
                if not 0 <= px < stack.width:
                    continue
                rows = range(stack.length) if side == "rear" else reversed(range(stack.length))
                py = next((row for row in rows if self.mask[z + dz][px, row]), None)
                if py is None:
                    continue
                edge = abs(dx) == rx or dz in (0, height - 1)
                color = shade(stack.paint(x + dx, py - stack.cy), .77 if edge else 1.10)
                stack.pixel(z + dz, x + dx, py - stack.cy, color)



    def fenders(self, rear, front, level, gap=0):
        stack = self.stack
        reach = stack.width // 2 - 1
        for side in (-1, 1):
            for y in range(rear, front + 1):
                if gap and (y - rear) % gap == gap - 1:
                    continue
                for x in (reach - 1, reach):
                    paint = stack.paint(x * side, y)
                    stack.pixel(level, x * side, y, shade(paint, 1.14 if x == reach else .96))
            for y in (rear, front):
                stack.pixel(level - 1, reach * side, y, stack.dark)

    def spare(self, x, y, length):
        for dy in range(length):
            self.brush(x, y + dy, LINK if dy % 2 == 0 else TREAD, 1)
            self.brush(x + 1, y + dy, TREAD if dy % 2 == 0 else LINK)

    def basket(self, rx, y, depth=3):
        z = self.roof_at(0, y + 1)
        if z < 2:
            return
        for dy in range(depth):
            row = y - dy
            self.stack.box(z - 2, z - 2, -rx, rx, row, row, self.stack.dark)
            for x in (-rx, rx):
                self.stack.pixel(z, x, row, LINK)
                self.stack.pixel(z - 1, x, row, RECESS)
        for x in range(-rx, rx + 1):
            self.stack.pixel(z, x, y - depth + 1, LINK if x % 2 else self.stack.dark)


# Individually laid out in the four-view coordinate system. Most ports and
# vents are cut into the hull surface; lockers, rails and hatches have relief.
# Tuples are (operation, x, y, ...) except fenders and baskets.
# Hull and turret layouts stay separate so fittings follow the correct pivot.
HULL = {
    "us_m1917": (
        ("hatch", -1, 7, 2, 2, "split"), ("seam", -4, -4, -4, 8, True),
        ("grille", 0, -6, 2, 2, "length"), ("pipe", 4, -8, 7), ("end_plate", 0, 4, 2, 3)),
    "us_m2a4_light": (
        ("fenders", -10, 10, 5), ("hatch", 2, 6, 2, 2, "rect"), ("sight", -2, 8),
        ("grille", 0, -8, 3, 2), ("locker", -5, -6, 1, 3), ("seam", -5, 9, 4, 9, True)),
    "us_m3_stuart": (
        ("fenders", -12, 10, 5), ("hatch", -2, 7, 2, 2, "rect"), ("sight", 3, 8),
        ("grille", 0, -9, 3, 3, "length"), ("locker", 5, -8, 1, 3), ("lamps", 5, 10), ("end_plate", 0, 3, 4, 4)),
    "us_m3_lee": (
        ("fenders", -15, 15, 5), ("hatch", -4, 9, 2, 3, "rect"), ("sight", -3, 12),
        ("seam", -6, 10, 5, 10, True), ("grille", 0, -11, 4, 3, "length"),
        ("locker", -7, -6, 1, 3), ("pipe", -4, -15, 8, True), ("end_plate", 0, 3, 5, 4)),
    "us_m4a1": (
        ("hatch", -3, 6, 2, 2), ("hatch", 3, 6, 2, 2), ("sight", -3, 9),
        ("grille", 0, -11, 5, 3, "length"), ("locker", -6, -12, 1, 3),
        ("lamps", 6, 12), ("seam", -6, 11, 6, 11), ("end_plate", 0, 3, 4, 4)),
    "us_m4a3e8": (
        ("fenders", -17, 17, 5), ("hatch", -4, 9, 2, 3), ("hatch", 4, 9, 2, 3),
        ("grille", -3, -13, 2, 3), ("grille", 3, -13, 2, 3), ("lamps", 7, 15),
        ("spare", 7, -12, 6), ("end_plate", 0, 3, 5, 4)),
    "us_m24": (
        ("fenders", -14, 13, 5), ("hatch", -4, 8, 2, 2), ("hatch", 3, 8, 2, 2),
        ("grille", -3, -11, 2, 3), ("grille", 3, -11, 2, 3), ("lamps", 6, 12), ("locker", 6, -5, 1, 3)),
    "us_m36": (
        ("fenders", -16, 15, 5), ("hatch", -3, 9, 2, 2, "rect"), ("hatch", 3, 9, 2, 2, "rect"),
        ("grille", -3, -11, 2, 3), ("grille", 3, -11, 2, 3), ("seam", -6, 12, 6, 12),
        ("spare", -7, -6, 5), ("lamps", 6, 13)),
    "us_m26": (
        ("fenders", -16, 16, 6), ("hatch", -4, 9, 2, 2), ("hatch", 4, 9, 2, 2),
        ("grille", 0, -12, 6, 3, "length"), ("locker", 8, -7, 1, 5), ("lamps", 7, 14), ("end_plate", 0, 4, 6, 4)),
    "us_m46": (
        ("fenders", -17, 17, 6), ("hatch", -4, 9, 2, 2), ("hatch", 4, 9, 2, 2),
        ("grille", -3, -13, 2, 4), ("grille", 3, -13, 2, 4), ("pipe", -8, -15, 8),
        ("pipe", 8, -15, 8), ("lamps", 8, 14), ("end_plate", 0, 4, 6, 4)),
    "us_m41": (
        ("fenders", -15, 14, 6, 6), ("hatch", -4, 8, 2, 2), ("sight", -3, 10),
        ("grille", -3, -11, 2, 3, "length"), ("grille", 3, -11, 2, 3, "length"),
        ("locker", 7, -9, 1, 4), ("lamps", 7, 12), ("end_plate", 0, 3, 5, 4)),
    "us_m56": (
        ("hatch", -3, 7, 2, 2, "rect"), ("grille", 2, 7, 2, 2, "length"),
        ("locker", -4, -6, 2, 3), ("locker", 4, -6, 2, 3),
        ("seam", -5, -10, 5, -10), ("end_plate", 0, 2, 4, 3)),
    "us_m50": (
        ("fenders", -11, 10, 4), ("hatch", -3, 6, 2, 2, "rect"), ("sight", -3, 8),
        ("grille", 3, 6, 2, 2, "length"), ("lamps", 5, 9), ("end_plate", -2, 3, 2, 4), ("end_plate", 3, 3, 2, 4)),
    "us_m103": (
        ("fenders", -21, 21, 6), ("hatch", 0, 12, 2, 2), ("sight", 0, 15), ("lamps", 9, 17),
        ("grille", -5, -16, 4, 4), ("grille", 5, -16, 4, 4), ("locker", 10, -11, 1, 5), ("end_plate", 0, 4, 8, 4)),
    "de_a7v": (
        ("grille", -6, -11, 2, 4, "length"), ("grille", 6, -11, 2, 4, "length"), ("sight", 0, 4, 2),
        ("hatch", 0, -2, 2, 2, "split"), ("seam", -8, 13, 8, 13, True), ("seam", -8, -16, 8, -16, True),
        ("seam", -8, -16, -8, 14, True), ("seam", 8, -16, 8, 14, True),
        ("end_plate", -5, 5, 2, 7), ("end_plate", 5, 5, 2, 7)),
    "de_pz2c": (
        ("fenders", -13, 13, 5), ("hatch", -2, 5, 2, 2, "rect"), ("sight", -2, 7),
        ("grille", 3, -9, 2, 3, "length"), ("locker", -5, -9, 1, 2), ("lamps", 5, 9),
        ("end_plate", 0, 4, 3, 3)),
    "de_pz3j": (
        ("fenders", -16, 16, 5), ("sight", -4, 10), ("hatch", 3, 8, 2, 2, "rect"),
        ("grille", -4, -11, 2, 3), ("grille", 4, -11, 2, 3),
        ("spare", 7, 1, 5), ("locker", -7, -8, 1, 3), ("end_plate", 0, 5, 4, 3)),
    "de_pz4f2": (
        ("fenders", -17, 17, 7), ("hatch", -5, 8, 1, 2, "rect"), ("hatch", 5, 8, 1, 2, "rect"),
        ("grille", -5, -13, 2, 3, "length"), ("grille", 5, -13, 2, 3, "length"),
        ("seam", -1, -15, -1, -10), ("locker", -9, -7, 1, 3), ("rail", 9, -2, 9, 5)),
    "de_stug3g": (
        ("fenders", -16, 16, 5), ("hatch", 3, -2, 2, 2, "split"), ("cupola", -3, -3, 2),
        ("sight", -3, 4), ("grille", -3, -12, 2, 3), ("grille", 3, -12, 2, 3),
        ("spare", -8, -6, 6), ("seam", -6, 11, 6, 11), ("rail", -5, -12, 5, -12)),
    "de_panther_g": (
        ("fenders", -20, 19, 7, 7), ("hatch", -5, 9, 2, 1, "rect"), ("hatch", 5, 9, 2, 1, "rect"),
        ("seam", -2, -18, -2, -10), ("pipe", 10, -4, 9), ("spare", -11, -14, 6)),
    "de_tiger_i": (
        ("fenders", -19, 19, 7, 6), ("hatch", -5, 10, 2, 2, "rect"), ("hatch", 5, 10, 2, 2, "rect"),
        ("grille", -3, -14, 2, 3), ("grille", 3, -14, 2, 3), ("seam", -9, 14, 9, 14),
        ("rail", -10, -9, -10, 5), ("spare", 10, -8, 6), ("end_plate", 0, 4, 6, 5)),
    "de_jagdpanther": (
        ("fenders", -20, 19, 6, 7), ("hatch", -1, -1, 3, 2, "split"), ("sight", -4, 4),
        ("grille", 0, -15, 2, 3, "length"), ("pipe", -10, -8, 9),
        ("spare", 10, -14, 5), ("rail", 7, -3, 7, 3), ("end_plate", 0, 4, 5, 5)),
    "de_m41g": (
        ("fenders", -16, 15, 6, 6), ("hatch", -4, 7, 2, 2), ("sight", -3, 9),
        ("grille", -3, -12, 2, 3, "length"), ("grille", 3, -12, 2, 3, "length"),
        ("spare", -7, -7, 4), ("locker", 7, 2, 1, 3), ("end_plate", 0, 3, 5, 4)),
    "de_kanjpz": (
        ("fenders", -17, 16, 5, 8), ("cupola", 3, 1, 2, 1), ("hatch", -3, 0, 2, 2, "rect"), ("sight", -3, 5),
        ("grille", -3, -12, 2, 3, "length"), ("grille", 3, -12, 2, 3, "length"),
        ("locker", 7, -7, 1, 3), ("end_plate", 0, 3, 6, 3)),
    "de_ru251": (
        ("fenders", -18, 17, 6), ("hatch", 5, 9, 2, 2), ("sight", 5, 11),
        ("grille", -4, -13, 3, 3, "length"), ("grille", 4, -13, 3, 3, "length"),
        ("locker", -9, -10, 1, 3), ("rail", 9, -6, 9, 3)),
    "de_wiesel_mk": (
        ("hatch", 2, 3, 1, 2), ("sight", 2, 5), ("grille", -2, 1, 1, 3, "length"),
        ("locker", -3, -6, 1, 2), ("lamps", 4, 5), ("end_plate", 0, 2, 3, 3)),
    "de_wiesel_tow": (
        ("hatch", 2, 3, 1, 2), ("sight", 2, 5), ("grille", -2, 1, 1, 3, "length"),
        ("hatch", 1, -5, 2, 2, "rect"), ("lamps", 4, 5), ("end_plate", 0, 2, 3, 3)),
    "ru_ms1": (
        ("hatch", 0, 7, 2, 2, "split"), ("grille", 0, -5, 2, 2, "length"),
        ("seam", -4, -5, -4, 7, True), ("pipe", 3, -7, 6), ("end_plate", 0, 4, 2, 3)),
    "ru_t26": (
        ("fenders", -11, 11, 5), ("hatch", 3, 8, 2, 2, "rect"), ("sight", 3, 10),
        ("grille", -1, -8, 3, 3, "length"), ("pipe", 3, -12, 6),
        ("locker", -5, -6, 1, 4), ("seam", -5, 9, 4, 9, True)),
    "ru_bt7": (
        ("fenders", -13, 12, 5), ("hatch", 0, 10, 2, 2, "split"), ("sight", 0, 12),
        ("grille", 0, -9, 3, 4, "length"), ("pipe", -5, -13, 7),
        ("seam", -4, 8, 4, 8, True), ("end_plate", 0, 4, 3, 3)),
    "ru_t28": (
        ("fenders", -22, 22, 7, 6), ("sight", 0, 16), ("hatch", 0, 13, 1, 2, "rect"),
        ("hatch", -4, 11, 1, 1), ("hatch", 4, 11, 1, 1), ("grille", -3, -16, 2, 4),
        ("grille", 3, -16, 2, 4), ("pipe", -6, -20, 7), ("pipe", 6, -20, 7), ("end_plate", 0, 4, 6, 4)),
    "ru_t34_1941": (
        ("hatch", -3, 11, 3, 2, "rect"), ("sight", -3, 12), ("lamps", 6, 11),
        ("grille", 0, -10, 4, 4, "length"), ("pipe", -4, -14, 4),
        ("pipe", 4, -14, 4), ("spare", 6, -9, 4), ("end_plate", 0, 4, 5, 3)),
    "ru_kv1": (
        ("fenders", -18, 16, 6), ("sight", 0, 12), ("hatch", 1, 9, 2, 2, "rect"),
        ("grille", -4, -12, 2, 3), ("grille", 4, -12, 2, 3), ("locker", -8, -8, 1, 5),
        ("locker", 8, -2, 1, 5), ("seam", -7, 13, 7, 13), ("end_plate", 0, 4, 6, 4)),
    "ru_t3485": (
        ("hatch", -3, 10, 3, 2, "rect"), ("sight", -2, 12), ("lamps", 6, 11),
        ("grille", 0, -11, 4, 3, "length"), ("pipe", -6, -12, 7),
        ("pipe", 6, -11, 7), ("seam", -5, -9, 5, -9), ("end_plate", 0, 4, 5, 3)),
    "ru_is2": (
        ("fenders", -17, 16, 6), ("sight", -1, 13), ("grille", 0, -13, 5, 4, "length"),
        ("pipe", -8, -15, 9), ("pipe", 8, -15, 6), ("spare", 7, 9, 4),
        ("lamps", 7, 13), ("end_plate", 0, 4, 6, 5)),
    "ru_su100": (
        ("cupola", 4, 1, 2), ("hatch", -3, 1, 2, 2, "split"), ("sight", -3, 7),
        ("grille", 0, -11, 4, 4, "length"), ("pipe", -7, -11, 8),
        ("pipe", 7, -11, 6), ("lamps", 7, 12), ("end_plate", 0, 4, 5, 4)),
    "ru_t44": (
        ("hatch", -3, 9, 2, 2), ("sight", -3, 11), ("grille", 0, -11, 5, 4, "length"),
        ("locker", 7, -7, 1, 3), ("pipe", -7, -13, 8), ("lamps", 7, 12), ("end_plate", 0, 4, 6, 3)),
    "ru_asu57": (
        ("hatch", -3, 2, 2, 2, "rect"), ("grille", -1, 7, 2, 2),
        ("locker", -4, -6, 1, 3), ("locker", 4, -6, 1, 3),
        ("rail", -4, -8, 4, -8), ("end_plate", 0, 2, 3, 3)),
    "ru_asu85": (
        ("fenders", -15, 14, 5), ("hatch", 3, 0, 2, 2, "split"), ("sight", 3, 5),
        ("grille", -2, -11, 3, 3, "length"), ("pipe", -6, -11, 7),
        ("locker", 6, -6, 1, 4), ("lamps", 6, 11), ("end_plate", 0, 3, 5, 4)),
    "ru_it1": (
        ("fenders", -17, 16, 6), ("hatch", -3, 9, 2, 2), ("sight", -3, 11),
        ("grille", 1, -12, 5, 4, "length"), ("locker", 8, 1, 1, 4),
        ("pipe", -8, -12, 10), ("lamps", 8, 13), ("end_plate", 0, 4, 6, 4)),
    "ru_obj906": (
        ("seam", -6, 10, 6, 10), ("hatch", 0, 12, 2, 2), ("sight", 0, 15),
        ("grille", 0, -12, 4, 4, "length"), ("rail", -6, -13, -6, -5),
        ("locker", 6, -7, 1, 4), ("lamps", 6, 14), ("end_plate", 0, 3, 5, 4)),
    "jp_hago": (
        ("fenders", -12, 12, 5), ("sight", 3, 8), ("hatch", 3, 6, 2, 2, "rect"),
        ("grille", 0, -9, 2, 3, "length"), ("pipe", 5, -11, 7), ("seam", -5, 9, 4, 9, True)),
    "jp_chiha": (
        ("fenders", -14, 13, 5), ("sight", 3, 9), ("hatch", 3, 7, 2, 2, "rect"),
        ("grille", -3, -10, 2, 3, "length"), ("pipe", 6, -12, 8),
        ("locker", -6, -7, 1, 3), ("seam", -5, 10, 5, 10, True)),
    "jp_shinhoto": (
        ("fenders", -14, 13, 5), ("sight", 3, 9), ("hatch", 3, 7, 2, 2, "rect"),
        ("grille", -3, -10, 2, 3, "length"), ("pipe", 6, -12, 8), ("locker", -6, -7, 1, 3),
        ("seam", -5, 10, 5, 10, True), ("end_plate", 0, 4, 4, 4)),
    "jp_chihe": (
        ("fenders", -14, 14, 5), ("hatch", 3, 8, 2, 2, "rect"), ("sight", 3, 9),
        ("grille", -3, -10, 2, 3), ("grille", 3, -10, 2, 3), ("pipe", 6, -12, 7),
        ("seam", -5, 11, 5, 11), ("end_plate", 0, 4, 5, 4)),
    "jp_chinu": (
        ("fenders", -15, 14, 5), ("hatch", 3, 9, 2, 2, "rect"), ("sight", 3, 10),
        ("grille", -3, -11, 2, 3), ("grille", 3, -11, 2, 3), ("locker", 6, -7, 1, 3),
        ("pipe", -6, -13, 8), ("end_plate", 0, 4, 5, 4)),
    "jp_chito": (
        ("fenders", -17, 17, 5), ("hatch", -4, 10, 2, 2, "rect"), ("sight", -4, 12),
        ("grille", -4, -12, 2, 4, "length"), ("grille", 4, -12, 2, 4, "length"),
        ("locker", 7, -10, 1, 4), ("lamps", 7, 14), ("end_plate", 0, 4, 5, 4)),
    "jp_chiri": (
        ("fenders", -20, 18, 6), ("hatch", 4, 12, 2, 2, "rect"), ("sight", 4, 14),
        ("grille", -4, -13, 3, 4, "length"), ("grille", 4, -13, 3, 4, "length"),
        ("locker", 8, -9, 1, 5), ("seam", -7, 14, 7, 14), ("end_plate", 0, 4, 6, 5)),
    "jp_kami": (
        ("seam", -7, 11, 7, 11, True), ("seam", -7, -12, 7, -12, True),
        ("hatch", 3, 7, 2, 2, "rect"), ("sight", 3, 8), ("grille", 0, -9, 3, 2, "length"),
        ("rail", -5, 13, -3, 19), ("rail", 5, 13, 3, 19), ("end_plate", 0, 4, 5, 3)),
    "jp_honi3": (
        ("fenders", -14, 13, 5), ("hatch", -2, 0, 3, 2, "split"), ("sight", 3, 4),
        ("seam", -4, 5, 4, 5, True), ("grille", -2, -10, 3, 3, "length"),
        ("pipe", 6, -12, 7), ("locker", -6, -5, 1, 4), ("end_plate", 0, 4, 4, 4)),
    "jp_m24": (
        ("fenders", -14, 13, 5), ("hatch", -4, 8, 2, 2), ("hatch", 3, 8, 2, 2),
        ("grille", -3, -11, 2, 3), ("grille", 3, -11, 2, 3), ("lamps", 6, 12),
        ("locker", 6, -5, 1, 3), ("end_plate", 0, 4, 5, 3)),
    "jp_type60": (
        ("fenders", -12, 11, 4), ("hatch", -3, 2, 2, 2, "split"), ("sight", -3, 5),
        ("grille", 1, -8, 3, 2, "length"), ("locker", 4, -6, 1, 3), ("end_plate", 0, 2, 4, 3)),
    "jp_type61": (
        ("fenders", -17, 16, 6), ("hatch", 4, 9, 2, 2), ("sight", 4, 11),
        ("grille", -4, -13, 3, 3), ("grille", 4, -13, 3, 3), ("locker", 8, -7, 1, 5),
        ("lamps", 7, 13), ("end_plate", 0, 4, 6, 4)),
    "jp_type74": (
        ("fenders", -18, 17, 6), ("hatch", -4, 9, 2, 2), ("sight", -4, 11),
        ("grille", -4, -13, 3, 3, "length"), ("grille", 4, -13, 3, 3, "length"),
        ("locker", 8, -9, 1, 4), ("lamps", 8, 14), ("end_plate", 0, 4, 6, 4)),
    "jp_type90": (
        ("fenders", -18, 17, 6, 6), ("hatch", -5, 10, 2, 2), ("sight", -5, 12),
        ("grille", -4, -14, 3, 4), ("grille", 4, -14, 3, 4), ("locker", 8, -12, 1, 4),
        ("lamps", 8, 15), ("end_plate", 0, 4, 7, 4)),
    "jp_type16": (
        ("grille", -4, 9, 3, 4, "length"), ("hatch", 5, 10, 2, 2), ("sight", 5, 13),
        ("seam", -7, 11, -7, 17), ("lamps", 7, 16), ("locker", -7, -12, 1, 3),
        ("end_plate", 0, 4, 5, 4)),
    "jp_type89ifv": (
        ("fenders", -18, 17, 6, 7), ("grille", -4, 9, 3, 3, "length"),
        ("hatch", 5, 9, 2, 2), ("sight", 5, 12), ("hatch", -3, -13, 3, 3, "rect"),
        ("hatch", 3, -13, 3, 3, "rect"), ("lamps", 7, 14), ("lamps", 8, -17, True),
        ("end_plate", -4, 3, 2, 5), ("end_plate", 4, 3, 2, 5)),
}



TURRET = {
    "us_m1917": (("hatch", 0, -1, 2, 2, "rect"), ("sight", 0, 2), ("seam", -2, -2, 2, -2, True)),
    "us_m2a4_light": (("hatch", 0, -1, 2, 2, "split"), ("sight", -1, 3), ("seam", -3, -3, 2, -3, True)),
    "us_m3_stuart": (("cupola", -1, -1, 2), ("sight", 2, 2), ("locker", 0, -4, 2, 1), ("seam", -3, 2, 3, 2, True)),
    "us_m3_lee": (("cupola", -1, -1, 2, 2), ("sight", 1, 2), ("seam", -2, -3, 2, -3, True)),
    "us_m4a1": (("cupola", -3, -2, 2, 2), ("sight", 2, 2), ("hatch", 2, -2, 1, 2), ("rail", -2, -4, 2, -4)),
    "us_m4a3e8": (("cupola", -3, -2, 2), ("hatch", 3, -1, 2, 2), ("sight", 2, 3), ("locker", 0, -5, 3, 1)),
    "us_m24": (("cupola", -3, -2, 2), ("hatch", 3, -2, 2, 2), ("sight", 2, 2), ("locker", 0, -5, 3, 1)),
    "us_m36": (("rail", -5, -4, -5, 2), ("rail", 5, -4, 5, 2),
               ("locker", 0, -7, 3, 1), ("sight", -4, 3)),
    "us_m26": (("cupola", -4, -2, 2), ("hatch", 3, -1, 2, 2), ("sight", 3, 3), ("rail", -5, -3, -5, 1)),
    "us_m46": (("cupola", -4, -2, 2, 2), ("hatch", 3, -2, 2, 2), ("sight", 3, 3),
               ("locker", 0, -6, 3, 1), ("rail", -5, -4, -5, 2)),
    "us_m41": (("cupola", -3, -2, 2), ("hatch", 3, -1, 2, 2), ("sight", 3, 3),
               ("basket", 5, -5, 3), ("locker", 5, -3, 1, 2)),
    "us_m56": (("sight", -2, 0), ("rail", -3, -1, -1, -1), ("locker", 2, 0, 1, 1)),
    "us_m50": (("sight", -2, 1), ("locker", 0, -2, 1, 2), ("rail", -3, -1, 3, -1)),
    "us_m103": (("cupola", -5, -3, 3), ("hatch", 4, -3, 2, 2), ("sight", 7, 2), ("sight", -7, 2),
                ("basket", 8, -9, 4), ("rail", -6, 2, -6, 7)),
    "de_a7v": (("rail", -2, 0, 2, 0), ("seam", -2, 1, 2, 1, True)),
    "de_pz2c": (("hatch", -1, -1, 2, 2, "round"), ("sight", 1, 2), ("seam", -3, -2, -3, 2, True)),
    "de_pz3j": (("cupola", 0, -3, 2, 2), ("hatch", -3, 0, 1, 2, "rect"), ("hatch", 3, 0, 1, 2, "rect"),
                ("sight", -2, 3), ("seam", -3, 3, 3, 3)),
    "de_pz4f2": (("cupola", 0, -4, 2, 2), ("hatch", -3, 2, 1, 1, "rect"),
                 ("seam", -3, 4, 3, 4)),
    "de_stug3g": (("seam", -2, 1, 2, 1), ("rail", -2, 0, 2, 0)),
    "de_panther_g": (("cupola", 4, -4, 2, 1), ("hatch", -3, 0, 1, 1),
                     ("seam", -4, 5, 4, 5)),
    "de_tiger_i": (("cupola", -4, -3, 2, 2), ("hatch", 4, -2, 2, 2),
                   ("sight", -4, 3), ("seam", -5, 5, 5, 5)),
    "de_jagdpanther": (("rail", -2, 0, 2, 0), ("seam", -3, 1, 3, 1)),
    "de_m41g": (("cupola", -3, -2, 2), ("hatch", 3, -1, 2, 2), ("sight", 3, 3),
                ("locker", -4, -3, 1, 2)),
    "de_kanjpz": (("seam", -2, 1, 2, 1), ("rail", -2, 0, 2, 0)),
    "de_ru251": (("cupola", -3, -2, 2, 1), ("hatch", 3, -2, 1, 2), ("rail", -5, -5, -6, -2)),
    "de_wiesel_mk": (("hatch", 0, -1, 1, 1, "rect"), ("sight", 1, 1), ("locker", -2, -2, 1, 1)),
    "de_wiesel_tow": (("sight", -2, 2), ("seam", -2, -2, 2, -2)),
    "ru_ms1": (("hatch", 0, -1, 2, 2), ("sight", -1, 2), ("seam", -2, -2, 2, -2, True)),
    "ru_t26": (("hatch", -2, -1, 1, 2, "rect"), ("hatch", 2, -1, 1, 2, "rect"), ("sight", -1, 3),
               ("seam", -3, -3, 3, -3, True)),
    "ru_bt7": (("hatch", 0, -1, 2, 2, "split"), ("sight", 2, 2), ("seam", -3, -2, -3, 2, True)),
    "ru_t28": (("hatch", -3, -2, 2, 2), ("hatch", 3, -2, 2, 2), ("sight", -3, 3),
               ("rail", -5, -3, -5, 2), ("rail", 5, -3, 5, 2)),
    "ru_t34_1941": (("hatch", 0, -1, 3, 2, "rect"), ("sight", -3, 2), ("seam", -3, 3, 3, 3)),
    "ru_kv1": (("hatch", -3, -2, 2, 2), ("sight", 3, 3), ("locker", 0, -6, 4, 1),
               ("seam", -5, 4, 5, 4), ("rail", -6, -3, -6, 2)),
    "ru_t3485": (("cupola", -4, -2, 2, 2), ("hatch", 2, -3, 2, 2), ("sight", 3, 2),
                 ("rail", -5, -4, -5, 2), ("rail", 5, -4, 5, 1)),
    "ru_is2": (("cupola", -4, -3, 2, 2), ("hatch", 3, -3, 2, 2), ("sight", 3, 2),
               ("rail", -6, -4, -6, 2), ("seam", -4, 4, 4, 4)),
    "ru_su100": (("rail", -2, 0, 2, 0), ("seam", -2, 1, 2, 1)),
    "ru_t44": (("cupola", -3, -2, 2), ("hatch", 3, -2, 2, 2), ("sight", -2, 3), ("rail", 5, -3, 5, 1)),
    "ru_asu57": (("sight", -2, 0), ("rail", -2, -1, 2, -1), ("seam", -2, 1, 2, 1)),
    "ru_asu85": (("rail", -2, 0, 2, 0), ("seam", -2, 1, 2, 1)),
    "ru_it1": (("cupola", -4, -3, 2), ("hatch", 4, -3, 2, 2), ("sight", 3, 1),
               ("grille", 0, -4, 2, 2), ("rail", -2, 0, -2, 4), ("rail", 2, 0, 2, 4)),
    "ru_obj906": (("cupola", -3, -2, 2), ("hatch", 3, -2, 2, 2), ("sight", -2, 3), ("rail", 5, -3, 4, 2)),
    "jp_hago": (("cupola", -1, -1, 2, 1), ("sight", 1, 2), ("seam", -2, -3, 2, -3, True)),
    "jp_chiha": (("cupola", -1, -1, 2), ("rail", -4, -3, -4, 2), ("rail", 4, -3, 4, 2),
                 ("rail", -4, -3, 4, -3), ("sight", 2, 2)),
    "jp_shinhoto": (("cupola", -2, -2, 2), ("hatch", 3, -2, 1, 2), ("sight", 2, 3),
                    ("locker", 0, -5, 3, 1)),
    "jp_chihe": (("cupola", -2, -2, 2), ("hatch", 3, -1, 1, 2), ("sight", 1, 3),
                 ("seam", -4, 2, 4, 2)),
    "jp_chinu": (("cupola", -3, -2, 2), ("hatch", 3, -1, 2, 2, "split"),
                 ("sight", 3, 3), ("locker", 0, -5, 4, 1)),
    "jp_chito": (("cupola", -3, -3, 2), ("hatch", 3, -2, 2, 2), ("sight", -2, 3),
                 ("seam", -4, 4, 4, 4), ("rail", 5, -4, 5, 1)),
    "jp_chiri": (("cupola", -4, -3, 2, 2), ("hatch", 3, -2, 2, 2, "split"), ("sight", -3, 4),
                 ("locker", 0, -7, 4, 2), ("seam", -5, 4, 5, 4)),
    "jp_kami": (("cupola", -1, -2, 2, 1), ("sight", 2, 2), ("seam", -3, -3, 3, -3, True)),
    "jp_honi3": (("rail", -2, 0, 2, 0), ("seam", -2, 1, 2, 1)),
    "jp_m24": (("cupola", -3, -2, 2), ("hatch", 3, -2, 2, 2), ("sight", 2, 2), ("locker", 0, -5, 3, 1),
               ("rail", 4, -3, 4, 1)),
    "jp_type60": (("sight", -1, 3), ("locker", 0, -2, 2, 1), ("rail", -2, 1, 2, 1)),
    "jp_type61": (("cupola", -4, -3, 2, 2), ("hatch", 3, -2, 2, 2), ("sight", 3, 3),
                  ("basket", 5, -5, 3), ("rail", -5, -4, -5, 1)),
    "jp_type74": (("cupola", -4, -2, 2, 1), ("hatch", 4, -2, 2, 2), ("locker", -4, 4, 1, 1, 2),
                  ("sight", -4, 5), ("rail", 6, -4, 6, 1)),
    "jp_type90": (("hatch", -5, 0, 2, 2), ("cupola", 5, -1, 2, 1), ("sight", -4, 5, 2), ("sight", 5, 4),
                  ("basket", 8, -8, 3), ("seam", -8, 4, -2, 7), ("seam", 8, 4, 2, 7)),
    "jp_type16": (("cupola", -4, -2, 2, 1), ("hatch", 4, -2, 2, 2), ("sight", -3, 4), ("sight", 5, 3),
                  ("basket", 6, -7, 3), ("seam", -6, 3, -2, 6), ("seam", 6, 3, 2, 6)),
    "jp_type89ifv": (("hatch", -3, -2, 2, 2), ("hatch", 3, -2, 2, 2), ("sight", -3, 2), ("sight", 3, 2),
                     ("locker", -6, -1, 1, 3), ("locker", 6, -1, 1, 3), ("seam", -4, 4, 4, 4)),
}


def draw_fittings(vehicle_id, hull, turret):
    for part, layout in ((hull, HULL[vehicle_id]), (turret, TURRET[vehicle_id])):
        fittings = Exterior(part)
        for operation, *coordinates in layout:
            getattr(fittings, operation)(*coordinates)

