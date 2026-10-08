# -*- coding: utf-8 -*-
"""1Key 앱 아이콘(.ico) 생성기 - 표준 라이브러리만 사용.
   Windows 11 스타일: 둥근 사각형 + 파란 그라데이션 + 흰색 열쇠 글리프."""
import math, struct, zlib, os

SIZES_BMP = [16, 20, 24, 32, 40, 48, 64]
SIZES_PNG = [128, 256]

# ---------- SDF helpers (normalized 0..1 coords) ----------
def sd_round_rect(px, py, cx, cy, hw, hh, r):
    qx = abs(px - cx) - (hw - r)
    qy = abs(py - cy) - (hh - r)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - r

def sd_circle(px, py, cx, cy, r):
    return math.hypot(px - cx, py - cy) - r

def key_sdf(px, py):
    """흰색 열쇠 모양의 signed distance (음수 = 내부)."""
    ring = max(sd_circle(px, py, 0.345, 0.500, 0.180),
               -sd_circle(px, py, 0.345, 0.500, 0.086))
    shaft = sd_round_rect(px, py, 0.640, 0.500, 0.190, 0.047, 0.040)
    tooth1 = sd_round_rect(px, py, 0.672, 0.583, 0.030, 0.088, 0.026)
    tooth2 = sd_round_rect(px, py, 0.788, 0.570, 0.030, 0.075, 0.026)
    return min(ring, shaft, tooth1, tooth2)

def lerp(a, b, t):
    return a + (b - a) * t

def render(size):
    """size x size RGBA 바이트 배열 반환 (안티에일리어싱 포함)."""
    px_buf = bytearray(size * size * 4)
    aa = float(size)                  # 1픽셀 폭 기준 AA
    radius = 0.2235                   # Win11 앱 아이콘 곡률
    # 여백: 아이콘이 타일에 꽉 차지 않도록 살짝 인셋
    inset = 0.055 if size >= 32 else 0.03
    hw = 0.5 - inset
    for y in range(size):
        v = (y + 0.5) / size
        for x in range(size):
            u = (x + 0.5) / size
            d_bg = sd_round_rect(u, v, 0.5, 0.5, hw, hw, radius)
            a_bg = min(max(0.5 - d_bg * aa, 0.0), 1.0)
            if a_bg <= 0.0:
                continue
            # 대각선 그라데이션 (좌상단 밝음 -> 우하단 진함)
            t = min(max((u * 0.45 + v * 0.55), 0.0), 1.0)
            r = int(lerp(0x4C, 0x0B, t) + 0.5)
            g = int(lerp(0xB0, 0x4B, t) + 0.5)
            b = int(lerp(0xFF, 0xA6, t) + 0.5)
            d_key = key_sdf(u, v)
            a_key = min(max(0.5 - d_key * aa, 0.0), 1.0)
            if a_key > 0.0:
                r = int(lerp(r, 0xFF, a_key) + 0.5)
                g = int(lerp(g, 0xFF, a_key) + 0.5)
                b = int(lerp(b, 0xFF, a_key) + 0.5)
            i = (y * size + x) * 4
            px_buf[i] = r; px_buf[i+1] = g; px_buf[i+2] = b
            px_buf[i+3] = int(a_bg * 255 + 0.5)
    return px_buf

# ---------- PNG ----------
def png_chunk(tag, data):
    return (struct.pack('>I', len(data)) + tag + data
            + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

def to_png(rgba, size):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        raw += rgba[y*size*4:(y+1)*size*4]
    return (b'\x89PNG\r\n\x1a\n'
            + png_chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + png_chunk(b'IDAT', zlib.compress(bytes(raw), 9))
            + png_chunk(b'IEND', b''))

# ---------- BMP(DIB) for ICO ----------
def to_dib(rgba, size):
    hdr = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, size*size*4, 0, 0, 0, 0)
    body = bytearray()
    for y in range(size - 1, -1, -1):          # bottom-up
        for x in range(size):
            i = (y*size + x) * 4
            body += bytes((rgba[i+2], rgba[i+1], rgba[i], rgba[i+3]))  # BGRA
    stride = ((size + 31) // 32) * 4           # AND 마스크 (모두 0)
    return hdr + bytes(body) + bytes(stride * size)

# ---------- ICO ----------
def build_ico(path):
    images = []
    for s in SIZES_BMP:
        images.append((s, to_dib(render(s), s)))
    for s in SIZES_PNG:
        images.append((s, to_png(render(s), s)))
    out = bytearray(struct.pack('<HHH', 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    entries, blobs = bytearray(), bytearray()
    for s, data in images:
        entries += struct.pack('<BBBBHHII', 0 if s >= 256 else s, 0 if s >= 256 else s,
                               0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    with open(path, 'wb') as f:
        f.write(bytes(out + entries + blobs))
    print('wrote %s (%d bytes, %d images)' % (path, 6 + 16*len(images) + len(blobs), len(images)))

if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    build_ico(os.path.join(here, '..', 'src', 'OneKey', 'app.ico'))
