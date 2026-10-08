using System.Globalization;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 웹사이트 즐겨찾기의 아이콘(2026-10-06 사용자 결정, Codex 02:46 조건).
/// - 순서: 사용자가 고른 이미지(명시적 덮어쓰기) → 저장 때 한 번 받아 둔 파비콘 → 이름 첫 글자 아이콘. [기본]은 사용자 이미지를 지워 자동 아이콘으로.
/// - 파일: 설정 폴더의 icons\&lt;id&gt;.user.png / icons\&lt;id&gt;.fav.png — 이름은 항목 id(8자리 16진)로만 만든다. 다시 그린 PNG 만 저장한다(받은 바이트·사용자 원본을 그대로 두지 않음).
/// - 받기(<see cref="Fetch"/>): 입력 주소의 scheme·host·port origin 의 /favicon.ico 만. 리다이렉트는 같은 origin 안에서 3번까지, 전체 5초, 실제 받은 바이트 64KiB,
///   쿠키·인증·자동 자격 증명 없음, 인증서 검사 그대로(WinHTTP 기본). 프록시는 WinHTTP 의 자동 프록시(시스템 설정·자동 구성) — 안 되면 WinHTTP 기본 프록시 설정.
///   표시·실행 때는 다시 받지 않는다. UI 스레드에서 부르지 않는다.
/// - 이미지(<see cref="Decode"/>): PNG·ICO 머리를 먼저 보고(크기·프레임 수), 디코드 뒤 픽셀 수를 다시 본 뒤 64×64 로 다시 그린다.
/// </summary>
internal static unsafe class WebIcon
{
    public const int FetchMs = 5000, MaxBytes = 64 * 1024, UserMaxBytes = 1024 * 1024, MaxSide = 512, MaxPixels = 262144, MaxFrames = 16, MaxRedirects = 3;
    /// <summary>저장·표시 크기. 띠는 아이콘을 32 논리 px 로 그려 150% 화면에서 48 px 가 되므로 64 로 둔다(Codex 예시 32 에서 바꾼 까닭).</summary>
    public const int Side = 64;

    // ------------------------------------------------------------------ 파일 위치

    public static string Dir => Path.Combine(Config.Dir, "icons");
    private static bool ValidId(string id) => id.Length == 8 && id.All(Uri.IsHexDigit);
    public static string? FavPath(string id) => ValidId(id) ? Path.Combine(Dir, id.ToLowerInvariant() + ".fav.png") : null;
    public static string? UserPath(string id) => ValidId(id) ? Path.Combine(Dir, id.ToLowerInvariant() + ".user.png") : null;

    private static bool IsLink(string path) { try { return Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return true; } }

    /// <summary>아이콘 폴더(재분석 지점이 아닌 보통 폴더)를 준비한다. 설정 폴더는 DirAcl 이 좁힌 권한으로 만든다.</summary>
    private static bool EnsureDir()
    {
        if (!DirAcl.EnsureDir(Config.Dir)) return false;
        string d = Dir;
        if (Directory.Exists(d)) return !IsLink(d);
        if (File.Exists(d)) return false;
        try { Directory.CreateDirectory(d); return !IsLink(d); } catch { return false; }
    }

    /// <summary>앱이 만든 PNG 를 쓴다(새 임시 파일 → 바꿔치기). 경로가 없거나 폴더가 링크면 false.</summary>
    public static bool Write(string? path, byte[] png)
    {
        if (path is null || png.Length == 0 || png.Length > MaxBytes || !EnsureDir()) return false;
        if (Program.TestFails("webicon:write")) return false;
        try
        {
            Backup.WriteNew(path + ".tmp", png);
            File.Move(path + ".tmp", path, overwrite: true);
            return true;
        }
        catch { try { File.Delete(path + ".tmp"); } catch { } return false; }
    }

    /// <summary>이 항목의 앱 소유 아이콘 파일만 지운다(사용자가 고른 원본 파일은 건드리지 않는다 — 원본은 저장하지 않는다).</summary>
    public static void Delete(string id, bool fav = true, bool user = true)
    {
        foreach (string? p in new[] { fav ? FavPath(id) : null, user ? UserPath(id) : null })
        {
            if (p is null) continue;
            try { if (File.Exists(p) || IsLink(p)) File.Delete(p); } catch { }   // 링크면 링크 자체만 지운다
        }
    }

