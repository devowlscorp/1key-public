# 공개판 1Key 아이콘(2026-10-08 사용자: 모티브가 된 금속 탁상시계로 아이콘 다시 그리기).
# 무광 알루미늄 둥근 사각 몸체 + 파인 시계판(분 눈금) + 위의 고리·용두(= 열쇠고리), 판 가운데에 남색 열쇠. 상표·글자는 넣지 않는다.
# 큰 크기(1024)에서 픽셀마다 계산해 그린 뒤 줄인다. 24px 이하는 눈금을 빼고 열쇠를 굵게 한 단순판.
#   py -3.12 make_icon.py <out_dir>
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(out, exist_ok=True)

def sdf_rrect(X, Y, cx, cy, hw, hh, r):
    qx = np.abs(X - cx) - (hw - r); qy = np.abs(Y - cy) - (hh - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r

def cov(d, aa):  # 부호 거리 → 덮임(가장자리 부드럽게)
    return np.clip(0.5 - d / aa, 0, 1)

def blur(a, s):
    if s <= 0: return a
    r = int(3 * s); k = np.exp(-np.arange(-r, r + 1) ** 2 / (2 * s * s)); k /= k.sum()
    a = np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 1, a)

def over(dst, rgb, a):
    # 미리 곱한 색으로 쌓는다(투명 바탕에 얹어도 가장자리가 검게 물들지 않게)
    a3 = a[..., None]
    dst[..., :3] = np.asarray(rgb, float) * a3 + dst[..., :3] * (1 - a3)
    dst[..., 3] = a + dst[..., 3] * (1 - a)

def over_img(dst, rgb_img, a):
    over(dst, rgb_img, a)

def lerp(c0, c1, t):
    t = np.clip(t, 0, 1)[..., None]
    return np.array(c0, float) * (1 - t) + np.array(c1, float) * t

