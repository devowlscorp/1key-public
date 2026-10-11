# -*- coding: utf-8 -*-
"""Black cat pictures from Codex's black key poses (public edition 0.5.22-H, 2026-10-11).

Codex repainted every key pose of the white cat as the black cat (work/cat-motions/dark: same canvas and alpha as the white keys,
checks.json lists source -> output). Stills and album cards use those pictures directly. The motions in between were made by a
video model from the white keys; making them again from the black keys would change the motion, so the white frames keep their
shape and alpha and get the black cat's colours:

  T = learnt from the white/black key pairs (same pixels): fur = distance-from-outline band x white luminance -> mean black RGB,
      pink (ear insides, nose, pads, tongue) = redness x luminance -> mean black RGB, both smoothed. The blue iris (ringed by the
      eyelid line) and the treat pouch keep their colours, as Codex kept them.
  taskbar motions: T(white frame)
  lock clips:      patches laid on the still (blink, meow, ears) that differ from it only a little, so
                   black = Codex still + T(white frame) - T(white still) - no seam against the Codex still
  gaze in-betweens (cat_dark_tw): not here - a head turn moves too much for that; RIFE between the Codex stills:
                   GAZE_THEMES=dark py -3.12 work/cat-motions/flf/gaze_tweens.py <out>   (HF_TOKENS from the Machine env, free ZeroGPU)

  py -3.12 tools/cat/dark_transfer.py all              stills, album, lock clips, taskbar motions (Assets/cat)
  py -3.12 tools/cat/dark_transfer.py sample OUT.png   white / old recolour / new, a few frames of each motion
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).resolve().parent))
import recolor_dark as rd

REPO = Path(__file__).resolve().parents[2]
CAT = REPO / "src" / "OneKey" / "Assets" / "cat"
DARK = REPO / "work" / "cat-motions" / "dark"

BANDS = (0.012, 0.03, 0.06)              # distance from the outline / cat height
TRAIN_H = 200                            # keys are scaled so the picture is about this tall (strips: cat 80 px)

def _band(a, h):
    d = ndimage.distance_transform_edt(a > 0.5) / max(1.0, h)
    return np.digitize(d, BANDS)

LB = 64                                  # luminance bins
LW = np.array([0.299, 0.587, 0.114], np.float32)

def _lbin(rgb):
    return np.clip(np.round((rgb @ LW) * (LB - 1)), 0, LB - 1).astype(np.int32)

def _small(path):
    im = Image.open(path).convert("RGBA")
    k = TRAIN_H / im.height
    if k < 1:
        a = np.asarray(im).astype(np.float32); a[..., :3] *= a[..., 3:4] / 255
        ch = [np.asarray(Image.fromarray(np.ascontiguousarray(a[..., i])).resize((round(im.width * k), TRAIN_H), Image.LANCZOS)) for i in range(4)]
        al = np.clip(ch[3], 0, 255)
        rgb = np.stack(ch[:3], 2) / np.maximum(al, 1)[..., None]
        return np.clip(rgb, 0, 1), al / 255
    a = np.asarray(im).astype(np.float32) / 255
    return a[..., :3], a[..., 3]

PS, PL = 16, 16                          # pink table: redness (r - g, 0..0.5) x luminance

def classes(rgb, a, h):
    """iris (kept as is, one pixel wider than the blue so its rim survives scaling), treat pouch (kept), pink (ear insides,
    nose, pads, tongue: clearly red, in blobs - not the warm fringe of grey fur), fur (the rest)."""
    iris, _, _ = rd.masks(rgb, a)
    solid = a > 0.35
    iris = ndimage.binary_dilation(iris, iterations=1) & solid
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # props keep their colours: the treat pouch / toy in the motions is golden (r-g 0.16, g-b 0.23 measured), grey fur is never that
    # yellow. (recolor_dark's cream test is for the album's paler pouch - on the video frames it also took pale ear insides.)
    gold = solid & (g - b > 0.12) & (r - b > 0.22) & ~iris
    cream = np.zeros_like(gold)
    lab, n = ndimage.label(gold)
    if n:
        sizes = ndimage.sum(gold, lab, range(1, n + 1))
        gold = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= max(6, h * h * 0.0008)])
        tinted = (r - b > 0.10) & (g - b > 0.04)                 # the pouch's shaded rim, not the white fur next to it
        cream = (ndimage.binary_fill_holes(gold) | (ndimage.binary_dilation(gold, iterations=2) & tinted)) & solid & ~iris
    pink = solid & (r - g > 0.07) & (r - b > 0.04) & ~iris & ~cream
    lab, n = ndimage.label(pink)
    if n:
        sizes = ndimage.sum(pink, lab, range(1, n + 1))
        pink = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= max(4, h * h * 0.0006)])
    return iris, cream, pink

def _pidx(rgb):
    sat = np.clip(np.round((rgb[..., 0] - rgb[..., 1]) / 0.5 * (PS - 1)), 0, PS - 1).astype(np.int32)
    lum = np.clip(np.round((rgb @ LW) * (PL - 1)), 0, PL - 1).astype(np.int32)
    return sat, lum

def _fill(S, N, sigma, axes):
    S = ndimage.gaussian_filter(S, [sigma if i in axes else 0 for i in range(S.ndim)])
    N = ndimage.gaussian_filter(N, [sigma if i in axes else 0 for i in range(N.ndim)])
    lut = S / np.maximum(N, 1e-6)[..., None]
    have = N > 0.5
    _, near = ndimage.distance_transform_edt(~have, return_indices=True)   # never seen: the nearest seen cell
    return lut[tuple(near)]

def learn():
    """Fur: distance band x white luminance -> mean black RGB. Pink: redness x luminance -> mean black RGB. Both smoothed."""
    files = json.loads((DARK / "checks.json").read_text(encoding="utf-8"))["files"]
    FS = np.zeros((4, LB, 3)); FN = np.zeros((4, LB))
    PSum = np.zeros((PS, PL, 3)); PN = np.zeros((PS, PL))
    for e in files:
        if "lock/keys" in e["rel"]: continue                  # opaque green canvas
        lr, la = _small(e["source"]); dr, da = _small(e["output"])
        if lr.shape != dr.shape: continue
        h = (la > 0.5).any(axis=1).sum()
        iris, cream, pink = classes(lr, la, h)
        solid = la > 0.9
        m = solid & ~iris & ~cream & ~pink
        band = _band(la, h); lb = _lbin(lr)
        np.add.at(FS, (band[m], lb[m]), dr[m]); np.add.at(FN, (band[m], lb[m]), 1)
        m = solid & pink
        ps, pl = _pidx(lr)
        np.add.at(PSum, (ps[m], pl[m]), dr[m]); np.add.at(PN, (ps[m], pl[m]), 1)
    return _fill(FS, FN, 2.0, (1,)), _fill(PSum, PN, 1.0, (0, 1))

def apply(lut, rgb, a, h):
    fur, pinkl = lut
    iris, cream, _ = classes(rgb, a, h)
    out = fur[_band(a, h), _lbin(rgb)]
    ps, pl = _pidx(rgb)
    # pink by degree, not yes/no: a pixel near the line flipped between fur and pink from frame to frame (the video's colour noise)
    # and the cat's colour flickered (catfull colour-jump 59 vs 0 before) - blend by redness, softened by a pixel
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    w = np.clip((r - g - 0.05) / 0.07, 0, 1) * np.clip((r - b - 0.02) / 0.05, 0, 1)
    w = ndimage.gaussian_filter(w, 0.8)[..., None]
    out = out * (1 - w) + pinkl[ps, pl] * w
    out[iris] = rgb[iris]; out[cream] = rgb[cream]
    return np.clip(out, 0, 1)

def cells(name, table):
    for l in (CAT / table).read_text(encoding="utf-8").splitlines():
        p = l.split()
        if p and p[0] == name: return p
    return None

def strip_apply(lut, jpg, alpha, out, cell_w=None, cat_h=80):
    rgb, a = rd.strip_straight(jpg, alpha)
    if cell_w:                                   # each cell on its own (distance bands per cell, not across the seam)
        res = np.zeros_like(rgb)
        for x in range(0, rgb.shape[1], cell_w):
            res[:, x:x + cell_w] = apply(lut, rgb[:, x:x + cell_w], a[:, x:x + cell_w], cat_h)
    else:
        res = apply(lut, rgb, a, cat_h)
    rd.save_strip_jpg(out, res, a)
    return rgb, res, a

GAZE = ["up-left", "up", "up-right", "left", "center", "right", "down-left", "down", "down-right"]
MEOW = ["meow-half", "meow-open"]

def _rgba(path):
    return np.asarray(Image.open(path).convert("RGBA")).astype(np.float32) / 255

def _save_rgba(path, rgb, a):
    Image.fromarray((np.dstack([rgb, a[..., None]]) * 255).round().clip(0, 255).astype(np.uint8), "RGBA").save(path, optimize=True)

def _pm_resize(rgb, a, w, h):
    pm = np.dstack([rgb * a[..., None], a]) * 255
    ch = [np.asarray(Image.fromarray(np.ascontiguousarray(pm[..., i])).resize((w, h), Image.LANCZOS)) for i in range(4)]
    al = np.clip(ch[3], 0, 255) / 255
    rgb2 = np.where(al[..., None] > 0, np.stack(ch[:3], 2) / 255 / np.maximum(al, 1e-3)[..., None], 0)
    return np.clip(rgb2, 0, 1), al

def stills():
    """cat_dark_<gaze|meow>.png: the white still's frame (cat_assets.py: feet at the bottom, body centred, height 256) cut from Codex's
    black picture. The alpha is the white still's (Codex kept the white key's alpha) - checked, then copied exactly."""
    src = {n: DARK / "mascot" / "light-gaze" / f"{n}.png" for n in GAZE}
    src.update({n: DARK / "meow" / "light" / f"{n}.png" for n in MEOW})
    white = {n: REPO / "work" / "mascot" / "light-gaze" / f"{n}.png" for n in GAZE}
    def load(p):
        x = np.asarray(Image.open(p).convert("RGBA")).astype(np.float32)
        x[..., 3] = np.clip((x[..., 3] - 6) * 255.0 / (252 - 6), 0, 255)
        return x
    geo = {}
    for n in GAZE:
        x = load(white[n]); m = x[..., 3] > 128
        ys, xs = np.nonzero(m); bottom = ys.max()
        band = m[int(bottom - (bottom - ys.min()) * 0.18):bottom + 1]
        bx = np.nonzero(band.any(axis=0))[0]
        geo[n] = ((bx.min() + bx.max()) / 2, bottom, ys.min(), xs.min(), xs.max())
    up = max(g[1] - g[2] for g in geo.values()) + 4
    half = max(max(g[0] - g[3], g[4] - g[0]) for g in geo.values()) + 4
    W0, H0 = int(np.ceil(half * 2)), int(np.ceil(up)) + 2
    for n in MEOW: geo[n] = geo["center"]
    out = {}
    for n, (cx, bottom, *_ ) in geo.items():
        x = load(src[n]) / 255
        left = int(round(cx - W0 / 2)); tp = int(bottom + 2 - H0)
        canvas = np.zeros((H0, W0, 4), np.float32)
        y0, x0 = max(0, tp), max(0, left)
        y1, x1 = min(x.shape[0], tp + H0), min(x.shape[1], left + W0)
        canvas[y0 - tp:y1 - tp, x0 - left:x1 - left] = x[y0:y1, x0:x1]
        ref = _rgba(CAT / f"cat_light_{n}.png")
        rgb, al = _pm_resize(canvas[..., :3], canvas[..., 3], ref.shape[1], ref.shape[0])
        dif = np.abs(al - ref[..., 3]).max()
        iou = ((al > 0.5) & (ref[..., 3] > 0.5)).sum() / ((al > 0.5) | (ref[..., 3] > 0.5)).sum()   # Codex cleared alpha < 16: faint edge pixels differ a little
        assert dif <= 20 / 255 and iou > 0.995, f"{n}: alpha differs from cat_light_{n}.png (max {dif * 255:.1f}, IoU {iou:.4f})"
        _save_rgba(CAT / f"cat_dark_{n}.png", rgb, ref[..., 3])
        out[n] = rgb
    return out

def album():
    """album_<card>_dark.png (+ -noprop): Codex's black cards, shrunk like prep_album.py (192 px, premultiplied)."""
    n = 0
    for p in sorted((DARK / "album").glob("*.png")):
        if not (CAT / f"album_{p.stem}.png").exists(): continue
        x = _rgba(p)
        rgb, al = _pm_resize(x[..., :3], x[..., 3], 192, 192)
        _save_rgba(CAT / f"album_{p.stem}_dark.png", rgb, al); n += 1
    return n

