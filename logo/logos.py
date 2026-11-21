"""Bar logo concepts: family squircle tiles + segmented ring tray icons, supersampled 8x.
python3 logos.py sheet.png          -> option sheet
"""
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageChops, ImageFont

SS = 8

def hexc(h):
    h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))

def gradient(size, a, b):
    a, b = np.array(hexc(a), float), np.array(hexc(b), float)
    y, x = np.mgrid[0:size, 0:size]
    t = ((x + y) / (2.0 * (size - 1)))[..., None]
    rgb = a * (1 - t) + b * t
    alpha = np.full((size, size, 1), 255.0)
    return Image.fromarray(np.concatenate([rgb, alpha], 2).astype(np.uint8), 'RGBA')

def disc(d, cx, cy, r, fill=255):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)

def thick_line(d, p0, p1, w, fill=255):
    d.line([p0, p1], fill=fill, width=int(w)); disc(d, *p0, w / 2, fill); disc(d, *p1, w / 2, fill)

def arc_band(d, c, r_out, r_in, a0, a1, fill=255, caps=True):
    pts = []
    N = 160
    for i in range(N + 1):
        t = math.radians(a0 + (a1 - a0) * i / N); pts.append((c + math.cos(t) * r_out, c + math.sin(t) * r_out))
    for i in range(N, -1, -1):
        t = math.radians(a0 + (a1 - a0) * i / N); pts.append((c + math.cos(t) * r_in, c + math.sin(t) * r_in))
    d.polygon(pts, fill=fill)
    if caps:
        rm, w = (r_out + r_in) / 2, (r_out - r_in) / 2
        for a in (a0, a1):
            t = math.radians(a); disc(d, c + math.cos(t) * rm, c + math.sin(t) * rm, w, fill)

# ---------------------------------------------------------------- glyphs (n = tile px)
def bottle(d, n, x0, x1, body_top, bottom, neck_w, neck_top, r):
    P = lambda u, v: (u * n, v * n)
    cx = (x0 + x1) / 2
    d.rounded_rectangle([*P(x0, body_top), *P(x1, bottom)], radius=r * n, fill=255)
    # shoulder
    d.polygon([P(x0 + 0.005, body_top + r * 0.9), P(cx - neck_w / 2, body_top - 0.07),
               P(cx + neck_w / 2, body_top - 0.07), P(x1 - 0.005, body_top + r * 0.9)], fill=255)
    d.rounded_rectangle([*P(cx - neck_w / 2, neck_top), *P(cx + neck_w / 2, body_top)], radius=neck_w * n * 0.3, fill=255)

def glyph_shelf(n, small=False):
    """back-bar shelf: two bottles and a tumbler standing on a shelf (one shelf = all the apps)"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    shelf_y = 0.72
    if small:
        bottle(d, n, 0.20, 0.42, 0.40, shelf_y, 0.10, 0.20, 0.05)
        d.polygon([P(0.50, 0.46), P(0.80, 0.46), P(0.77, shelf_y), P(0.53, shelf_y)], fill=255)
        d.rounded_rectangle([*P(0.15, shelf_y + 0.04), *P(0.85, shelf_y + 0.12)], radius=0.04 * n, fill=255)
    else:
        bottle(d, n, 0.20, 0.36, 0.38, shelf_y, 0.075, 0.18, 0.04)          # tall bottle
        d.polygon([P(0.42, 0.50), P(0.58, 0.50), P(0.565, shelf_y), P(0.435, shelf_y)], fill=255)  # tumbler
        d.rectangle([*P(0.41, 0.565), *P(0.59, 0.59)], fill=0)               # liquid line
        bottle(d, n, 0.64, 0.80, 0.46, shelf_y, 0.065, 0.29, 0.04)          # short bottle
        d.rounded_rectangle([*P(0.14, shelf_y + 0.035), *P(0.86, shelf_y + 0.095)], radius=0.03 * n, fill=255)
    return m

def glyph_shaker(n, small=False):
    """cocktail shaker (the bartender looks after every drink)"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    gap = 0.035 if small else 0.028
    # cap
    d.rounded_rectangle([*P(0.43, 0.15), *P(0.57, 0.23)], radius=0.03 * n, fill=255)
    # lid: narrow at top, wide at bottom
    lid_top, lid_bot = 0.23 + gap, 0.40
    d.polygon([P(0.42, lid_top), P(0.58, lid_top), P(0.66, lid_bot), P(0.34, lid_bot)], fill=255)
    # body: wide at top, slightly narrower at bottom, rounded base
    b_top = lid_bot + gap
    d.polygon([P(0.32, b_top), P(0.68, b_top), P(0.635, 0.80), P(0.365, 0.80)], fill=255)
    d.rounded_rectangle([*P(0.365, 0.74), *P(0.635, 0.84)], radius=0.05 * n, fill=255)
    if not small:   # highlight stripe cut in the body
        d.polygon([P(0.405, b_top + 0.07), P(0.445, b_top + 0.07), P(0.435, 0.74), P(0.395, 0.74)], fill=0)
    return m