    /// <summary>저장해 둔 아이콘 파일을 읽는다(링크·과대는 null).</summary>
    public static byte[]? Read(string? path)
    {
        if (path is null) return null;
        try
        {
            if (!File.Exists(path) || IsLink(path) || IsLink(Dir)) return null;
            return ReadLimited(path, MaxBytes);
        }
        catch { return null; }
    }

    /// <summary>사용자가 고른 이미지 파일(.png/.ico)을 읽는다. 1MiB 이하만. 원본은 읽기만 한다. 작업 스레드에서 부른다(UNC 경로가 화면을 멈추지 않게, Codex R78-2).</summary>
    public static byte[]? ReadUserFile(string path)
    {
        try
        {
            if (!LaunchStore.IsRootedPath(path)) return null;
            return ReadLimited(path, UserMaxBytes);
        }
        catch { return null; }
    }

    /// <summary>
    /// 한 파일 핸들에서 길이를 보고 max 바이트까지만 읽는다(Codex R78-2): 그사이 커졌으면(max 를 넘는 바이트가 더 있으면) null, 줄었으면 null.
    /// 확인과 읽기 사이에 파일이 바뀌어도 상한을 넘게 할당·읽지 않는다.
    /// </summary>
    internal static byte[]? ReadLimited(string path, int max)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long len = fs.Length;
            if (len is <= 0 || len > max) return null;
            var buf = new byte[(int)len];
            int got = 0;
            while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
            if (got != buf.Length) return null;          // 그사이 줄어듦
            if (fs.ReadByte() != -1) return null;        // 그사이 커짐: 더 읽지 않는다
            return buf;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ 받기 (WinHTTP)

