using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 고르게 뛰는 그림 틱(0.5.15-A 잠금 위젯 고양이 — 작업 표시줄 고양이 CatClips 의 틱 스레드와 같은 방식): 고해상도 대기 타이머 스레드가 period 마다
/// 창에 메시지 하나를 보낸다. 앞 틱을 아직 처리하지 않았으면 보내지 않는다(쌓이지 않게 — 늦으면 한 장 늦을 뿐 몰아서 그리지 않음).
/// SetTimer(62) 는 15.6 ms 눈금에 맞춰 62.5 ms 와 78 ms 가 섞여 장이 고르지 않았다. 받은 쪽은 처리 끝에 <see cref="Done"/> 을 부른다.
/// </summary>
internal sealed class AnimTicker
{
    private readonly uint _msg;
    private int _gen, _pending;
    private long _period;
    private bool _on;

    public AnimTicker(uint msg) { _msg = msg; }

    /// <summary>틱 번호(세대): 멈추거나 다시 시작하면 바뀐다 — 받은 메시지의 wParam 과 같을 때만 처리한다.</summary>
    public int Gen => Volatile.Read(ref _gen);
    public bool Running => _on;

    public bool Start(nint hwnd, int ms)
    {
        double per = ms >= 200 ? ms : Math.Round(ms / 15.625) * 15.625;   // 62 → 62.5 ms
        Interlocked.Exchange(ref _period, (long)(per * Stopwatch.Frequency / 1000));
        if (_on) return true;
        nint timer = CreateWaitableTimerExW(0, null, 2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003);
        if (timer == 0) return false;
        int gen = Interlocked.Increment(ref _gen);
        Interlocked.Exchange(ref _pending, 0);
        var th = new Thread(() => Loop(timer, hwnd, gen)) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "1Key anim tick" };
        try { th.Start(); } catch { CloseHandle(timer); return false; }
        _on = true;
        return true;
    }

    public void Stop()
    {
        if (!_on) return;
        Interlocked.Increment(ref _gen);
        _on = false;
    }

    /// <summary>틱 하나를 처리했다: 다음 틱을 보내도 된다.</summary>
    public void Done() => Interlocked.Exchange(ref _pending, 0);

    private void Loop(nint timer, nint hwnd, int gen)
    {
        try
        {
            long next = Stopwatch.GetTimestamp() + Interlocked.Read(ref _period);
            while (Volatile.Read(ref _gen) == gen)
            {
                long wait = next - Stopwatch.GetTimestamp();
                if (wait > 0)
                {
                    long due = -Math.Max(1, wait * 10_000_000 / Stopwatch.Frequency);
                    if (SetWaitableTimerEx(timer, ref due, 0, 0, 0, 0, 0)) WaitForSingleObject(timer, 0xFFFFFFFF);
                    else Thread.Sleep((int)Math.Max(1, wait * 1000 / Stopwatch.Frequency));
                }
                if (Volatile.Read(ref _gen) != gen) break;
                if (Interlocked.Exchange(ref _pending, 1) == 0 && !Native.PostMessageW(hwnd, _msg, gen, 0)) Interlocked.Exchange(ref _pending, 0);
                long per = Interlocked.Read(ref _period);
                next += per;
                long now = Stopwatch.GetTimestamp();
                if (now - next > per * 3) next = now + per;
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
