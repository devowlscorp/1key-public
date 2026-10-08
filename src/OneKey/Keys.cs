namespace OneKey;

/// <summary>단축키 콤보 박스에 쓰이는 가상 키 목록.</summary>
internal static class Keys
{
    /// <summary>(가상 키 코드, 표시 이름) 목록. 첫 항목은 "없음".</summary>
    public static readonly (uint Vk, string Name)[] All = Build();

    private static (uint, string)[] Build()
    {
        var list = new List<(uint, string)> { (0u, "") };   // 이름이 언어에 따라 바뀌는 키(없음·숫자패드·메뉴)는 NameOf 가 그때 언어로 만든다

        for (char c = '0'; c <= '9'; c++) list.Add((c, c.ToString()));
        for (char c = 'A'; c <= 'Z'; c++) list.Add((c, c.ToString()));
        for (uint i = 0; i < 24; i++) list.Add((0x70 + i, $"F{i + 1}"));   // F1 ~ F24
        for (uint i = 0; i < 10; i++) list.Add((0x60 + i, ""));

        // 평소 잘 쓰지 않아 단축키로 적합한 특수 키
        list.Add((0x2C, "PrintScreen"));
        list.Add((0x91, "ScrollLock"));
        list.Add((0x13, "Pause"));
        list.Add((0x90, "NumLock"));
        list.Add((0x14, "CapsLock"));
        list.Add((0x5D, ""));
        list.Add((0x1B, "Esc"));
        list.Add((0x09, "Tab"));
        list.Add((0x08, "Backspace"));

        list.Add((0x0D, "Enter"));   // 목록 입력 확정 키(기본 Ctrl+Alt+Enter)
        list.Add((0x20, "Space"));
        list.Add((0x2D, "Insert"));
        list.Add((0x2E, "Delete"));
        list.Add((0x24, "Home"));
        list.Add((0x23, "End"));
        list.Add((0x21, "PageUp"));
        list.Add((0x22, "PageDown"));
        list.Add((0x25, "←"));
        list.Add((0x26, "↑"));
        list.Add((0x27, "→"));
        list.Add((0x28, "↓"));
        list.Add((0xC0, "`"));
        list.Add((0xBD, "-"));
        list.Add((0xBB, "="));
        list.Add((0xDB, "["));
        list.Add((0xDD, "]"));
        list.Add((0xDC, "\\"));
        list.Add((0xBA, ";"));
        list.Add((0xDE, "'"));
        list.Add((0xBC, ","));
        list.Add((0xBE, "."));
        list.Add((0xBF, "/"));

        return list.ToArray();
    }

    /// <summary>
    /// 조합 키(Ctrl/Alt/Shift/Win) 없이 단독으로 단축키에 써도 평소 입력을 방해하지 않는 키인지.
    /// PrintScreen, ScrollLock, Pause 처럼 거의 안 쓰는 키만 허용한다.
    /// </summary>
    public static bool AllowsBareHotkey(uint vk)
    {
        if (vk >= 0x7C && vk <= 0x87) return true;    // F13 ~ F24
        switch (vk)
        {
            case 0x2C:   // PrintScreen
            case 0x91:   // ScrollLock
            case 0x13:   // Pause
            case 0x5D:   // 메뉴(Apps)
                return true;
            default:
                return false;
        }
    }

    public static int IndexOf(uint vk)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Vk == vk) return i;
        return 0;
    }

    public static string NameOf(uint vk)
    {
        int i = IndexOf(vk);
        if (i <= 0) return T.CommonNone;
        if (vk is >= 0x60 and <= 0x69) return T.KeyNumpad(vk - 0x60);
        if (vk == 0x5D) return T.KeyApps;
        return All[i].Name;
    }
}