def render(N, simple):
    s = N / 1024.0
    Y, X = np.mgrid[0:N, 0:N].astype(float) + 0.5
    aa = 1.2 * max(1.0, s)
    img = np.zeros((N, N, 4))          # 곧은 알파 RGBA(0..1, 색은 0..255)
    # ---- 몸체 자리
    # 단순판(32px 이하): 고리·용두 없이 몸체가 칸을 채운다
    bx, by, bw, bh, br = (512 * s, 512 * s, 470 * s, 470 * s, 190 * s) if simple else (512 * s, 600 * s, 372 * s, 372 * s, 150 * s)
    body_d = sdf_rrect(X, Y, bx, by, bw, bh, br)
    # 바닥 그늘(아래로 부드럽게)
    sh = blur(cov(sdf_rrect(X, Y, bx, by + 26 * s, bw - 6 * s, bh - 10 * s, br), aa), 26 * s)
    over(img, np.array([30, 30, 36]), sh * 0.42)
    # ---- 고리(열쇠고리)와 용두: 몸체 위
    rx, ry, rR, rt = 512 * s, 128 * s, 98 * s, 22 * s
    if simple: rR = 0
    ring_d = (np.zeros_like(X) + 1e9) if simple else np.abs(np.hypot((X - rx) / 1.0, (Y - ry) / 0.92) - rR) - rt
    ring = cov(ring_d, aa)
    # 크롬: 위쪽 밝고 아래 어두운 줄무늬(반사)
    ang = np.arctan2(Y - ry, X - rx)
    chrome = lerp([250, 250, 252], [120, 122, 128], (np.sin(ang) + 1) / 2 * 0.85)
    edge = np.clip(1 - np.abs(np.hypot(X - rx, (Y - ry) / 0.92) - rR) / rt, 0, 1)
    chrome = chrome * (0.75 + 0.25 * edge[..., None])
    rsh = blur(ring, 6 * s); over(img, np.array([20, 20, 26]), np.roll(rsh, int(8 * s), axis=0) * 0.25)
    over_img(img, chrome, ring)
    # 용두: 둥근 원기둥 두 단
    for (cy0, hw, hh, rr) in (() if simple else ((236 * s, 46 * s, 20 * s, 10 * s), (196 * s, 30 * s, 22 * s, 8 * s))):
        d = sdf_rrect(X, Y, 512 * s, cy0, hw, hh, rr)
        c = cov(d, aa)
        t = (X - (512 * s - hw)) / (2 * hw)
        col = lerp([150, 152, 158], [252, 252, 253], 1 - np.abs(t - 0.38) * 1.9)
        over_img(img, col, c)
    # ---- 몸체: 무광 알루미늄(152° 그라데이션 + 가는 가로 결 + 가장자리 빛·그늘)
    body = cov(body_d, aa)
    u = ((X - bx) * np.sin(np.radians(152)) - (Y - by) * np.cos(np.radians(152))) / (2 * bw) + 0.5
    base = lerp([250, 250, 250], [206, 207, 210], u)
    rng = np.random.default_rng(7)
    grain = blur(rng.normal(0, 1, (N, N)), 0.6 * max(1, s)) if not simple else np.zeros((N, N))
    grain = np.repeat(grain.mean(axis=1, keepdims=True), N, axis=1) * 0.6 + grain * 0.4   # 가로로 흐른 결
    base = base + grain[..., None] * 2.2
    # 안쪽 빛(왼쪽 위 가장자리)·안쪽 그늘(오른쪽 아래)
    inner = np.clip(-body_d / (10 * s), 0, 1)
    hl = np.clip(1 - (-body_d) / (6 * s), 0, 1) * np.clip(((bx - X) + (by - Y)) / (bw * 1.2) + 0.4, 0, 1)
    dk = np.clip(1 - (-body_d) / (14 * s), 0, 1) * np.clip(((X - bx) + (Y - by)) / (bw * 1.2) + 0.2, 0, 1)
    base = base * (1 - dk[..., None] * 0.22) + 255 * hl[..., None] * 0.55
    over_img(img, np.clip(base, 0, 255), body)
    # ---- 파인 시계판
    dx, dy, dR = (512 * s, 512 * s, 390 * s) if simple else (512 * s, 600 * s, 300 * s)
    dial_d = np.hypot(X - dx, Y - dy) - dR
    # 판 둘레의 얕은 턱(아래쪽 빛, 위쪽 그늘)
    lip = cov(np.abs(dial_d + 2 * s) - 7 * s, aa)
    lipcol = lerp([150, 151, 156], [250, 250, 251], (Y - (dy - dR)) / (2 * dR))
    over_img(img, lipcol, lip * body)
    dial = cov(dial_d + 6 * s, aa)
    rr_ = np.hypot((X - (dx - 0.24 * dR)) / (1.9 * dR), (Y - (dy - 0.45 * dR)) / (1.5 * dR))
    face = lerp([246, 246, 247], [205, 206, 209], rr_)
    # 안쪽 그늘: 위 가장자리에서 아래로
    ins = np.clip(1 - (-(dial_d + 6 * s)) / (34 * s), 0, 1) ** 2 * np.clip(((dy - Y) / dR) * 0.8 + 0.5, 0, 1)
    face = face * (1 - ins[..., None] * 0.35)
    over_img(img, face, dial)
    if not simple:
        # 분 눈금 60개(5분마다 길고 굵게)
        a = np.arctan2(Y - dy, X - dx); rad = np.hypot(X - dx, Y - dy)
        for i in range(60):
            th = i * np.pi / 30 - np.pi / 2
            long = i % 5 == 0
            r0, r1, wdt = (dR - 58 * s, dR - 22 * s, 7.5 * s) if long else (dR - 40 * s, dR - 22 * s, 4 * s)
            ux, uy = np.cos(th), np.sin(th)
            px, py = X - dx, Y - dy
            along = px * ux + py * uy; across = np.abs(-px * uy + py * ux)
            d = np.maximum(np.maximum(r0 - along, along - r1), across - wdt / 2)
            over(img, np.array([92, 94, 100]) if long else np.array([128, 130, 136]), cov(d, aa) * dial * (0.95 if long else 0.8))
    # ---- 열쇠(남색): 왼쪽 고리 + 오른쪽 축 + 이빨 둘. 판 가운데
    navy = np.array([47, 62, 143])
    k = 1.5 if simple else 1.0
    kx, ky = dx - 102 * s * k, dy   # 고리 왼끝 ~ 축 오른끝의 가운데가 판 가운데
    bowR, bowT = 62 * s * k, 26 * s * k
    bow = np.abs(np.hypot(X - kx, Y - ky) - bowR) - bowT
    shaft = sdf_rrect(X, Y, kx + bowR + 112 * s * k, ky, 118 * s * k, 21 * s * k, 21 * s * k)
    t1 = sdf_rrect(X, Y, kx + bowR + 150 * s * k, ky + 46 * s * k, 17 * s * k, 36 * s * k, 12 * s * k)
    t2 = sdf_rrect(X, Y, kx + bowR + 212 * s * k, ky + 40 * s * k, 17 * s * k, 30 * s * k, 12 * s * k)
    key_d = np.minimum.reduce([bow, shaft, t1, t2])
    keyc = cov(key_d, aa)
    ksh = blur(keyc, 5 * s * k)
    over(img, np.array([10, 12, 30]), np.roll(np.roll(ksh, int(5 * s), axis=0), int(2 * s), axis=1) * 0.28 * dial)
    kcol = lerp([70, 88, 186], [36, 48, 116], (Y - (ky - bowR - bowT)) / (2 * (bowR + bowT)))
    over_img(img, kcol, keyc)
    out_img = np.zeros((N, N, 4), np.uint8)
    al = np.maximum(img[..., 3:4], 1e-6)
    out_img[..., :3] = np.clip(img[..., :3] / al, 0, 255).astype(np.uint8)
    out_img[..., 3] = np.clip(img[..., 3] * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(out_img, "RGBA")

big = render(1024, False)
big.save(os.path.join(out, "icon-1024.png"))
small = render(512, True)
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
frames = []
for z in sizes:
    src = small if z <= 32 else big
    frames.append(src.resize((z, z), Image.LANCZOS))
for z, f in zip(sizes, frames): f.save(os.path.join(out, f"icon-{z}.png"))
frames[-1].save(os.path.join(out, "app.ico"), sizes=[(z, z) for z in sizes], append_images=frames[:-1])
print("ok")
