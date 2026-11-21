"""Rebuild the photo-referenced vehicle sprites (Python 3 + Pillow).

Run with --output-dir to stage the PNGs and catalog. The game uses a separate
horizontal slice sheet for each hull and turret. Positive y points forward
in the PNG, and z increases from the ground to the deck.
"""

import argparse
import json
from math import cos, sin, sqrt
from pathlib import Path

from PIL import Image, ImageDraw
from armata_four_views import sculpt_armata
from fleet_four_views import build_fleet
from patton_four_views import MUZZLE_Z as PATTON_MUZZLE_HEIGHT, sculpt_patton
from tiger_ii_four_views import GEOMETRY as TIGER_II_GEOMETRY
from tiger_ii_four_views import MUZZLE_Z as TIGER_II_MUZZLE_HEIGHT, sculpt_tiger_ii
from tiger_ii_four_views import TURRET_SLICES as TIGER_II_TURRET_SLICES


RESOURCES = Path(__file__).resolve().parents[1] / "Assets/Modern8bitTanks/Resources"
RUBBER = (48, 46, 38)
TREAD = (67, 62, 49)
STEEL = (91, 87, 67)
GLASS = (75, 106, 100)
LENS = (162, 181, 140)
OLIVE = ((128, 126, 81),)
FIELD = ((112, 124, 83),)
SAND = ((169, 151, 110),)
MERDC = ((135, 131, 84), (132, 102, 69), (83, 88, 58))
NATO = ((116, 127, 88), (127, 100, 69), (71, 76, 57))
SOVIET = ((131, 139, 83),)
EXPO = ((121, 132, 83), (167, 147, 101), (79, 86, 62))


def tone(rgb, factor):
    return tuple(min(255, round(channel * factor)) for channel in rgb)


class Stack:
    def __init__(self, width, length, height, palette):
        self.width, self.length = width, length
        self.cx, self.cy = width // 2, length // 2
        self.layers = [Image.new("RGBA", (width, length)) for _ in range(height)]
        self.draw = [ImageDraw.Draw(layer) for layer in self.layers]
        self.palette = palette
        self.base = palette[0]
        self.light = tone(self.base, 1.23)
        self.dark = tone(self.base, .70)
        self.edge = tone(self.base, .51)

    def paint(self, x, y):
        if len(self.palette) == 1:
            return self.base
        patches = cos(x * .35 + y * .22) + sin(y * .43 - x * .19)
        return self.palette[2 if patches > .96 else 1 if patches < -.8 else 0]

    def pixel(self, z, x, y, color):
        self.draw[z].point((self.cx + x, self.cy + y), fill=color)

    def box(self, z0, z1, x0, x1, y0, y1, color):
        for z in range(z0, z1 + 1):
            self.draw[z].rectangle(
                (self.cx + x0, self.cy + y0, self.cx + x1, self.cy + y1), fill=color
            )

    def ellipse(self, z, x, y, rx, ry, color):
        self.draw[z].ellipse(
            (self.cx + x - rx, self.cy + y - ry, self.cx + x + rx, self.cy + y + ry),
            fill=color,
        )

    def shell(self, z, rear, front, half_width, rear_cut, front_cut, brightness,
              round_bow=False):
        """Lay a chamfered armor section across one horizontal slice."""
        for y in range(rear, front + 1):
            rear_inset = max(0, rear_cut - y + rear)
            front_inset = max(0, front_cut - front + y)
            if round_bow and front_inset:
                arc = sqrt(1 - .80 * (front_inset / front_cut) ** 2)
                front_inset = round(half_width * (1 - arc))
            width = max(1, half_width - max(rear_inset, front_inset))
            for x in range(-width, width + 1):
                lip = 1.09 if y == front else .83 if y == rear else .94 if abs(x) == width else 1
                self.pixel(z, x, y, tone(self.paint(x, y), brightness * lip))

    def loft(self, start, sections, round_bow=False):
        for z, (rear, front, width, rear_cut, front_cut) in enumerate(sections, start):
            gain = .76 + .30 * (z - start) / max(1, len(sections) - 1)
            self.shell(z, rear, front, width, rear_cut, front_cut, gain, round_bow)

    def plate(self, z, outline, brightness):
        mask = Image.new("1", (self.width, self.length))
        ImageDraw.Draw(mask).polygon([(self.cx + x, self.cy + y) for x, y in outline], fill=1)
        left, rear, right, front = mask.getbbox()
        for py in range(rear, front):
            for px in range(left, right):
                if mask.getpixel((px, py)):
                    x, y = px - self.cx, py - self.cy
                    self.pixel(z, x, y, tone(self.paint(x, y), brightness))

    def hatch(self, z, x, y, rx=2, ry=2):
        self.ellipse(z, x, y, rx, ry, self.edge)
        self.ellipse(z, x, y, max(0, rx - 1), max(0, ry - 1), self.light)
        self.pixel(z, x, y - ry, self.dark)

    def lid(self, z, x0, x1, y0, y1):
        self.box(z, z, x0, x1, y0, y1, self.edge)
        self.box(z, z, x0 + 1, x1 - 1, y0 + 1, y1 - 1, self.light)
        self.pixel(z, x1 - 1, y1, self.dark)

    def grille(self, z, x0, x1, y0, y1):
        self.box(z, z, x0, x1, y0, y1, self.dark)
        for y in range(y0 + 1, y1 + 1, 2):
            self.box(z, z, x0 + 1, x1 - 1, y, y, self.edge)

    def sight(self, z, x, y, width=1, height=2):
        self.box(z, z + height - 1, x - width, x + width, y - 1, y, self.dark)
        self.box(z + height - 1, z + height - 1, x - width, x + width, y - 1, y - 1,
                 self.light)
        self.pixel(z + height - 1, x, y, GLASS)

    def save(self, path):
        last = max(z for z, layer in enumerate(self.layers) if layer.getbbox())
        sheet = Image.new("RGBA", (self.width * (last + 1), self.length))
        for z, layer in enumerate(self.layers[: last + 1]):
            sheet.paste(layer, (z * self.width, 0))
        sheet.save(path)
        return last + 1


def tracks(hull, wheel_centers, end_centers, band=3, height=5, wheel_profile=None):
    # Tread contour and return run surround the distinct road wheels on both sides.
    reach = hull.cx - 1
    rear, front = end_centers
    if wheel_profile is None:
        wheel_profile = (1, *(2 for _ in range(height - 4)), 1)
    for side in (-1, 1):
        left, right = sorted((side * (reach - band + 1), side * reach))
        for z in range(height):
            end_inset = 2 if z == 0 else 1 if z == height - 1 else 0
            a, b = rear + end_inset, front - end_inset
            hull.box(z, z, left, right, a, b, TREAD if z in (0, height - 1) else RUBBER)
            for y in range(a + 1, b, 2):
                if z in (0, height - 1) or y <= a + 1 or y >= b - 2:
                    hull.pixel(z, side * reach, y, tone(TREAD, 1.23))
        for index, y in enumerate(wheel_centers):
            rim = tone(hull.base, .83 if index % 2 else .91)
            for z, radius in enumerate(wheel_profile, 1):
                for dy in range(-radius, radius + 1):
                    hull.pixel(z, side * reach, y + dy, STEEL if abs(dy) == radius else rim)
            hull.pixel(2, side * reach, y, hull.dark)
            hull.pixel(3, side * reach, y - 1, tone(rim, 1.14))
        for y in (rear + 1, front - 1):
            hull.pixel(height - 2, side * reach, y, STEEL)
            hull.pixel(height - 3, side * reach, y, hull.dark)


