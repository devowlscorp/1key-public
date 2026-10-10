# -*- coding: utf-8 -*-
"""Black cat = the white (grey tabby) cat recoloured (public edition 0.5.22, 2026-10-10 user: "same as the white cat, only the colour differs").

Every dark-theme cat picture is made from its light counterpart with the same shape and the same alpha, so motions, timing and
the joins between stills and motions are identical for both cats:
  fur (everything that is not eye or pink) -> near-black with the original light/shade kept as soft texture
  iris (blue, with the pupil and catch-lights it encloses)    -> unchanged
  pink (ear insides, nose, paw pads, tongue)                   -> dusky pink
recolour(rgb, a) works on straight (not premultiplied) float RGB 0..1 and an alpha 0..1.

  py -3.12 tools/cat/recolor_dark.py all        rebuild every dark asset in src/OneKey/Assets/cat from the light ones
  py -3.12 tools/cat/recolor_dark.py sample OUT  a light/dark comparison sheet
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage

REPO = Path(__file__).resolve().parents[2]
CAT = REPO / "src" / "OneKey" / "Assets" / "cat"

FUR_BASE, FUR_GAIN, FUR_GAMMA = 0.085, 0.20, 1.1
RIM = 0.34                  # light rim along the outline (the hand-drawn black cat has one) so the cat reads on a dark taskbar
RIM_TINT = np.array([0.80, 0.84, 0.92])
FUR_TINT = np.array([1.0, 0.975, 0.985])
PINK_KEEP = 0.46

def masks(rgb, a):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    solid = a > 0.35
    blue = solid & (b > r + 0.07) & (b > g + 0.02)
    lab, n = ndimage.label(blue)
    iris = np.zeros_like(blue)
    if n:
        sizes = ndimage.sum(blue, lab, range(1, n + 1))
        keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= max(3, blue.shape[0] * blue.shape[1] * 0.00004)])
        cand = ndimage.binary_fill_holes(ndimage.binary_closing(keep, iterations=1)) & solid
        # an eye is small and ringed by the dark eyelid line; the video model's bluish tint on fur (tail tip, back) sits on light fur
        # (measured: open eyes ring 20th-percentile brightness 0.10-0.24, tints 0.38-0.84 — half-shut eyes ~0.4 turn dark, which reads fine)
        L = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
        lab2, n2 = ndimage.label(cand)
        maxA = 0.008 * blue.shape[0] ** 2          # strip: cell height; still: picture height
        rw = max(1, round(blue.shape[0] * 0.02))
        for i in range(1, n2 + 1):
            m = lab2 == i
            if m.sum() > maxA: continue
            ring = ndimage.binary_dilation(m, iterations=rw) & ~m & solid
            if ring.any() and np.percentile(L[ring], 20) <= 0.30: iris |= m
    pink = solid & (r > g + 0.07) & (r > b + 0.03) & ~iris
    L = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    # treat pouch (props keep their colour): judged on smoothed colour (JPEG strips have noisy chroma), then closed and grown a little
    gb = ndimage.gaussian_filter(g - b, 1.2); rg = ndimage.gaussian_filter(r - g, 1.2)
    cream = solid & (gb > 0.058) & (rg < 0.16) & (L > 0.62) & ~iris   # pouch g-b ~0.08 · fur ~0.016 (album treat measured)
    cream = ndimage.binary_closing(cream, iterations=2)
    lab, n = ndimage.label(cream)
    if n:
        sizes = ndimage.sum(cream, lab, range(1, n + 1))
        cream = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= 12])
    cream = ndimage.binary_dilation(ndimage.binary_fill_holes(ndimage.binary_closing(cream, iterations=4)), iterations=1) & solid & ~iris & ~pink   # 봉지 안 그늘 줄까지
    return iris, pink, cream

def recolour(rgb, a):
    rgb = rgb.astype(np.float32)
    iris, pink, cream = masks(rgb, a)
    L = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    fur = (FUR_BASE + FUR_GAIN * np.power(np.clip(L, 0, 1), FUR_GAMMA))[..., None] * FUR_TINT
    # rim: distance from the outline, in proportion to the picture height (a strip's cells share its height)
    dist = ndimage.distance_transform_edt(a > 0.5)
    w = max(1.0, a.shape[0] * 0.018)
    rim = np.exp(-dist / w)[..., None] * RIM
    fur = fur * (1 - rim) + RIM_TINT * rim
    out = fur.copy()
    out[cream] = rgb[cream]
    dusk = np.clip(rgb * PINK_KEEP, 0, 1) * 0.75 + fur * 0.25
    out[pink] = dusk[pink]
    out[iris] = rgb[iris]
    return np.clip(out, 0, 1)

# ---------------------------------------------------------------- file helpers

def png_straight(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32) / 255
    return im[..., :3], im[..., 3]

def save_png(path, rgb, a):
    Image.fromarray(np.dstack([rgb, a[..., None]]).__mul__(255).round().clip(0, 255).astype(np.uint8), "RGBA").save(path, optimize=True)

def strip_straight(jpg, alpha_png):
    pre = np.asarray(Image.open(jpg).convert("RGB")).astype(np.float32) / 255
    a = np.asarray(Image.open(alpha_png).convert("L")).astype(np.float32) / 255
    rgb = np.where(a[..., None] > 0.01, np.minimum(pre, a[..., None]) / np.maximum(a[..., None], 1e-3), 0)
    return rgb, a

def save_strip_jpg(path, rgb, a):
    pre = (rgb * a[..., None] * 255).round().clip(0, 255).astype(np.uint8)
    Image.fromarray(pre, "RGB").save(path, quality=88, subsampling=0, optimize=True)

def build_all():
    n = 0
    for p in sorted(CAT.glob("cat_light_*.png")):                       # gaze stills + meow stills
        rgb, a = png_straight(p); save_png(CAT / p.name.replace("_light_", "_dark_"), recolour(rgb, a), a); n += 1
    rgb, a = strip_straight(CAT / "cat_light_tw.jpg", CAT / "cat_light_tw_a.png")     # gaze in-betweens
    save_strip_jpg(CAT / "cat_dark_tw.jpg", recolour(rgb, a), a)
    Image.open(CAT / "cat_light_tw_a.png").save(CAT / "cat_dark_tw_a.png"); n += 1
    for p in sorted(CAT.glob("lockclip_*_light.jpg")):                   # lock widget motions
        ap = CAT / p.name.replace(".jpg", "_a.png")
        rgb, a = strip_straight(p, ap)
        save_strip_jpg(CAT / p.name.replace("_light", "_dark"), recolour(rgb, a), a)
        Image.open(ap).save(CAT / ap.name.replace("_light", "_dark")); n += 1
    lines = (CAT / "lockclips.txt").read_text(encoding="utf-8").splitlines()   # dark lines = light lines (same strips now)
    light = {l.split()[0]: l for l in lines if len(l.split()) > 2 and l.split()[1] == "light"}
    out = [l if not (len(l.split()) > 2 and l.split()[1] == "dark") else light[l.split()[0]].replace(" light ", " dark ", 1) for l in lines]
    (CAT / "lockclips.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    for p in sorted(CAT.glob("catclip_*.jpg")):                           # taskbar motions: colour strip only, alpha shared
        if p.stem.endswith("_dark"): continue
        rgb, a = strip_straight(p, CAT / f"{p.stem}_a.png")
        save_strip_jpg(CAT / f"{p.stem}_dark.jpg", recolour(rgb, a), a); n += 1
    for p in sorted(CAT.glob("album_*.png")):                             # notebook album cards (stage badges are bread - no cat)
        if p.stem.endswith("_dark") or "stage" in p.stem: continue
        rgb, a = png_straight(p); d = recolour(rgb, a)
        npp = CAT / f"{p.stem}-noprop.png"
        if npp.exists() and not p.stem.endswith("-noprop"):              # the prop (pouch, feather, yarn) keeps its colours
            rn, an = png_straight(npp)
            prop = (np.abs(rgb - rn).max(axis=2) > 0.06) | (np.abs(a - an) > 0.06)
            prop = ndimage.binary_opening(prop, iterations=1) & (a > an + 0.02) | (prop & (an < 0.3))
            d[prop] = rgb[prop]
        save_png(CAT / f"{p.stem}_dark.png", d, a); n += 1
    print("dark assets", n)

def sample(out):
    picks = [("cat_light_center.png", None), ("cat_light_meow-open.png", None), ("catclip_M04.jpg", 40), ("catclip_WL.jpg", 3),
             ("catclip_I2.jpg", 60), ("catclip_HP.jpg", 14), ("album_pet.png", None), ("album_treat.png", None)]
    tiles = []
    for name, cell in picks:
        if name.endswith(".png"):
            rgb, a = png_straight(CAT / name)
        else:
            rgb, a = strip_straight(CAT / name, CAT / name.replace(".jpg", "_a.png"))
            info = {l.split()[0]: l.split() for l in (CAT / "catclips.txt").read_text().splitlines() if l.strip()}[Path(name).stem.split("_", 1)[1]]
            cw = int(info[2]); rgb, a = rgb[:, cell * cw:(cell + 1) * cw], a[:, cell * cw:(cell + 1) * cw]
        d = recolour(rgb, a)
        for img in (rgb, d):
            t = Image.fromarray((np.dstack([img, a[..., None]]) * 255).round().astype(np.uint8), "RGBA")
            k = 160 / max(t.size); t = t.resize((max(1, round(t.width * k)), max(1, round(t.height * k))), Image.LANCZOS)
            tiles.append(t)
    W = sum(t.width + 8 for t in tiles[::2]); H = 2 * (160 + 8) * 2
    sheet = Image.new("RGBA", (W, H), (243, 243, 243, 255)); sheet.paste((32, 32, 32, 255), (0, H // 2, W, H))
    x = 0
    for i in range(0, len(tiles), 2):
        for row, bgy in ((0, 0), (1, H // 2)):
            sheet.alpha_composite(tiles[i], (x, bgy + 4)); sheet.alpha_composite(tiles[i + 1], (x, bgy + 172))
        x += tiles[i].width + 8
    dk = Image.open(CAT / "cat_dark_center.png").convert("RGBA"); dk.thumbnail((160, 160))
    sheet.alpha_composite(dk, (W - dk.width - 4, H // 2 + 4)) if False else None
    sheet.convert("RGB").save(out); print(out)

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "all": build_all()
    elif len(sys.argv) > 2 and sys.argv[1] == "sample": sample(sys.argv[2])
    else: print(__doc__)
