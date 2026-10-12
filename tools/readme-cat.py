# README cat pictures, drawn from the app's own cat pictures (no screen capture): moving pictures (animated WebP) of the cat's motions and
# play, the growth stages and the album. Each comes in a light (white page, white cat) and a dark (GitHub dark page,
# black cat) version for <picture> in README.md.
#   py -3.12 tools/readme-cat.py            -> docs/images/cat/*.webp|png
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, 'src', 'OneKey', 'Assets', 'cat')
OUT = os.path.join(ROOT, 'docs', 'images', 'cat')
FONT = os.path.join(ROOT, 'src', 'OneKey', 'Fonts', 'Pretendard-SemiBold.ttf')
FONT_R = os.path.join(ROOT, 'src', 'OneKey', 'Fonts', 'Pretendard-Regular.ttf')
os.makedirs(OUT, exist_ok=True)

THEMES = {   # page colour, text, soft text, card
    'light': ((255, 255, 255), (31, 35, 40), (101, 109, 118), (246, 248, 250)),
    'dark': ((13, 17, 23), (230, 237, 243), (145, 152, 161), (22, 27, 34)),
}
FRAME_MS = 50   # the app ticks about every 47 ms

CLIPS = {}
for line in open(os.path.join(A, 'catclips.txt'), encoding='utf-8'):
    p = line.split()
    if p: CLIPS[p[0]] = (int(p[1]), int(p[2]), int(p[3]))

_strip = {}
def frame(name, i, theme):
    n, w, h = CLIPS[name]
    key = (name, theme)
    if key not in _strip:
        rgb = Image.open(os.path.join(A, f'catclip_{name}{"_dark" if theme == "dark" else ""}.jpg')).convert('RGB')
        a = Image.open(os.path.join(A, f'catclip_{name}_a.png')).convert('L')
        _strip[key] = (rgb, a)
    rgb, a = _strip[key]
    i = max(0, min(n - 1, i))
    box = (i * w, 0, i * w + w, h)
    im = rgb.crop(box); im.putalpha(a.crop(box))
    return im

def font(size, bold=True): return ImageFont.truetype(FONT if bold else FONT_R, size)

def text_c(d, cx, y, s, f, fill):
    w = d.textlength(s, font=f)
    d.text((cx - w / 2, y), s, font=f, fill=fill)

