# -*- coding: utf-8 -*-
"""Support screen QR codes, rendered at development time (no network, no package): a small QR encoder (byte mode, versions 1-10,
error correction M) after the ISO/IEC 18004 procedure, and a PNG writer with one pixel per module plus a 4-module quiet zone.
1Key draws the PNG with whole-pixel modules (nearest neighbour), so the code stays sharp at every DPI.

  py -3.12 tools/support/make_qr.py            writes src/OneKey/Assets/support/qr_kakaopay.png and prints the module matrix hash

The payload is the allowlisted link in src/OneKey/SupportLinks.cs (read from there, so the picture and the button cannot disagree).
"""
import re, sys, zlib, struct, hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

# (EC codewords per block, [(block count, data codewords per block), ...]) for level M
EC_M = {1: (10, [(1, 16)]), 2: (16, [(1, 28)]), 3: (26, [(1, 44)]), 4: (18, [(2, 32)]), 5: (24, [(2, 43)]), 6: (16, [(4, 27)]),
        7: (18, [(4, 31)]), 8: (22, [(2, 38), (2, 39)]), 9: (22, [(3, 36), (2, 37)]), 10: (26, [(4, 43), (1, 44)])}
ALIGN = {1: [], 2: [6, 18], 3: [6, 22], 4: [6, 26], 5: [6, 30], 6: [6, 34], 7: [6, 22, 38], 8: [6, 24, 42], 9: [6, 26, 46], 10: [6, 28, 50]}
FORMAT_M = 0   # level bits: L=1 M=0 Q=3 H=2

def gf_mul(x, y):
    z = 0
    for i in range(7, -1, -1):
        z = (z << 1) ^ ((z >> 7) * 0x11D)
        z ^= ((y >> i) & 1) * x
    return z & 0xFF

def rs_divisor(degree):
    r = [0] * (degree - 1) + [1]
    root = 1
    for _ in range(degree):
        for j in range(degree):
            r[j] = gf_mul(r[j], root)
            if j + 1 < degree: r[j] ^= r[j + 1]
        root = gf_mul(root, 0x02)
    return r

def rs_remainder(data, div):
    r = [0] * len(div)
    for b in data:
        f = b ^ r.pop(0); r.append(0)
        for i, c in enumerate(div): r[i] ^= gf_mul(c, f)
    return r

