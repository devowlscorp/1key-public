# -*- coding: utf-8 -*-
"""Check every frame of a whole cat run (tools/tests/catfull.ps1, 0.5.15-X).

Input: <root>/frames/NNNNN.png (what the app pushed to the screen, straight alpha) + frames.txt (index, ms, window x y w h, work area,
sitting box, sink, what). Each frame is placed at its screen position. Measured per frame:
  below   opaque pixels under the taskbar top line (front paws in front of the taskbar) and how deep
  box     height, width, area and width/height of the cat (alpha > 128)
  fur     mean brightness and colour cast of the fur (opaque, low-saturation pixels)
  diff    change from the previous frame on one canvas (mean |difference| over both cats)
  dt      time from the previous frame
Flags (frame to frame, so a real jump stands out from steady motion):
  paws-flicker   paws under the line, then none, then under again within 8 frames
  size-jump      height or width changes more than 7 % (or area 12 %) from the previous frame
  ratio-jump     width/height changes more than 8 %
  colour-jump    fur brightness changes more than 6 or colour cast more than 4
  pop            diff more than 3x the median of its neighbours and above 6
  freeze         a motion step identical to the previous one
  late           a motion step later than 62.5 + 20 ms after the previous one
Output in <root>: report.md, flagged.png (each flagged frame with its neighbours), run.mp4 (the run at real timing, flagged frames framed in red).
  py -3.12 tools/cat/catfull_check.py <root>
"""
import sys, os, re, subprocess, tempfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

root = Path(sys.argv[1]); fdir = root / "frames"
rows = []
for line in (fdir / "frames.txt").read_text(encoding="utf-8").splitlines():
    p = line.split()
    if len(p) < 13 or not p[0].isdigit(): continue   # 예전 표본 덤프(DumpClipFrame)가 같은 파일에 쓴 줄
    n, ms, x, y, w, h = map(int, p[:6])
    wl, wt, wr, wb = map(int, p[7].split(","))
    what = " ".join(p[12:])
    rows.append(dict(n=n, ms=ms, x=x, y=y, w=w, h=h, line=wb, what=what, kind=p[12], name=p[13] if p[12] == "clip" else "still"))
if not rows: print("no frames"); sys.exit(1)

X0 = min(r["x"] for r in rows) - 10; X1 = max(r["x"] + r["w"] for r in rows) + 10
Y0 = min(r["y"] for r in rows) - 10; Y1 = max(r["y"] + r["h"] for r in rows) + 10
CW, CH = X1 - X0, Y1 - Y0

def place(r):
    im = np.asarray(Image.open(fdir / f"{r['n']:05d}.png").convert("RGBA")).astype(np.float32)
    c = np.zeros((CH, CW, 4), np.float32)
    ox, oy = r["x"] - X0, r["y"] - Y0
    c[oy:oy + r["h"], ox:ox + r["w"]] = im[:CH - oy, :CW - ox]
    return c

prev = None
for r in rows:
    c = place(r)
    a = c[..., 3] / 255.0
    op = a > 0.5
    line_c = r["line"] - Y0
    ys, xs = np.nonzero(op)
    r["below"] = int(op[line_c:].sum()) if line_c < CH else 0
    r["depth"] = int(max(0, ys.max() + 1 - line_c)) if len(ys) else 0
    if len(ys):
        r["H"] = int(ys.max() + 1 - ys.min()); r["W"] = int(xs.max() + 1 - xs.min()); r["A"] = int(op.sum())
    else: r["H"] = r["W"] = r["A"] = 0
    rgb = c[..., :3]
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    fur = (a > 0.9) & (mx - mn < 40) & (mx > 60)
    if fur.sum() > 20:
        m = rgb[fur].mean(axis=0); r["lum"] = float(m.mean()); r["cast"] = float(m[0] - m[2])
    else: r["lum"] = r["cast"] = float("nan")
    bg = np.array([238, 238, 242], np.float32)
    comp = rgb * a[..., None] + bg * (1 - a[..., None])
    if prev is not None:
        pc, pa = prev
        union = (a > 0.05) | (pa > 0.05)
        r["diff"] = float(np.abs(comp - pc)[union].mean()) if union.any() else 0.0
    else: r["diff"] = 0.0
    prev = (comp, a)

flags = []
N = len(rows)
def add(i, kind, note): flags.append((i, kind, note))
for i in range(1, N):
    r, q = rows[i], rows[i - 1]
    if q["H"] and r["H"]:
        dh, dw = abs(r["H"] - q["H"]) / q["H"], abs(r["W"] - q["W"]) / max(1, q["W"])
        da = abs(r["A"] - q["A"]) / max(1, q["A"])
        if dh > 0.07 or dw > 0.07 or da > 0.12: add(i, "size-jump", f"H {q['H']}->{r['H']} W {q['W']}->{r['W']} A {q['A']}->{r['A']}")
        ra, qa = r["W"] / r["H"], q["W"] / q["H"]
        if abs(ra - qa) / qa > 0.08: add(i, "ratio-jump", f"W/H {qa:.2f}->{ra:.2f}")
    if not (np.isnan(r["lum"]) or np.isnan(q["lum"])):
        if abs(r["lum"] - q["lum"]) > 6 or abs(r["cast"] - q["cast"]) > 4: add(i, "colour-jump", f"lum {q['lum']:.1f}->{r['lum']:.1f} cast {q['cast']:.1f}->{r['cast']:.1f}")
    lo, hi = max(1, i - 8), min(N, i + 9)
    neigh = [rows[j]["diff"] for j in range(lo, hi) if j != i]
    med = float(np.median(neigh)) if neigh else 0
    if r["diff"] > max(6.0, 3 * med): add(i, "pop", f"diff {r['diff']:.1f} vs median {med:.1f}")
    if r["kind"] == "clip" and q["kind"] == "clip" and r["name"] == q["name"]:
        if r["diff"] == 0.0: add(i, "freeze", "same picture as the previous step")
        if r["ms"] - q["ms"] > 62.5 + 20: add(i, "late", f"{r['ms'] - q['ms']} ms")
