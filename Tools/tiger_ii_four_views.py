"""Build the Tiger II production turret and combat-track hull for sprite stacking.

The four-view artwork is registered against the restored production Tiger II
shown at https://tankmuseum.org/article/king_tiger_arrives. The end views
establish the transverse taper; the longitudinal slope follows the side view
and the straight front plate seen on the museum's Tiger II (Production).
"""

from pathlib import Path

from PIL import Image, ImageFilter


PHOTO = Path(__file__).with_name("VehicleArt") / "tiger-ii-four-views.png"
GEOMETRY = {
    "hullWidth": 26,
    "hullLength": 51,
    "hullBaseHeight": 12,
    "hullPivotOffsetY": 1.0,
    "turretWidth": 25,
    "turretLength": 85,
}
MUZZLE_Y = 41
MUZZLE_Z = 3
TURRET_SLICES = 9
TAN = (148, 128, 91)
OLIVE = (111, 114, 87)
BROWN = (119, 94, 76)
TRACK = (57, 51, 41)
TRACK_FACE = (78, 70, 55)
WHEEL_DARK = (72, 66, 52)
WHEEL_FACE = (109, 98, 76)
WHEEL_RIM = (129, 116, 92)
STEEL = (85, 77, 63)
SHADOW = (44, 39, 31)

# Per-slice traces: rear, front, half-width, rear and front corner bevel.
# The bow peaks midway up, where the upper and lower glacis meet.
HULL = (
    (-17, 17, 8, 2, 2), (-19, 18, 8, 2, 2), (-21, 20, 8, 2, 2),
    (-22, 22, 8, 1, 2), (-23, 23, 8, 1, 1), (-23, 24, 8, 1, 1),
    (-23, 22, 11, 1, 3), (-22, 20, 11, 1, 3), (-22, 18, 11, 1, 2),
    (-21, 16, 10, 1, 2), (-20, 14, 10, 1, 1), (-19, 12, 9, 1, 1),
)
TURRET = (
    (-9, 7, 6, 2, 2), (-12, 9, 8, 2, 3), (-12, 9, 8, 2, 3),
    (-11, 9, 7, 2, 3), (-11, 8, 7, 2, 3), (-10, 8, 6, 2, 2),
    (-9, 6, 5, 2, 2),
)


def tone(color, gain):
    return tuple(min(255, round(channel * gain)) for channel in color)


def camouflage(rgb):
    r, g, b = rgb
    brightness = (r + 2 * g + b) / 4
    if r - b < 9:
        return TAN
    if r - g > 14 and g - b < 17:
        base = BROWN
    elif r - g < 14 and g - b >= 8:
        base = OLIVE
    else:
        base = TAN
    gain = max(.87, min(1.10, 1 + (brightness - 121) / 285))
    return tone(base, gain)


class Exterior:
    def __init__(self, photo):
        self.photo = photo

    def paint(self, part, face, x, y, z):
        if part == "hull":
            # The end views are framed closer than the plan and side views.
            if face == "top":
                point = (301 + 10.6 * y, 261 - 12.0 * x)
            elif face == "side":
                point = (1090 + 11.6 * y, 404 - 14.3 * z)
            elif face == "front":
                point = (384 + 21.5 * x, 983 - 24.4 * z)
            else:
                point = (1152 - 21.5 * x, 983 - 24.2 * z)
        else:
            if face == "top":
                point = (304 + 11.5 * y, 261 - 14.0 * x)
            elif face == "side":
                point = (1066 + 12.6 * y, 241 - 13.6 * z)
            elif face == "front":
                point = (384 + 20.8 * x, 700 - 18.3 * z)
            else:
                point = (1152 - 20.8 * x, 700 - 18.3 * z)
        return camouflage(self.photo.getpixel(tuple(round(p) for p in point)))


def armor(stack, sections, reference, part):
    occupied = set()
    for z, (rear, front, span, tail_cut, nose_cut) in enumerate(sections):
        for y in range(rear, front + 1):
            inset = max(0, rear + tail_cut - y, y - front + nose_cut)
            occupied.update((x, y, z) for x in range(-span + inset, span - inset + 1))
    for x, y, z in occupied:
        outside = x + (1 if x >= 0 else -1)
        if (x, y + 1, z) not in occupied:
            face, gain = "front", .94 if (x, y, z + 1) not in occupied else .81
        elif (x, y - 1, z) not in occupied:
            face, gain = "rear", .83
        elif (outside, y, z) not in occupied:
            face, gain = "side", .89
        else:
            face, gain = "top", 1.01
        stack.pixel(z, x, y, tone(reference.paint(part, face, x, y, z), gain))


