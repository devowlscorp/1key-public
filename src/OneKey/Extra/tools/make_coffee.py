# -*- coding: utf-8 -*-
"""후원 카드의 커피 잔(2026-10-09 사용자가 준 그림 두 장: 검은 잔·흰 잔, 흰 바탕 JPEG) → 투명 바탕 PNG.
- 검은 잔: 거의 흰 칸(손잡이 안쪽 구멍 포함)은 모두 바탕. 경계는 흰색에서 얼마나 어두운가로 투명도.
- 흰 잔: 잔·접시도 흰색이라 바탕과 가를 수 없다(경계가 들쭉날쭉, 손잡이 구멍이 흰 판으로 남음) → 같은 모양인 검은 잔의 투명도를
  가장 잘 겹치는 자리로 옮겨 쓰고, 색은 흰 잔 그림에서(경계는 흰 바탕과 섞인 만큼 되돌림).
  py -3.12 src/OneKey/Extra/tools/make_coffee.py      art/coffee_*_src.jpg → coffee_dark.png, coffee_light.png (이 폴더는 저장소에 없다)
"""
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage

X = Path(__file__).resolve().parents[1]
dark = np.asarray(Image.open(X / "art" / "coffee_dark_src.jpg").convert("RGB")).astype(np.float32)
light = np.asarray(Image.open(X / "art" / "coffee_light_src.jpg").convert("RGB")).astype(np.float32)

# 검은 잔의 투명도
mn = dark.min(axis=2)
near = mn >= 244
lab, n = ndimage.label(near)
sizes = ndimage.sum(near, lab, range(1, n + 1))
edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
keep = [i + 1 for i in range(n) if (i + 1) in edge or sizes[i] > 60]   # 바탕 = 가장자리에 닿거나 큰 흰 칸(손잡이 구멍). 거품의 작은 반짝임은 남긴다
bg = np.isin(lab, keep)
dist = ndimage.distance_transform_edt(~bg)
soft = np.clip((255 - mn) / 40.0, 0, 1)
alpha = np.where(bg, 0.0, np.where(dist <= 2, soft, 1.0))
alpha = ndimage.gaussian_filter(alpha, 0.6)

def save(rgb, a, name):
    t = np.maximum(a, 0.05)[..., None]
    col = np.clip((rgb - (1 - t) * 255) / t, 0, 255)
    out = np.dstack([col, a * 255]).astype(np.uint8)
    ys, xs = np.nonzero(a > 0.02)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    side = max(y1 - y0, x1 - x0) + 4
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    sq.paste(Image.fromarray(out[y0:y1, x0:x1]), ((side - (x1 - x0)) // 2, (side - (y1 - y0)) // 2))
    sq.save(X / f"coffee_{name}.png", optimize=True)
    print(name, sq.size)

save(dark, alpha, "dark")

# 흰 잔: 검은 잔 투명도를 가장 잘 겹치는 자리로(흰 잔의 "흰색이 아닌" 칸과 겹침이 크고, 흰 칸에 덮이는 것은 적은 이동)
lm = light.min(axis=2) < 246
def score(d):
    m = np.roll(np.roll(alpha > 0.5, d[1], 0), d[0], 1)
    return float((m & lm).sum() - 0.5 * (m & ~lm).sum())
best = max(((dx, dy) for dx in range(-5, 6) for dy in range(-4, 5)), key=score)
la = np.roll(np.roll(alpha, best[1], 0), best[0], 1)
print("light shift", best)
save(light, la, "light")
