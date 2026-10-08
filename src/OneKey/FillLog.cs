namespace OneKey;

/// <summary>
/// 최근 채우기 결과(2026-10-02 사용자 요청: Nexacro 업무 사이트 비밀번호가 가끔 안 들어가는 원인을 일반 실행에서도 보려고). 고급 › 진단 ›
/// "최근 채우기 결과"에 보인다. **메모리에만** 두고(1Key 를 끄면 사라짐) 최근 20건까지. 입력한 값·아이디·비밀번호·항목 이름·
/// 주소는 남기지 않는다(사용자 조건: "비밀번호 등은 저장하지 말아줘"). 남기는 것: 시각, 경로([채우기]/단축키), 몇 칸 중 몇 칸,
/// 결과 또는 멈춘 이유 문구(1Key 의 안내 문구, 값 없음), 커서를 옮긴 방법 단계, 보조 판정 사용 여부, 커서 옮기기를 다시 한 횟수, Tab 으로 옮겼는지, 클릭했는데 커서가 없었는지.
/// </summary>
internal static class FillLog
{
    internal readonly record struct Entry(DateTime Time, bool ByButton, int Done, int Total, bool Ok, string Reason, int FocusMethod, bool FocusFallback, int FocusRetries, bool TabUsed, bool ClickMissed, (int Method, int Retries, bool Fallback, string Probe)[] Inputs, int Platform = 2);

    private const int Max = 20;
    private static readonly object _lock = new();
    private static readonly List<Entry> _items = new();

    public static void Add(Entry e)
    {
        lock (_lock)
        {
            _items.Add(e);
            if (_items.Count > Max) _items.RemoveAt(0);
        }
    }

    public static List<Entry> Snapshot() { lock (_lock) return new(_items); }

    /// <summary>진단 화면 글: 최근 것부터.</summary>
    public static string Describe()
    {
        var items = Snapshot();
        if (items.Count == 0) return T.DiagFillLogEmpty + "\n\n" + T.DiagFillLogNote;
        var sb = new System.Text.StringBuilder();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Entry e = items[i];
            string result = e.Ok ? T.DiagFillOk : e.Reason;
            sb.Append(T.DiagFillLogLine(e.Time.ToString("HH:mm:ss"), e.ByButton ? "[" + T.FillButton + "]" : T.DiagFillViaHotkey, e.Done.ToString(), e.Total.ToString(), result));
            sb.Append(" · ").Append(e.Platform switch { 1 => T.DiagFillNexacro, 0 => T.DiagFillGeneral, _ => T.DiagFillPlatformUnknown });
            // 칸별: "입력 1 클릭 · 입력 2 Tab(다시 1번)"
            for (int k = 0; k < e.Inputs.Length; k++)
            {
                var (m, r, f, probe) = e.Inputs[k];
                // 31·32 = 화면 전체(MainFrame)에서 Shift+Tab 1·2번으로 도착, 40–42 = MainFrame 에서 출발했지만 0–2번 뒤 멈춤
                string how = m switch
                {
                    1 => T.DiagFillHowFocus, 2 => T.DiagFillHowAction, 3 => T.DiagFillHowClick, 4 => T.DiagFillHowTab, 5 => T.DiagFillHowShiftTab,
                    31 or 32 => T.DiagFillHowMain((m - 30).ToString()),
                    40 or 41 or 42 => T.DiagFillHowMainStop((m - 40).ToString()),
                    _ => T.DiagFillHowFailed,
                };
                sb.Append("\n    ").Append(T.EditInput(k + 1)).Append(": ").Append(how);
                if (f) sb.Append(" · ").Append(T.DiagFillFallback);
                if (r > 0) sb.Append(" · ").Append(T.DiagFillRetries(r.ToString()));
                // 실패한 칸: 마지막 확인에서 본 것("포커스된 곳 Document · 칸 스스로 아니요"), 값 없음
                // "#mf=n": 화면 전체(MainFrame) 출발 확인 결과(0 허용, 8 팝업·알림 창 또는 확인 불가, 그 밖 출발 조건 아님)
                int mf = probe.IndexOf("#mf=", StringComparison.Ordinal);
                string mfr = mf >= 0 ? probe[(mf + 4)..] : "";
                if (mf >= 0) probe = probe[..mf];
                string[] pr = probe.Split('|');
                if (pr.Length == 3) sb.Append("\n      ").Append(T.DiagFillProbe(pr[0] == "-" ? "?" : pr[0], pr[1] == "1" ? T.CommonYes : pr[1] == "0" ? T.CommonNo : "?"));
                if (mfr.Length > 0) sb.Append("\n      ").Append(mfr == "0" ? T.DiagFillMainOk : mfr == "8" ? T.DiagFillMainPopup : mfr == "9" ? T.DiagFillMainBrowserList : T.DiagFillMainNo(mfr));
            }
            if (e.ClickMissed) sb.Append("\n    ").Append(T.DiagFillClickMissed);
            sb.Append('\n');
        }
        return sb.Append('\n').Append(T.DiagFillLogNote).ToString();
    }
}