def glyph_martini(n, small=False):
    """cocktail glass with an olive on a pick (neon bar sign)"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    w = (0.075 if small else 0.06) * n
    d.polygon([P(0.20, 0.24), P(0.80, 0.24), P(0.5, 0.55)], fill=255)          # bowl
    thick_line(d, P(0.5, 0.52), P(0.5, 0.76), w)                               # stem
    d.rounded_rectangle([*P(0.33, 0.74), *P(0.67, 0.80)], radius=0.03 * n, fill=255)  # foot
    if not small:
        disc(d, 0.43 * n, 0.33 * n, 0.065 * n, 0)                               # olive (cut out)
        d.line([P(0.43, 0.33), P(0.66, 0.12)], fill=255, width=int(0.035 * n))  # pick
        disc(d, 0.66 * n, 0.12 * n, 0.03 * n)
        d.line([P(0.43, 0.33), P(0.52, 0.248)], fill=0, width=int(0.035 * n))   # pick inside the olive area stays cut
        disc(d, 0.43 * n, 0.33 * n, 0.065 * n, 0)
    else:
        disc(d, 0.42 * n, 0.33 * n, 0.07 * n, 0)
    return m


def stool(d, n, cx, seat_y, floor_y, small):
    P = lambda u, v: (u * n, v * n)
    sw = 0.20 if small else 0.17
    d.rounded_rectangle([*P(cx - sw / 2, seat_y), *P(cx + sw / 2, seat_y + (0.065 if small else 0.05))], radius=0.025 * n, fill=255)
    lw = (0.06 if small else 0.04) * n
    d.rectangle([*P(cx, seat_y), *P(cx, floor_y)], fill=255)
    d.line([P(cx, seat_y + 0.04), P(cx, floor_y)], fill=255, width=int(lw))
    if not small:
        d.line([P(cx - 0.05, floor_y - 0.085), P(cx + 0.05, floor_y - 0.085)], fill=255, width=int(0.028 * n))
    d.rounded_rectangle([*P(cx - 0.065, floor_y - 0.02), *P(cx + 0.065, floor_y + 0.02)], radius=0.02 * n, fill=255)

def glyph_counter(n, small=False):
    """bar counter: a bottle and a glass on the counter top, two stools in front"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    if small:
        bottle(d, n, 0.24, 0.40, 0.25, 0.44, 0.08, 0.12, 0.04)
        d.polygon([P(0.56, 0.28), P(0.74, 0.28), P(0.72, 0.44), P(0.58, 0.44)], fill=255)
        d.rounded_rectangle([*P(0.12, 0.46), *P(0.88, 0.54)], radius=0.03 * n, fill=255)
        stool(d, n, 0.33, 0.62, 0.84, True); stool(d, n, 0.67, 0.62, 0.84, True)
    else:
        bottle(d, n, 0.25, 0.37, 0.27, 0.45, 0.055, 0.14, 0.03)
        d.polygon([P(0.57, 0.31), P(0.71, 0.31), P(0.695, 0.45), P(0.585, 0.45)], fill=255)
        d.rectangle([*P(0.56, 0.355), *P(0.72, 0.375)], fill=0)
        d.rounded_rectangle([*P(0.12, 0.465), *P(0.88, 0.525)], radius=0.025 * n, fill=255)
        stool(d, n, 0.32, 0.60, 0.84, False); stool(d, n, 0.68, 0.60, 0.84, False)
    return m