    /// <summary>
    /// site 의 origin 에서 /favicon.ico 를 받는다. 실패·시간 초과·취소면 null. cancelled() 가 true 를 돌려주면 다음 단계로 가지 않는다.
    /// 전체 시간 FetchMs: 남은 시간을 각 호출의 제한으로 주고, 그 시간이 지나면 감시 타이머가 요청 핸들을 닫아 막힌 호출을 끝낸다.
    /// </summary>
    public static byte[]? Fetch(Uri site, Func<bool> cancelled)
    {
        var (scheme, host, port) = LaunchStore.WebOrigin(site);
        if (scheme is not ("http" or "https") || host.Length == 0) return null;
        long deadline = Environment.TickCount64 + (Program.IsTestMode && TestFetchMs > 0 ? TestFetchMs : FetchMs);
        string path = "/favicon.ico", origHost = host;
        nint ses = 0, con = 0, req = 0;
        object gate = new();
        bool expired = false;
        char* loc = stackalloc char[2048];
        byte* buf = stackalloc byte[8192];
        using var watch = new Timer(_ =>
        {
            lock (gate) { expired = true; if (req != 0) { WinHttpCloseHandle(req); req = 0; } }   // 막힌 Send/Receive/Read 를 끝낸다
        }, null, (int)Math.Max(1, deadline - Environment.TickCount64), Timeout.Infinite);
        try
        {
            fixed (char* agent = "1Key") ses = WinHttpOpen(agent, 4 /* WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY (8.1+) */, null, null, 0);
            if (ses == 0) fixed (char* agent = "1Key") ses = WinHttpOpen(agent, 0 /* DEFAULT_PROXY */, null, null, 0);
            if (ses == 0) return null;
            // 아래 설정(리다이렉트 직접 처리·시간 제한·쿠키/인증/자동 로그온 끔)이 하나라도 실패하면 요청을 보내지 않는다(Codex R78-3)
            if (Program.TestFails("webicon:option")) return null;   // 시험: 설정 실패 주입 — 서버에 요청 0회
            uint never = 0;   // WINHTTP_OPTION_REDIRECT_POLICY_NEVER: 리다이렉트는 아래에서 같은 origin 인지 보고 직접 따라간다
            if (WinHttpSetOption(ses, 88 /* WINHTTP_OPTION_REDIRECT_POLICY */, &never, 4) == 0) return null;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                int left = (int)(deadline - Environment.TickCount64);
                if (left <= 0 || cancelled()) return null;
                if (WinHttpSetTimeouts(ses, left, left, left, left) == 0) return null;
                if (con == 0) fixed (char* h = host) con = WinHttpConnect(ses, h, (ushort)port, 0);
                if (con == 0) return null;
                nint r;
                fixed (char* verb = "GET") fixed (char* obj = path) r = WinHttpOpenRequest(con, verb, obj, null, null, null, scheme == "https" ? 0x00800000u /* WINHTTP_FLAG_SECURE */ : 0);
                if (r == 0) return null;
                lock (gate) { if (expired) { WinHttpCloseHandle(r); return null; } req = r; }
                uint off = 0x1 | 0x2 | 0x4 | 0x8;   // WINHTTP_DISABLE_COOKIES | REDIRECTS | AUTHENTICATION | KEEP_ALIVE
                if (WinHttpSetOption(r, 63 /* WINHTTP_OPTION_DISABLE_FEATURE */, &off, 4) == 0) return null;
                uint high = 2;                       // WINHTTP_AUTOLOGON_SECURITY_LEVEL_HIGH: 자동 로그온(Windows 자격 증명) 없음
                if (WinHttpSetOption(r, 77 /* WINHTTP_OPTION_AUTOLOGON_POLICY */, &high, 4) == 0) return null;
                if (WinHttpSendRequest(r, null, 0, null, 0, 0, 0) == 0 || WinHttpReceiveResponse(r, null) == 0) return null;
                uint status = 0, len = 4;
                if (WinHttpQueryHeaders(r, 19 /* STATUS_CODE */ | 0x20000000 /* FLAG_NUMBER */, null, &status, &len, null) == 0) return null;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    uint ll = 2048 * 2;
                    if (WinHttpQueryHeaders(r, 33 /* LOCATION */, null, loc, &ll, null) == 0) return null;
                    string location = new string(loc, 0, (int)(ll / 2));
                    if (NextHop(scheme, origHost, host, port, path, location) is not (string nh, string next)) return null;   // 다른 호스트·포트·하향·사용자 정보: 따라가지 않는다
                    lock (gate) { if (req != 0) { WinHttpCloseHandle(req); req = 0; } }
                    if (!string.Equals(nh, host, StringComparison.OrdinalIgnoreCase)) { WinHttpCloseHandle(con); con = 0; host = nh; }   // www 별칭: 그 호스트로 다시 연결
                    path = next;
                    continue;
                }
                if (status != 200) return null;
                var ms = new MemoryStream();
                while (true)
                {
                    if (cancelled()) return null;
                    uint got = 0;
                    if (WinHttpReadData(r, buf, 8192, &got) == 0) return null;
                    if (got == 0) break;
                    if (ms.Length + got > MaxBytes) return null;   // 실제 받은 바이트로 자른다(Content-Length 를 믿지 않음)
                    ms.Write(new ReadOnlySpan<byte>(buf, (int)got));
                }
                return ms.Length > 0 ? ms.ToArray() : null;
            }
            return null;   // 리다이렉트가 너무 많음
        }
        catch { return null; }
        finally
        {
            lock (gate) { expired = true; if (req != 0) { WinHttpCloseHandle(req); req = 0; } }
            if (con != 0) WinHttpCloseHandle(con);
            if (ses != 0) WinHttpCloseHandle(ses);
        }
    }

    /// <summary>시험용: 받기 전체 제한을 줄인다(시간 초과 시험). 0 이면 FetchMs.</summary>
    internal static int TestFetchMs;

    /// <summary>
    /// 리다이렉트 Location 을 따라가도 되면 새 경로(path?query), 아니면 null. 같은 scheme·host·port 만(다른 호스트·포트·스킴·https→http·사용자 정보 거절).
    /// </summary>
    internal static string? NextHop(string scheme, string host, int port, string curPath, string location)
        => NextHop(scheme, host, host, port, curPath, location) is (string h, string pq) && string.Equals(h, host, StringComparison.OrdinalIgnoreCase) ? pq : null;

    /// <summary>
    /// 리다이렉트를 따라가도 되면 (새 호스트, path?query), 아니면 null. "같은 origin 또는 정확한 www 별칭"(Codex 11:00): 같은 scheme·port, 사용자 정보 없음,
    /// 새 호스트(IDN 정규화)가 처음 주소의 호스트와 같거나, 앞에 "www." 를 한 번 더하거나 뺀 값일 때만. 같은 등록 도메인 전체·다른 하위 도메인·접미사는 거절.
    /// origHost = 처음 주소의 호스트(별칭은 늘 이것을 기준으로 — www.www. 로 번지지 않게), curHost = 지금 요청한 호스트.
    /// </summary>
    internal static (string Host, string PathQuery)? NextHop(string scheme, string origHost, string curHost, int port, string curPath, string location)
    {
        if (location.Length is 0 or > 2048) return null;
        var baseUri = new Uri($"{scheme}://{(curHost.Contains(':') ? "[" + curHost + "]" : curHost)}:{port.ToString(CultureInfo.InvariantCulture)}{curPath}");
        if (!Uri.TryCreate(baseUri, location, out Uri? next)) return null;
        if (next.Scheme != scheme || next.UserInfo.Length > 0 || next.Port != port) return null;
        string h = next.IdnHost;
        bool alias = string.Equals(h, origHost, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(h, "www." + origHost, StringComparison.OrdinalIgnoreCase)
                  || (origHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && string.Equals(h, origHost[4..], StringComparison.OrdinalIgnoreCase));
        if (!alias || h.Contains(':')) return null;   // IPv6 주소에는 별칭을 두지 않는다(같은 주소만 — 위 첫 조건)
        string pq = next.PathAndQuery;
        return pq.Length is > 0 and <= 2048 ? (h, pq) : null;
    }

    // ------------------------------------------------------------------ 이미지 검사·다시 그리기 (GDI+)

    /// <summary>
    /// 디코드 전에 머리를 본다: PNG 는 IHDR 의 가로·세로, ICO 는 프레임 수(MaxFrames 이하)와 각 프레임의 위치·크기(안의 PNG 는 그 IHDR, BMP 는 BITMAPINFOHEADER).
    /// 그 밖의 형식(GIF·JPEG·BMP·SVG 등)은 받지 않는다.
    /// </summary>
    internal static bool HeaderOk(ReadOnlySpan<byte> d)
    {
        try { return HeaderOkCore(d); }
        catch { return false; }   // 예: Math.Abs(int.MinValue) — 거절로 끝낸다(Codex R78-2)
    }

    private static bool HeaderOkCore(ReadOnlySpan<byte> d)
    {
        if (IsPng(d)) return PngSizeOk(d);
        if (d.Length >= 6 && d[0] == 0 && d[1] == 0 && d[2] == 1 && d[3] == 0)
        {
            int n = d[4] | d[5] << 8;
            if (n is < 1 or > MaxFrames || d.Length < 6 + 16 * n) return false;
            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<byte> e = d.Slice(6 + 16 * i, 16);
                long size = BitConverter.ToUInt32(e[8..12]), at = BitConverter.ToUInt32(e[12..16]);
                if (size < 40 || at < 6 + 16 * n || at + size > d.Length) return false;
                ReadOnlySpan<byte> img = d.Slice((int)at, (int)size);
                if (IsPng(img)) { if (!PngSizeOk(img)) return false; continue; }
                int hdr = BitConverter.ToInt32(img[..4]), w = BitConverter.ToInt32(img[4..8]), h = BitConverter.ToInt32(img[8..12]);
                if (hdr != 40 && hdr != 108 && hdr != 124) return false;   // BITMAPINFOHEADER / V4 / V5
                if (w is < 1 or > 256 || Math.Abs(h) is < 1 or > 512) return false;   // ICO 의 BMP 는 높이가 마스크까지 두 배
            }
            return true;
        }
        return false;
    }

    private static bool IsPng(ReadOnlySpan<byte> d) => d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static bool PngSizeOk(ReadOnlySpan<byte> d)
    {
        if (!d.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        uint w = (uint)(d[16] << 24 | d[17] << 16 | d[18] << 8 | d[19]), h = (uint)(d[20] << 24 | d[21] << 16 | d[22] << 8 | d[23]);
        return w is >= 1 and <= MaxSide && h is >= 1 and <= MaxSide && (long)w * h <= MaxPixels;
    }

    /// <summary>검사한 뒤 디코드해 Side×Side 32비트 GDI+ 비트맵으로 다시 그린다. 실패하면 0. 결과는 <see cref="Free"/> 로 해제.</summary>
    public static nint Decode(byte[] data)
    {
        if (data.Length is 0 or > UserMaxBytes || !HeaderOk(data)) return 0;
        Gdiplus.Init();
        nint stream = 0, src = 0, dst = 0, g = 0;
        try
        {
            fixed (byte* p = data) stream = SHCreateMemStream(p, (uint)data.Length);
            if (stream == 0) return 0;
            if (GdipCreateBitmapFromStream(stream, out src) != 0 || src == 0) return 0;
            if (GdipGetImageWidth(src, out uint w) != 0 || GdipGetImageHeight(src, out uint h) != 0) return 0;
            if (w is 0 or > MaxSide || h is 0 or > MaxSide || (long)w * h > MaxPixels) return 0;   // 디코드 뒤에도 픽셀 수를 다시 본다
            if (GdipCreateBitmapFromScan0(Side, Side, 0, 0x26200A /* PixelFormat32bppARGB */, 0, out dst) != 0 || dst == 0) return 0;
            if (GdipGetImageGraphicsContext(dst, out g) != 0) return 0;
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
            GdipGraphicsClear(g, 0);
            if (GdipDrawImageRectI(g, src, 0, 0, Side, Side) != 0) return 0;
            nint ok = dst; dst = 0;
            return ok;
        }
        catch { return 0; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (dst != 0) GdipDisposeImage(dst);
            if (src != 0) GdipDisposeImage(src);
            if (stream != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)stream)[2])(stream);   // IStream::Release(이미지를 지운 뒤)
        }
    }

    public static void Free(nint bmp) { if (bmp != 0) GdipDisposeImage(bmp); }

    private static readonly Guid PngEncoder = new("557CF406-1A04-11D3-9A73-0000F81EF32E");

    /// <summary>다시 그린 비트맵을 PNG 바이트로(임시 메모리 스트림). 실패하면 null.</summary>
    public static byte[]? ToPng(nint bmp)
    {
        if (bmp == 0) return null;
        nint stream = SHCreateMemStream(null, 0);
        if (stream == 0) return null;
        try
        {
            Guid enc = PngEncoder;
            if (GdipSaveImageToStream(bmp, stream, &enc, 0) != 0) return null;
            // IStream: Seek(5) 처음으로, Stat 대신 Seek 끝으로 길이, 그다음 Read(3)
            var vt = *(nint**)stream;
            ulong end;
            if (((delegate* unmanaged[Stdcall]<nint, long, uint, ulong*, int>)vt[5])(stream, 0, 2 /* STREAM_SEEK_END */, &end) < 0 || end is 0 or > MaxBytes) return null;
            if (((delegate* unmanaged[Stdcall]<nint, long, uint, ulong*, int>)vt[5])(stream, 0, 0 /* SET */, null) < 0) return null;
            var outb = new byte[(int)end];
            uint read;
            fixed (byte* o = outb) if (((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)vt[3])(stream, o, (uint)end, &read) < 0 || read != end) return null;
            return outb;
        }
        catch { return null; }
        finally { ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)stream)[2])(stream); }
    }

    /// <summary>비트맵 → HICON(띠·미리보기). 실패하면 0. 받은 쪽이 DestroyIcon.</summary>
    public static nint ToIcon(nint bmp) => bmp != 0 && GdipCreateHICONFromBitmap(bmp, out nint ic) == 0 ? ic : 0;

    /// <summary>저장한 PNG 파일 → HICON(다시 검사해서 읽는다). 없거나 못 읽으면 0.</summary>
    public static nint IconFromFile(string? path)
    {
        byte[]? b = Read(path);
        if (b is null) return 0;
        nint bmp = Decode(b);
        try { return ToIcon(bmp); } finally { Free(bmp); }
    }

    /// <summary>글자 아이콘 바탕색(이름, COLORREF). 글자는 흰색. 비어 있는 이름 = 테마 강조색(목록에 없음).</summary>
    public static readonly (string Key, uint Bg)[] LetterColors =
    {
        ("blue", 0xDB6F2F), ("green", 0x578B2E), ("orange", 0x247BE0), ("red", 0x3F44D9), ("purple", 0xC94F7B), ("gray", 0x80726B),
    };

    public static uint? LetterColor(string key)
    {
        foreach (var (k, bg) in LetterColors) if (k == key) return bg;
        return null;
    }

    /// <summary>이름 첫 글자를 넣은 둥근 아이콘(사이트 아이콘이 없을 때). 색은 부르는 쪽(테마 또는 고른 바탕색). 받은 쪽이 DestroyIcon.</summary>
    public static nint Letter(string name, uint bgColorref, uint fgColorref)
    {
        Gdiplus.Init();
        string letter = "?";
        string n = name.Trim();
        if (n.Length > 0) { var e = StringInfo.GetTextElementEnumerator(n); if (e.MoveNext()) letter = ((string)e.Current).ToUpperInvariant(); }
        nint bmp = 0, g = 0, brush = 0, fam = 0, font = 0, fmt = 0, tb = 0;
        try
        {
            if (GdipCreateBitmapFromScan0(Side, Side, 0, 0x26200A, 0, out bmp) != 0 || bmp == 0) return 0;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return 0;
            GdipSetSmoothingMode(g, 4 /* AntiAlias */);
            GdipSetTextRenderingHint(g, 4 /* AntiAlias */);
            GdipGraphicsClear(g, 0);
            if (GdipCreateSolidFill(Gdiplus.Argb(bgColorref), out brush) == 0) GdipFillEllipse(g, brush, 2, 2, Side - 4, Side - 4);
            foreach (string face in new[] { "Malgun Gothic", "Segoe UI" })
                fixed (char* f = face) if (GdipCreateFontFamilyFromName(f, 0, out fam) == 0 && fam != 0) break;
            if (fam != 0 && GdipCreateFont(fam, Side * 0.46f, 1 /* Bold */, 2 /* UnitPixel */, out font) == 0
                && GdipCreateStringFormat(0, 0, out fmt) == 0 && GdipCreateSolidFill(Gdiplus.Argb(fgColorref), out tb) == 0)
            {
                GdipSetStringFormatAlign(fmt, 1 /* Center */);
                GdipSetStringFormatLineAlign(fmt, 1);
                var rc = new RectF { X = 0, Y = 1, W = Side, H = Side };
                fixed (char* s = letter) GdipDrawString(g, s, letter.Length, font, &rc, fmt, tb);
            }
            GdipDeleteGraphics(g); g = 0;
            return ToIcon(bmp);
        }
        catch { return 0; }
        finally
        {
            if (tb != 0) GdipDeleteBrush(tb);
            if (fmt != 0) GdipDeleteStringFormat(fmt);
            if (font != 0) GdipDeleteFont(font);
            if (fam != 0) GdipDeleteFontFamily(fam);
            if (brush != 0) GdipDeleteBrush(brush);
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectF { public float X, Y, W, H; }

    [DllImport("winhttp.dll")] private static extern nint WinHttpOpen(char* agent, uint accessType, char* proxy, char* bypass, uint flags);
    [DllImport("winhttp.dll")] private static extern nint WinHttpConnect(nint session, char* server, ushort port, uint reserved);
    [DllImport("winhttp.dll")] private static extern nint WinHttpOpenRequest(nint connect, char* verb, char* obj, char* version, char* referrer, char** accept, uint flags);
    [DllImport("winhttp.dll")] private static extern int WinHttpSendRequest(nint req, char* headers, uint headersLen, void* optional, uint optionalLen, uint totalLen, nuint context);
    [DllImport("winhttp.dll")] private static extern int WinHttpReceiveResponse(nint req, void* reserved);
    [DllImport("winhttp.dll")] private static extern int WinHttpQueryHeaders(nint req, uint level, char* name, void* buffer, uint* bufferLen, uint* index);
    [DllImport("winhttp.dll")] private static extern int WinHttpReadData(nint req, void* buffer, uint toRead, uint* read);
    [DllImport("winhttp.dll")] private static extern int WinHttpSetOption(nint h, uint option, void* buffer, uint len);
    [DllImport("winhttp.dll")] private static extern int WinHttpSetTimeouts(nint h, int resolve, int connect, int send, int receive);
    [DllImport("winhttp.dll")] private static extern int WinHttpCloseHandle(nint h);

    [DllImport("shlwapi.dll", EntryPoint = "#12")] private static extern nint SHCreateMemStream(byte* init, uint size);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromStream(nint stream, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint width);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint height);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetTextRenderingHint(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int w, int h);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipSaveImageToStream(nint image, nint stream, Guid* encoder, nint parameters);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateHICONFromBitmap(nint bitmap, out nint icon);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFontFamilyFromName(char* name, nint collection, out nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateStringFormat(int attributes, ushort language, out nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatLineAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawString(nint graphics, char* s, int length, nint font, RectF* layout, nint format, nint brush);
}