def _delta(lut, white, a_w, base_white, base_dark, a_base, h):
    """Codex still + T(white frame) - T(white still) where both are solid; T(white frame) where only the frame is."""
    t_f = apply(lut, white, a_w, h); t_b = apply(lut, base_white, a_base, h)
    both = (a_w > 0.5) & (a_base > 0.5)
    out = t_f.copy()
    out[both] = (base_dark + t_f - t_b)[both]
    return np.clip(out, 0, 1)

def _padded(rgb, a, pad):
    return np.pad(rgb, ((0, 0), (pad, pad), (0, 0))), np.pad(a, ((0, 0), (pad, pad)))

def lock_clips(lut, dark_stills):
    """lockclip_<m>_dark.jpg: patches laid on the lock widget's still (wide cell = still + pad each side) at x y of lockclips.txt."""
    lines = (CAT / "lockclips.txt").read_text(encoding="utf-8").splitlines()
    pad = int(next(l.split()[1] for l in lines if l.startswith("pad ")))
    w = _rgba(CAT / "cat_light_center.png")
    wr, wa = _padded(w[..., :3], w[..., 3], pad); dr, _ = _padded(dark_stills["center"], w[..., 3], pad)
    h = int((w[..., 3] > 0.5).any(axis=1).sum())
    for l in lines:
        q = l.split()
        if len(q) < 7 or q[1] != "light": continue
        m, n, x, y, cw, ch = q[0], int(q[2]), int(q[3]), int(q[4]), int(q[5]), int(q[6])
        rgb, a = rd.strip_straight(CAT / f"lockclip_{m}_light.jpg", CAT / f"lockclip_{m}_light_a.png")
        bw, ba, bd = wr[y:y + ch, x:x + cw], wa[y:y + ch, x:x + cw], dr[y:y + ch, x:x + cw]
        res = np.zeros_like(rgb)
        for f in range(n):
            sl = slice(f * cw, (f + 1) * cw)
            res[:, sl] = _delta(lut, rgb[:, sl], a[:, sl], bw, bd, ba, h)
        rd.save_strip_jpg(CAT / f"lockclip_{m}_dark.jpg", res, a)
        Image.open(CAT / f"lockclip_{m}_light_a.png").save(CAT / f"lockclip_{m}_dark_a.png")

