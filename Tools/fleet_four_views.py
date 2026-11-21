"""Rebuild the fleet's game-scale exteriors from registered photo reference sheets.

The seed archive retains the original part split, hard points and thin exterior
features. Photo registration sets hull proportions, corrects the visible
envelope, and samples the finish. Running gear is reconstructed separately.
"""

from collections import deque
from dataclasses import dataclass
from io import BytesIO
import json
from math import sqrt
from pathlib import Path
from statistics import median
from zipfile import ZipFile

from PIL import Image

from leopard1_turrets import LEOPARD1_TURRETS, paint_turret
from vehicle_photo_revision import HULLS as PHOTO_HULLS, TRACES as PHOTO_TURRETS, build_vehicle


REFERENCES = Path(__file__).with_name("VehicleArt") / "Fleet"
SEEDS = REFERENCES / "original-slices.zip"
KEEP = {"us_m47", "de_m47", "us_m48a1", "us_m48a3"}

# Axle / road-wheel counts, with the five- and six-wheel Soviet families kept
# separate for their different spacing.
RUNNING_GEAR = {
    "us_m60": 6, "us_m60a3": 6, "us_xm1": 7, "us_m1": 7, "us_m1a1": 7,
    "us_m1a2_sep2": 7, "us_m1a2_sep3": 7, "us_m1e3": 7,
    "us_m551": 5, "us_m551a1": 5, "us_m8_ags": 6, "us_m1128": -4,
    "us_m10_booker": 7, "us_m901": 5, "us_m2": 6, "us_m2a2": 6, "us_m3a3": 6, "us_m2a4": 6,
    "de_m48a2": 6, "de_leo1": 7, "de_leo1a1a1": 7, "de_leo1a5": 7,
    "de_leo2av": 7, "de_leo2a4": 7, "de_leo2a5": 7, "de_leo2a7v": 7, "de_leo2a8": 7,
    "de_kf51": 7, "de_luchs": -4, "de_wiesel_mk": 3, "de_wiesel_tow": 3, "de_marder": 6,
    "de_puma": 6, "de_kf41_lynx": 6, "de_rakjpz2": 5, "de_jaguar2": 5, "de_jaguar1": 5, "de_boxer": -4,
    "ru_t54": 5, "ru_t55a": 5, "ru_t62": 5, "ru_t64a": 6, "ru_t72a": 6, "ru_t72b": 6,
    "ru_t80u": 6, "ru_t90a": 6, "ru_t72b3m": 6, "ru_t90m": 6, "ru_t14": 7,
    "ru_pt76b": 6, "ru_bmp1": 6, "ru_bmp2": 6, "ru_bmp3": 6, "ru_bmp3m": 6,
    "ru_9p148": -2, "ru_9p149": 6, "ru_2s25": 7, "ru_2s25m": 7, "ru_bmpt": 6,
}
SPACING = {
    "ru_t54": (-.56, -.31, -.07, .18, .61),
    "ru_t55a": (-.56, -.31, -.07, .19, .61),
    "ru_t62": (-.61, -.25, .08, .32, .57),
    "ru_9p148": (-.53, .53),
    "de_luchs": (-.73, -.26, .26, .73),
    "de_boxer": (-.68, -.31, .28, .67),
    "us_m1128": (-.70, -.30, .23, .63),
}
WHEEL_SCALE = {
    "de_wiesel_mk": 1.24,
    "de_wiesel_tow": 1.24,
    "ru_t64a": .73,
    "ru_t72a": 1.15,
    "ru_t72b": 1.15,
    "ru_t80u": .86,
    "ru_t90a": 1.15,
    "ru_t72b3m": 1.15,
    "ru_t90m": 1.15,
}
LONG_HULLS = {
    "us_m60": 43, "us_m60a3": 43, "us_xm1": 43, "us_m1": 43, "us_m1a1": 43,
    "us_m1a2_sep2": 43, "us_m1a2_sep3": 43, "us_m1e3": 43,
    "de_m48a2": 43, "de_leo1": 42, "de_leo1a1a1": 42, "de_leo1a5": 42,
    "de_leo2av": 44, "de_leo2a4": 44, "de_leo2a5": 44, "de_leo2a7v": 44,
    "de_leo2a8": 44, "de_kf51": 44, "ru_t62": 42,
}
# Target deck, rear fittings above the deck, and turret levels at game scale.
# The older compact silhouettes sit lower than the taller modern roof fittings.
LOW_PROFILES = {
    "ru_t54": (7, 1, 6),
    "ru_t55a": (7, 1, 6),
    "ru_t64a": (7, 0, 6),
    "ru_t72a": (8, 0, 5),
    "ru_t72b": (8, 0, 6),
    "ru_t80u": (8, 0, 7),
    "ru_t90a": (8, 1, 6),
    "ru_t72b3m": (8, 1, 8),
    "ru_t90m": (8, 1, 9),
    "ru_t14": (9, 0, 9),
}
SKIRTED = {
    "us_xm1", "us_m1", "us_m1a1", "us_m1a2_sep2", "us_m1a2_sep3", "us_m1e3",
    "us_m2a2", "us_m3a3", "us_m2a4", "de_leo1a1a1", "de_leo1a5", "de_leo2av",
    "de_leo2a4", "de_leo2a5", "de_leo2a7v", "de_leo2a8", "de_kf51", "de_marder",
    "de_puma", "de_kf41_lynx", "de_jaguar1", "ru_t72b", "ru_t80u", "ru_t90a",
    "ru_t72b3m", "ru_t90m", "ru_t14", "ru_bmp3m", "ru_bmpt",
}
# Small, raised exterior landmarks needed where similar family silhouettes
# otherwise read the same at battle scale. Positions are in sprite pixels.
ROOF_BLOCKS = {
    "us_m1a2_sep2": ((-5, 2, 2, 2, 2),),
    "us_m1a2_sep3": ((-5, 3, 2, 2, 1), (3, -4, 2, 3, 1)),
    "us_m1e3": ((-1, -2, 3, 3, 2), (3, 1, 2, 2, 1)),
    "us_m3a3": ((-4, 0, 2, 2, 2),),
    "us_m2a4": ((-5, 2, 2, 2, 1), (3, -3, 2, 2, 1)),
    "de_leo2a5": ((-4, 0, 2, 2, 2),),
    "de_leo2a7v": ((-4, 0, 2, 3, 2), (4, -5, 3, 2, 1)),
    "de_leo2a8": ((-7, 1, 2, 3, 1), (7, 1, 2, 3, 1)),
    "de_kf51": ((-5, 1, 2, 2, 2), (4, -6, 3, 2, 1)),
    "ru_t72b3m": ((-4, 4, 2, 3, 2),),
    "ru_t90m": ((-4, 0, 2, 2, 2), (5, -5, 3, 2, 1)),
    "ru_bmp3m": ((-4, 1, 2, 2, 2),),
    "ru_2s25m": ((-4, 3, 2, 2, 2),),
}
# Sprite pixels forward of the swivel to the exposed tube, followed by the
# fume extractor's start and end relative to the muzzle. The T-54 artwork
# depicts the muzzle-mounted extractor; the T-62's sits forward of mid-tube.
SOVIET_BARREL_PROFILES = {
    "ru_t54": (9, -3, 0),
    "ru_t62": (10, -10, -7),
}
TRACK = (57, 56, 49, 255)
TRACK_EDGE = (73, 71, 61, 255)
RUBBER = (44, 45, 41, 255)
GLASS = (66, 87, 83, 255)