def tires(hull, axles, large=False):
    width = hull.cx - 1
    profile = (1, 2, 3, 3, 3, 2, 1) if large else (1, 2, 2, 2, 2, 1)
    for side in (-1, 1):
        left, right = sorted((side * (width - 2), side * width))
        for y in axles:
            for z, radius in enumerate(profile):
                hull.box(z, z, left, right, y - radius, y + radius, RUBBER)
                for dy in range(-radius, radius + 1, 2):
                    hull.pixel(z, side * (width - 1), y + dy, TREAD)
            for z in (2, 3, 4):
                radius = 1 if large or z == 3 else 0
                for dy in range(-radius, radius + 1):
                    hull.pixel(z, side * width, y + dy, tone(hull.base, .88))
            hull.pixel(3, side * width, y, hull.edge)


def skirts(hull, z0, z1, ranges, inset=0):
    for side in (-1, 1):
        x = side * (hull.cx - 1 - inset)
        for rear, front in ranges:
            for z in range(z0, z1 + 1):
                for y in range(rear, front + 1):
                    shade = .77 if z == z0 or y == rear else 1.04 if z == z1 else .94
                    for dx in (0, -side):
                        hull.pixel(z, x + dx, y, tone(hull.paint(x, y), shade))


def lights(hull, z, y, separation, rear=False):
    for x in (-separation, separation):
        hull.pixel(z, x, y, (139, 56, 38) if rear else (211, 192, 133))


def star(hull, z, x, y):
    for dx, dy in ((0, 0), (-1, 0), (1, 0), (0, 1), (0, -1)):
        hull.pixel(z, x + dx, y + dy, (198, 188, 149))


def cupola(turret, z, x, y, tall=False):
    turret.ellipse(z, x, y, 3, 3, turret.dark)
    turret.ellipse(z + 1, x, y, 2, 2, turret.base)
    if tall:
        turret.ellipse(z + 2, x, y, 2, 2, turret.light)
        turret.box(z + 1, z + 1, x - 1, x + 1, y + 2, y + 2, GLASS)
    else:
        turret.pixel(z + 1, x, y + 2, GLASS)


def basket(turret, z, rear, front, half_width):
    turret.box(z, z, -half_width, half_width, rear, front, turret.dark)
    for x in (-half_width, half_width):
        turret.box(z + 1, z + 1, x, x, rear, front, turret.base)
    turret.box(z + 1, z + 1, -half_width, half_width, rear, rear, turret.light)
    for x in range(-half_width + 2, half_width, 3):
        turret.pixel(z + 1, x, rear + 1, turret.edge)


