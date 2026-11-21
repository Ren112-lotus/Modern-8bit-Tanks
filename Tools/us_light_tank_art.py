"""M24 and M41 exteriors in US, Japanese and West German service. Grid +y is the bow."""

from expansion_four_view import brightness, finish_part
from expansion_vehicle_fittings import Exterior, draw_wheel, shade


RUBBER = (43, 45, 41)
TRACK = (62, 62, 53)
LINK = (86, 84, 70)
CANVAS = (94, 89, 69)


def paint_armor(plate, stack, turret=False):
    finish_part(plate, stack, turret)
    # Sample the overhead photograph once per exposed column. Skip the turret's
    # footprint on the hull so a traversed turret reveals plain deck underneath.
    roof = Exterior(stack)
    midtone = brightness(plate.midtone)
    for y in range(-stack.cy, stack.length - stack.cy):
        for x in range(-stack.cx, stack.width - stack.cx):
            z = roof.roof_at(x, y)
            if z < 3:
                continue
            if not turret and abs(y - plate.trace.pivot) < 8 and abs(x) < 7:
                continue
            view, u, v = plate.frames(x, y, z, turret)[0]
            sample = view.color(u, v)
            if sample is not None:
                rgb = stack.layers[z].getpixel((stack.cx + x, stack.cy + y))[:3]
                gain = max(.85, min(1.12, 1 + (brightness(sample) - midtone) / 220))
                stack.pixel(z, x, y, shade(rgb, gain))


def track_run(hull, centers, ends, drive, radius):
    outer = hull.cx - 1
    rear, front = ends
    for sign in (-1, 1):
        left, right = sorted((sign * outer, sign * (outer - 2)))
        # Bent end runs leave daylight below the idler and sprocket.
        for z, inset in enumerate((4, 3, 2, 1, 0, 1)):
            a, b = rear + inset, front - inset
            if z in (0, 5):
                hull.box(z, z, left, right, a, b, TRACK)
                for y in range(a, b + 1, 2):
                    hull.box(z, z, left, right, y, y, LINK)
            else:
                for y in (a, b):
                    hull.box(z, z, left, right, y, y, LINK if z % 2 else TRACK)
        for x in range(left, right + 1):
            for y in centers:
                draw_wheel(hull, x, y, 2.6, radius, 2.65, hull.base, hull.light, RUBBER)
                hull.pixel(2, x, y, hull.dark)
            for y in (rear + 2, front - 2):
                rim = LINK if y == drive else hull.dark
                draw_wheel(hull, x, y, 4, 1.95, 2.0, rim, hull.base, TRACK)
        for y in (centers[1] - 1, 0, centers[-2] + 1):
            draw_wheel(hull, sign * (outer - 1), y, 4, 1.35, 1.2, hull.dark, LINK)
        # Thin fender lip above the rubber, with a folded guard on each end.
        hull.box(6, 6, left, right, rear + 1, front - 2, shade(hull.base, .94))
        hull.box(6, 6, sign * outer, sign * outer, rear + 2, front - 3, shade(hull.base, 1.09))
        for y in (rear, front - 1):
            hull.box(5, 5, left, right, y, y + 1, hull.dark)


def barrel(turret, trace, start, bulldog=False):
    z, end = trace.muzzle_height, trace.muzzle
    # A continuous shaded centreline ties the rotating stack to its visual muzzle.
    turret.box(z - 1, z - 1, 0, 0, start, start + 3, turret.dark)
    turret.box(z, z, 0, 0, start, end, shade(turret.base, 1.16))
    if bulldog:
        turret.box(z - 1, z, -1, 1, end - 5, end - 3, turret.dark)
        turret.box(z, z, 0, 0, end - 5, end - 3, turret.light)
        turret.box(z, z, -1, 1, end - 1, end, shade(turret.base, .84))
        turret.pixel(z + 1, 0, end - 1, turret.base)
    turret.pixel(z, 0, end, TRACK)


def lamps(hull, reach, y, z):
    for sign in (-1, 1):
        x = sign * reach
        hull.box(z, z, x - 1, x + 1, y, y + 1, hull.dark)
        hull.pixel(z + 1, x, y, shade(hull.base, 1.15))
        hull.pixel(z, x, y + 1, (151, 151, 127))