def glyph_booth(n, small=False):
    """diner/lounge booth from the side: two high-back seats, a table with a drink, a pendant lamp"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    r = 0.03 * n
    # seats
    for left in (True, False):
        bx0, bx1 = (0.13, 0.22) if left else (0.78, 0.87)
        sx0, sx1 = (0.13, 0.36) if left else (0.64, 0.87)
        d.rounded_rectangle([*P(bx0, 0.36), *P(bx1, 0.82)], radius=r, fill=255)
        d.rounded_rectangle([*P(sx0, 0.60), *P(sx1, 0.68)], radius=r, fill=255)
        lx = (0.31 if left else 0.69)
        d.rectangle([*P(min(lx, bx1), 0.66), *P(max(lx, bx1), 0.82)], fill=255) if False else None
        d.rectangle([*P(lx - 0.02, 0.66), *P(lx + 0.02, 0.82)], fill=255)
    # table
    d.rounded_rectangle([*P(0.40, 0.50), *P(0.60, 0.545)], radius=0.02 * n, fill=255)
    lw = (0.05 if small else 0.036) * n
    d.line([P(0.5, 0.53), P(0.5, 0.80)], fill=255, width=int(lw))
    d.rounded_rectangle([*P(0.43, 0.79), *P(0.57, 0.83)], radius=0.02 * n, fill=255)
    # drink on the table
    d.polygon([P(0.455, 0.415), P(0.545, 0.415), P(0.535, 0.49), P(0.465, 0.49)], fill=255)
    # pendant lamp
    d.line([P(0.5, 0.06), P(0.5, 0.20)], fill=255, width=int((0.03 if small else 0.018) * n))
    d.pieslice([*P(0.41, 0.19), *P(0.59, 0.35)], 180, 360, fill=255)
    return m

def glyph_fridge(n, small=False):
    """a hotel minibar: small glass-door cabinet with bottles and cans on two shelves"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    d.rounded_rectangle([*P(0.21, 0.15), *P(0.79, 0.83)], radius=0.07 * n, fill=255)
    fr = 0.065 if small else 0.05
    d.rounded_rectangle([*P(0.21 + fr, 0.15 + fr), *P(0.79 - fr, 0.72)], radius=0.035 * n, fill=0)
    if small:
        d.rectangle([*P(0.25, 0.43), *P(0.75, 0.47)], fill=255)
        for x in (0.35, 0.50, 0.65):
            d.rounded_rectangle([*P(x - 0.045, 0.27), *P(x + 0.045, 0.43)], radius=0.02 * n, fill=255)
        for x in (0.38, 0.62):
            d.rounded_rectangle([*P(x - 0.06, 0.53), *P(x + 0.06, 0.68)], radius=0.02 * n, fill=255)
    else:
        d.rectangle([*P(0.26, 0.435), *P(0.74, 0.46)], fill=255)          # shelf
        for x, top in ((0.34, 0.24), (0.45, 0.28), (0.56, 0.24), (0.67, 0.30)):   # mini bottles
            bottle(d, n, x - 0.035, x + 0.035, top + 0.07, 0.435, 0.026, top, 0.015)
        for x in (0.355, 0.465):                                          # cans
            d.rounded_rectangle([*P(x - 0.042, 0.53), *P(x + 0.042, 0.68)], radius=0.015 * n, fill=255)
        bottle(d, n, 0.60, 0.69, 0.55, 0.68, 0.03, 0.49, 0.02)            # bottle lying low
        d.line([P(0.705, 0.25), P(0.705, 0.40)], fill=255, width=int(0.02 * n))   # door handle hint
        d.rounded_rectangle([*P(0.29, 0.765), *P(0.71, 0.785)], radius=0.01 * n, fill=0)  # drawer line
    return m

