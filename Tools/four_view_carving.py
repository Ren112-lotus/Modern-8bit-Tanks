"""Register exterior images and intersect silhouettes on a sprite-stack grid."""

from PIL import Image, ImageDraw


def register(photo, size, outline, project, colorize, profile=False):
    """Align a photographed view and its traced outline on the same grid."""
    width, height = size
    row_origin = height - 1 if profile else height // 2
    row_sign = -1 if profile else 1
    mask = Image.new("1", size)
    ImageDraw.Draw(mask).polygon([(x + width // 2, row_origin + row_sign * y)
                                  for x, y in outline], fill=1)
    view = Image.new("RGBA", size)
    for row in range(height):
        for column in range(width):
            if mask.getpixel((column, row)):
                x, y = project(column - width // 2, (row - row_origin) * row_sign)
                view.putpixel((column, row), colorize(photo.getpixel((round(x), round(y)))))
    return view


def intersect(width, length, height, views):
    top, side, front, rear = views
    occupied = set()
    for z in range(height):
        row = height - 1 - z
        for y in range(length):
            if not side[y, row][3]:
                continue
            for x in range(width):
                if top[x, y][3] and front[x, row][3] and rear[x, row][3]:
                    occupied.add((x, y, z))
    return occupied