def encode(text):
    data = text.encode("utf-8")
    for ver in range(1, 11):
        ecl, groups = EC_M[ver]
        cap = sum(n * k for n, k in groups)
        if 4 + 8 + 8 * len(data) <= cap * 8: break
    else: raise ValueError("too long")
    bits = [0, 1, 0, 0] + [(len(data) >> i) & 1 for i in range(7, -1, -1)]
    for b in data: bits += [(b >> i) & 1 for i in range(7, -1, -1)]
    bits += [0] * min(4, cap * 8 - len(bits))
    bits += [0] * (-len(bits) % 8)
    cw = [int("".join(map(str, bits[i:i + 8])), 2) for i in range(0, len(bits), 8)]
    pad = [0xEC, 0x11]
    while len(cw) < cap: cw.append(pad[(len(cw) - len(bits) // 8) % 2])
    blocks, k = [], 0
    for n, size in groups:
        for _ in range(n): blocks.append(cw[k:k + size]); k += size
    div = rs_divisor(ecl)
    ecs = [rs_remainder(b, div) for b in blocks]
    out = []
    for i in range(max(len(b) for b in blocks)):
        for b in blocks:
            if i < len(b): out.append(b[i])
    for i in range(ecl):
        for e in ecs: out.append(e[i])
    return ver, out

def build(text):
    ver, cws = encode(text)
    n = 17 + 4 * ver
    m = [[False] * n for _ in range(n)]
    fn = [[False] * n for _ in range(n)]
    def put(x, y, v): m[y][x] = v; fn[y][x] = True
    for i in range(n): put(6, i, i % 2 == 0); put(i, 6, i % 2 == 0)
    for cx, cy in ((3, 3), (n - 4, 3), (3, n - 4)):
        for dy in range(-4, 5):
            for dx in range(-4, 5):
                x, y = cx + dx, cy + dy
                if 0 <= x < n and 0 <= y < n: put(x, y, max(abs(dx), abs(dy)) not in (2, 4))
    al = ALIGN[ver]
    for i, ax in enumerate(al):
        for j, ay in enumerate(al):
            if (i == 0 and j == 0) or (i == 0 and j == len(al) - 1) or (i == len(al) - 1 and j == 0): continue
            for dy in range(-2, 3):
                for dx in range(-2, 3): put(ax + dx, ay + dy, max(abs(dx), abs(dy)) != 1)
    def format_bits(mask):
        d = FORMAT_M << 3 | mask
        r = d
        for _ in range(10): r = (r << 1) ^ ((r >> 9) * 0x537)
        return (d << 10 | r) ^ 0x5412
    def draw_format(mask):
        b = format_bits(mask)
        g = lambda i: (b >> i) & 1 == 1
        for i in range(6): put(8, i, g(i))
        put(8, 7, g(6)); put(8, 8, g(7)); put(7, 8, g(8))
        for i in range(9, 15): put(14 - i, 8, g(i))
        for i in range(8): put(n - 1 - i, 8, g(i))
        for i in range(8, 15): put(8, n - 15 + i, g(i))
        put(8, n - 8, True)
    draw_format(0)
    if ver >= 7:
        r = ver
        for _ in range(12): r = (r << 1) ^ ((r >> 11) * 0x1F25)
        b = ver << 12 | r
        for i in range(18):
            v = (b >> i) & 1 == 1; a, c = n - 11 + i % 3, i // 3
            put(a, c, v); put(c, a, v)
    i = 0
    total = len(cws) * 8
    right = n - 1
    while right >= 1:
        if right == 6: right = 5
        for vert in range(n):
            for j in range(2):
                x = right - j
                up = ((right + 1) & 2) == 0
                y = n - 1 - vert if up else vert
                if not fn[y][x] and i < total:
                    m[y][x] = (cws[i >> 3] >> (7 - (i & 7))) & 1 == 1; i += 1
        right -= 2
    masks = [lambda x, y: (x + y) % 2 == 0, lambda x, y: y % 2 == 0, lambda x, y: x % 3 == 0, lambda x, y: (x + y) % 3 == 0,
             lambda x, y: (x // 3 + y // 2) % 2 == 0, lambda x, y: x * y % 2 + x * y % 3 == 0,
             lambda x, y: (x * y % 2 + x * y % 3) % 2 == 0, lambda x, y: ((x + y) % 2 + x * y % 3) % 2 == 0]
    def apply(k):
        for y in range(n):
            for x in range(n):
                if not fn[y][x] and masks[k](x, y): m[y][x] = not m[y][x]
    def penalty():
        p = 0
        lines = [row[:] for row in m] + [[m[y][x] for y in range(n)] for x in range(n)]
        for ln in lines:
            run = 1
            for a in range(1, n + 1):
                if a < n and ln[a] == ln[a - 1]: run += 1
                else:
                    if run >= 5: p += 3 + run - 5
                    run = 1
            s = "".join("1" if v else "0" for v in ln)
            p += 40 * (len(re.findall("(?=10111010000)", s)) + len(re.findall("(?=00001011101)", s)))
        for y in range(n - 1):
            for x in range(n - 1):
                if m[y][x] == m[y][x + 1] == m[y + 1][x] == m[y + 1][x + 1]: p += 3
        dark = sum(v for row in m for v in row)
        p += 10 * (abs(dark * 20 - n * n * 10) // (n * n))
        return p
    best = None
    for k in range(8):
        apply(k); draw_format(k)
        sc = penalty()
        if best is None or sc < best[0]: best = (sc, k)
        apply(k)   # undo (XOR)
    apply(best[1]); draw_format(best[1])
    return ver, best[1], m

def png(m, path, quiet=4):
    n = len(m); s = n + 2 * quiet
    raw = b""
    for y in range(s):
        row = bytearray([0])
        for x in range(s):
            xx, yy = x - quiet, y - quiet
            dark = 0 <= xx < n and 0 <= yy < n and m[yy][xx]
            row.append(0 if dark else 255)
        raw += bytes(row)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", s, s, 8, 0, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)

if __name__ == "__main__":
    src = (REPO / "src" / "OneKey" / "SupportLinks.cs").read_text(encoding="utf-8")
    url = re.search(r'KakaoPay\s*=\s*"([^"]+)"', src).group(1)
    ver, mask, m = build(url)
    out = REPO / "src" / "OneKey" / "Assets" / "support" / "qr_kakaopay.png"
    png(m, out)
    bits = "".join("1" if v else "0" for row in m for v in row)
    print(f"{url}\nversion {ver}, mask {mask}, {len(m)}x{len(m)} modules -> {out.relative_to(REPO)}")
    print("matrix", bits)
    print("sha256", hashlib.sha256(bits.encode()).hexdigest())