def tile(size, a, b, glyph):
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=0.24 * n, fill=255)
    out.paste(gradient(n, a, b), (0, 0), mask)
    gm = glyph(n, size < 40)
    out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 255)), (0, 0), gm)
    return out.resize((size, size), Image.LANCZOS)

def tray(size, accent, lit=4, total=5, update=False):
    """segmented ring: one segment per app, lit = running; white centre (accent dot when an update is ready)"""
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    c = n / 2; ro = 0.47 * n; ri = ro - 0.19 * n
    gapdeg = 22 if size <= 16 else 18
    seg = 360.0 / total
    for i in range(total):
        a0 = -90 + i * seg + gapdeg / 2; a1 = -90 + (i + 1) * seg - gapdeg / 2
        lay = Image.new('L', (n, n), 0); arc_band(ImageDraw.Draw(lay), c, ro, ri, a0, a1, caps=False)
        col = hexc(accent) + (255,) if i < lit else (255, 255, 255, 70)
        out.paste(Image.new('RGBA', (n, n), col), (0, 0), lay)
    dot = Image.new('L', (n, n), 0); disc(ImageDraw.Draw(dot), c, c, (0.14 if update else 0.1) * n)
    out.paste(Image.new('RGBA', (n, n), hexc(accent) + (255,) if update else (255, 255, 255, 255)), (0, 0), dot)
    return out.resize((size, size), Image.LANCZOS)

OPTIONS = [
    dict(key='A', name='Counter', sub='bar counter, bottle + glass, two stools; whiskey amber', a='#3E1A07', b='#E39A35', accent='#F2C14E', glyph=glyph_counter),
    dict(key='B', name='Booth', sub='lounge booth, table + drink, pendant lamp; deep teal', a='#06312F', b='#35B79C', accent='#3DDBB0', glyph=glyph_booth),
    dict(key='C', name='Minibar', sub='the hotel minibar itself, bottles + cans; neon ube', a='#2B1268', b='#D457B8', accent='#C77DFF', glyph=glyph_fridge),
]

def font(sz, bold=False):
    p = '/usr/share/fonts/opentype/inter/Inter-SemiBold.otf' if bold else '/usr/share/fonts/opentype/inter/Inter-Regular.otf'
    try: return ImageFont.truetype(p, sz)
    except Exception: return ImageFont.load_default()