def chaffee(plate, hull, turret, model):
    # A low wedge with two separate front hatches and a flat twin-engine deck.
    hull.loft(1, ((-12, 12, 6, 2, 2), (-14, 14, 7, 2, 2), (-15, 15, 7, 1, 2),
                  (-16, 16, 8, 2, 1), (-16, 14, 8, 1, 1),
                  (-15, 12, 8, 1, 2), (-14, 10, 7, 1, 2)))
    turret.loft(0, ((-5, 5, 5, 2, 2), (-7, 6, 6, 2, 2), (-8, 6, 7, 3, 3),
                    (-7, 6, 6, 2, 2), (-6, 5, 5, 1, 2)))
    # Broad rounded cast shield on the front of the faceted turret.
    for z, (width, nose) in enumerate(((3, 7), (4, 9), (4, 9), (3, 8)), 1):
        turret.shell(z, 4, nose, width, 0, 1, .81 + .05 * z)
    paint_armor(plate, hull)
    paint_armor(plate, turret, turret=True)
    track_run(hull, (-10, -5, 0, 5, 10), (-16, 16), 14, 2.45)

    deck = Exterior(hull)
    deck.grille(0, -13, 5, 2, "length")
    for x in (-5, 5):
        deck.hatch(x, 8, 2, 2)
        deck.sight(x, 10)
    deck.locker(8, -6, 1, 3)
    deck.rail(-8, -10, -8, -4)
    lamps(hull, 7, 13, 6)

    roof = Exterior(turret)
    roof.cupola(3, -2, 2, 1)
    roof.hatch(-3, 0, 2, 2)
    roof.sight(-2, 3)
    # Cast pistol-port boss and the lifting eyes on the broad shield.
    turret.box(2, 3, 7, 7, -3, -2, turret.dark)
    turret.pixel(3, 7, -3, turret.base)
    for x in (-3, 3):
        turret.pixel(4, x, 7, turret.light)
    if model == "jp_m24":
        # Higashi-Chitose exhibit: sloping sheet-metal bin and an empty A-frame pintle.
        for z, rear, width in ((1, -9, 4), (2, -10, 5), (3, -11, 5)):
            turret.shell(z, rear, -6, width, 1, 0, .80 + .06 * z)
        turret.shell(4, -10, -7, 5, 1, 0, 1.06)
        turret.pixel(3, 0, -11, LINK)
        for x in (-2, 0):
            turret.box(4, 5, x, x, -5, -5, turret.dark)
        turret.pixel(5, -1, -5, turret.dark)
        turret.pixel(6, -1, -5, LINK)
        for x in range(-6, 7):
            hull.pixel(5, x, -16, LINK if x % 2 else hull.dark)
        for x in (-7, 7):
            hull.pixel(6, x, -15, TRACK)
    else:
        for y, span in ((11, 0), (12, 2), (13, 1), (14, 1)):
            for x in range(-span, span + 1):
                if y != 14 or x:
                    deck.brush(x, y, (154, 151, 126))
        turret.box(1, 3, -3, 3, -9, -8, CANVAS)
        for x in (-2, 2):
            turret.box(2, 3, x, x, -9, -8, turret.dark)
        turret.box(4, 6, -2, -2, -5, -5, turret.dark)
        turret.box(7, 7, -3, -2, -6, -4, TRACK)
        turret.box(7, 7, -2, -2, -3, 2, (71, 73, 65))
    barrel(turret, plate.trace, 7)


