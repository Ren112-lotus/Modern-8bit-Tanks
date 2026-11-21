"""Section profiles fitted to the expansion's four-view plates.

Offsets are game pixels, measured inward from the lower edge of each part.
Individual front and rear cuts preserve casting and plate shapes in rotation.
"""
from math import sqrt


# Upper bow setback, rear setback, roof side inset, bow bevel, rear bevel.
HULL_SECTIONS = {
    "us_m1917": (4, 1, 0, 1, 1), "us_m2a4_light": (3, 2, 1, 1, 1),
    "us_m3_stuart": (4, 2, 1, 2, 2), "us_m3_lee": (2, 3, 0, 2, 2),
    "us_m4a1": (6, 2, 2, 7, 3), "us_m4a3e8": (9, 2, 0, 1, 1),
    "us_m24": (7, 3, 1, 4, 3), "us_m36": (6, 2, 2, 3, 2),
    "us_m26": (6, 2, 0, 4, 2), "us_m46": (6, 2, 0, 4, 3),
    "us_m41": (8, 2, 1, 3, 2), "us_m56": (5, 2, 0, 1, 1),
    "us_m50": (6, 2, 1, 2, 2), "us_m103": (9, 3, 1, 7, 2),
    "de_a7v": (3, 4, 2, 3, 3),
    "de_wiesel_mk": (6, 1, 1, 3, 1), "de_wiesel_tow": (6, 1, 1, 3, 1),
    "ru_ms1": (4, 1, 0, 2, 1), "ru_t26": (4, 1, 0, 1, 1),
    "ru_bt7": (10, 2, 2, 5, 1), "ru_t28": (3, 1, 0, 2, 1),
    "ru_t34_1941": (9, 3, 2, 3, 2), "ru_kv1": (4, 2, 0, 2, 1),
    "ru_t3485": (9, 3, 2, 3, 2), "ru_is2": (9, 2, 0, 4, 2),
    "ru_su100": (8, 3, 2, 3, 2), "ru_t44": (8, 3, 1, 3, 2),
    "ru_asu57": (5, 2, 0, 1, 1), "ru_asu85": (9, 3, 1, 4, 2),
    "ru_it1": (9, 2, 1, 3, 2), "ru_obj906": (10, 4, 1, 5, 3),
    "jp_hago": (5, 2, 1, 3, 2), "jp_chiha": (4, 2, 1, 3, 3),
    "jp_shinhoto": (4, 2, 1, 3, 3), "jp_chihe": (3, 2, 0, 1, 2),
    "jp_chinu": (4, 2, 0, 1, 2), "jp_chito": (6, 2, 1, 2, 2),
    "jp_chiri": (4, 2, 0, 1, 2), "jp_kami": (10, 6, 1, 4, 3),
    "jp_honi3": (4, 2, 1, 2, 2), "jp_m24": (7, 3, 1, 4, 3),
    "jp_type60": (5, 1, 0, 1, 1), "jp_type61": (7, 2, 1, 4, 2),
    "jp_type74": (8, 2, 1, 3, 2), "jp_type90": (7, 2, 0, 2, 1),
    "jp_type16": (9, 3, 1, 5, 2), "jp_type89ifv": (10, 0, 1, 3, 1),
}

# Turret roof setback at front and rear, side inset, face and rear corner
# cuts, and lateral drift from the ring. Cast sections use a round shoulder.
TURRET_SECTIONS = {
    "us_m1917": (1, 1, 0, 2, 2, 0), "us_m2a4_light": (1, 1, 1, 2, 2, 0),
    "us_m3_stuart": (2, 1, 1, 2, 2, 0), "us_m3_lee": (1, 1, 1, 2, 2, -1),
    "us_m4a1": (3, 2, 2, 4, 3, 0), "us_m4a3e8": (4, 1, 1, 3, 2, 0),
    "us_m24": (4, 2, 1, 4, 2, 0), "us_m36": (3, 0, 2, 3, 1, 0),
    "us_m26": (4, 1, 2, 3, 2, 0), "us_m46": (4, 1, 2, 4, 2, 0),
    "us_m41": (5, 1, 1, 4, 2, 0), "us_m103": (6, 2, 4, 6, 5, 0),
    "de_wiesel_mk": (2, 1, 1, 2, 1, 0),
    "ru_ms1": (1, 1, 2, 2, 2, 0), "ru_t26": (1, 1, 0, 3, 3, 0),
    "ru_bt7": (2, 1, 2, 2, 2, 0), "ru_t28": (3, 1, 2, 4, 3, 0),
    "ru_t34_1941": (2, 1, 1, 3, 3, 0), "ru_kv1": (2, 1, 1, 2, 1, 0),
    "ru_t3485": (2, 2, 2, 3, 3, 0), "ru_is2": (3, 1, 3, 4, 3, 0),
    "ru_t44": (4, 2, 2, 4, 3, 0), "ru_it1": (3, 2, 2, 4, 3, 0),
    "ru_obj906": (4, 2, 2, 4, 3, 0),
    "jp_hago": (1, 1, 1, 2, 2, -2), "jp_chiha": (1, 1, 1, 2, 2, 0),
    "jp_shinhoto": (3, 1, 1, 3, 1, 1), "jp_chihe": (2, 1, 1, 3, 2, 1),
    "jp_chinu": (3, 0, 1, 4, 1, 0), "jp_chito": (3, 1, 2, 3, 2, 0),
    "jp_chiri": (3, 0, 0, 4, 1, 0), "jp_kami": (1, 1, 1, 2, 2, 0),
    "jp_m24": (4, 2, 1, 4, 2, 0), "jp_type61": (4, 1, 2, 4, 2, 0),
    "jp_type74": (4, 3, 3, 4, 4, 0), "jp_type90": (2, 1, 1, 2, 1, 0),
    "jp_type16": (5, 1, 1, 5, 1, 0), "jp_type89ifv": (4, 1, 1, 3, 1, 0),
}


def hull_section(trace, model, z):
    bow_loss, rear_loss, side_loss, bow_clip, rear_clip = HULL_SECTIONS[model]
    span, half = trace.length // 2, trace.width // 2
    tier = max(0, (z - 2) / (trace.deck - 3))
    # Below the fender line the belly fits between the track runs.
    width = half - (3 if z < 4 else 1 + round(tier * side_loss))
    rear_gap = 6 if trace.hull == "tail" or "float" in trace.markings else 3
    bow_gap = 4 if trace.hull == "landship" else 2
    forward_taper = tier ** (1.45 if bow_loss < 5 else .87)
    front = span - bow_gap - round(bow_loss * forward_taper)
    rear = -span + rear_gap + round(rear_loss * tier)
    return rear, front, width, rear_clip, bow_clip


def shoulder_width(width, depth, clip, rounded):
    if rounded:
        fraction = min(1, depth / max(1, clip))
        return round(width * sqrt(1 - .67 * fraction ** 2))
    return width - depth


def turret_section(trace, model, z):
    bow_loss, rear_loss, side_loss, bow_clip, rear_clip, center = TURRET_SECTIONS[model]
    height = z / max(1, trace.turret_height - 1)
    depth = trace.turret_depth // 2
    width = trace.turret_width // 2
    rounded = trace.turret in {"cast", "rivet", "launcher"}
    shoulder = 1 if z == 0 and trace.turret_height > 4 else 0
    width -= round(height * side_loss) + shoulder
    rear = -depth + round(height * rear_loss) + shoulder
    front = depth - round(height * bow_loss) - shoulder
    return rear, front, width, rear_clip, bow_clip, center, rounded

