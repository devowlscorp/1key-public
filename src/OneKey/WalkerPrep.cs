using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 마스코트 그리기를 막지 않는 두 가지(0.3.131-E, 2026-10-08 사용자 시험 빌드 기록 — %TEMP%\1Key-walker-timing.log):
/// 1. 상황 그림 미리 준비: 동작을 시작할 때마다 그림 띠(PNG)를 풀고 칸 크기로 줄이느라 그리기 스레드가 0.2~0.4초 멈췄다(기록 5건).
///    다음에 나올 그림(섞는 차례의 다음 것, 짝 그림, 매달리기, 정시 59분에는 자전거)을 다른 스레드에서 미리 메모리에 그려 둔다.
///    준비가 안 됐으면 예전처럼 그 자리에서 그린다. 준비해 둔 것은 많아야 4개, 배율·창 크기가 바뀌면 버린다.
/// 2. 틱: Windows 타이머(WM_TIMER)는 간격의 약 10 % 를 한 눈금(15.6 ms) 늦게 깨웠다(기록: 62 ms 중 100/1000 이 76~80 ms).
///    고해상도 대기 타이머(CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Windows 10 1803+)를 쓰는 작은 스레드가 정해진 시각마다 창에
///    WM_ANIMTICK 을 보낸다(앞 것을 아직 처리 안 했으면 쌓지 않는다). 간격은 장 길이 그대로(62.5 ms 등). 만들 수 없으면 예전 SetTimer.
/// </summary>
internal static unsafe partial class Walker
{
    // ------------------------------------------------------------------ 1. 미리 준비

    private sealed class Prepared
    {
        public string Name = "";
        public double K;
        public int Vw, Vh, Dw, Dh;
        public nint Bits;
        public bool Done, Failed;
    }

    private static readonly object _prepLock = new();
    private static readonly List<Prepared> _prep = new();