def bulldog(plate, hull, turret, model):
    hull.loft(1, ((-14, 13, 6, 2, 3), (-16, 15, 7, 2, 3), (-17, 17, 8, 1, 3),
                  (-18, 18, 9, 1, 2), (-18, 16, 9, 1, 2),
                  (-18, 14, 9, 1, 2), (-17, 12, 8, 1, 2), (-17, 9, 8, 1, 2)))
    # Low turret ring, widest along the cast lower lip. The long welded cheeks
    # fold inward from that lip to a much smaller, offset crown.
    sections = (
        (-7, 6, 6, 2, 2), (-9, 7, 7, 2, 3), (-10, 8, 8, 2, 4),
        (-10, 7, 7, 2, 3), (-9, 5, 7, 2, 3), (-7, 3, 5, 2, 2),
    )
    turret.loft(0, sections)
    for z, reach in enumerate((2, 3, 3, 2), 1):
        turret.shell(z, 5, 11 - abs(2 - z), reach, 0, 1, .78 + z * .045)
    paint_armor(plate, hull)
    paint_armor(plate, turret, turret=True)
    track_run(hull, (-12, -6, 0, 6, 12), (-18, 18), -16, 2.8)

    deck = Exterior(hull)
    for sign in (-1, 1):
        deck.grille(sign * 4, -13, 3, 3)
        # Muffler shields over the rear fenders have an exposed dark exhaust opening.
        x = sign * 9
        hull.box(7, 7, x - 1, x + 1, -16, -8, hull.dark)
        hull.box(8, 8, x - 1, x + 1, -15, -9, shade(hull.base, .96))
        hull.pixel(7, x, -17, RUBBER)
        hull.pixel(7, x, -16, LINK)
        for y in (-14, -10):
            hull.box(8, 8, x - 1, x + 1, y, y, shade(hull.base, .79))
        # Two shallow stowage bins occupy the fender between the exhaust and lamps.
        for rear, front in ((-6, 0), (2, 8)):
            hull.box(7, 7, x - 1, x + 1, rear, front, shade(hull.base, .86))
            hull.box(7, 7, x, x, rear + 1, front - 1, hull.base)
            hull.pixel(7, x + sign, (rear + front) // 2, LINK)
    deck.hatch(5, 9, 2, 2)
    deck.sight(5, 11)
    lamps(hull, 8, 14, 6)
    roof = Exterior(turret)
    roof.cupola(-3, -2, 2, 1)
    roof.hatch(3, -1, 2, 2)
    roof.sight(-3, 3)
    for sign in (-1, 1):
        # Handrail welded to the slab side, underneath the break in the cheek.
        x = sign * 8
        turret.box(3, 3, x, x, -6, -1, shade(turret.base, 1.06))
        for y in (-6, -1):
            turret.pixel(2, x, y, turret.dark)
    # Open storage frame follows the rear wall, leaving the deck visible through it.
    for x in (-5, 5):
        turret.box(2, 2, x, x, -13, -9, turret.dark)
        turret.box(4, 4, x, x, -13, -9, shade(turret.base, .94))
    for z in (2, 4):
        turret.box(z, z, -5, 5, -13, -13, turret.dark if z == 2 else turret.base)
    for x in (-5, -2, 2, 5):
        turret.pixel(3, x, -13, turret.dark)
    if model == "de_m41g":
        bundeswehr_stowage(turret)
    barrel(turret, plate.trace, 8, bulldog=True)


def bundeswehr_stowage(turret):
    # Schulte's M41 carries a bundled net in the rear rack.
    net = ((66, 69, 52), (77, 77, 56), (87, 86, 62), (92, 89, 65))
    for z, width, rear, front in ((3, 5, -13, -9), (4, 6, -13, -8), (5, 5, -12, -9)):
        for y in range(rear, front + 1):
            span = width - (y in (rear, front))
            for x in range(-span, span + 1):
                turret.pixel(z, x, y, net[(x * x + 3 * y + z * x) % len(net)])
    # Outline and dark centre follow the existing inclined surface on each cheek.
    for side in (-1, 1):
        for z, pattern in enumerate((" w#w ", "ww#ww", "#####", "ww#ww", " w#w "), 1):
            for y, ink in enumerate(pattern, -5):
                if ink == " ":
                    continue
                row = turret.cy + y
                edge = next(x for x in range(turret.cx, -1, -1)
                            if turret.layers[z].getpixel((turret.cx + side * x, row))[3])
                turret.pixel(z, side * edge, y, (180, 180, 162) if ink == "w" else (45, 46, 41))


SCULPTS = {"us_m24": chaffee, "jp_m24": chaffee, "us_m41": bulldog, "de_m41g": bulldog}
