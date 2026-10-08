using System.Text;

namespace OneKey;

/// <summary>
/// 특정 창에 왜 입력이 들어가지 않는지 알아내기 위한 진단.
/// 사내 뱅킹 결재창처럼 입력이 막히는 경우 원인을 한 번에 특정하기 위해 쓴다.
/// </summary>
internal static unsafe class Diag
{
    public static string Inspect(nint target)
    {
        var sb = new StringBuilder();
        bool meElevated = Native.IsElevated();

        // 표시 글은 다국어 표(diag.*). 칸 맞춤 공백 대신 "이름: 값" 으로 쓴다(언어마다 이름 폭이 달라서).
        sb.Append("■ 1Key\n");
        sb.Append("    ").Append(T.DiagPriv).Append(": ").Append(meElevated ? T.DiagAdmin : T.DiagUser).Append('\n');
        sb.Append("    ").Append(T.DiagBits).Append(": ").Append(IntPtr.Size == 8 ? T.DiagBit64 : T.DiagBit32).Append("\n\n");

        if (target == 0)
            return sb.Append("■ ").Append(T.DiagTarget).Append("\n    ").Append(T.DiagNoActive).Append('\n').ToString();

        string title = Native.GetWindowText(target);
        string cls = Native.GetClassName(target);
        uint tid = Native.GetWindowThreadProcessId(target, out uint pid);

        string exe = T.CommonUnknown;
        string targetPriv = T.CommonUnknown;
        bool targetHigher = false;

        nint hProc = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc != 0)
        {
            try
            {
                Span<char> buf = stackalloc char[520];
                uint size = (uint)buf.Length;
                fixed (char* p = buf)
                    if (Native.QueryFullProcessImageNameW(hProc, 0, p, ref size))
                        exe = new string(p, 0, (int)size);

                if (Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out nint token))
                {
                    try
                    {
                        uint elevated = 0;
                        if (Native.GetTokenInformation(token, Native.TokenElevation, &elevated, sizeof(uint), out _))
                        {
                            targetPriv = elevated != 0 ? T.DiagAdmin : T.DiagUser;
                            targetHigher = elevated != 0 && !meElevated;
                        }
                    }
                    finally { Native.CloseHandle(token); }
                }
                else
                {
                    // 토큰을 열 수 없다 = 이 프로세스보다 무결성 수준이 높다.
                    targetPriv = T.DiagHigherProtected;
                    targetHigher = true;
                }
            }
            finally { Native.CloseHandle(hProc); }
        }
        else
        {
            targetPriv = T.DiagHigherDenied;
            targetHigher = true;
        }

        var gti = new Native.GUITHREADINFO { cbSize = (uint)sizeof(Native.GUITHREADINFO) };
        string focusCls = T.CommonNone;
        bool hasFocus = false;
        if (Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != 0)
        {
            focusCls = Native.GetClassName(gti.hwndFocus);
            hasFocus = true;
        }

        sb.Append("■ ").Append(T.DiagTarget).Append('\n');
        sb.Append("    ").Append(T.DiagWinTitle).Append(": ").Append(title.Length > 0 ? title : T.CommonNone).Append('\n');
        sb.Append("    ").Append(T.DiagClass).Append(": ").Append(cls).Append('\n');
        sb.Append("    ").Append(T.DiagProcess).Append(": ").Append(ShortName(exe)).Append("  (PID ").Append(pid).Append(")\n");
        sb.Append("    ").Append(T.DiagPriv).Append(": ").Append(targetPriv).Append('\n');
        sb.Append("    ").Append(T.DiagFocus).Append(": ").Append(focusCls).Append("\n\n");

        sb.Append("■ ").Append(T.DiagVerdict).Append('\n');
        string verdict = targetHigher ? T.DiagVHigher : !hasFocus ? T.DiagVNoFocus : T.DiagVOk;
        foreach (string line in verdict.Split('\n')) sb.Append("    ").Append(line).Append('\n');

        return sb.ToString();
    }

    private static string ShortName(string path)
    {
        int i = path.LastIndexOf('\\');
        return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
    }
}
