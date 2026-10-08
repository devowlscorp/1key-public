# -*- coding: utf-8 -*-
"""Taskbar cat motion strips for the app (public edition, 2026-10-09).

Input: the checked frames of each motion (one folder per motion, NNN.png, 480x480 RGBA, sitting pose S01 with its feet on y=449 at
the start and the end of every rest motion) and, for walking, the joined chain frames (the turn, three steps, the turn back).
Output in src/OneKey/Assets/cat/:
  catclip_<name>.jpg   one strip per motion: every frame side by side, cropped to the motion's union box, scaled so the sitting
                       cat is SIT_H px tall; colour PREMULTIPLIED by alpha (black where transparent), JPEG q88
  catclip_<name>_a.png the strip's alpha as an 8-bit grey PNG (the app joins the two into one premultiplied strip — the in-house
                       0.3.121 way: an RGBA PNG strip was 3-4x bigger)
  catclips.txt         one line per motion: name frames cellW cellH anchorX feetY sitH [x:1 = not a rest motion (walk parts)]
                       anchorX = the sitting cat's centre in the cell, feetY = its feet line from the cell top
  py -3.12 tools/cat/make_clips.py <final frames dir> <chain dir>
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "src" / "OneKey" / "Assets" / "cat"
SIT_H = 80            # sitting cat height in the strip (52 logical px at 100 % in the app; 80 ~ 150 %)
REST = ["M04", "M08", "M05", "M17", "M07", "M12"]   # rest motions (start and end sitting, front)

def load(d): return [np.asarray(Image.open(p).convert("RGBA")) for p in sorted(Path(d).glob("*.png"))]

def bbox(f, thr=24):
    ys, xs = np.nonzero(f[..., 3] > thr)
    return (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1) if len(xs) else None

def pm_resize(f, w, h):
    a = f.astype(np.float32); a[..., :3] *= a[..., 3:4] / 255.0
    ch = [np.asarray(Image.fromarray(np.ascontiguousarray(a[..., i])).resize((w, h), Image.LANCZOS)) for i in range(4)]
    al = np.clip(ch[3], 0, 255)
    rgb = np.stack(ch[:3], 2) * 255.0 / np.maximum(al, 1)[..., None]
    out = np.dstack([np.clip(rgb, 0, 255), al]).astype(np.uint8)
    out[out[..., 3] < 8] = 0
    return out

def strip(name, frames, sit_box, extra=""):
    x0 = min(b[0] for b in map(bbox, frames) if b); y0 = min(b[1] for b in map(bbox, frames) if b)
    x1 = max(b[2] for b in map(bbox, frames) if b); y1 = max(b[3] for b in map(bbox, frames) if b)
    k = SIT_H / (sit_box[3] - sit_box[1])
    cw, chh = int(np.ceil((x1 - x0) * k)) + 2, int(np.ceil((y1 - y0) * k)) + 2
    W = cw * len(frames)
    sheet = np.zeros((chh, W, 4), np.uint8)
    for i, f in enumerate(frames):
        crop = f[y0:y1, x0:x1]
        s = pm_resize(crop, max(1, round((x1 - x0) * k)), max(1, round((y1 - y0) * k)))
        sheet[1:1 + s.shape[0], i * cw + 1:i * cw + 1 + s.shape[1]] = s
    a = sheet[..., 3:4].astype(np.float32)
    pre = (sheet[..., :3].astype(np.float32) * a / 255.0).round().astype(np.uint8)
    Image.fromarray(pre, "RGB").save(OUT / f"catclip_{name}.jpg", quality=88, subsampling=0, optimize=True)
    Image.fromarray(sheet[..., 3], "L").save(OUT / f"catclip_{name}_a.png", optimize=True)
    ax = round(((sit_box[0] + sit_box[2]) / 2 - x0) * k) + 1
    fy = round((sit_box[3] - y0) * k) + 1
    line = f"{name} {len(frames)} {cw} {chh} {ax} {fy} {SIT_H}{extra}"
    print(line, f"{((OUT / f'catclip_{name}.jpg').stat().st_size + (OUT / f'catclip_{name}_a.png').stat().st_size) // 1024} KB")
    return line

if __name__ == "__main__":
    final, chain = Path(sys.argv[1]), Path(sys.argv[2])
    lines = []
    sit = None
    for m in REST:
        fr = load(final / m)
        sit = sit or bbox(fr[0])
        lines.append(strip(m, fr, bbox(fr[0])))
    # walk (0.5.15-D, user: walk around like the in-house edition): from the joined chain, two strips that share the front-sit reference
    # (same scale and anchor, so the app can switch between them in place):
    #   WT = turn out: M02 (front sit -> stand -> side) + the RIFE seam into the walk (the app plays it backwards to turn back)
    #   WL = one walk cycle of M03 (16 frames, cyclic: frame 16 == frame 0) - the app repeats it and moves the window
    meta = json.loads((chain / "chain.json").read_text(encoding="utf-8"))["frames"]
    labs = [lab for lab, _ in meta]
    m03 = labs.index("M03")
    turn = list(range(0, m03))                       # M02 + seam
    cycle = list(range(m03, m03 + 16))
    load_c = lambda ids: [np.asarray(Image.open(chain / "chain" / f"{i:04d}.png").convert("RGBA")) for i in ids]
    ref = bbox(load_c([0])[0])                       # front sit of the chain
    lines.append(strip("WT", load_c(turn), ref, " x:1"))
    lines.append(strip("WL", load_c(cycle), ref, " x:1"))
    (OUT / "catclips.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    total = sum(p.stat().st_size for p in OUT.glob("catclip_*"))
    print("total", total // 1024, "KB")