for i in range(1, N - 1):
    if rows[i]["below"] == 0 and rows[i - 1]["below"] > 0:
        for j in range(i + 1, min(N, i + 9)):
            if rows[j]["below"] > 0: add(i, "paws-flicker", f"paws under the line {rows[i-1]['below']} px -> 0 (frame {i}) -> {rows[j]['below']} px (frame {j})"); break

# report
from collections import Counter, OrderedDict
segs = OrderedDict()
for r in rows:
    key = r["name"] if r["kind"] == "clip" else "still"
    segs.setdefault(key, []).append(r)
lines = ["# Whole cat run — frame check", "", f"frames {N}, run {(rows[-1]['ms'] - rows[0]['ms']) / 1000:.1f} s, canvas {CW}x{CH}, taskbar line y {rows[0]['line']}", ""]
lines += ["## Flags", ""] + [f"- {k}: {v}" for k, v in Counter(f[1] for f in flags).most_common()] + [""]
lines += ["## Per part", "", "| part | frames | height min-max | width min-max | fur brightness min-max | paws under line (frames) |", "|---|---|---|---|---|---|"]
for k, rs in segs.items():
    H = [r["H"] for r in rs if r["H"]]; W = [r["W"] for r in rs if r["W"]]; L = [r["lum"] for r in rs if not np.isnan(r["lum"])]
    lines.append(f"| {k} | {len(rs)} | {min(H)}-{max(H)} | {min(W)}-{max(W)} | {min(L):.0f}-{max(L):.0f} | {sum(1 for r in rs if r['below'] > 0)} |")
lines += ["", "## Flagged frames", ""]
for i, kind, note in flags[:200]:
    r = rows[i]
    lines.append(f"- #{i} {kind}: {note} — {r['what']} (prev: {rows[i-1]['what']})")
(root / "report.md").write_text("\n".join(lines), encoding="utf-8")
print("\n".join(lines[:30]))

# flagged contact sheet
fl = sorted(set(f[0] for f in flags))[:60]
if fl:
    S = 2; tiles = []
    for i in fl:
        row = []
        for j in (i - 1, i, i + 1):
            if 0 <= j < N:
                c = place(rows[j]); a = c[..., 3:4] / 255.0
                img = (c[..., :3] * a + np.array([238, 238, 242]) * (1 - a)).astype(np.uint8)
                im = Image.fromarray(img).resize((CW * S, CH * S), Image.NEAREST); d = ImageDraw.Draw(im)
                ly = (rows[j]["line"] - Y0) * S; d.line([(0, ly), (CW * S, ly)], fill=(255, 0, 0))
                d.text((4, 4), f"#{j} {rows[j]['what'][:40]}", fill=(200, 0, 0) if j == i else (0, 0, 0))
                row.append(im)
        tiles.append(row)
    tw, th = CW * S, CH * S
    sheet = Image.new("RGB", (tw * 3, th * len(tiles)), (255, 255, 255))
    for k, row in enumerate(tiles):
        for c, im in enumerate(row): sheet.paste(im, (c * tw, k * th))
    sheet.save(root / "flagged.png")

# video at real timing (60 fps, each frame held until the next one)
flagset = set(f[0] for f in flags)
with tempfile.TemporaryDirectory() as tmp:
    t0 = rows[0]["ms"]; out_n = 0; S = 2
    for i, r in enumerate(rows):
        c = place(r); a = c[..., 3:4] / 255.0
        img = (c[..., :3] * a + np.array([238, 238, 242]) * (1 - a)).astype(np.uint8)
        im = Image.fromarray(img).resize((CW * S, CH * S), Image.NEAREST); d = ImageDraw.Draw(im)
        ly = (r["line"] - Y0) * S; d.line([(0, ly), (CW * S, ly)], fill=(120, 120, 130), width=2)
        d.text((4, 4), f"#{i} {r['what'][:46]}", fill=(0, 0, 0))
        if i in flagset: d.rectangle([0, 0, CW * S - 1, CH * S - 1], outline=(220, 0, 0), width=3)
        end = rows[i + 1]["ms"] if i + 1 < N else r["ms"] + 500
        hold = max(1, round((end - r["ms"]) * 60 / 1000))
        hold = min(hold, 120)
        for _ in range(hold):
            im.save(os.path.join(tmp, f"v{out_n:06d}.png")); out_n += 1
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-framerate", "60", "-i", os.path.join(tmp, "v%06d.png"), "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                    "-c:v", "libx264", "-crf", "18", "-pix_fmt", "yuv420p", str(root / "run.mp4")], check=True)
print("report", root / "report.md")
