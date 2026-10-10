# -*- coding: utf-8 -*-
"""Taskbar cat hunt strips (public edition, 2026-10-10 — the visiting toy: the cat crouches, wiggles and pounces).

Input: the hunt frame folders HC HW HP HJ HR (640x480 RGBA, built on the walk chain's scale and place) and the walk chain folder
(its first frame, the front sit, is the shared reference — the same one the walk strips WT/WL/WI use, so the app can switch between
walk and hunt strips in place). Writes catclip_H*.jpg / _a.png like make_clips.py and replaces the H lines in catclips.txt (x:1), with
k:a,b,... = the frames where each key pose lands (hunt.json "bounds" — the app times the leap's travel and height and the catch by them).
  py -3.12 tools/cat/make_hunt.py <hunt frames dir> <chain dir>
"""
import sys, json
from pathlib import Path
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_clips as mc

NAMES = ["HC", "HW", "HP", "HJ", "HR"]

if __name__ == "__main__":
    hunt, chain = Path(sys.argv[1]), Path(sys.argv[2])
    ref = mc.bbox(np.asarray(Image.open(chain / "chain" / "0000.png").convert("RGBA")))
    bounds = json.loads((hunt / "hunt.json").read_text(encoding="utf-8")).get("bounds", {})
    lines = [mc.strip(n, mc.load(hunt / n), ref, " x:1" + (" k:" + ",".join(map(str, bounds[n])) if n in bounds else ""))
             for n in NAMES if (hunt / n).exists()]
    txt = mc.OUT / "catclips.txt"
    keep = [l for l in txt.read_text(encoding="utf-8").splitlines() if l and l.split()[0] not in NAMES]
    txt.write_text("\n".join(keep + lines) + "\n", encoding="utf-8")
    print("hunt total", sum((mc.OUT / f"catclip_{n}{s}").stat().st_size for n in NAMES for s in (".jpg", "_a.png")
                            if (mc.OUT / f"catclip_{n}{s}").exists()) // 1024, "KB")