def fit(value, minimum, maximum):
    return max(minimum, min(maximum, value))


def luma(rgb):
    return (rgb[0] + rgb[1] * 2 + rgb[2]) / 4


class View:
    def __init__(self, image):
        self.image = image.copy()
        self.image.thumbnail((360, 256), Image.Resampling.LANCZOS)
        self.pixels = self.image.load()
        self.width, self.height = self.image.size
        edge = [self.pixels[x, y] for x in (1, self.width - 2) for y in (1, self.height - 2)]
        bg = tuple(median(p[n] for p in edge) for n in range(3))
        w, h = self.image.size
        mask = bytearray(
            luma(p) < luma(bg) - 24 or (max(p) - min(p) > 31 and luma(p) < luma(bg) + 6)
            for p in self.image.getdata()
        )
        # Keep the vehicle component and discard labels, framing, and stray shadows.
        biggest = []
        for start, on in enumerate(mask):
            if not on:
                continue
            queue = deque([start])
            component = [start]
            mask[start] = 0
            while queue:
                index = queue.popleft()
                x, y = index % w, index // w
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    pos = ny * w + nx
                    if 0 <= nx < w and 0 <= ny < h and mask[pos]:
                        mask[pos] = 0
                        queue.append(pos)
                        component.append(pos)
            if len(component) > len(biggest):
                biggest = component
        if len(biggest) < w * h * .035:
            raise ValueError("Could not isolate a vehicle in one of the four views")
        self.mask = bytearray(w * h)
        for index in biggest:
            self.mask[index] = 1
        xs, ys = zip(*((i % w, i // w) for i in biggest))
        self.left, self.right = min(xs), max(xs)
        self.top, self.bottom = min(ys), max(ys)

    def occupied(self, x, y):
        x, y = round(x), round(y)
        return 0 <= x < self.width and 0 <= y < self.height and self.mask[y * self.width + x]

    def color(self, x, y):
        x, y = round(x), round(y)
        samples = [self.pixels[nx, ny] for nx, ny in ((x,y), (x-1,y), (x+1,y), (x,y-1), (x,y+1))
                   if self.occupied(nx, ny)]
        if not samples:
            return None
        return tuple(round(median(p[n] for p in samples)) for n in range(3))

    def span(self, top, bottom):
        run = [x for x in range(self.left, self.right + 1)
               if any(self.occupied(x, y) for y in range(round(top), round(bottom) + 1))]
        return (run[0], run[-1]) if len(run) > 12 else (self.left, self.right)

    def plan_hull(self):
        depth = self.bottom - self.top
        columns = [x for x in range(self.left, self.right + 1)
                   if sum(self.occupied(x, y) for y in range(self.top, self.bottom + 1)) >= depth * .40]
        if len(columns) < 12:
            return self.left, self.right
        return columns[0], columns[-1]


class Registration:
    def __init__(self, filename, hull_fraction, turret_offset):
        with Image.open(filename) as source:
            photo = source.convert("RGB")
        w, h = photo.size
        self.plan, self.side, self.front, self.rear = (
            View(photo.crop(rect)) for rect in ((0, 0, w//2, h//2), (w//2, 0, w, h//2),
                                                (0, h//2, w//2, h), (w//2, h//2, w, h))
        )
        self.hull_fraction = hull_fraction
        self.turret_offset = turret_offset
        self.plan_left, self.plan_right = self.plan.plan_hull()
        self.side_left, self.side_right = self.side.span(self.deck(self.side), self.side.bottom)
        self.plan_body_aspect = (self.plan_right - self.plan_left) / max(1, self.plan.bottom - self.plan.top)

    def deck(self, view):
        return view.bottom - (view.bottom - view.top) * self.hull_fraction

    def picture(self, face, x, y, z, turret):
        if turret:
            y += self.turret_offset
            # Preset camera frames use the portion of each image occupied by the turret.
            length = (self.plan_right - self.plan_left)
            if face == "top":
                return self.plan, (self.plan_right + self.plan_left) / 2 + .48 * length * y, +                    (self.plan.top + self.plan.bottom) / 2 - (self.plan.bottom-self.plan.top) * .46 * x
            if face == "side":
                return self.side, (self.side_left + self.side_right) / 2 + .50 * (self.side_right-self.side_left) * y, +                    self.deck(self.side) - (self.deck(self.side) - self.side.top) * z
            view = self.front if face == "front" else self.rear
            deck = self.deck(view)
            cross = x if face == "front" else -x
            return view, view.left + (view.right-view.left) * (.5 + .43 * cross), deck - (deck-view.top)*z
        if face == "top":
            return self.plan, self.plan_left + (self.plan_right-self.plan_left) * (.5+.5*y), +                self.plan.top + (self.plan.bottom-self.plan.top) * (.5-.5*x)
        if face == "side":
            return self.side, self.side_left + (self.side_right-self.side_left) * (.5+.5*y), +                self.side.bottom - (self.side.bottom-self.deck(self.side))*z
        view = self.front if face == "front" else self.rear
        cross = x if face == "front" else -x
        return view, view.left + (view.right-view.left) * (.5+.5*cross), view.bottom-(view.bottom-self.deck(view))*z

    def sample(self, face, x, y, z, turret):
        view, px, py = self.picture(face, x, y, z, turret)
        return view.color(px, py)


@dataclass
class Part:
    layers: list
    width: int
    length: int

    @classmethod
    def read(cls, archive, meta, part):
        name = f"{meta['id']}_{part}_slices.png"
        with archive.open(name) as stream, Image.open(BytesIO(stream.read())) as sheet:
            width = meta[part + "Width"]
            length = meta[part + "Length"]
            return cls([sheet.crop((x, 0, x + width, length)).convert("RGBA")
                        for x in range(0, sheet.width, width)], width, length)

    def stretch(self, length):
        if length != self.length:
            self.layers = [layer.resize((self.width, length), Image.Resampling.NEAREST) for layer in self.layers]
            self.length = length

    def write(self, path):
        sheet = Image.new("RGBA", (self.width * len(self.layers), self.length))
        for z, layer in enumerate(self.layers):
            sheet.paste(layer, (z*self.width, 0))
        sheet.save(path)

    def trim_empty_top(self):
        while len(self.layers) > 1 and self.layers[-1].getchannel("A").getbbox() is None:
            self.layers.pop()


def matte(original, photo, gain, edge, grain):
    if photo is not None:
        v = luma(photo)
        reference = [v + (c-v)*.74 for c in photo[:3]]
        # Keep the visible paint pattern while bringing the materials into the
        # same lighting range across the photo sheets.
        weight = .46 if luma(original) > 69 else .26
        rgb = [old + (fit(ref, 32, 153) - old) * weight for old, ref in zip(original[:3], reference)]
    else:
        rgb = original[:3]
    return tuple(fit(round((rgb[i]*gain + edge+grain)/2)*2, 0, 255) for i in range(3)) + (255,)


def repaint(part, reference, turret=False):
    before = [layer.copy().load() for layer in part.layers]
    w, h, d = part.width, part.length, len(part.layers)
    for z, layer in enumerate(part.layers):
        out = layer.load()
        for y in range(h):
            ly = (y-h//2) / max(1, h//2)
            if turret:
                # The turret sheet is centered at its swivel, with blank space
                # aft to allow the barrel to rotate independently.
                ly *= 1.34
            for x in range(w):
                p = before[z][x,y]
                if p[3] == 0:
                    continue
                lx = (x-w//2) / max(1, w//2)
                lz = z / max(1, d-1)
                top = z == d-1 or not before[z+1][x,y][3]
                front = y == h-1 or not before[z][x,y+1][3]
                rear = y == 0 or not before[z][x,y-1][3]
                on_side = x in (0,w-1) or not (before[z][x-1,y][3] and before[z][x+1,y][3])
                face = "top" if top else "front" if front else "rear" if rear else "side"
                photo = reference.sample(face, lx, ly, lz, turret)
                # Directional contact shading is strongest underneath fenders
                # and turret rings; don't bake a single world-light direction.
                gain = 1.03 if top else .91 if on_side else .96
                edge = -4 if z < 2 else 0
                grain = (0,-1,0,1,0,2,-2)[(x*53+y*17+z*7)%7]
                out[x,y] = matte(p, photo, gain, edge, grain)


def registered_length(part, reference, name):
    aspect = reference.plan_body_aspect
    if not 1.42 <= aspect <= 2.65:
        return LONG_HULLS.get(name, part.length)
    # The overhead silhouette establishes the long axis, constrained to a
    # modest adjustment from the roster's common sprite scale.
    desired = (part.width-2) * aspect + 2
    lower = max(part.length*.96, LONG_HULLS.get(name, 0))
    return round(fit(part.length*.43 + desired*.57, lower, max(lower, part.length*1.14)))


def gear_layout(part, name, reference):
    count = RUNNING_GEAR[name]
    w, h = part.width, part.length
    ox, oy = w//2, h//2
    x_out = min(ox, w-ox-1)
    y_range = h//2-2
    level = fit(round(len(part.layers)*.53), 3, 6)
    core = max(2, x_out-(2 if count > 0 else 1))
    skirted = name in SKIRTED
    tracks = count > 0
    wheel_count = abs(count)
    spans = SPACING.get(name)
    if spans is None:
        reach = .70 if wheel_count > 6 else .63
        spans = tuple(-reach + 2*reach*i/(wheel_count-1) for i in range(wheel_count))
    centers = [round(oy + y_range*t) for t in spans]
    spacing = min(b-a for a,b in zip(centers, centers[1:]))
    radius = fit(spacing * .46 * WHEEL_SCALE.get(name, 1), 1.2, 3.0 if tracks else 4.1)
    rim = reference.sample("side", 0, 0, .2, False)
    rim = matte((93,91,72), rim, .89, 0, 0)
    for z in range(min(len(part.layers), level+1)):
        if skirted and tracks and z >= level-1:
            continue
        px = part.layers[z].load()
        for x in range(w):
            dx = abs(x-ox)
            if dx < core or dx > x_out+1:
                continue
            for y in range(h):
                dy = abs(y-oy)
                old = px[x,y]
                if dy <= y_range+1 and old[3]:
                    px[x,y] = (0,0,0,0)
                if dx > x_out or dy > y_range:
                    continue
                if tracks:
                    top = level-1
                    end = y_range - (2 if z == 0 else 0 if z >= top-1 else 1)
                    if dy > end:
                        continue
                    if z in (0,top) or dy >= end-1:
                        px[x,y] = TRACK_EDGE if (y+z)%3 == 0 else TRACK
                    else:
                        wheel = min(abs(y-c) for c in centers)
                        cycle = (wheel/radius)**2 + ((z-top*.43)/max(1.1,top*.44))**2
                        if cycle <= 1.18:
                            px[x,y] = rim if dx == x_out and cycle <= .59 else RUBBER
                        elif dx == core and dy < y_range-2:
                            px[x,y] = (47,48,43,255)
                else:
                    wheel = min(abs(y-c) for c in centers)
                    cycle = (wheel/radius)**2 + ((z-level*.5)/max(1.45,level*.5))**2
                    if cycle <= 1.13:
                        px[x,y] = rim if dx == x_out and cycle < .31 else RUBBER if (y+z)%4 else TRACK


def contour_hull(part, reference):
    """Transfer the slope of the photographed bow and deck into the outer edge."""
    w,h,d = part.width,part.length,len(part.layers)
    top = d-1
    for z in range(max(4,d//2),d):
        layer = part.layers[z]
        px = layer.load()
        for x in range(max(1,w//2-6),min(w-1,w//2+7)):
            active = [y for y in range(h) if px[x,y][3]]
            if len(active) < h*.30:
                continue
            for end, direction in ((active[0],-1),(active[-1],1)):
                y = end+direction
                if not 0 <= y < h or z == top:
                    continue
                lx = (x-w//2)/(w/2)
                ly = (y-h//2)/(h/2)
                lz = z/max(1,top)
                side = reference.sample("side",lx,ly,lz,False)
                above = reference.sample("top",lx,ly,lz,False)
                if side and above and luma(above) < 161:
                    px[x,y] = matte(px[x,end], side, .91, -2, 0)


def contour_turret(part, reference):
    """Trace the shoulders from the head-on, rear and side views.

    Work on wide surfaces of the cast or welded body. Narrow shafts, optics
    and launcher frames keep their independently rotating seed geometry.
    """
    w, h, d = part.width, part.length, len(part.layers)
    for z in range(1, d-1):
        px = part.layers[z].load()
        lz = z/(d-1)
        for y in range(max(1,h//2-w//2), min(h-1,h//2+w//2+1)):
            occupied = [x for x in range(w) if px[x,y][3]]
            if len(occupied) < max(4, w*.30):
                continue
            ly = (y-h//2) / (h/2) * 1.34
            for end, step in ((occupied[0], -1), (occupied[-1], 1)):
                lx = (end-w//2)/(w/2)
                front = reference.sample("front", lx, ly, lz, True)
                rear = reference.sample("rear", lx, ly, lz, True)
                side = reference.sample("side", lx, ly, lz, True)
                if front is None and rear is None and side is not None and z >= d*.43:
                    px[end,y] = (0,0,0,0)
                    continue
                x = end+step
                if not 0 <= x < w or not (front and rear and side):
                    continue
                beyond = (x-w//2)/(w/2)
                if (reference.sample("front", beyond, ly, lz, True)
                        and reference.sample("rear", beyond, ly, lz, True)
                        and reference.sample("top", beyond, ly, lz, True)):
                    px[x,y] = matte(px[end,y], side, .94, 0, 0)


def raise_roof_details(part, name):
    original_count = len(part.layers)
    for dx, dy, width, length, rise in ROOF_BLOCKS.get(name, ()):
        x = part.width//2+dx
        y = part.length//2+dy
        below = [z for z, layer in enumerate(part.layers[:original_count]) if layer.getpixel((x,y))[3]]
        if not below:
            continue
        roof = below[-1]
        base = part.layers[roof].getpixel((x,y))
        last = min(roof+rise, original_count)
        if last == len(part.layers):
            part.layers.append(Image.new("RGBA", (part.width,part.length)))
        for z in range(roof+1,last+1):
            layer = part.layers[z]
            for px in range(x,x+width):
                for py in range(y,y+length):
                    if layer.getpixel((px,py))[3]:
                        continue
                    color = GLASS if py == y+length-1 and px == x else matte(base, None, .89 if z < last else 1.04, 0, 0)
                    layer.putpixel((px,py), color)


def select_sheet(id):
    existing = {
        "ru_t14": REFERENCES.parent / "t14-armata-four-views.png",
        # Photoreal reconstruction of the rollout vehicle in ARMOR, July-August
        # 1994, back cover: https://media-cdn.dvidshub.net/pubs/pdf_33806.pdf
        "us_m8_ags": REFERENCES.parent / "m8-ags-four-views.png",
    }
    return existing.get(id, REFERENCES / f"{id}.jpg")


def compact_slices(layers, count):
    if len(layers) <= count:
        return layers
    if count == 0:
        return []
    compacted = [Image.new("RGBA", layers[0].size) for _ in range(count)]
    for i, layer in enumerate(layers):
        compacted[i * count // len(layers)].alpha_composite(layer)
    return compacted


def lower_profile(hull, turret, original):
    deck, rear_fittings, turret_height = LOW_PROFILES[original["id"]]
    original_deck = original["hullBaseHeight"]
    running_gear = 5
    hull.layers = (
        hull.layers[:running_gear]
        + compact_slices(hull.layers[running_gear:original_deck], deck - running_gear)
        + compact_slices(hull.layers[original_deck:], rear_fittings)
    )
    if len(turret.layers) > turret_height:
        # The muzzle layers and roof silhouette retain their pixels.
        gun_layers = (original.get("muzzleHeight") or 2) + 1
        turret.layers = (
            turret.layers[:gun_layers]
            + compact_slices(turret.layers[gun_layers:-1], turret_height - gun_layers - 1)
            + turret.layers[-1:]
        )
    return deck


def narrow_soviet_barrel(turret, vehicle, profile):
    start, extractor_start, extractor_end = profile
    x, origin = turret.width // 2, turret.length // 2
    muzzle = vehicle["muzzleDist"]
    upper = vehicle.get("muzzleHeight") or 2
    lower = turret.layers[upper - 1]
    tube = turret.layers[upper]
    raised = turret.layers[upper + 1]
    for offset in range(start, muzzle + 1):
        y = origin + offset
        for layer in (lower, tube):
            layer.putpixel((x - 1, y), (0, 0, 0, 0))
            layer.putpixel((x + 1, y), (0, 0, 0, 0))
        if muzzle + extractor_start <= offset <= muzzle + extractor_end:
            raised.putpixel((x, y), matte(tube.getpixel((x, y)), None, 1.03, 0, 0))
        else:
            raised.putpixel((x, y), (0, 0, 0, 0))


def build_fleet(catalog, png_dir, *, skip=()):
    rebuilt = set()
    if not SEEDS.is_file():
        return rebuilt
    with ZipFile(SEEDS) as archive:
        originals = {v["id"]:v for v in json.loads(archive.read("vehicles_catalog.json"))["vehicles"]}
        for vehicle in catalog["vehicles"]:
            name = vehicle["id"]
            if name in KEEP or name in skip:
                continue
            if name not in RUNNING_GEAR:
                continue
            photo = select_sheet(name)
            if not photo.is_file():
                continue
            old = originals[name]
            if name in PHOTO_HULLS:
                build_vehicle(vehicle, old, png_dir, photo, View, RUNNING_GEAR[name], name in SKIRTED)
                rebuilt.add(name)
                continue
            hull = Part.read(archive, old, "hull")
            turret = Part.read(archive, old, "turret")
            hull_fraction = fit(old["hullBaseHeight"]/(old["hullBaseHeight"]+max(4,len(turret.layers)-1)),.54,.76)
            turret_offset = 2 * old["hullPivotOffsetY"] / old["hullLength"]
            try:
                reference = Registration(photo, hull_fraction, turret_offset)
            except ValueError as error:
                raise ValueError(f"{name}: {error}") from error
            hull.stretch(registered_length(hull, reference, name))
            contour_hull(hull, reference)
            repaint(hull, reference)
            gear_layout(hull,name,reference)
            if name in LEOPARD1_TURRETS:
                turret.layers = paint_turret(turret, photo, name, old["muzzleDist"])
            elif name not in PHOTO_TURRETS:
                contour_turret(turret, reference)
                repaint(turret, reference, turret=True)
                raise_roof_details(turret, name)
            turret.trim_empty_top()
            if name in LOW_PROFILES:
                vehicle["hullBaseHeight"] = lower_profile(hull, turret, old)
            if name in SOVIET_BARREL_PROFILES:
                narrow_soviet_barrel(turret, vehicle, SOVIET_BARREL_PROFILES[name])
            hull.write(png_dir / f"{name}_hull_slices.png")
            vehicle["hullLength"] = hull.length
            vehicle["hullPivotOffsetY"] = round(old["hullPivotOffsetY"] * hull.length / old["hullLength"], 3)
            vehicle["hullSlices"] = len(hull.layers)
            if name in PHOTO_TURRETS:
                build_vehicle(vehicle, old, png_dir, photo, View, RUNNING_GEAR[name], name in SKIRTED)
            else:
                turret.write(png_dir / f"{name}_turret_slices.png")
                vehicle["turretSlices"] = len(turret.layers)
            rebuilt.add(name)
    return rebuilt