def barrel(turret, start, tip, x=0, z=2, style="cannon"):
    body = tone(turret.base, .82)
    highlight = tone(turret.base, 1.12)
    turret.box(z, z, x, x, start, tip, body)
    if style == "auto":
        turret.box(z - 1, z - 1, x - 1, x + 1, start, tip, turret.edge)
        turret.box(z, z, x - 1, x + 1, start, start + 2, turret.dark)
        turret.box(z, z, x, x, start + 2, tip - 1, tone(STEEL, 1.37))
        turret.box(z, z, x - 1, x + 1, tip - 1, tip, turret.edge)
    else:
        turret.box(z - 1, z - 1, x - 1, x + 1, start, tip, turret.edge)
        turret.box(z, z, x, x, start, tip - 1, highlight)
        if style in ("cannon", "sleeve", "t_brake"):
            first = tip - (5 if style == "t_brake" else (tip - start) // 2)
            turret.box(z, z, x - 1, x + 1, first, first + 2, body)
            turret.box(z + 1, z + 1, x, x, first, first + 2, highlight)
        if style == "sleeve":
            for y in range(start + 3, tip - 2, 4):
                turret.box(z - 1, z - 1, x - 1, x + 1, y, y, STEEL)
    if style in ("t_brake", "baffle"):
        turret.box(z - 1, z, x - 2, x + 2, tip - 1, tip, body)
        if style == "baffle":
            turret.box(z - 1, z - 1, x - 2, x + 2, tip - 3, tip - 3, highlight)
    turret.pixel(z, x, tip, RUBBER)
    return tip


def tube(turret, x, rear, front, z, color=None):
    turret.box(z - 1, z - 1, x - 1, x + 1, rear, front, turret.edge)
    turret.box(z, z, x, x, rear, front - 1, color or turret.light)
    turret.pixel(z, x, front, RUBBER)
    turret.pixel(z, x, rear, STEEL)


def mantlet(turret, y, width=2):
    turret.box(1, 2, -width, width, y - 1, y + 1, turret.dark)
    turret.box(3, 3, -width + 1, width - 1, y - 1, y, turret.base)


def patton(hull, turret, vehicle):
    model = vehicle["id"]
    m60 = model in ("us_m60", "us_m60a3")
    slick = model == "us_m60"
    german = model == "de_m48a2"
    long_cast = model == "us_m60a3"
    top = vehicle["hullBaseHeight"] - 1
    tracks(hull, (-12, -7, -2, 3, 8, 13), (-17, 17), height=6)
    hull.loft(2, [
        (-15, 15, 7, 2, 4), (-17, 17, 8, 2, 3 if m60 else 7),
        (-17, 18, 9, 2, 1 if m60 else 7), (-17, 17, 9, 1, 1 if m60 else 6),
        (-17, 14, 9, 1, 1 if m60 else 5), (-17, 11, 8, 1, 1 if m60 else 4),
        *[(-16, 9, 8, 2, 2)] * (top - 7),
    ], round_bow=not m60)
    skirts(hull, 6, 6, [(-17, 16)])
    for x in (-9, 9):
        hull.box(7, 7, x - 1, x + 1, -10, -3, hull.dark)
        hull.box(8, 8, x, x, -10, -3, hull.light)
        if german:
            hull.box(7, 8, x - 1, x + 1, -15, -11, hull.base)
            hull.pixel(8, x, -11, hull.light)
    for x in (-4, 4):
        rear = -15 if m60 else -14
        hull.grille(top, x - 3, x + 3, rear, -9 if m60 else -10)
    if m60:
        for x in range(-6, 7, 2):
            hull.box(5, 7, x, x, -17, -17, hull.edge)
    hull.hatch(top, 0, 7, 2, 1)
    lights(hull, 6, 13 if m60 else 12, 8)
    lights(hull, 6, -17, 7, rear=True)
    if not german:
        star(hull, 6, 0, 11)
    for z, (rx, ry) in enumerate(((7, 8), (9, 10), (10, 11), (9, 10), (8, 9), (6, 7))):
        if long_cast:
            turret.shell(z, -ry + 1, ry + 2, rx, 3, 6, .73 + z * .07)
        else:
            turret.ellipse(z, 0, -1, rx, ry, tone(turret.base, .73 + z * .07))
    for x in (-9, 9):
        turret.box(3, 4, min(x, x * 8 // 9), max(x, x * 8 // 9), 1, 3, turret.dark)
        turret.pixel(4, x, 2, GLASS)
    basket(turret, 2, -13, -9, 6)
    turret.hatch(5, -3, -2, 2, 2)
    if german:
        turret.hatch(6, 3, -2, 2, 2)
        turret.box(6, 6, 3, 3, -1, 4, STEEL)
    else:
        cupola(turret, 5, 3, -3, tall=True)
    mantlet(turret, 9 if not long_cast else 12, 3)
    if long_cast:
        turret.sight(5, -3, 5)
        turret.box(5, 7, -4, -4, -9, -9, STEEL)
        for x in (-7, 7):
            turret.box(4, 4, x - 1, x + 1, 6, 7, turret.edge)
    style = "sleeve" if long_cast else "cannon" if slick or german else "t_brake"
    return barrel(turret, 9 if not long_cast else 12, vehicle["muzzleDist"], style=style)


def sheridan(hull, turret, vehicle):
    upgraded = vehicle["id"] == "us_m551a1"
    tracks(hull, (-10, -5, 0, 5, 10), (-14, 14), height=5)
    hull.loft(2, [
        (-13, 13, 6, 2, 4), (-14, 15, 7, 1, 4), (-15, 15, 8, 1, 3),
        (-15, 14, 8, 1, 4), (-14, 11, 8, 1, 3), (-13, 9, 7, 2, 2),
    ])
    skirts(hull, 5, 5, [(-14, 12)])
    hull.grille(7, -6, -1, -12, -7)
    hull.grille(7, 2, 6, -12, -7)
    hull.hatch(7, 0, 8, 2, 1)
    lights(hull, 5, 12, 6)
    turret.loft(0, [(-7, 6, 5, 2, 3), (-9, 8, 7, 2, 3), (-8, 8, 8, 2, 4),
                    (-8, 7, 7, 2, 3), (-7, 5, 5, 2, 2)])
    turret.hatch(4, -3, -2)
    cupola(turret, 4, 3, -3)
    turret.box(5, 5, 4, 4, -1, 4, STEEL)
    basket(turret, 2, -11, -8, 5)
    mantlet(turret, 7)
    if upgraded:
        turret.sight(4, -4, 5, width=2)
        turret.box(5, 5, -5, -3, 6, 6, LENS)
    return barrel(turret, 8, vehicle["muzzleDist"], style="short")


def modern_light(hull, turret, vehicle):
    booker = vehicle["id"] == "us_m10_booker"
    top = vehicle["hullBaseHeight"] - 1
    tracks(hull, (-12, -7, -2, 3, 8, 13) if booker else (-11, -6, -1, 4, 9, 13),
           (-16, 16), height=5)
    hull.loft(2, [
        (-15, 15, 7, 2, 3), (-16, 16, 8, 2, 2), (-17, 17, 8, 2, 2),
        (-16, 16, 9 if booker else 8, 2, 3), (-16, 13, 8, 1, 2),
        *[(-15, 10, 8, 2, 3)] * (top - 6),
    ])
    skirts(hull, 4, 6, [(-15, -10), (-9, -4), (-3, 2), (3, 8), (9, 15)])
    if booker:
        hull.grille(top, -7, -2, 5, 10)
        hull.hatch(top, 5, 7, 2, 2)
        hull.lid(top, -5, 6, -14, -9)
    else:
        hull.grille(top, 1, 7, -14, -8)
        hull.hatch(top, -3, 8, 2, 2)
        hull.lid(top, -7, -3, -14, -9)
    lights(hull, 5, 15, 6)
    lights(hull, 4, -16, 6, rear=True)
    if booker:
        turret.loft(0, [(-10, 7, 7, 2, 3), (-12, 9, 9, 1, 5), (-12, 10, 9, 1, 5),
                        (-12, 9, 9, 1, 4), (-11, 6, 7, 1, 3), (-10, 4, 6, 1, 2)])
        turret.sight(5, -4, 4, width=2)
        turret.hatch(5, 3, -3)
        turret.box(6, 6, 4, 4, -2, 3, STEEL)
    else:
        turret.loft(0, [(-10, 6, 6, 2, 3), (-11, 8, 8, 2, 4), (-11, 9, 8, 2, 5),
                        (-10, 7, 7, 2, 4), (-10, 5, 6, 2, 3)])
        turret.hatch(4, -3, -4)
        turret.sight(4, 3, 2)
    basket(turret, 2, -14, -11, 6)
    mantlet(turret, 8)
    return barrel(turret, 9, vehicle["muzzleDist"], style="sleeve" if booker else "baffle")


def stryker(hull, turret, vehicle):
    hull.loft(2, [(-16, 15, 6, 2, 3), (-17, 16, 7, 2, 4), (-18, 17, 8, 2, 5),
                  (-18, 17, 8, 2, 5), (-17, 13, 8, 2, 5), (-17, 10, 7, 2, 4)])
    tires(hull, (-13, -5, 4, 12))
    hull.grille(7, -6, -1, 1, 8)
    hull.hatch(7, 3, 7)
    hull.lid(7, -5, 5, -16, -10)
    hull.box(6, 6, -7, 7, -15, -11, hull.dark)
    lights(hull, 5, 12, 5)
    lights(hull, 5, -17, 5, rear=True)
    turret.loft(0, [(-9, 5, 4, 1, 1), (-11, 6, 5, 1, 2), (-10, 6, 3, 1, 1),
                    (-9, 5, 2, 1, 1)])
    # The long barrel rides over a low, narrow overhead mount.
    turret.box(0, 1, -7, -4, -5, 3, turret.base)
    turret.box(0, 1, 4, 7, -4, 2, turret.dark)
    turret.sight(3, 2, 3)
    turret.box(3, 3, -1, 1, -8, -5, turret.light)
    mantlet(turret, 5)
    return barrel(turret, 6, vehicle["muzzleDist"], style="sleeve")


def luchs(hull, turret, vehicle):
    hull.loft(2, [(-15, 15, 6, 4, 4), (-17, 17, 7, 5, 5), (-18, 18, 8, 5, 5),
                  (-18, 17, 8, 6, 6), (-15, 13, 7, 4, 4), (-12, 10, 6, 2, 2)])
    tires(hull, (-13, -6, 6, 13))
    hull.hatch(7, 3, 8)
    hull.hatch(7, -3, -10)
    hull.grille(7, -6, 1, -9, -6)
    for x in (-8, 8):
        hull.box(6, 6, x, x, -11, 11, hull.dark)
    lights(hull, 5, 14, 4)
    lights(hull, 5, -15, 4, rear=True)
    turret.loft(0, [(-6, 4, 4, 1, 2), (-7, 5, 5, 1, 2), (-7, 5, 5, 2, 3),
                    (-6, 4, 4, 2, 2)])
    turret.hatch(3, -2, -3, 2, 1)
    turret.hatch(3, 2, -2, 2, 1)
    turret.sight(3, -2, 2, height=1)
    return barrel(turret, 5, 16, style="auto")


def boxer(hull, turret, vehicle):
    hull.loft(2, [(-17, 15, 6, 1, 2), (-18, 16, 7, 1, 3), (-18, 18, 8, 1, 3),
                  (-19, 18, 9, 1, 4), (-19, 17, 9, 1, 4), (-18, 13, 9, 1, 4),
                  (-18, 9, 8, 1, 3)])
    tires(hull, (-14, -6, 3, 12), large=True)
    for x in (-8, 8):
        hull.box(8, 8, x, x, -17, -3, hull.light)
        hull.box(6, 8, x, x, -3, -3, hull.edge)
    hull.lid(8, -6, 6, -17, -7)
    hull.hatch(8, 4, 7, 2, 1)
    hull.grille(8, -6, 0, 1, 6)
    hull.box(5, 7, -5, 5, -19, -19, hull.dark)
    hull.box(7, 7, -4, 4, -19, -19, hull.base)
    lights(hull, 6, 15, 6)
    lights(hull, 6, -18, 7, rear=True)
    turret.loft(0, [(-7, 4, 4, 1, 2), (-9, 7, 7, 2, 3), (-9, 8, 7, 2, 4),
                    (-8, 7, 6, 2, 4), (-8, 3, 5, 2, 2)])
    turret.sight(4, -3, 3)
    turret.sight(4, 2, -5, height=2)
    turret.box(2, 3, -6, -5, 4, 6, turret.dark)
    return barrel(turret, 7, 21, style="auto")


def bradley(hull, turret, vehicle):
    model = vehicle["id"]
    early = model == "us_m2"
    cavalry = model == "us_m3a3"
    a4 = model == "us_m2a4"
    tracks(hull, (-12, -7, -2, 3, 8, 12), (-16, 16), height=6 if a4 else 5)
    width = 8 if early else 9
    hull.loft(2, [
        (-15, 13, 7, 1, 3), (-16, 16, 8, 1, 3), (-17, 17, 8, 1, 2),
        (-17, 17, width, 1, 2), (-17, 14, width, 1, 3),
        (-16, 11, width, 1, 3), (-16, 8, width - 1, 1, 2),
    ])
    if early:
        skirts(hull, 4, 5, [(-15, -10), (-9, -4), (-3, 2), (3, 8), (9, 14)], inset=1)
        for x in (-8, 8):
            for y in (-11, -6, -3):
                hull.pixel(7, x, y, hull.edge)
    else:
        skirts(hull, 4 + int(a4), 7, [(-16, -10), (-9, -4), (-3, 2), (3, 8), (9, 14)])
        if a4:
            for side in (-1, 1):
                for y in (-12, -5, 2, 9):
                    x = side * (hull.cx - 1)
                    hull.box(7, 8, x - 1, x + 1, y, y + 2, hull.dark)
                    hull.pixel(8, x, y + 2, hull.light)
        if not a4:
            hull.box(6, 6, -4, 4, 15, 16, hull.dark)
    hull.grille(8, -7, -1, 3, 8)
    hull.hatch(8, 4, 6)
    hull.lid(8, -5, 5, -15, -8)
    hull.box(4, 7, -5, 5, -17, -17, hull.edge)
    hull.box(5, 7, -4, 4, -17, -17, hull.base)
    hull.pixel(6, 3, -17, hull.light)
    lights(hull, 5, 15, 6)
    lights(hull, 5, -17, 7, rear=True)
    turret.loft(0, [(-6, 5, 5, 2, 2), (-8, 7, 6, 1, 3), (-8, 8, 7 if not early else 6, 1, 4),
                    (-8, 7, 6, 1, 3), (-7, 5, 5, 1, 2)])
    turret.box(1, 2, -9, -5, -4, 5, turret.dark)
    turret.box(3, 3, -9, -6, -4, 5, turret.base)
    turret.box(3, 3, -9, -6, 5, 5, turret.edge)
    for x in (-9, -7):
        turret.pixel(3, x, 5, STEEL)
    basket(turret, 2, -10 if early else -11, -8, 5)
    turret.hatch(4, 2, -4, 2, 2)
    turret.sight(4, 3, 4, width=2 if not early else 1)
    if cavalry:
        turret.sight(5, -3, -3, height=3, width=1)
        turret.pixel(7, -3, -4, LENS)
    elif a4:
        turret.box(3, 5, 4, 7, -6, -3, turret.base)
        turret.box(5, 6, 4, 6, -2, -1, turret.dark)
        turret.pixel(6, 5, -1, GLASS)
    return barrel(turret, 7, 19, style="auto")


def german_ifv(hull, turret, vehicle):
    model = vehicle["id"]
    marder = model == "de_marder"
    puma = model == "de_puma"
    top = vehicle["hullBaseHeight"] - 1
    tracks(hull, (-13, -8, -3, 2, 7, 12), (-17, 17))
    width = 9 if marder else 10
    hull.loft(2, [
        (-16, 15, 7, 1, 2), (-17, 17, 8, 1, 2), (-18, 18, 9, 1, 3),
        (-18, 17, width, 1, 3), (-18, 14, width, 2, 4), (-17, 10, width - 1, 2, 3),
        *[(-17, 6, width - 1, 2, 2)] * (top - 7),
    ])
    ranges = [(-16, -11), (-10, -5), (-4, 1), (2, 7), (8, 14)]
    skirts(hull, 5 if marder else 4, 6 if marder else 7, ranges)
    hull.hatch(7, 4, 7, 2, 1)
    hull.grille(7, -7, -1, 1, 8)
    if marder:
        for x in (-5, 5):
            hull.lid(top, x - 2, x + 2, -15, -8)
        hull.box(5, 7, -4, 4, -18, -18, hull.dark)
        hull.box(6, 7, -3, 3, -18, -18, hull.base)
        turret.loft(0, [(-5, 5, 4, 2, 2), (-6, 6, 5, 2, 2), (-7, 5, 5, 2, 2),
                        (-6, 4, 4, 1, 2)])
        turret.box(3, 4, -2, 2, 1, 5, turret.dark)
        turret.hatch(3, 3, -3)
        turret.sight(3, -3, 1, height=2)
        tube(turret, 5, -4, 4, 4)
    else:
        hull.lid(top, -6, 6, -16, -7)
        hull.box(5, 7, -6, 6, -18, -18, hull.edge)
        hull.box(6, 7, -5, 5, -18, -18, hull.base)
        hull.box(7, 7, -4, 4, -18, -18, hull.light)
        for y in (-13, -7, -1, 5):
            for x in (-width, width):
                hull.pixel(7, x, y, hull.light)
        if puma:
            turret.loft(0, [(-8, 4, 4, 1, 2), (-8, 7, 6, 2, 3), (-8, 7, 6, 1, 4),
                            (-7, 6, 5, 1, 3), (-7, 2, 4, 1, 2)])
            turret.box(1, 3, 5, 7, -7, 2, turret.dark)
            turret.sight(4, 1, -4, width=1, height=3)
            turret.sight(3, -3, 3)
        else:
            turret.loft(0, [(-8, 5, 6, 1, 3), (-9, 8, 8, 2, 4), (-9, 8, 8, 2, 5),
                            (-9, 8, 7, 2, 5), (-8, 6, 6, 2, 3), (-7, 4, 5, 2, 2)])
            turret.sight(5, -3, 2, width=2)
            turret.sight(5, 3, -4, height=3)
            turret.box(2, 4, -8, -6, -3, 4, turret.dark)
            turret.box(4, 4, -8, -7, -2, 3, turret.base)
    lights(hull, 5, 16, 6)
    lights(hull, 6, -18, 7, rear=True)
    return barrel(turret, 6 if marder else 7, 17 if marder else 20 if puma else 21,
                  z=3 if marder else 2, style="auto")


def wiesel(hull, turret, vehicle):
    tow = vehicle["id"] == "de_wiesel_tow"
    tracks(hull, (-4, 1, 6), (-10, 10), band=2)
    hull.loft(2, [(-10, 9, 4, 1, 2), (-11, 10, 5, 1, 2), (-11, 10, 6, 1, 2),
                  (-11, 8, 6, 1, 2), (-10, 6, 5, 1, 2)])
    if tow:
        hull.lid(6, -4, 2, -9, -3)
        hull.hatch(6, 3, -5, 2, 2)
    else:
        hull.lid(6, -4, 4, -9, -3)
    hull.hatch(6, 3, 4, 2, 1)
    hull.grille(6, -4, 0, 0, 5)
    lights(hull, 5, 7, 4)
    lights(hull, 4, -11, 4, rear=True)
    if tow:
        turret.ellipse(0, 0, 0, 3, 3, turret.dark)
        turret.box(1, 3, -1, 1, -2, 1, turret.dark)
        tube(turret, 0, -6, 8, 4)
        turret.sight(3, -2, 3, height=2)
        return 8
    turret.loft(0, [(-4, 4, 3, 1, 1), (-5, 4, 4, 1, 2), (-5, 3, 4, 1, 2),
                    (-4, 2, 3, 1, 2)])
    turret.sight(3, 2, 2, height=2)
    turret.hatch(3, -1, -2, 2, 1)
    return barrel(turret, 3, 12, style="auto")


def itv(hull, turret, vehicle):
    tracks(hull, (-10, -5, 0, 5, 10), (-13, 13), band=2)
    hull.loft(2, [(-13, 12, 6, 1, 2), (-14, 13, 7, 1, 2), (-15, 14, 7, 1, 2),
                  (-15, 13, 8, 1, 2), (-14, 11, 8, 1, 1), (-14, 8, 8, 1, 1)])
    hull.hatch(7, 4, 6)
    hull.grille(7, -6, -1, 1, 7)
    hull.lid(7, -5, 5, -12, -5)
    hull.box(4, 7, -4, 4, -15, -15, hull.edge)
    hull.box(5, 6, -3, 3, -15, -15, hull.light)
    lights(hull, 5, 12, 5)
    lights(hull, 5, -15, 7, rear=True)
    # The raised head is a single broad housing on a narrow swing arm.
    turret.ellipse(0, 0, -3, 4, 3, turret.dark)
    turret.box(1, 2, -2, 2, -5, -2, turret.dark)
    turret.box(3, 4, -1, 1, -4, 1, turret.base)
    turret.box(5, 6, -5, 5, -2, 4, turret.base)
    turret.box(6, 6, -4, 4, -1, 3, turret.light)
    turret.box(5, 5, -4, 4, 4, 4, turret.edge)
    for x in (-3, 3):
        turret.box(5, 6, x - 1, x + 1, 5, 5, STEEL)
        turret.pixel(6, x, 5, RUBBER)
    turret.pixel(6, 0, 5, GLASS)
    return 5


def jaguar(hull, turret, vehicle):
    model = vehicle["id"]
    ss11 = model == "de_rakjpz2"
    hot = model == "de_jaguar1"
    tracks(hull, (-12, -6, 0, 6, 12), (-16, 16), height=6)
    hull.loft(2, [(-15, 14, 6, 1, 3), (-16, 16, 7, 1, 2), (-17, 17, 8, 1, 3),
                  (-16, 16, 8, 1, 3), (-16, 13, 7, 1, 4), (-15, 9, 6, 2, 3)])
    if ss11:
        skirts(hull, 5, 5, [(-16, 15)])
    else:
        skirts(hull, 5 if not hot else 4, 6, [(-15, -10), (-9, -4), (-3, 3), (4, 10), (11, 15)])
        hull.box(5, 5, -6, 6, 14, 15, hull.dark)
        for x in range(-5, 6, 2):
            hull.pixel(6, x, 12, hull.edge)
    hull.grille(7, -6, 6, -14, -7)
    hull.hatch(7, -3, 6, 2, 1)
    hull.hatch(7, 3, 4, 2, 1)
    lights(hull, 5, 15, 6)
    lights(hull, 5, -16, 6, rear=True)
    if ss11:
        turret.box(0, 1, -5, 5, -4, 2, turret.base)
        for x in (-4, 4):
            turret.box(2, 2, x - 1, x + 1, -2, 3, turret.dark)
            tube(turret, x, -4, 6, 3)
        return 6
    turret.ellipse(0, 0, 0, 4, 4, turret.dark)
    turret.box(1, 2, -2, 2, -3, 2, turret.base)
    if hot:
        turret.sight(2, 2, 2, height=3)
        tube(turret, 0, -4, 6, 3)
        turret.box(1, 2, -5, -4, -3, 1, turret.dark)
        return 6
    turret.box(3, 3, -3, 3, -4, 2, turret.dark)
    turret.sight(3, -2, 3)
    tube(turret, 0, -5, 8, 4)
    return 8


def konkurs(hull, turret, vehicle):
    hull.loft(2, [(-13, 12, 5, 3, 4), (-14, 13, 6, 2, 4), (-15, 14, 7, 2, 3),
                  (-15, 14, 7, 2, 3), (-14, 11, 6, 2, 3), (-13, 7, 5, 2, 2)])
    tires(hull, (-9, 9))
    hull.grille(7, -4, 4, -13, -8)
    hull.lid(7, -5, 5, -6, 1)
    hull.box(6, 6, -4, -1, 8, 10, hull.dark)
    hull.box(6, 6, 1, 4, 8, 10, hull.dark)
    hull.box(6, 6, -3, -1, 10, 10, GLASS)
    hull.box(6, 6, 1, 3, 10, 10, GLASS)
    lights(hull, 5, 12, 4)
    lights(hull, 5, -14, 5, rear=True)
    turret.ellipse(0, 0, -1, 3, 3, turret.dark)
    turret.box(1, 2, -2, 2, -3, 1, turret.dark)
    turret.box(3, 3, -6, 6, -5, 2, turret.edge)
    for x in (-6, -3, 0, 3, 6):
        tube(turret, x, -5, 5, 4)
    return 5


def shturm(hull, turret, vehicle):
    tracks(hull, (-12, -7, -2, 3, 8, 12), (-16, 16))
    hull.loft(2, [(-15, 14, 6, 1, 2), (-16, 16, 7, 1, 2), (-17, 17, 7, 1, 2),
                  (-16, 16, 8, 1, 3), (-16, 12, 7, 2, 3)])
    hull.lid(6, -6, 6, -14, -4)
    hull.grille(6, -5, 5, -3, 2)
    for x in (-4, 4):
        hull.hatch(6, x, 8, 2, 1)
        hull.box(6, 6, x - 1, x + 1, 10, 11, GLASS)
    lights(hull, 5, 13, 6)
    lights(hull, 5, -16, 6, rear=True)
    turret.ellipse(0, 0, -2, 3, 3, turret.dark)
    turret.box(1, 3, -1, 1, -4, -3, turret.dark)
    turret.box(3, 4, -1, 1, -3, 0, turret.base)
    tube(turret, 0, -6, 10, 5)
    return 10


def pt76(hull, turret, vehicle):
    tracks(hull, (-13, -8, -3, 2, 7, 12), (-17, 16))
    hull.loft(2, [(-15, 14, 6, 2, 3), (-17, 16, 7, 1, 3), (-18, 17, 8, 1, 4),
                  (-18, 18, 9, 1, 3), (-17, 15, 8, 1, 2)])
    hull.hatch(6, 0, 12, 2, 1)
    hull.grille(6, -6, 6, -15, -7)
    hull.box(5, 5, -6, 6, 17, 18, hull.light)
    for x in (-5, 5):
        hull.box(3, 4, x - 1, x + 1, -18, -18, hull.dark)
        hull.pixel(4, x, -18, hull.edge)
    lights(hull, 5, 14, 6)
    lights(hull, 5, -17, 7, rear=True)
    for z, (rx, ry) in enumerate(((6, 6), (7, 7), (6, 6), (5, 5))):
        turret.ellipse(z, 0, 0, rx, ry, tone(turret.base, .79 + z * .095))
    turret.hatch(3, 0, -2, 4, 2)
    turret.sight(3, -3, 2, height=2)
    mantlet(turret, 6)
    return barrel(turret, 6, 22, style="baffle")


def bmp(hull, turret, vehicle):
    model = vehicle["id"]
    first = model == "ru_bmp1"
    second = model == "ru_bmp2"
    upgraded = model == "ru_bmp3m"
    rear_engine = not (first or second)
    top = vehicle["hullBaseHeight"] - 1
    wheel_centers = (-12, -7, -2, 3, 8, 13) if rear_engine else (-12, -7, -3, 2, 7, 12)
    tracks(hull, wheel_centers, (-16, 16), band=2 if not upgraded else 3)
    hull.loft(2, [(-15, 14, 6, 1, 3), (-16, 16, 7, 1, 4), (-17, 17, 8, 1, 4),
                  (-17, 16, 9 if rear_engine else 8, 1, 3),
                  (-16, 12 if rear_engine else 10, 8, 1, 3),
                  *[(-16, 11, 8, 1, 2)] * (top - 6)])
    skirts(hull, 4 if upgraded else 5, 6 if upgraded else 5,
           [(-16, -11), (-10, -5), (-4, 1), (2, 7), (8, 14)])
    hull.box(5, 5, -6, 6, 15, 16, hull.light)
    if rear_engine:
        hull.hatch(top, 0, 10, 2, 1)
        hull.grille(top, -4, 4, -15, -10)
        for x in (-6, 6):
            hull.hatch(top, x, 8, 2, 1)
            hull.pixel(5, x, 14, hull.edge)
    else:
        hull.hatch(top, 4, 7, 2, 2)
        hull.grille(top, -7, 0, 3, 9)
        for x in (-4, 4):
            for y in ((-11,) if first else (-13, -8)):
                hull.lid(top, x - 2, x + 2, y - 2, y + 2)
    for x in (-3, 3):
        hull.box(4, 5, x - 2, x + 2, -17, -17, hull.dark)
        hull.box(5, 5, x - 1, x + 1, -17, -17, hull.base)
    lights(hull, 5, 13, 6)
    lights(hull, 5, -17, 6, rear=True)
    if first:
        turret.loft(0, [(-4, 4, 4, 1, 2), (-6, 5, 5, 2, 2), (-5, 4, 4, 2, 2)])
        turret.hatch(2, -2, -2, 2, 1)
        turret.sight(2, 2, 1, height=2)
        turret.box(3, 3, 0, 0, 3, 8, turret.dark)
        tube(turret, 0, 5, 12, 4)
        barrel(turret, 4, 13, style="short")
        return 12
    if second:
        turret.loft(0, [(-6, 5, 5, 2, 2), (-7, 6, 6, 2, 3), (-6, 5, 5, 2, 3),
                        (-6, 3, 4, 1, 2)])
        turret.hatch(3, -2, -3, 2, 2)
        turret.hatch(3, 2, -3, 2, 1)
        turret.sight(3, 2, 2, height=2)
        turret.box(4, 4, 0, 0, -3, -1, turret.edge)
        tube(turret, 0, -6, 3, 5)
        return barrel(turret, 5, 20, style="auto")
    turret.loft(0, [(-7, 5, 5, 2, 2), (-8, 7, 7, 2, 3), (-7, 7, 6, 2, 3),
                    (-6, 5, 5, 2, 2)])
    turret.hatch(3, -3, -2, 2, 2)
    turret.hatch(3, 3, -2, 2, 2)
    turret.sight(3, -3, 3, width=2 if upgraded else 1, height=3 if upgraded else 2)
    if upgraded:
        for x in (-7, 7):
            turret.box(1, 2, x, x, -6, 0, turret.dark)
            turret.box(3, 3, x, x, -6, -1, turret.light)
        turret.sight(3, 3, 3, height=2)
    barrel(turret, 6, 18, x=2, style="auto")
    return barrel(turret, 6, 20, style="short")


def sprut(hull, turret, vehicle):
    sdm = vehicle["id"] == "ru_2s25m"
    tracks(hull, (-13, -9, -5, 0, 5, 9, 13), (-17, 17), band=2)
    hull.loft(2, [(-16, 15, 7, 1, 3), (-17, 17, 8, 1, 3), (-18, 18, 9, 1, 4),
                  (-18, 17, 9, 1, 4), (-17, 14, 8, 1, 3), (-17, 10, 8, 1, 3)])
    skirts(hull, 4 if sdm else 5, 6 if sdm else 5,
           [(-16, -11), (-10, -5), (-4, 1), (2, 7), (8, 14)])
    hull.hatch(7, 0, 8, 2, 1)
    hull.grille(7, -6, 5, -16, -8)
    hull.box(5, 5, -6, 6, 16, 16, hull.light)
    lights(hull, 5, 14, 6)
    lights(hull, 5, -18, 6, rear=True)
    turret.loft(0, [(-7, 7, 6, 2, 3), (-9, 9, 8, 2, 4), (-9, 9, 8, 2, 4),
                    (-8, 7, 7, 2, 3), (-8, 5, 6, 2, 3)])
    turret.hatch(4, -3, -3, 2, 2)
    turret.hatch(4, 3, -3, 2, 2)
    turret.sight(4, -3, 4, height=2)
    if sdm:
        turret.sight(4, 4, 2, width=2, height=3)
        turret.box(4, 5, 4, 6, -6, -4, turret.dark)
        turret.box(6, 6, 5, 5, -6, 1, STEEL)
        basket(turret, 2, -12, -9, 7)
    mantlet(turret, 8)
    return barrel(turret, 9, vehicle["muzzleDist"], style="sleeve")


def terminator(hull, turret, vehicle):
    turret.loft(0, [(-7, 5, 5, 1, 2), (-8, 6, 6, 2, 2), (-8, 6, 5, 2, 2)])
    turret.box(2, 4, -3, 3, -4, 3, turret.dark)
    turret.box(4, 4, -2, 2, -5, 3, turret.base)
    for x in (-2, 2):
        barrel(turret, 4, 20, x=x, z=2, style="auto")
        turret.box(2, 3, x, x, 4, 7, turret.dark)
    for side in (-1, 1):
        inner, outer = sorted((side * 4, side * 10))
        turret.box(3, 3, inner, outer, -3, 1, turret.dark)
        for x in (side * 7, side * 9):
            tube(turret, x, -6, 5, 5, turret.light)
            turret.pixel(5, x, 5, (138, 70, 43))
        a, b = sorted((side * 6, side * 10))
        turret.box(6, 6, a, b, -7, 2, turret.base)
        turret.box(6, 6, a + 1, b - 1, -6, -5, turret.dark)
    turret.sight(4, -2, 3, height=2)
    turret.sight(4, 1, -4, width=1, height=4)
    turret.pixel(7, 1, -5, LENS)
    return 20


def early_abrams(hull, turret, vehicle):
    prototype = vehicle["id"] == "us_xm1"
    tracks(hull, (-15, -10, -5, 0, 5, 10, 15), (-19, 19), height=6)
    hull.loft(2, [
        (-16, 15, 8, 3, 3), (-18, 18, 9, 2, 2), (-19, 19, 10, 2, 2),
        (-19, 19, 10, 2, 1), (-19, 17, 10, 2, 1), (-18, 14, 10, 2, 2),
        (-17, 11, 9, 2, 2), (-17, 8, 9, 2, 2), (-16, 7, 8, 1, 1),
    ])
    if prototype:
        skirts(hull, 4, 7, [(-16, -12), (-11, -6), (-5, 0), (1, 6), (7, 12), (13, 17)])
        for x in (-9, 9):
            hull.box(8, 8, x - 1, x + 1, -15, -9, hull.dark)
            hull.pixel(8, x, 10, hull.light)
        hull.grille(10, -6, 6, -15, -10)
        hull.box(5, 7, -6, 6, -19, -19, hull.dark)
        for x in range(-5, 6, 2):
            hull.pixel(6, x, -19, hull.edge)
    else:
        skirts(hull, 5, 7, [(-16, -12), (-11, -7), (-6, -2), (-1, 4)])
        skirts(hull, 4, 7, [(5, 10), (11, 17)])
        hull.grille(10, -7, -2, -15, -8)
        hull.grille(10, 2, 7, -15, -8)
        hull.lid(10, -2, 2, -15, -8)
        hull.box(5, 7, -8, 8, -19, -19, hull.edge)
        for x in (-8, -3, 3, 8):
            hull.box(5, 7, x, x, -19, -19, hull.dark)
        for x in (-6, 6):
            hull.hatch(9, x, 6, 1, 1)
    hull.lid(10, -2, 2, 3, 7)
    hull.box(10, 10, -1, 1, 7, 7, GLASS)
    lights(hull, 6, 16, 8)
    lights(hull, 6, -19, 8, rear=True)

    # Four levels of sloping cheeks, followed by the flat roof.
    if prototype:
        outline = (
            ((-6, -8), (6, -8), (8, -2), (7, 4), (3, 8), (-4, 8), (-8, 3), (-8, -3)),
            ((-8, -10), (8, -10), (10, -2), (8, 5), (3, 10), (-4, 10), (-10, 3), (-10, -4)),
            ((-8, -10), (8, -10), (10, -3), (9, 3), (3, 10), (-4, 9), (-10, 2), (-10, -4)),
            ((-7, -9), (7, -9), (9, -3), (8, 2), (3, 8), (-4, 7), (-9, 1), (-9, -4)),
            ((-6, -8), (6, -8), (7, -3), (6, 1), (2, 5), (-4, 5), (-7, 1), (-8, -4)),
        )
    else:
        outline = (
            ((-8, -9), (8, -9), (8, 1), (5, 6), (2, 8), (-4, 8), (-8, 5)),
            ((-10, -11), (10, -11), (10, 1), (7, 8), (3, 10), (-4, 10), (-10, 5)),
            ((-10, -11), (10, -11), (10, 1), (7, 8), (3, 10), (-4, 10), (-10, 5)),
            ((-10, -10), (10, -10), (10, 0), (7, 6), (3, 9), (-4, 9), (-10, 4)),
            ((-9, -10), (9, -10), (9, 0), (6, 4), (2, 7), (-4, 7), (-9, 3)),
        )
    for z, section in enumerate(outline):
        turret.plate(z, section, .76 + .078 * z)
    turret.hatch(4, 4, -2, 2, 2)
    cupola(turret, 4, -4, -2)
    turret.box(5, 6, -4, -4, -2, 4, STEEL)
    turret.sight(4, -4, 4 if prototype else 5, width=1 if prototype else 2)
    if prototype:
        turret.lid(4, -4, 4, -8, -4)
        turret.box(2, 3, -6, 6, -11, -10, turret.dark)
    else:
        for x in (-9, 9):
            turret.box(2, 3, x - 1, x + 1, -7, -3, turret.dark)
            turret.box(4, 4, x - 1, x + 1, -7, -3, turret.paint(x, -5))
            turret.box(3, 3, x - 1, x + 1, 3, 5, turret.edge)
        for x in (-4, 4):
            turret.lid(4, x - 3, x + 3, -9, -5)
            turret.box(4, 4, x - 1, x + 1, -11, -10, turret.dark)
    mantlet(turret, 9, 3 if prototype else 2)
    return barrel(turret, 9, 27, style="cannon")


def soviet_deck(hull, front, auxiliary_drums=True):
    hull.hatch(9, 0, front - 1, 2, 1)
    hull.box(9, 9, -1, 1, front, front, GLASS)
    lights(hull, 6, 17, 9)
    lights(hull, 6, -19, 9, rear=True)
    if auxiliary_drums:
        for x0, x1 in ((-10, -3), (3, 10)):
            hull.box(6, 6, x0, x1, -21, -18, hull.edge)
            hull.box(7, 7, x0, x1, -21, -18, hull.dark)
            hull.box(8, 8, x0, x1, -20, -19, hull.base)
            hull.box(8, 8, x0 + 1, x1 - 1, -20, -20, hull.light)


def soviet_turret_details(turret, roof, front, hatch_y, searchlight_x):
    cupola(turret, roof, -4, hatch_y)
    turret.hatch(roof, 4, hatch_y, 3, 2)
    turret.box(roof + 1, roof + 2, -5, -5, hatch_y - 1, hatch_y + 4, STEEL)
    turret.sight(roof, 4, hatch_y + 3, height=2)
    turret.box(2, 3, searchlight_x - 1, searchlight_x + 1, front - 2, front, turret.edge)
    turret.box(3, 3, searchlight_x - 1, searchlight_x + 1, front, front, LENS)
    turret.pixel(4, searchlight_x, front - 1, turret.light)
    mantlet(turret, front, 2)


def t64a(hull, turret, vehicle):
    tracks(hull, (-15, -10, -5, 1, 7, 13), (-19, 19), band=2, wheel_profile=(0, 1, 1))
    hull.loft(2, [
        (-17, 16, 8, 2, 3), (-18, 18, 9, 2, 2), (-19, 20, 10, 2, 3),
        (-19, 20, 10, 2, 3), (-19, 18, 10, 2, 2), (-19, 15, 10, 2, 2),
        (-18, 12, 9, 1, 2), (-17, 10, 9, 1, 2),
    ])
    skirts(hull, 5, 5, [(-19, 17)])
    for side in (-1, 1):
        # The folded fender shields leave the small wheels visible.
        for y in (0, 5, 10, 15):
            x = side * 12
            hull.box(4, 5, x, x, y - 2, y + 1, hull.dark)
            hull.box(5, 5, x - side, x - side, y - 2, y + 1, hull.light)
        x = side * 10
        hull.box(6, 7, x - 1, x + 1, -15, -8, hull.dark)
        hull.box(7, 7, x, x, -14, -9, hull.light)
    hull.grille(9, -7, 7, -16, -10)
    hull.box(8, 8, -5, 5, -18, -18, hull.edge)
    soviet_deck(hull, 9, auxiliary_drums=False)
    for z, (rx, ry) in enumerate(((7, 7), (9, 9), (10, 9), (9, 8), (7, 7), (5, 5))):
        turret.ellipse(z, 0, 0, rx, ry, tone(turret.base, .76 + z * .057))
    soviet_turret_details(turret, 5, 8, -2, -4)
    turret.box(4, 4, -5, -2, -7, -5, turret.dark)
    return barrel(turret, 8, 30, style="cannon")


def kontakt_brick(stack, z, x, y, length=2):
    stack.box(z - 1, z - 1, x, x + 1, y - 1, y + length - 1, stack.edge)
    stack.box(z, z, x, x + 1, y, y + length - 1, tone(stack.paint(x, y), 1.12))
    stack.box(z, z, x, x + 1, y + length - 1, y + length - 1, stack.light)


def t72(hull, turret, vehicle):
    reactive = vehicle["id"] == "ru_t72b"
    tracks(hull, (-14, -8, -2, 4, 10, 15), (-19, 19), height=6)
    hull.loft(2, [
        (-17, 17, 8, 2, 2), (-19, 19, 10, 2, 3), (-20, 20, 10, 2, 3),
        (-20, 20, 11, 2, 2), (-19, 18, 11, 2, 2), (-19, 15, 10, 2, 2),
        (-19, 12, 10, 2, 1), (-18, 10, 9, 1, 1),
    ])
    skirts(hull, 5, 6, [(-18, -12), (-11, -6), (-5, 0), (1, 6), (7, 12), (13, 17)])
    hull.grille(9, -7, -1, -17, -10)
    hull.grille(9, 1, 7, -17, -10)
    hull.lid(9, 5, 8, -11, -7)
    soviet_deck(hull, 10)
    if reactive:
        for y, z, columns in ((16, 7, range(-9, 9, 3)), (13, 8, range(-8, 9, 3)),
                              (10, 9, (-9, -6, 5, 8))):
            for x in columns:
                kontakt_brick(hull, z, x, y)
        for side in (-1, 1):
            for y in (1, 5, 9, 13):
                x = side * 12
                hull.box(5, 6, x, x, y, y + 2, hull.dark)
                hull.box(7, 7, x, x, y, y + 2, hull.base)
                hull.pixel(7, x, y + 2, hull.light)
    else:
        hull.box(7, 7, -7, 7, 15, 15, hull.dark)
        hull.box(8, 8, -6, 6, 12, 12, hull.light)

    sections = (
        ((-8, 8, 8, 3, 2), (-10, 10, 10, 3, 2), (-10, 11, 11, 3, 2),
         (-10, 11, 11, 3, 3), (-9, 9, 9, 3, 3), (-8, 6, 7, 2, 2))
        if reactive else
        ((-8, 7, 8, 3, 2), (-9, 10, 10, 3, 3), (-10, 10, 10, 3, 3),
         (-9, 9, 9, 3, 3), (-8, 8, 8, 3, 3), (-7, 6, 6, 2, 2))
    )
    turret.loft(0, sections, round_bow=True)
    if reactive:
        for side in (-1, 1):
            for x, y, z in ((3, 9, 4), (6, 8, 4), (9, 6, 4), (11, 3, 4),
                            (4, 6, 5), (7, 5, 5), (9, 2, 5), (10, -1, 5)):
                kontakt_brick(turret, z, x if side == 1 else -x - 1, y)
        turret.box(2, 4, 10, 11, -4, -1, turret.dark)
        turret.box(4, 4, 10, 10, -4, -1, STEEL)
    else:
        for side in (-1, 1):
            for x, y in ((7, 7), (8, 6), (9, 5)):
                turret.pixel(3, side * x, y, turret.dark)
                turret.pixel(4, side * x, y, STEEL)
    turret.box(3, 4, -5, 5, -10, -8, turret.dark)
    turret.box(4, 4, -4, 4, -10, -9, turret.base)
    soviet_turret_details(turret, 5, 10 if reactive else 9, -3, -3 if reactive else -4)
    return barrel(turret, 10 if reactive else 9, 30, style="cannon")


def t80u(hull, turret, vehicle):
    tracks(hull, (-14, -9, -3, 3, 9, 14), (-19, 19), height=6, wheel_profile=(0, 2, 2, 0))
    hull.loft(2, [
        (-18, 16, 8, 2, 3), (-19, 18, 10, 2, 3), (-20, 20, 11, 2, 3),
        (-20, 20, 11, 2, 3), (-20, 18, 11, 2, 2), (-20, 15, 11, 2, 2),
        (-19, 12, 10, 2, 2), (-19, 10, 9, 2, 2),
    ])
    skirts(hull, 4, 6, [(-18, -13), (-12, -7), (-6, -1)])
    skirts(hull, 4, 7, [(0, 5), (6, 11), (12, 17)])
    for side in (-1, 1):
        for x0, x1 in ((2, 5), (6, 9)):
            left, right = sorted((side * x0, side * x1))
            hull.box(6, 7, left, right, 14, 17, hull.dark)
            hull.box(8, 8, left, right, 13, 15, hull.paint(side * x1, 14))
            hull.box(8, 8, left, right, 15, 15, hull.light)
        hull.box(4, 5, side * 7 - 2, side * 7 + 2, 19, 20, RUBBER)
        for y in (2, 8, 14):
            hull.pixel(7, side * 12, y, hull.light)
    hull.grille(9, -7, 0, -18, -11)
    hull.lid(9, 1, 7, -18, -11)
    hull.box(5, 7, -8, 8, -20, -20, hull.edge)
    for x in range(-7, 8, 2):
        hull.pixel(6, x, -20, STEEL)
    soviet_deck(hull, 10)

    turret.loft(0, [(-9, 6, 8, 3, 3), (-11, 9, 10, 3, 3), (-11, 10, 10, 3, 3),
                    (-10, 9, 9, 3, 3), (-9, 7, 7, 3, 2), (-8, 6, 6, 2, 2)])
    for side in (-1, 1):
        # Faceted blocks form a broad arc above the hanging rubber panels.
        for x, rear, front in ((4, 7, 11), (7, 5, 9), (10, 2, 6)):
            left, right = sorted((side * x, side * (x + 2)))
            turret.box(0, 1, left, right, front - 1, front, RUBBER)
            turret.box(2, 3, left, right, rear, front, turret.edge)
            turret.box(3, 4, left, right, rear - 1, front - 1, turret.paint(side * x, rear))
            turret.box(4, 4, left, right, front - 1, front - 1, turret.light)
            turret.box(5, 5, left, right, rear - 2, rear, turret.dark)
    turret.box(3, 4, -7, 7, -12, -9, turret.dark)
    turret.box(4, 4, -6, 6, -12, -11, turret.paint(0, -11))
    soviet_turret_details(turret, 5, 9, -3, -3)
    return barrel(turret, 9, 30, style="cannon")


MODELS = {
    "us_m48a1": (sculpt_patton, ((116, 109, 90),)),
    "us_m48a3": (sculpt_patton, ((105, 112, 90),)),
    "us_m60": (patton, OLIVE),
    "us_m60a3": (patton, MERDC),
    "us_xm1": (early_abrams, ((109, 119, 86),)),
    "us_m1": (early_abrams, MERDC),
    "us_m551": (sheridan, OLIVE),
    "us_m551a1": (sheridan, MERDC),
    "us_m8_ags": (modern_light, SAND),
    "us_m1128": (stryker, FIELD),
    "us_m10_booker": (modern_light, FIELD),
    "us_m901": (itv, OLIVE),
    "us_m2": (bradley, FIELD),
    "us_m2a2": (bradley, SAND),
    "us_m3a3": (bradley, MERDC),
    "us_m2a4": (bradley, NATO),
    "de_tiger_ii": (sculpt_tiger_ii, SAND),
    "de_m48a2": (patton, OLIVE),
    "de_luchs": (luchs, FIELD),
    "de_wiesel_mk": (wiesel, NATO),
    "de_marder": (german_ifv, NATO),
    "de_puma": (german_ifv, NATO),
    "de_kf41_lynx": (german_ifv, FIELD),
    "de_rakjpz2": (jaguar, OLIVE),
    "de_jaguar2": (jaguar, NATO),
    "de_jaguar1": (jaguar, NATO),
    "de_wiesel_tow": (wiesel, FIELD),
    "de_boxer": (boxer, EXPO),
    "ru_t64a": (t64a, OLIVE),
    "ru_t72a": (t72, SOVIET),
    "ru_t72b": (t72, ((104, 121, 77),)),
    "ru_t80u": (t80u, EXPO),
    "ru_t14": (sculpt_armata, FIELD),
    "ru_pt76b": (pt76, SOVIET),
    "ru_bmp1": (bmp, SOVIET),
    "ru_bmp2": (bmp, SOVIET),
    "ru_bmp3": (bmp, FIELD),
    "ru_bmp3m": (bmp, EXPO),
    "ru_9p148": (konkurs, SOVIET),
    "ru_9p149": (shturm, FIELD),
    "ru_2s25": (sprut, SOVIET),
    "ru_2s25m": (sprut, EXPO),
    "ru_bmpt": (terminator, EXPO),
}

# Elevated muzzle sprites use a different visual origin from the standard z=2 barrel.
MUZZLE_HEIGHTS = {
    "us_m48a1": PATTON_MUZZLE_HEIGHT,
    "us_m48a3": PATTON_MUZZLE_HEIGHT,
    "us_m901": 6,
    "de_tiger_ii": TIGER_II_MUZZLE_HEIGHT,
    "de_marder": 3,
    "de_rakjpz2": 3,
    "de_jaguar2": 4,
    "de_jaguar1": 3,
    "de_wiesel_tow": 4,
    "ru_bmp1": 4,
    "ru_9p148": 4,
    "ru_9p149": 5,
    "ru_t14": 3,
}


def build(output_dir):
    catalog = json.loads((RESOURCES / "Data/vehicles_catalog.json").read_text(encoding="utf-8"))
    required = MODELS.keys() - {vehicle["id"] for vehicle in catalog["vehicles"]}
    if required:
        raise ValueError(f"Missing vehicle definitions: {sorted(required)}")
    png_dir = output_dir / "Vehicles"
    png_dir.mkdir(parents=True, exist_ok=True)
    catalog_dir = output_dir / "Data"
    catalog_dir.mkdir(exist_ok=True)
    rebuilt = build_fleet(catalog, png_dir)
    for vehicle in catalog["vehicles"]:
        model = vehicle["id"]
        if model in rebuilt or model not in MODELS:
            continue
        turret_capacity = 9
        if model == "de_tiger_ii":
            vehicle.update(TIGER_II_GEOMETRY)
            turret_capacity = TIGER_II_TURRET_SLICES
        sculpt, palette = MODELS[model]
        hull = Stack(vehicle["hullWidth"], vehicle["hullLength"], vehicle["hullBaseHeight"], palette)
        turret = Stack(vehicle["turretWidth"], vehicle["turretLength"], turret_capacity, palette)
        vehicle["muzzleDist"] = sculpt(hull, turret, vehicle)
        if model in MUZZLE_HEIGHTS:
            vehicle["muzzleHeight"] = MUZZLE_HEIGHTS[model]
        if model != "ru_bmpt":
            vehicle["hullSlices"] = hull.save(png_dir / f"{model}_hull_slices.png")
        vehicle["turretSlices"] = turret.save(png_dir / f"{model}_turret_slices.png")
    (catalog_dir / "vehicles_catalog.json").write_text(
        json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path)
    build(parser.parse_args().output_dir)