def drive_gear(hull):
    # Nine staggered steel wheel stations sit inside each wide combat track.
    for side in (-1, 1):
        for z in range(7):
            reach = (19, 21, 23, 24, 24, 23, 21)[z]
            inner = (17, 19, 21, 22, 22, 21, 19)[z]
            for y in range(-reach, reach + 1):
                if z in (0, 6) or abs(y) > inner:
                    for x in range(8, 13):
                        lug = (y + z) % 3 == 0 and (x in (8, 12) or z == 0)
                        hull.pixel(z, side * x, y, TRACK_FACE if lug else TRACK)
        for i, y in enumerate((-17, -13, -9, -4, 0, 4, 9, 13, 17)):
            outer = i % 2 == 0
            for z in range(1, 6):
                for offset in range(-2, 3):
                    radius = offset * offset + (z - 3) ** 2
                    if radius > 6:
                        continue
                    face = WHEEL_RIM if radius >= 4 and z >= 3 else WHEEL_FACE
                    if not outer or z == 1:
                        face = tone(face, .81)
                    hull.pixel(z, side * (12 if outer else 11), y + offset,
                               WHEEL_DARK if radius == 0 else face)
        for y in (-21, 21):
            for dy in range(-2, 3):
                for z in range(2, 7):
                    radius = dy**2 + (z - 4)**2
                    if radius <= 6:
                        rim = WHEEL_RIM if y == 21 and radius >= 4 else WHEEL_FACE
                        hull.pixel(z, side * 12, y + dy, rim if radius != 1 else WHEEL_DARK)


def hull_details(hull, exterior):
    for side in (-1, 1):
        for y in range(-22, 25):
            height = 7 if y <= 17 else 7 - (y - 17) // 3
            for x in (11, 12):
                color = exterior.paint("hull", "side", side * x, y, height)
                seam = y in (-16, -9, -2, 5, 12, 18)
                hull.pixel(height, side * x, y, tone(color, .77 if seam else .97))
                if 8 <= abs(y) or x == 12:
                    hull.pixel(height - 1, side * x, y, tone(color, .68 if seam else .82))
        for x in (4, 7):
            for y in (-17, -12):
                hull.box(11, 11, side * x, side * x, y - 1, y + 2, SHADOW)
                for rib in (y, y + 2):
                    hull.pixel(11, side * x, rib, STEEL)
        x = 4 * side
        hull.box(4, 6, x - 1, x + 1, -24, -22, STEEL)
        left, right = sorted((x, x + side))
        hull.box(7, 9, left, right, -24, -23, STEEL)
        hull.pixel(9, x, -24, SHADOW)
        hull.pixel(6, 8 * side, 23, WHEEL_RIM)
        hull.pixel(5, 8 * side, 23, STEEL)
    hull.ellipse(9, -4, 16, 2, 1, tone(TAN, .83))
    hull.pixel(9, -4, 17, SHADOW)
    hull.pixel(9, 0, 18, WHEEL_RIM)


def turret_details(turret, exterior):
    roof = len(TURRET) - 1
    for x, y in ((3, -3), (-3, -1)):
        shadow = tone(exterior.paint("turret", "top", x, y, roof), .71)
        turret.ellipse(roof, x, y, 2, 2, shadow)
        turret.ellipse(roof + 1, x, y, 2, 2, tone(TAN, .92))
    turret.ellipse(roof + 2, 3, -3, 2, 2, tone(TAN, 1.02))
    turret.pixel(roof + 1, 3, -1, SHADOW)
    turret.pixel(roof + 1, 5, -3, SHADOW)
    turret.ellipse(roof, 0, -7, 1, 1, STEEL)
    turret.box(2, 4, -2, 2, -12, -11, tone(TAN, .72))
    turret.pixel(4, 1, -12, STEEL)
    for side in (-1, 1):
        for y in (-8, -6, -4):
            turret.pixel(2, side * 8, y, SHADOW)
            turret.pixel(3, side * 8, y, TRACK_FACE)
            turret.pixel(4, side * 7, y, TRACK)


def gun(turret, exterior):
    # A shaped mantlet joins the flat turret face; the brake has two dark ports.
    for y, radius in ((8, 2.8), (9, 2.8), (10, 2.6), (11, 2.4), (12, 1.9), (13, 1.6)):
        for x in range(-3, 4):
            for z in range(1, 7):
                if x*x + (z - MUZZLE_Z)**2 <= radius**2:
                    base = exterior.paint("turret", "front", x, 9, z)
                    turret.pixel(z, x, y, tone(base, .80 + .045 * (z - 1)))
    for y in range(14, MUZZLE_Y - 4):
        color = camouflage(exterior.photo.getpixel((round(1220 + (y-14) * 11.3), 210)))
        turret.pixel(MUZZLE_Z, 0, y, color)
        turret.pixel(MUZZLE_Z - 1, 0, y, tone(color, .67))
        if y < 21:
            turret.pixel(MUZZLE_Z, -1, y, tone(color, .77))
            turret.pixel(MUZZLE_Z, 1, y, tone(color, .82))
    for y in range(MUZZLE_Y - 4, MUZZLE_Y + 1):
        turret.box(MUZZLE_Z - 1, MUZZLE_Z + 1, -1, 1, y, y, STEEL)
        if y in (MUZZLE_Y - 3, MUZZLE_Y - 1):
            turret.pixel(MUZZLE_Z, -1, y, SHADOW)
            turret.pixel(MUZZLE_Z, 1, y, SHADOW)
    turret.pixel(MUZZLE_Z, 0, MUZZLE_Y, SHADOW)


def sculpt_tiger_ii(hull, turret, _vehicle):
    with Image.open(PHOTO) as source:
        if source.size != (1536, 1024):
            raise ValueError("The Tiger II photo sheet must be re-registered after resizing.")
        reference = Exterior(source.convert("RGB").filter(ImageFilter.BoxBlur(3)))
    armor(hull, HULL, reference, "hull")
    drive_gear(hull)
    hull_details(hull, reference)
    armor(turret, TURRET, reference, "turret")
    turret_details(turret, reference)
    gun(turret, reference)
    return MUZZLE_Y
