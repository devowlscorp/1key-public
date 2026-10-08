# 공개판 1Key 아이콘 2판(2026-10-08 사용자가 보낸 금속 탁상시계 사진 두 장에 더 가깝게):
# 무광 알루미늄 둥근 사각 몸체에 거의 꽉 찬 평평한 시계판(가는 분 눈금), 위에 가는 크롬 고리 + 크롬 용두, 오른쪽 위 작은 검은 점.
# 판 가운데 열쇠는 시계 숫자처럼 흑연색(색 없는 단색 — 사진에 강조색이 없다). 상표·글자는 넣지 않는다.
# 작은 칸(16~32px, 제목 표시줄·작업 표시줄·탐색기)도 같은 모양(고리·용두·판·열쇠)을 굵게 줄인 판을 쓴다(0.5.15-A, 사용자: "윈도우 프로그램
# 아이콘은 변경이 안됐어" — 예전 32px 이하 단순판은 고리·눈금이 없어 예전 아이콘처럼 보였다). 32px 은 12시간 눈금, 24px 은 4개, 20px 이하는 눈금 없이 열쇠를 크게. tone=silver|cream
#   py -3.12 make_icon2.py <out_dir> [silver|cream]
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else "."
tone = sys.argv[2] if len(sys.argv) > 2 else "silver"
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

T = {
    "silver": dict(b0=(246, 246, 247), b1=(200, 201, 205), f0=(240, 240, 241), f1=(214, 215, 218), tick=(70, 72, 78), key=(52, 54, 60)),
    "cream":  dict(b0=(246, 242, 235), b1=(214, 206, 194), f0=(244, 242, 238), f1=(222, 218, 211), tick=(78, 74, 70), key=(56, 54, 52)),
}[tone]

def chrome_band(t):
    # 크롬: 밝은 띠와 어두운 띠가 번갈아(반사)
    return lerp([95, 97, 104], [255, 255, 255], 0.5 + 0.5 * np.cos(t * np.pi * 2.2))