def save_gif(frames, path):
    """Animated WebP (a GIF of the same frames is 5-10x bigger)."""
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=FRAME_MS, loop=0, quality=82, method=6)
    print(os.path.relpath(path, ROOT), len(frames), 'frames', os.path.getsize(path) // 1024, 'KB')

def cells_gif(items, theme, path, scale=1.0, cell_w=150, top=8, sub=None):
    """items = [(clip, label, sublabel)]: every clip plays once side by side, then all hold their last frame briefly."""
    bg, fg, soft, _ = THEMES[theme]
    h = max(CLIPS[c][2] for c, _, _ in items)
    sh = int(h * scale)
    W, H = cell_w * len(items), top + sh + 48
    longest = max(CLIPS[c][0] for c, _, _ in items) + 12
    f1, f2 = font(15), font(12, False)
    frames = []
    for t in range(longest):
        im = Image.new('RGB', (W, H), bg); d = ImageDraw.Draw(im)
        for k, (c, label, s) in enumerate(items):
            fr = frame(c, t, theme)
            if scale != 1.0: fr = fr.resize((int(fr.width * scale), int(fr.height * scale)), Image.LANCZOS)
            x = k * cell_w + (cell_w - fr.width) // 2
            im.paste(fr, (x, top + sh - fr.height), fr)
            text_c(d, k * cell_w + cell_w / 2, top + sh + 6, label, f1, fg)
            if s: text_c(d, k * cell_w + cell_w / 2, top + sh + 27, s, f2, soft)
        frames.append(im)
    save_gif(frames, path)

def walk_gif(theme, path):
    """The cat walks in from the left, turns to the front, sits and waves."""
    bg, fg, soft, _ = THEMES[theme]
    W, H = 420, 150
    frames = []
    nWL, wWL, hWL = CLIPS['WL']
    x = -40.0
    step = 2.6
    t = 0
    while x < 190:   # walk cycle repeats while moving right
        im = Image.new('RGB', (W, H), bg)
        fr = frame('WL', t % nWL, theme)
        im.paste(fr, (int(x), H - 8 - fr.height), fr)
        frames.append(im); x += step; t += 1
    for name in ('WI', 'M04'):
        n, w, h = CLIPS[name]
        for i in range(n):
            im = Image.new('RGB', (W, H), bg)
            fr = frame(name, i, theme)
            cx = int(x) + wWL // 2
            im.paste(fr, (cx - fr.width // 2, H - 8 - fr.height), fr)
            frames.append(im)
    frames += [frames[-1]] * 20
    save_gif(frames, path)

def growth_png(theme, path):
    bg, fg, soft, card = THEMES[theme]
    names = ['콩떡', '찹쌀떡', '모닝빵', '식빵', '통식빵', '대왕식빵']
    cat = Image.open(os.path.join(A, f'cat_{theme}_center.png')).convert('RGBA')
    full = 150   # height of the cat at 100 %
    cw = 128
    W, H = cw * 6, 64 + full + 64
    im = Image.new('RGB', (W, H), bg); d = ImageDraw.Draw(im)
    f1, f2 = font(16), font(12, False)
    base = 64 + full
    for k in range(6):
        badge = Image.open(os.path.join(A, f'album_stage{k}.png')).convert('RGBA').resize((48, 48), Image.LANCZOS)
        im.paste(badge, (k * cw + (cw - 48) // 2, 6), badge)
        s = 0.5 + 0.1 * k
        h = int(full * s); w = int(cat.width * h / cat.height)
        c = cat.resize((w, h), Image.LANCZOS)
        im.paste(c, (k * cw + (cw - w) // 2, base - h), c)
        text_c(d, k * cw + cw / 2, base + 10, names[k], f1, fg)
        text_c(d, k * cw + cw / 2, base + 33, f'{k * 20}점 · {int(s * 100)}%', f2, soft)
    im.save(path, optimize=True)
    print(os.path.relpath(path, ROOT), os.path.getsize(path) // 1024, 'KB')

def album_png(theme, path):
    bg, fg, soft, card = THEMES[theme]
    cards = [('wave', '앞발 인사'), ('meow', '야옹'), ('groom', '그루밍'), ('stretch', '기지개'), ('belly', '벌러덩'), ('wall', '벽 기대기'), ('walk', '산책'),
             ('peek', '빼꼼'), ('pet', '쓰담'), ('treat', '츄르'), ('play', '놀이'), ('toy', '깜짝 놀잇감'), ('stamp', '발도장')]
    cols, cs = 7, 112
    rows = (len(cards) + cols - 1) // cols
    W, H = cols * cs, rows * (cs + 26) + 4
    im = Image.new('RGB', (W, H), bg); d = ImageDraw.Draw(im)
    f = font(12, False)
    for k, (n, label) in enumerate(cards):
        r, c = divmod(k, cols)
        x, y = c * cs, r * (cs + 26)
        d.rounded_rectangle((x + 4, y + 4, x + cs - 4, y + cs - 4), 14, fill=card)
        p = os.path.join(A, f'album_{n}{"_dark" if theme == "dark" else ""}.png')
        a = Image.open(p).convert('RGBA').resize((cs - 16, cs - 16), Image.LANCZOS)
        im.paste(a, (x + 8, y + 8), a)
        text_c(d, x + cs / 2, y + cs + 2, label, f, soft)
    im.save(path, optimize=True)
    print(os.path.relpath(path, ROOT), os.path.getsize(path) // 1024, 'KB')

for th in ('light', 'dark'):
    walk_gif(th, os.path.join(OUT, f'walk-{th}.webp'))
    cells_gif([('M04', '앞발 인사', None), ('M08', '야옹', None), ('M05', '그루밍', None), ('M17', '기지개', None), ('M07', '벌러덩', None)],
              th, os.path.join(OUT, f'motions-{th}.webp'), cell_w=136)
    cells_gif([('I1', '쓰담', '+6점'), ('I2', '츄르', '+10점'), ('I3', '놀이', '+8점')], th, os.path.join(OUT, f'play-{th}.webp'), cell_w=150)
    growth_png(th, os.path.join(OUT, f'growth-{th}.png'))
    album_png(th, os.path.join(OUT, f'album-{th}.png'))