def build_all(lut):
    ds = stills(); print("stills", len(ds))
    print("album", album())
    lock_clips(lut, ds); print("lock clips")
    n = 0
    for p in sorted(CAT.glob("catclip_*.jpg")):
        if p.stem.endswith("_dark"): continue
        c = cells(p.stem.split("_", 1)[1], "catclips.txt")
        strip_apply(lut, p, CAT / f"{p.stem}_a.png", CAT / f"{p.stem}_dark.jpg", int(c[2]), int(c[6])); n += 1
    print("taskbar motions", n)

def sample(lut, out):
    picks = [("M04", (0, 40, 80)), ("WL", (3, 11)), ("I2", (30, 60)), ("I3", (20, 60)), ("HP", (14, 30)), ("M05", (50,))]
    tiles = []
    for name, idx in picks:
        c = cells(name, "catclips.txt"); cw = int(c[2])
        rgb, a = rd.strip_straight(CAT / f"catclip_{name}.jpg", CAT / f"catclip_{name}_a.png")
        old, _ = rd.strip_straight(CAT / f"catclip_{name}_dark.jpg", CAT / f"catclip_{name}_a.png")
        for k in idx:
            sl = slice(k * cw, (k + 1) * cw)
            new = apply(lut, rgb[:, sl], a[:, sl], int(c[6]))
            tiles.append([x for x in (rgb[:, sl], old[:, sl], new)] + [a[:, sl]])
    S = 2; W = sum(t[0].shape[1] * S + 6 for t in tiles); H = max(t[0].shape[0] for t in tiles) * S
    sheet = Image.new("RGB", (W, H * 6 + 30), (243, 243, 243))
    for row, bg in ((0, (243, 243, 243)), (1, (28, 28, 28))):
        sheet.paste(bg, (0, row * H * 3 + row * 30, W, (row + 1) * H * 3 + row * 30))
        x = 0
        for t in tiles:
            for j in range(3):
                im = Image.fromarray((np.dstack([t[j], t[3][..., None]]) * 255).round().clip(0, 255).astype(np.uint8), "RGBA")
                im = im.resize((im.width * S, im.height * S), Image.NEAREST)
                bgim = Image.new("RGBA", im.size, bg + (255,)); bgim.alpha_composite(im)
                sheet.paste(bgim.convert("RGB"), (x, row * (H * 3 + 30) + j * H))
            x += t[0].shape[1] * S + 6
    sheet.save(out); print(out)

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "all": build_all(learn())
    elif len(sys.argv) > 2 and sys.argv[1] == "sample": sample(learn(), sys.argv[2])
    else: print(__doc__)