def render(N, mode):
    simple = mode != "big"
    tiny = mode == "small16"
    s = N / 1024.0
    Y, X = np.mgrid[0:N, 0:N].astype(float) + 0.5
    aa = 1.2 * max(1.0, s)
    img = np.zeros((N, N, 4))
    if mode == "small32": bx, by, bh, br = 512 * s, 612 * s, 398 * s, 120 * s
    elif simple: bx, by, bh, br = 512 * s, 594 * s, 420 * s, 120 * s   # 24px 이하: 몸체를 키우고 고리를 작게
    else: bx, by, bh, br = 512 * s, 640 * s, 350 * s, 118 * s
    body_d = sdf_rrect(X, Y, bx, by, bh, bh, br)
    sh = blur(cov(sdf_rrect(X, Y, bx, by + 22 * s, bh - 8 * s, bh - 8 * s, br), aa), 22 * s)
    over(img, [28, 28, 34], sh * (0.30 if simple else 0.40))
    # 고리: 가는 크롬 원(시계를 거는 고리 = 열쇠고리). 작은 칸은 굵게(한 칸 이상 남도록)
    if mode == "small32": rx, ry, rR, rt = 512 * s, 150 * s, 118 * s, 34 * s
    elif simple: rx, ry, rR, rt = 512 * s, 142 * s, 92 * s, (46 if tiny else 40) * s
    else: rx, ry, rR, rt = 512 * s, 150 * s, 112 * s, 13 * s
    rd = np.hypot(X - rx, Y - ry)
    ring = cov(np.abs(rd - rR) - rt, aa)
    ang = np.arctan2(Y - ry, X - rx)
    rc = chrome_band((ang + np.pi) / (2 * np.pi) * 2 + (rd - rR) / (rt * 4))
    rsh = blur(ring, 5 * s)
    over(img, [20, 20, 26], np.roll(rsh, int(9 * s), axis=0) * 0.22)
    if simple: rc = rc * 0.72   # 작은 칸: 밝은 바탕에서도 고리가 보이게 조금 어둡게
    over(img, rc, ring)
    # 용두: 넓은 받침 · 목 · 둥근 갓(가로 크롬 띠)
    crown = ((278 * s, 58 * s, 16 * s, 8 * s), (246 * s, 26 * s, 22 * s, 6 * s), (214 * s, 64 * s, 15 * s, 15 * s)) if not simple \
        else ((222 * s, 96 * s, 30 * s, 14 * s),) if mode == "small32" else ()
    for (cy0, hw, hh, rr) in crown:
        d = sdf_rrect(X, Y, 512 * s, cy0, hw, hh, rr)
        c = cov(d, aa)
        over(img, chrome_band((X - (512 * s - hw)) / (2 * hw) * 1.2 + 0.15), c)
        over(img, [30, 30, 36], c * np.clip((Y - (cy0 + hh * 0.3)) / (hh * 1.4), 0, 1) * 0.35)
    # 몸체: 무광 알루미늄 152° + 가로 결 + 가장자리 빛·그늘
    body = cov(body_d, aa)
    u = ((X - bx) * np.sin(np.radians(152)) - (Y - by) * np.cos(np.radians(152))) / (2 * bh) + 0.5
    base = lerp(T["b0"], T["b1"], u)
    if not simple:
        g = np.random.default_rng(3).normal(0, 1, (N, N))
        g = np.repeat(blur(g, 0.7).mean(axis=1, keepdims=True), N, axis=1) * 0.7 + blur(g, 0.7) * 0.3
        base = base + g[..., None] * 1.8
    hl = np.clip(1 - (-body_d) / (5 * s), 0, 1) * np.clip(((bx - X) + (by - Y)) / (bh * 1.3) + 0.45, 0, 1)
    dk = np.clip(1 - (-body_d) / (16 * s), 0, 1) * np.clip(((X - bx) + (Y - by)) / (bh * 1.3) + 0.2, 0, 1)
    base = base * (1 - dk[..., None] * 0.20) + 255 * hl[..., None] * 0.5
    over(img, np.clip(base, 0, 255), body)
    # 시계판: 몸체를 거의 채운 평평한 원, 가장자리에 아주 얕은 턱
    dR = bh * (0.90 if simple else 0.89)
    dd = np.hypot(X - bx, Y - by) - dR
    lip = cov(np.abs(dd) - 4 * s * (1.6 if simple else 1), aa)
    over(img, lerp([130, 131, 136], [252, 252, 252], (Y - (by - dR)) / (2 * dR)), lip * body * 0.9)
    dial = cov(dd + 4 * s, aa)
    rr_ = np.hypot((X - (bx - 0.3 * dR)) / (2.0 * dR), (Y - (by - 0.4 * dR)) / (1.7 * dR))
    face = lerp(T["f0"], T["f1"], rr_)
    ins = np.clip(1 - (-(dd + 4 * s)) / (18 * s), 0, 1) ** 2 * np.clip((by - Y) / dR * 0.8 + 0.5, 0, 1)
    face = face * (1 - ins[..., None] * 0.18)
    over(img, face, dial)
    px, py = X - bx, Y - by
    ticks = 60 if not simple else 12 if mode == "small32" else 4 if mode == "small24" else 0
    for i in range(ticks):
        th = i * 2 * np.pi / ticks - np.pi / 2
        long = not simple and i % 5 == 0
        if simple: r0, r1, wd = dR - (78 if tiny else 70) * s, dR - 16 * s, (46 if tiny else 34) * s
        else: r0, r1, wd = (dR - 50 * s, dR - 18 * s, 6 * s) if long else (dR - 34 * s, dR - 18 * s, 3.4 * s)
        ux, uy = np.cos(th), np.sin(th)
        al = px * ux + py * uy; ac = np.abs(-px * uy + py * ux)
        d = np.maximum(np.maximum(r0 - al, al - r1), ac - wd / 2)
        over(img, T["tick"], cov(d, aa) * dial * (0.95 if long or simple else 0.75))
    if not simple:
        # 오른쪽 위 작은 검은 점(사진의 센서 점)
        ddot = np.hypot(X - (bx + 0.83 * bh), Y - (by - 0.83 * bh)) - 15 * s
        over(img, [24, 24, 28], cov(ddot, aa))
        over(img, [120, 120, 130], cov(np.hypot(X - (bx + 0.83 * bh - 5 * s), Y - (by - 0.83 * bh - 5 * s)) - 4 * s, aa) * 0.6)
    # 열쇠(흑연색): 고리 + 축 + 이빨 둘, 판 가운데
    k = 0.95 if not simple else 1.12 if mode == "small32" else 1.3 if mode == "small24" else 1.45
    kx, ky = bx - 102 * s * k, by
    bowR, bowT = 62 * s * k, 25 * s * k
    bow = np.abs(np.hypot(X - kx, Y - ky) - bowR) - bowT
    shaft = sdf_rrect(X, Y, kx + bowR + 112 * s * k, ky, 118 * s * k, 20 * s * k, 20 * s * k)
    t1 = sdf_rrect(X, Y, kx + bowR + 150 * s * k, ky + 44 * s * k, 17 * s * k, 34 * s * k, 11 * s * k)
    t2 = sdf_rrect(X, Y, kx + bowR + 212 * s * k, ky + 38 * s * k, 17 * s * k, 28 * s * k, 11 * s * k)
    kd = np.minimum.reduce([bow, shaft, t1, t2])
    kc = cov(kd, aa)
    over(img, [10, 10, 14], np.roll(blur(kc, 4 * s * k), int(4 * s), axis=0) * 0.18 * dial)
    over(img, lerp([96, 98, 106], T["key"], (Y - (ky - bowR - bowT)) / (2 * (bowR + bowT))), kc)
    o = np.zeros((N, N, 4), np.uint8)
    al = np.maximum(img[..., 3:4], 1e-6)
    o[..., :3] = np.clip(img[..., :3] / al, 0, 255).astype(np.uint8)
    o[..., 3] = np.clip(img[..., 3] * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(o, "RGBA")

big = render(1024, "big"); big.save(os.path.join(out, "icon-1024.png"))
small = {m: render(512, m) for m in ("small32", "small24", "small16")}
for m, im in small.items(): im.save(os.path.join(out, f"icon-{m}-512.png"))
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
def pick(z): return big if z > 32 else small["small32" if z == 32 else "small24" if z == 24 else "small16"]
frames = [pick(z).resize((z, z), Image.LANCZOS) for z in sizes]
for z, f in zip(sizes, frames): f.save(os.path.join(out, f"icon-{z}.png"))
frames[-1].save(os.path.join(out, "app.ico"), sizes=[(z, z) for z in sizes], append_images=frames[:-1])
print("ok", tone)
