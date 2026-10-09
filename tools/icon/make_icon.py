# 공개판 1Key 아이콘 3판(2026-10-09 사용자: "앱 아이콘과 파비콘이 너무 실제 제품하고 비슷" — 2판은 사진 속 탁상시계의 고리·용두·검은 점·눈금판을
# 그대로 따랐다 → 시안 A·B·C 중 A): 알루미늄 키보드 키(키캡) 위에 흑연색 열쇠. 1Key = 단축키 한 번으로 입력.
# 윗면(손가락이 닿는 면)을 살짝 위로 올려 앞쪽 옆면이 아래에 더 보이게 한다(비스듬히 위에서 본 키 — 가운데 두면 액자처럼 보인다, 사용자 확인).
# 색 없는 단색(새 디자인의 금속·흑연), 상표·글자 없음. 작은 칸(16~32px, 제목 표시줄·작업 표시줄·탐색기)은 결을 빼고 열쇠를 키운 판.
#   py -3.12 tools/icon/make_icon.py <out_dir>
#   out_dir 에 icon-1024.png, icon-<size>.png, app.ico(16~256), favicon.ico(16·32·48), icon-readme.png(256)를 쓴다
#   → app.ico 는 src/OneKey/app.ico, favicon.ico·icon-readme.png 는 docs/images/favicon.ico·icon.png 로 복사
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(out, exist_ok=True)

def sdf_rrect(X, Y, cx, cy, hw, hh, r):
    qx = np.abs(X - cx) - (hw - r); qy = np.abs(Y - cy) - (hh - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r

def cov(d, aa): return np.clip(0.5 - d / aa, 0, 1)

def blur(a, s):
    if s <= 0.3: return a
    r = int(3 * s); k = np.exp(-np.arange(-r, r + 1) ** 2 / (2 * s * s)); k /= k.sum()
    a = np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 1, a)

def over(dst, rgb, a):
    a3 = a[..., None]
    dst[..., :3] = np.asarray(rgb, float) * a3 + dst[..., :3] * (1 - a3)
    dst[..., 3] = a + dst[..., 3] * (1 - a)

def lerp(c0, c1, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.array(c0, float) * (1 - t) + np.array(c1, float) * t

def key_sdf(X, Y, cx, cy, s, k):
    """열쇠: 고리 + 축 + 이빨 둘(2판과 같은 모양)."""
    kx = cx - 102 * s * k
    bowR, bowT = 62 * s * k, 25 * s * k
    bow = np.abs(np.hypot(X - kx, Y - cy) - bowR) - bowT
    shaft = sdf_rrect(X, Y, kx + bowR + 112 * s * k, cy, 118 * s * k, 20 * s * k, 20 * s * k)
    t1 = sdf_rrect(X, Y, kx + bowR + 150 * s * k, cy + 44 * s * k, 17 * s * k, 34 * s * k, 11 * s * k)
    t2 = sdf_rrect(X, Y, kx + bowR + 212 * s * k, cy + 38 * s * k, 17 * s * k, 28 * s * k, 11 * s * k)
    return np.minimum.reduce([bow, shaft, t1, t2])

def render(N, mode):
    """mode: big(40px 이상) / small32 / small24 / small16 — 작을수록 옆면을 얇게, 열쇠를 크게, 결·접시 그늘 없이."""
    small = mode != "big"
    s = N / 1024.0
    Y, X = np.mgrid[0:N, 0:N].astype(float) + 0.5
    aa = 1.2 * max(1.0, s)
    img = np.zeros((N, N, 4))
    c = 512 * s
    hw = (430 if not small else 452) * s            # 키 바깥(옆면 포함) 반폭
    r = (150 if not small else 170) * s
    rim = {"big": 78, "small32": 64, "small24": 58, "small16": 54}[mode] * s   # 옆면 두께
    lift = {"big": 34, "small32": 30, "small24": 28, "small16": 26}[mode] * s  # 윗면을 위로(앞 옆면이 보이게)
    # 그림자
    over(img, [28, 28, 34], blur(cov(sdf_rrect(X, Y, c, c + 30 * s, hw - 10 * s, hw - 10 * s, r), aa), 26 * s) * (0.38 if not small else 0.28))
    # 옆면: 위 밝고 아래(앞) 어둡게
    d0 = sdf_rrect(X, Y, c, c, hw, hw, r)
    skirt = cov(d0, aa)
    u = (Y - (c - hw)) / (2 * hw)
    over(img, lerp([214, 215, 219], [150, 151, 157] if not small else [140, 141, 147], u), skirt)
    # 윗면: 무광 알루미늄, 왼쪽 위 밝게, 가운데 살짝 오목
    tw = hw - rim; tc = c - lift
    d1 = sdf_rrect(X, Y, c, tc, tw, tw - 6 * s, r - 50 * s)
    top = cov(d1, aa)
    base = lerp([250, 250, 251], [212, 213, 217], np.hypot(X - (c - 0.4 * tw), Y - (tc - 0.5 * tw)) / (2.2 * tw))
    if not small:
        g = np.random.default_rng(3).normal(0, 1, (N, N))
        g = np.repeat(blur(g, 0.7).mean(axis=1, keepdims=True), N, axis=1) * 0.7 + blur(g, 0.7) * 0.3
        base = base + g[..., None] * 1.8
        dish = np.clip(1 - np.hypot(X - c, Y - tc) / (tw * 1.05), 0, 1) ** 1.5
        base = base * (1 - dish[..., None] * 0.07)
    edge = np.clip(1 - (-d1) / (6 * s), 0, 1)
    base = base + 255 * edge[..., None] * 0.35 * np.clip((tc - Y) / tw + 0.6, 0, 1)[..., None]
    over(img, np.clip(base, 0, 255), top)
    # 열쇠(흑연색) — 윗면 가운데, 아래에 얇은 빛(눌러 새긴 느낌)
    k = {"big": 1.0, "small32": 1.32, "small24": 1.48, "small16": 1.62}[mode]
    kd = key_sdf(X, Y, c + 6 * s, tc, s, k)
    kc = cov(kd, aa)
    if not small: over(img, [255, 255, 255], np.roll(kc, int(3 * s), axis=0) * 0.6 * top)
    over(img, lerp([96, 98, 106], [48, 50, 56], (Y - (tc - 90 * s * k)) / (180 * s * k)) if not small else [52, 54, 60], kc)
    o = np.zeros((N, N, 4), np.uint8)
    al = np.maximum(img[..., 3:4], 1e-6)
    o[..., :3] = np.clip(img[..., :3] / al, 0, 255).astype(np.uint8)
    o[..., 3] = np.clip(img[..., 3] * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(o, "RGBA")

big = render(1024, "big"); big.save(os.path.join(out, "icon-1024.png"))
small = {m: render(512, m) for m in ("small32", "small24", "small16")}
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
def pick(z): return big if z > 32 else small["small32" if z == 32 else "small24" if z == 24 else "small16"]
frames = {z: pick(z).resize((z, z), Image.LANCZOS) for z in sizes}
for z, f in frames.items(): f.save(os.path.join(out, f"icon-{z}.png"))
frames[256].save(os.path.join(out, "app.ico"), sizes=[(z, z) for z in sizes], append_images=[frames[z] for z in sizes if z != 256])
frames[48].save(os.path.join(out, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48)], append_images=[frames[16], frames[32]])
frames[256].save(os.path.join(out, "icon-readme.png"))
print("ok")