    /// <summary>그림 띠를 칸 크기(배율 k)로 줄여 새 메모리(미리 곱한 ARGB, 폭 dw×장 수, 높이 dh)에. 어느 스레드에서나. 실패하면 0.</summary>
    private static nint RenderClipPixels(string name, int frames, int cellW, double k, int vw, int vh, out int dw, out int dh)
    {
        dw = dh = 0;
        nint img = 0, stream = 0, bmp = 0, g = 0, bits = 0;
        bool ok = false;
        try
        {
            using var s = typeof(Walker).Assembly.GetManifestResourceStream(name);
            if (s is null) return 0;
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            fixed (byte* p = data) stream = SHCreateMemStream(p, (uint)data.Length);
            if (stream == 0 || GdipCreateBitmapFromStream(stream, out img) != 0 || img == 0) return 0;
            GdipGetImageHeight(img, out uint ih);
            if (ih == 0) return 0;
            dw = Math.Clamp((int)Math.Round(cellW * k), 1, Math.Max(1, vw));
            dh = Math.Clamp((int)Math.Round(ih * k), 1, Math.Max(1, vh));
            int w = dw * frames, h = dh;
            bits = (nint)NativeMemory.AllocZeroed((nuint)((long)w * h * 4));
            if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B /* PARGB */, bits, out bmp) != 0 || bmp == 0) return 0;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return 0;
            GdipSetInterpolationMode(g, 7);
            GdipSetPixelOffsetMode(g, 2);
            GdipGraphicsClear(g, 0);
            for (int i = 0; i < frames; i++) GdipDrawImageRectRectI(g, img, i * dw, 0, dw, dh, i * cellW, 0, cellW, (int)ih, 2, 0, 0, 0);
            ok = true;
            return bits;
        }
        catch { return 0; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            if (img != 0) GdipDisposeImage(img);
            if (stream != 0) Marshal.Release(stream);
            if (!ok && bits != 0) NativeMemory.Free((void*)bits);
        }
    }

    /// <summary>이 그림을 지금 배율·창 크기로 다른 스레드에서 미리 그려 둔다(이미 있거나 그리는 중이면 그대로).</summary>
    private static void Prefetch(string name)
    {
        if (_fh == 0 || _baseH == 0) return;
        var list = ClipList();
        int i = Array.FindIndex(list, c => c.Name == name);
        if (i < 0) return;
        var c = list[i];
        double k = _fh / (double)_baseH;
        int vw = _vw, vh = _vh;
        var pr = new Prepared { Name = name, K = k, Vw = vw, Vh = vh };
        lock (_prepLock)
        {
            if (_prep.Exists(p => p.Name == name && p.K == k && p.Vw == vw && p.Vh == vh)) return;
            while (_prep.Count >= 4)
            {
                var old = _prep.Find(p => p.Done);
                if (old is null) return;   // 넷 다 그리는 중: 이번 것은 건너뛴다
                _prep.Remove(old);
                if (old.Bits != 0) { NativeMemory.Free((void*)old.Bits); old.Bits = 0; }
            }
            _prep.Add(pr);
        }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            nint b = 0; int dw = 0, dh = 0;
            try { b = RenderClipPixels(c.Name, c.Frames, c.CellW, k, vw, vh, out dw, out dh); } catch { }
            lock (_prepLock)
            {
                if (!_prep.Contains(pr)) { if (b != 0) NativeMemory.Free((void*)b); return; }   // 그동안 버려졌다
                pr.Bits = b; pr.Dw = dw; pr.Dh = dh; pr.Failed = b == 0; pr.Done = true;
                Monitor.PulseAll(_prepLock);
            }
        });
    }

    /// <summary>미리 그린 것을 넘겨받는다(목록에서 빠진다). 그리는 중이면 잠깐(최대 1.5초) 기다린다. 없거나 실패면 0.</summary>
    private static nint TakePrepared(string name, double k, int vw, int vh, out int dw, out int dh)
    {
        dw = dh = 0;
        lock (_prepLock)
        {
            var pr = _prep.Find(p => p.Name == name && p.K == k && p.Vw == vw && p.Vh == vh);
            if (pr is null) return 0;
            long until = Environment.TickCount64 + 1500;
            while (!pr.Done)
            {
                long left = until - Environment.TickCount64;
                if (left <= 0) return 0;
                Monitor.Wait(_prepLock, (int)left);
            }
            _prep.Remove(pr);
            if (pr.Failed || pr.Bits == 0) return 0;
            dw = pr.Dw; dh = pr.Dh;
            return pr.Bits;
        }
    }

    /// <summary>미리 그린 것을 모두 버린다(배율·창이 바뀌거나 그림 자원을 해제할 때). 그리는 중인 것은 끝나면 스스로 버린다.</summary>
    private static void ClearPrepared()
    {
        lock (_prepLock)
        {
            foreach (var p in _prep) if (p.Bits != 0) { NativeMemory.Free((void*)p.Bits); p.Bits = 0; }
            _prep.Clear();
        }
    }

    /// <summary>다음 쉬기에 나올 그림(과 짝 그림, 매달리기)을 미리 그린다.</summary>
    private static void PrefetchNext()
    {
        var list = ClipList();
        if (list.Length == 0 || _fh == 0) return;
        int next;
        if (SeqTest) next = _seqNext % list.Length;
        else { EnsureBag(list); if (_clipBag.Count == 0) return; next = _clipBag[^1]; }
        var c = list[next];
        Prefetch(c.Name);
        if (c.Group.Length > 0 && c.Dir != 0 && !SeqTest)   // 자리가 모자라면 반대쪽 짝으로 바뀐다(구르기)
            foreach (var o in list) if (o.Group == c.Group && o.Dir == -c.Dir) { Prefetch(o.Name); break; }
        if (!SeqTest) Prefetch("clip_" + HangClip + ".png");
    }

    // ------------------------------------------------------------------ 2. 틱

    /// <summary>고해상도 시계(ms) — 정시 알림의 시간 재기(TickCount64 는 15.6 ms 눈금).</summary>
    private static long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    private const uint WM_ANIMTICK = 0x8032;   // WM_APP + 0x32: 틱 스레드가 보내는 그리기 틱
    private static int _tickGen, _tickPending;
    private static long _tickPeriod;           // Stopwatch 눈금
    private static bool _tickThreadOn;

    /// <summary>정한 간격(ms)을 장 길이에 맞춘다: 62 → 62.5, 78 → 78.125, 46 → 46.875(15.625 의 배수로 정한 값들).</summary>
    private static double PeriodMs(int ms) => ms >= 200 ? ms : Math.Round(ms / 15.625) * 15.625;

    /// <summary>틱 스레드를 시작하거나(이미 돌면 간격만 바꾼다). 만들 수 없으면 false(호출한 쪽이 SetTimer).</summary>
    private static bool StartTicks(int ms)
    {
        Interlocked.Exchange(ref _tickPeriod, (long)(PeriodMs(ms) * Stopwatch.Frequency / 1000));
        if (_tickThreadOn) return true;
        nint timer = CreateWaitableTimerExW(0, null, 2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003 /* TIMER_ALL_ACCESS */);
        if (timer == 0) return false;
        int gen = Interlocked.Increment(ref _tickGen);
        nint hwnd = _hwnd;
        Interlocked.Exchange(ref _tickPending, 0);
        var th = new Thread(() => TickLoop(timer, hwnd, gen)) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "1Key walker tick" };
        try { th.Start(); } catch { CloseHandle(timer); return false; }
        _tickThreadOn = true;
        return true;
    }

    private static void StopTicks()
    {
        if (!_tickThreadOn) return;
        Interlocked.Increment(ref _tickGen);   // 돌던 스레드는 다음에 깨어나면 끝난다
        _tickThreadOn = false;
    }

    private static void TickLoop(nint timer, nint hwnd, int gen)
    {
        try
        {
            long next = Stopwatch.GetTimestamp() + Interlocked.Read(ref _tickPeriod);
            while (Volatile.Read(ref _tickGen) == gen)
            {
                long wait = next - Stopwatch.GetTimestamp();
                if (wait > 0)
                {
                    long due = -Math.Max(1, wait * 10_000_000 / Stopwatch.Frequency);   // 100 ns 단위, 음수 = 지금부터
                    if (SetWaitableTimerEx(timer, ref due, 0, 0, 0, 0, 0)) WaitForSingleObject(timer, 0xFFFFFFFF);
                    else Thread.Sleep((int)Math.Max(1, wait * 1000 / Stopwatch.Frequency));
                }
                if (Volatile.Read(ref _tickGen) != gen) break;
                // 앞 틱을 창이 아직 처리 못 했으면 쌓지 않는다(밀린 틱이 한꺼번에 와서 건너뛰는 일이 없게)
                if (Interlocked.Exchange(ref _tickPending, 1) == 0 && !Native.PostMessageW(hwnd, WM_ANIMTICK, 0, 0)) Interlocked.Exchange(ref _tickPending, 0);
                long per = Interlocked.Read(ref _tickPeriod);
                next += per;
                long now = Stopwatch.GetTimestamp();
                if (now - next > per * 3) next = now + per;   // 크게 밀렸으면(절전 등) 지금부터 다시
            }
        }
        catch { }
        finally { CloseHandle(timer); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimerEx(nint timer, ref long dueTime, int period, nint completion, nint arg, nint wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint ms);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