def sheet(path):
    W, colw, H = 1260, 420, 640
    img = Image.new('RGBA', (W, H), (24, 24, 24, 255)); d = ImageDraw.Draw(img)
    for i, o in enumerate(OPTIONS):
        x0 = i * colw
        if i: d.line([x0, 30, x0, H - 30], fill=(48, 48, 48), width=1)
        img.alpha_composite(tile(200, o['a'], o['b'], o['glyph']), (x0 + 110, 40))
        d.text((x0 + 210, 262), f"{o['key']}  ·  {o['name']}", font=font(26, True), fill=(242, 242, 242), anchor='mm')
        d.text((x0 + 210, 294), o['sub'], font=font(15), fill=(160, 160, 160), anchor='mm')
        xx = x0 + 70
        for s in (64, 32, 24, 16):
            img.alpha_composite(tile(s, o['a'], o['b'], o['glyph']), (xx, 330 + (64 - s) // 2)); xx += s + 26
        d.text((xx + 6, 362), 'exe icon sizes', font=font(13), fill=(112, 112, 112), anchor='lm')
        strip = (x0 + 30, 420, x0 + colw - 30, 470)
        d.rounded_rectangle(strip, radius=8, fill=(32, 32, 32))
        xx = strip[0] + 16
        for s in (16, 20, 24):
            img.alpha_composite(tray(s, o['accent'], 4), (xx, 445 - s // 2)); xx += s + 16
        img.alpha_composite(tray(20, o['accent'], 2, update=True), (xx + 6, 435))
        d.text((xx + 36, 445), '4 of 5 running · update', font=font(13), fill=(112, 112, 112), anchor='lm')
        z = tray(16, o['accent'], 4).resize((64, 64), Image.NEAREST)
        img.alpha_composite(z, (x0 + 50, 492))
        d.text((x0 + 50, 566), 'tray 16 px, zoomed', font=font(12), fill=(112, 112, 112))
        # accent sample: an app card
        bx = x0 + 150
        d.rounded_rectangle([bx, 492, bx + 230, 556], radius=8, fill=(45, 45, 45))
        disc(d, bx + 18, 512, 5, hexc(o['accent']))
        d.text((bx + 32, 512), 'Echoa  · running', font=font(14), fill=(242, 242, 242), anchor='lm')
        d.text((bx + 32, 536), 'v1.5.1', font=font(12), fill=(160, 160, 160), anchor='lm')
        d.rounded_rectangle([bx + 150, 526, bx + 218, 548], radius=11, fill=hexc(o['accent']))
        d.text((bx + 184, 537), 'Update', font=font(12, True), fill=(24, 24, 24), anchor='mm')
        d.text((x0 + 210, 600), o['accent'], font=font(13), fill=(160, 160, 160), anchor='mm')
    img.save(path)

if __name__ == '__main__':
    sheet(sys.argv[1] if len(sys.argv) > 1 else 'bar_logo_options.png')


# ---------------------------------------------------------------- hub approach: built from the family's own colours
FAMILY = ['#5FD068', '#D0644A', '#FF5C9E', '#FF8A3D', '#D99A62']   # Juice, Pour, Fizz, Pulp, Echoa

def tile_color(size, a, b, painter):
    """dark gradient tile, glyph painted in colour by painter(img, n, small)"""
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=0.24 * n, fill=255)
    out.paste(gradient(n, a, b), (0, 0), mask)
    painter(out, n, size < 40)
    return out.resize((size, size), Image.LANCZOS)

def paint_bars(img, n, small):
    """five bottles in the five app colours, different heights: a minibar shelf that is also a bar chart"""
    d = ImageDraw.Draw(img)
    cols = FAMILY
    k = len(cols); x0, x1 = 0.17, 0.83
    w = (x1 - x0) / k
    heights = [0.40, 0.55, 0.32, 0.48, 0.60] if not small else [0.42, 0.56, 0.36, 0.50, 0.60]
    base = 0.76
    for i, (c, h) in enumerate(zip(cols, heights)):
        bx0 = x0 + i * w + w * (0.10 if not small else 0.06); bx1 = x0 + (i + 1) * w - w * (0.10 if not small else 0.06)
        d.rounded_rectangle([bx0 * n, (base - h) * n, bx1 * n, base * n], radius=(bx1 - bx0) * n * 0.35, fill=hexc(c) + (255,))
    d.rounded_rectangle([0.14 * n, (base + 0.035) * n, 0.86 * n, (base + (0.09 if not small else 0.11)) * n], radius=0.03 * n, fill=(255, 255, 255, 255))

def paint_ring(img, n, small):
    """a ring split into the five app colours, white centre"""
    d = ImageDraw.Draw(img); c = n / 2
    ro = 0.33 * n; ri = ro - (0.13 if small else 0.10) * n
    gap = 16 if small else 12
    for i, col in enumerate(FAMILY):
        a0 = -90 + i * 72 + gap / 2; a1 = -90 + (i + 1) * 72 - gap / 2
        lay = Image.new('L', (n, n), 0); arc_band(ImageDraw.Draw(lay), c, ro, ri, a0, a1, caps=False)
        img.paste(Image.new('RGBA', (n, n), hexc(col) + (255,)), (0, 0), lay)
    disc(d, c, c, (0.10 if small else 0.08) * n, (255, 255, 255, 255))

def paint_grid(img, n, small):
    """3x2 grid of rounded squares: five app colours + white (Minibar itself)"""
    d = ImageDraw.Draw(img)
    cols = FAMILY[:2] + ['#FFFFFF'] + FAMILY[2:]
    cell = 0.19 if not small else 0.21; gap = 0.05 if not small else 0.04
    gw = 3 * cell + 2 * gap; gh = 2 * cell + gap
    ox, oy = (1 - gw) / 2, (1 - gh) / 2
    for i, col in enumerate(cols):
        r, cc = divmod(i, 3)
        x = ox + cc * (cell + gap); y = oy + r * (cell + gap)
        d.rounded_rectangle([x * n, y * n, (x + cell) * n, (y + cell) * n], radius=cell * n * 0.28, fill=hexc(col) + (255,))

HUB = [
    dict(key='D', name='Lineup', sub='five bottles in the apps\' colours (also a bar chart)', a='#26262B', b='#0E0E10', accent='#F2C14E', paint=paint_bars),
    dict(key='E', name='Ring', sub='one ring, a segment per app, in their colours', a='#26262B', b='#0E0E10', accent='#F2C14E', paint=paint_ring),
    dict(key='F', name='Tiles', sub='the apps as tiles, white one = Minibar', a='#26262B', b='#0E0E10', accent='#F2C14E', paint=paint_grid),
]


def _c(col): return hexc(col) + (255,)
WHITE = (255, 255, 255, 255)

def paint_taps(img, n, small):
    """beer-tap rail: five tap handles in the app colours on a white rail"""
    d = ImageDraw.Draw(img); P = lambda u, v: (u * n, v * n)
    rail_y = 0.60
    d.rounded_rectangle([*P(0.13, rail_y), *P(0.87, rail_y + (0.07 if not small else 0.09))], radius=0.03 * n, fill=WHITE)
    k = 5; x0, x1 = 0.20, 0.80; step = (x1 - x0) / (k - 1)
    hw = 0.075 if not small else 0.09
    for i, col in enumerate(FAMILY):
        cx = x0 + i * step
        top = 0.20 + (0.06 if i % 2 else 0)
        d.rounded_rectangle([*P(cx - hw / 2, top), *P(cx + hw / 2, rail_y - 0.07)], radius=hw * n * 0.45, fill=_c(col))
        d.rectangle([*P(cx - 0.012, rail_y - 0.08), *P(cx + 0.012, rail_y)], fill=WHITE)
        if not small:   # spouts
            d.rounded_rectangle([*P(cx - 0.018, rail_y + 0.07), *P(cx + 0.018, rail_y + 0.14)], radius=0.01 * n, fill=WHITE)

def paint_layers(img, n, small):
    """layered shot: one glass, five layers in the app colours (Minibar mixes them all)"""
    P = lambda u, v: (u * n, v * n)
    top, bot = 0.18, 0.80
    xt0, xt1, xb0, xb1 = 0.27, 0.73, 0.34, 0.66
    glass = Image.new('L', (n, n), 0)
    ImageDraw.Draw(glass).polygon([P(xt0, top), P(xt1, top), P(xb1, bot), P(xb0, bot)], fill=255)
    ImageDraw.Draw(glass).rounded_rectangle([*P(xb0, bot - 0.06), *P(xb1, bot)], radius=0.03 * n, fill=255)
    img.paste(Image.new('RGBA', (n, n), WHITE), (0, 0), glass)
    wall = 0.035 if not small else 0.045
    inner = Image.new('L', (n, n), 0)
    ImageDraw.Draw(inner).polygon([P(xt0 + wall, top + 0.10), P(xt1 - wall, top + 0.10), P(xb1 - wall * 0.8, bot - wall * 1.6), P(xb0 + wall * 0.8, bot - wall * 1.6)], fill=255)
    y0, y1 = top + 0.10, bot - wall * 1.6
    h = (y1 - y0) / 5
    for i, col in enumerate(reversed(FAMILY)):
        band = Image.new('L', (n, n), 0)
        ImageDraw.Draw(band).rectangle([0, (y0 + i * h) * n, n, (y0 + (i + 1) * h) * n + 1], fill=255)
        img.paste(Image.new('RGBA', (n, n), _c(col)), (0, 0), ImageChops.multiply(band, inner))

def paint_fridge(img, n, small):
    """white minibar cabinet with five bottles inside in the app colours"""
    d = ImageDraw.Draw(img); P = lambda u, v: (u * n, v * n)
    d.rounded_rectangle([*P(0.20, 0.14), *P(0.80, 0.84)], radius=0.08 * n, fill=WHITE)
    fr = 0.055 if not small else 0.07
    d.rounded_rectangle([*P(0.20 + fr, 0.14 + fr), *P(0.80 - fr, 0.72)], radius=0.04 * n, fill=(22, 22, 26, 255))
    d.rectangle([*P(0.25, 0.45), *P(0.75, 0.475)], fill=WHITE)
    m = Image.new('L', (n, n), 0); dm = ImageDraw.Draw(m)
    spots = [(0.33, 0.24, 0.45), (0.45, 0.20, 0.45), (0.57, 0.27, 0.45), (0.67, 0.22, 0.45)] if not small else [(0.37, 0.24, 0.45), (0.63, 0.22, 0.45)]
    lows = [(0.42, 0.53, 0.68)] if not small else [(0.50, 0.53, 0.68)]
    cols = FAMILY
    for idx, (x, t, b) in enumerate(spots + lows):
        lay = Image.new('L', (n, n), 0); dl = ImageDraw.Draw(lay)
        bw = 0.075 if not small else 0.12
        bottle(dl, n, x - bw / 2, x + bw / 2, t + 0.08, b, bw * 0.4, t, 0.02)
        img.paste(Image.new('RGBA', (n, n), _c(cols[idx % 5])), (0, 0), lay)
    if not small:
        lay = Image.new('L', (n, n), 0)
        ImageDraw.Draw(lay).rounded_rectangle([*P(0.55, 0.55), *P(0.65, 0.68)], radius=0.015 * n, fill=255)
        img.paste(Image.new('RGBA', (n, n), _c(FAMILY[4])), (0, 0), lay)
        d.rounded_rectangle([*P(0.30, 0.765), *P(0.70, 0.785)], radius=0.01 * n, fill=(22, 22, 26, 255))

def paint_stools(img, n, small):
    """white bar counter, five stools in the app colours"""
    d = ImageDraw.Draw(img); P = lambda u, v: (u * n, v * n)
    d.rounded_rectangle([*P(0.11, 0.30), *P(0.89, 0.38 if not small else 0.40)], radius=0.03 * n, fill=WHITE)
    k = 5 if not small else 3
    cols = FAMILY if not small else [FAMILY[0], FAMILY[2], FAMILY[3]]
    x0, x1 = 0.20, 0.80; step = (x1 - x0) / (k - 1)
    sw = 0.10 if not small else 0.16
    for i, col in enumerate(cols):
        cx = x0 + i * step; c = _c(col)
        d.rounded_rectangle([*P(cx - sw / 2, 0.50), *P(cx + sw / 2, 0.56 if not small else 0.58)], radius=0.02 * n, fill=c)
        d.rectangle([*P(cx - (0.012 if not small else 0.025), 0.54), *P(cx + (0.012 if not small else 0.025), 0.80)], fill=c)
        d.rounded_rectangle([*P(cx - sw * 0.38, 0.78), *P(cx + sw * 0.38, 0.82)], radius=0.015 * n, fill=c)

HUB2 = [
    dict(key='G', name='Taps', sub='a tap rail, one handle per app', a='#26262B', b='#0E0E10', paint=paint_taps),
    dict(key='H', name='Layers', sub='a layered shot: all your apps in one glass', a='#26262B', b='#0E0E10', paint=paint_layers),
    dict(key='I', name='Stocked', sub='the minibar, stocked with your apps', a='#26262B', b='#0E0E10', paint=paint_fridge),
    dict(key='J', name='Stools', sub='a counter with a stool for each app', a='#26262B', b='#0E0E10', paint=paint_stools),
]
