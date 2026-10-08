using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 사이트 채우기(브라우저 UI Automation): 연결 칩·[채우기] 버튼·감시. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 사이트 채우기 (브라우저, UI Automation)

    /// <summary>편집 화면의 연결 상태 글과 [연결]/[해제] 글을 지금 편집 중인 연결(_editSite)에 맞춘다.</summary>

    /// <summary>사이트 채우기 줄 k 의 글: "입력 n → " + 연결한 칸 / 주소, 프로그램, 연결 없음, 또는 (입력 1 이 프로그램이면) Tab 안내.</summary>
    private string SiteRowText(int k)
    {
        SiteLink? l = _editSites[k];
        AppLink? app = k == 0 ? _editApp : null;
        return app is not null ? AppLinks.Describe(app)
             : l is null ? (k > 0 && _editApp is not null ? T.EditAppTabNext : T.EditSiteNone)
             : FieldDescription(l) + " · " + SiteUrl.Display(l.Url);
    }

    private void UpdateSiteRow()
    {
        for (int k = 0; k < _editInputs; k++)
        {
            nint t = C(SiteTextId(k)), b = C(SiteBtnId(k));
            if (t == 0) continue;
            bool linked = _editSites[k] is not null || (k == 0 && _editApp is not null) || (k == 0 && _editInputs == 2 && !_editPerInput && _editSites[1] is not null);
            string text = SiteRowText(k);
            Native.SetText(t, text);
            SetTip(t, text);   // 한 줄로 줄여 보이므로 마우스를 올리면 전체
            _staticStyle[t] = (Theme.CardBrush, Theme.SecondaryText);
            Native.InvalidateRect(t, 0, true);
            if (b == 0) continue;   // 짝 연결(입력 2개): 입력 2 줄에는 버튼이 없다
            Native.SetText(b, linked ? T.EditSiteUnlink : T.EditSiteLink);
            // 입력 1 이 프로그램 칸에 연결되면 입력 2 부터는 연결할 수 없다(그 칸에서 Tab 으로 넘어감): [연결]을 끄고 그 줄에 이유를 둔다
            // (2026-10-01 사용자: 누를 수 있는 버튼은 의미 없는 연결을 유도하고, 숨기면 "왜 없지" 하고 헷갈린다 → 비활성 + 설명)
            Native.EnableWindow(b, !(k > 0 && _editApp is not null));
            Native.InvalidateRect(b, 0, false);
        }

    }

    /// <summary>연결한 입력란을 사람이 알아볼 이름으로: 페이지가 준 이름, 없으면 "비밀번호 칸 2" 처럼 종류와 순서.</summary>
    /// <summary>연결 주소의 호스트만(목록 부제에 짧게). "https://a.b.c/x" → "a.b.c".</summary>
    private static string SiteHost(string url)
    {
        string d = SiteUrl.Display(url);
        int sep = d.IndexOf("://", StringComparison.Ordinal);
        if (sep >= 0) d = d[(sep + 3)..];
        int slash = d.IndexOf('/');
        return slash < 0 ? d : d[..slash];
    }

    /// <summary>연결한 칸을 한 줄로: "이름(또는 종류) (id 끝부분)". id 는 마지막 '.' 뒤만(Nexacro 처럼 긴 경로형 id).</summary>
    private static string FieldDescription(SiteLink l)
    {
        string id = l.FieldId;
        int dot = id.LastIndexOf('.');
        if (dot >= 0 && dot < id.Length - 1) id = id[(dot + 1)..];
        string label = FieldLabel(l);
        if (id.Length == 0 && l.FieldName.Length == 0 && l.Class.Length > 0)   // id·이름 없는 칸: 무엇으로 찾는지
            return label + " (" + (l.Class == "*" ? T.SiteFieldSole : "." + l.Class) + ")";
        return id.Length > 0 && id != label ? label + " (" + id + ")" : label;
    }

    private static string FieldLabel(SiteLink l)
        => l.FieldName.Length > 0 ? l.FieldName : l.Password ? T.SiteFieldPassword(l.Index + 1) : T.SiteFieldText(l.Index + 1);

    /// <summary>
    /// 편집 화면의 [연결]: 본창을 내리고 칩을 띄운다. 사용자가 브라우저에서 넣을 입력란을 클릭한 뒤 칩의 [연결]을 누르면
    /// 그 순간 포커스가 있는 입력란을 UIA 로 읽어 연결 초안에 둔다(<see cref="LinkConfirm"/>). 저장은 편집 화면의 [저장].
    /// </summary>
    private void StartLinkChip(int input = 0)
    {
        if (!Unlocked || _cur != Screen.Edit || input < 0 || input >= _editInputs) return;
        ReleaseChip();
        _linkInput = input;
        string name = Native.GetWindowText(C(IdEName)).Trim();
        if (name.Length == 0) name = T.SlotDefault(_editSlot + 1);
        if (_editInputs > 1) name += " · " + T.EditInput(input + 1);
        // 입력 2개 항목의 [연결](짝 연결): 단계 칩 — ① 아이디 칸 줄, 끝나면 [연결 완료]로 바뀌고 ② 비밀번호 칸 줄이 생긴다(2026-10-03 사용자)
        bool steps = input == 0 && _editInputs == 2 && !_editPerInput;
        string title = T.ChipLinkTitle(steps ? Native.GetWindowText(C(IdEName)).Trim() is { Length: > 0 } nm ? nm : T.SlotDefault(_editSlot + 1) : name);
        string hint = steps ? T.ChipStepId : T.ChipLinkHint;
        _chipGen++;
        nint chip = Chip.Create(_hwnd, _chipGen, title, hint, _dpi, WorkArea.For(_hwnd), T.EditSiteLink, steps);
        if (chip == 0) { Msg(T.ChipCreateFailed, AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
        _chip = chip; _chipSlot = _editSlot; _chipHotkey = false; _chipHint = hint; _chipLink = true;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        Native.ShowWindow(_chip, Native.SW_SHOWNOACTIVATE);
        Native.SetTimer(_hwnd, TimerChipExpire, (uint)ChipMs, 0);
        UpdateTrayTip();
    }

    /// <summary>
    /// 링크 칩의 [연결]: 누른 순간의 활성 창이 브라우저이고 포커스가 그 페이지의 입력란이면 연결 초안으로 받는다.
    /// UIA 읽기는 기다리지 않고 올린다(UI 스레드가 멈추지 않게, Codex V55-3). 결과는 칩·세대·잠금·화면이 그대로일 때만 쓴다.
    /// </summary>
    private void LinkConfirm(Injector.Request target)
    {
        if (!Unlocked || _cur != Screen.Edit) { ReleaseChip(); return; }
        nint w = target.Window;
        if (!Uia.IsBrowser(w))
        {
            // 윈도우 프로그램의 칸(2026-10-01 인하우스 뱅킹): 첫 입력만. 나머지 입력은 그 칸에서 Tab 으로 넘어간다.
            if (_linkInput != 0) { _testLinkResult = 42; ChipHint(T.ChipAppOnlyFirst); return; }
            if (AppLinks.Capture(target) is not AppLink al) { _testLinkResult = 40; ChipHint(T.ChipAppNotField); return; }
            _testLinkResult = 41;
            _editApp = al;
            for (int k = 0; k < Slot.MaxInputs; k++) _editSites[k] = null;   // 웹 연결과 함께 쓰지 않는다
            _editDirty = true;
            ReleaseChip();
            ShowMainWindow();
            UpdateSiteRow();
            return;
        }
        int gen = ++_reqGen, chipGen = _chipGen;
        _testLinkResult = 255;
        if (_linkInput == 0 && _editInputs == 2 && !_editPerInput)
        {
            // [연결] 한 번(2026-10-03 단순화, Codex 05:45-C): 누른 칸과 같은 폼·컨테이너 안의 반대 종류 칸 하나를 짝으로. 못 찾으면 그 칸을 더 누르게 한다.
            Uia.Post(_hwnd, job => Uia.CapturePair(w, job), 3000, null, (ok, pr) =>
            {
                if (gen != _reqGen || chipGen != _chipGen || !_chipLink || !Unlocked || _cur != Screen.Edit) return;
                if (!ok) { _testLinkResult = 2; ChipHint(T.DiagSiteUnavailable); return; }
                if (pr.Error != Uia.CaptureError.None)
                {
                    _testLinkResult = 10 + (int)pr.Error;
                    ChipHint(pr.Error switch { Uia.CaptureError.NotBrowser => T.ChipLinkNotBrowser, Uia.CaptureError.NoIdentity => T.ChipLinkNoIdentity, _ => T.ChipLinkFailed });
                    return;
                }
                string? purl = SiteUrl.Normalize(pr.Url);
                if (purl is null) { _testLinkResult = 30; ChipHint(T.ChipLinkBadUrl); return; }
                SiteLink L(Uia.Field f) => new(purl, f.AutomationId, f.Name, f.Password, f.Index, f.Class);
                // 아이디(일반 칸) = 입력 1, 비밀번호 칸 = 입력 2
                Uia.Field? idF = pr.Field.Password ? pr.Partner : pr.Field, pwF = pr.Field.Password ? pr.Field : pr.Partner;
                _editApp = null;
                _editDirty = true;
                if (idF is Uia.Field a) _editSites[0] = L(a);
                if (pwF is Uia.Field b) _editSites[1] = L(b);
                // 지금 차례 줄을 끝낸다(누른 칸이 무엇이었는지)
                Chip.CompleteStep(_chip, pr.Field.Password ? T.ChipStepPwDone(FieldDescription(L(pr.Field))) : T.ChipStepIdDone(FieldDescription(L(pr.Field))));
                UpdateSiteRow();
                if (pr.Partner is null && (_editSites[0] is null || _editSites[1] is null))
                {
                    // 짝을 스스로 정하지 못함(그룹웨어 등): 다음 차례 줄을 더한다 — 오류가 아니라 정상 2단계(빨간 글씨 없음)
                    _testLinkResult = 50;
                    _linkInput = _editSites[1] is null ? 1 : 0;
                    _sameFieldWarned = null;
                    _chipHint = _linkInput == 1 ? T.ChipStepPw : T.ChipStepIdNext;
                    Chip.AddStep(_chip, _chipHint, T.EditSiteLink);
                    if (_linkInput == 1 && _editSites[0] is SiteLink idLink) MoveToNextField(w, idLink, chipGen);   // 비밀번호 칸으로 커서를 옮겨 둔다
                    return;
                }
                // 짝을 한 번에 찾음: 남은 줄도 바로 완료로 보이고 잠깐 뒤 닫는다
                _testLinkResult = 51;
                if (pr.Partner is Uia.Field pf)
                {
                    Chip.AddStep(_chip, "", T.EditSiteLink);
                    Chip.CompleteStep(_chip, pf.Password ? T.ChipStepPwDone(FieldDescription(L(pf))) : T.ChipStepIdDone(FieldDescription(L(pf))));
                }
                FinishLinkChip();
            });
            return;
        }
        Uia.Post(_hwnd, job => Uia.CaptureFocused(w, job), 3000, null, (ok, cap) =>
        {
            if (gen != _reqGen || chipGen != _chipGen || !_chipLink || !Unlocked || _cur != Screen.Edit) return;   // 그 사이 잠김·취소·새 요청
            if (!ok) { _testLinkResult = 2; ChipHint(T.DiagSiteUnavailable); return; }
            if (cap.Error != Uia.CaptureError.None)
            {
                _testLinkResult = 10 + (int)cap.Error;
                ChipHint(cap.Error switch
                {
                    Uia.CaptureError.NotBrowser => T.ChipLinkNotBrowser,
                    Uia.CaptureError.NoIdentity => T.ChipLinkNoIdentity,
                    _ => T.ChipLinkFailed,
                });
                return;
            }
            string? url = SiteUrl.Normalize(cap.Url);
            if (url is null) { _testLinkResult = 30; ChipHint(T.ChipLinkBadUrl); return; }
            _testLinkResult = 1;
            if (_linkInput >= _editInputs) { ReleaseChip(); ShowMainWindow(); return; }
            var captured = new SiteLink(url, cap.Field.AutomationId, cap.Field.Name, cap.Field.Password, cap.Field.Index, cap.Field.Class);
            if (_editInputs == 2 && !_editPerInput && _editSites[1 - _linkInput] is SiteLink other && other == captured && _sameFieldWarned != captured)
            {
                // 단계 칩 ②에서 ①과 같은 칸을 누름(커서를 옮기지 않음): 알리고, 같은 칸으로 한 번 더 누르면 그대로 연결한다(2026-10-03 사용자)
                _testLinkResult = 52;
                _sameFieldWarned = captured;
                ChipHint(_linkInput == 1 ? T.ChipStepSameAsId : T.ChipStepSameAsPw);
                return;
            }
            _sameFieldWarned = null;
            _editSites[_linkInput] = captured;
            _editApp = null;   // 프로그램 연결과 함께 쓰지 않는다
            _editDirty = true;
            UpdateSiteRow();
            if (_editInputs == 2 && !_editPerInput)
            {
                // 단계 칩의 두 번째 줄: 완료로 바꾸고 잠깐 보여 준 뒤 닫는다
                SiteLink done = _editSites[_linkInput]!;
                Chip.CompleteStep(_chip, done.Password ? T.ChipStepPwDone(FieldDescription(done)) : T.ChipStepIdDone(FieldDescription(done)));
                FinishLinkChip();
                return;
            }
            ReleaseChip();
            ShowMainWindow();
        });
    }

    /// <summary>
    /// 단계 칩 ① 뒤: 아이디 칸에 커서가 그대로 있을 때만 Tab 한 번을 보내 다음 칸(보통 비밀번호 칸)으로 옮겨 둔다(2026-10-03 사용자).
    /// 사용자는 그 칸이 맞으면 [연결]만 누르면 된다. 보내기 전 확인: 칩이 그대로·잠금 아님·앞의 창이 그 브라우저·포커스가 연결한 아이디 칸.
    /// 무엇을 연결할지는 여전히 사용자가 [연결]을 누른 순간의 칸으로 정한다(Tab 이 엉뚱한 곳으로 가도 그 칸을 저절로 연결하지 않음).
    /// </summary>
    private void MoveToNextField(nint w, SiteLink idLink, int chipGen)
    {
        int gen = Injector.Generation;
        nint me = _hwnd;
        var t = new Thread(() =>
        {
            try
            {
                Thread.Sleep(150);
                if (chipGen != _chipGen || !_cfg.IsUnlocked || Native.GetForegroundWindow() != w) return;
                bool OnId() => Uia.Run(job => Uia.IsFocusedOn(w, idLink, job), 1000, null, out bool on) && on;
                if (!OnId() || !Injector.TryBegin()) return;
                try { Injector.PressTabInField(Injector.Capture(), gen, Environment.TickCount64 + 2000, OnId); }
                finally { Injector.Abandon(); }
            }
            catch { }
        }) { IsBackground = true };
        t.Start();
    }

    /// <summary>단계 칩의 모든 줄이 끝났다: 완료 표시를 잠깐(0.9초) 보여 준 뒤 칩을 닫고 편집 화면으로 돌아간다.</summary>
    private void FinishLinkChip()
    {
        Native.KillTimer(_hwnd, TimerChipExpire);
        Native.SetTimer(_hwnd, TimerChipDone, 900, 0);
    }

    /// <summary>잠금을 푼 동안, 내용과 연결이 있는 항목이 하나라도 있을 때만 활성 브라우저를 주기적으로 확인한다.</summary>
    private void UpdateSiteWatch()
    {
        bool want = Unlocked && _cfg.Slots.Any(s => (s.HasAnySite || s.App is not null) && s.HasPassword);
        if (want == _siteWatch) { if (!want) HideFill(); return; }
        _siteWatch = want;
        _uiaGen++;   // 켜고 끌 때마다 이전 확인 결과는 버린다
        _siteProbePending = false; _probePendingId = 0;
        if (want) Native.SetTimer(_hwnd, TimerSite, SiteTickMs, 0);
        else { Native.KillTimer(_hwnd, TimerSite); HideFill(); _siteLast = null; }
    }

    /// <summary>[채우기]를 숨긴다. 숨기면 그 버튼의 토큰도 무효: 이미 큐에 들어간 클릭은 처리되지 않는다 (R60-3).</summary>
    private void HideFill() { FillButton.Hide(); _fillToken = 0; _fillSignature = ""; _appFillSlot = -1; }

    // 프로그램 연결의 [채우기](2026-10-01 사용자 요청): 지금 버튼이 가리키는 슬롯(-1 = 웹 버튼이거나 없음)
    private int _appFillSlot = -1;

    /// <summary>
    /// 앞의 창이 브라우저가 아니면: 프로그램에 연결한 항목 가운데 그 창(실행 파일·창 클래스)에 맞고 연결한 칸이 창 안에 보이는 것이 있으면
    /// 그 칸 옆에 [채우기]를 띄운다. 누르면 단축키와 같은 프로그램 채우기(AppLinkedFill). 하나도 없으면 숨긴다. UI 스레드에서 Win32 만 쓴다(가볍다).
    /// </summary>
    private void AppTick(nint fg)
    {
        string? exe = null;
        for (int i = 0; i < Config.SlotCount; i++)
        {
            Slot s = _cfg.Slots[i];
            if (s.App is not AppLink l || !l.Usable || !s.HasPassword || Native.GetClassName(fg) != l.WindowClass) continue;
            exe ??= AppLinks.PathOf(fg);
            if (!string.Equals(exe, l.Exe, StringComparison.OrdinalIgnoreCase)) continue;
            if (!AppLinks.ButtonAnchor(fg, l, out Native.RECT fr) || !Native.GetWindowRect(fg, out Native.RECT wr)) continue;
            string sig = "app|" + fg + "|" + fr.left + "," + fr.top + "|" + i;
            if (_fillToken == 0 || sig != _fillSignature || _fillTokenGen != _uiaGen || !FillButton.Visible)
            {
                _fillToken = ++_fillTokenSeq; if (_fillToken == 0) _fillToken = ++_fillTokenSeq;
                _fillSignature = sig; _fillTokenGen = _uiaGen;
            }
            _appFillSlot = i;
            string name = s.DisplayName(i);
            FillButton.ShowAt(_hwnd, fr, wr, FillEnter(new[] { i }) ? T.FillTipEnter(name) : T.FillTip(name), _fillToken);
            return;
        }
        HideFill();
    }

    /// <summary>연결 목록. 키 = 슬롯 × Slot.MaxInputs + 입력 번호(0 = 첫 입력): 한 항목의 입력마다 다른 칸에 연결할 수 있다.</summary>
    private List<(int Slot, SiteLink Link)> SiteLinks()
    {
        var list = new List<(int, SiteLink)>();
        for (int i = 0; i < Config.SlotCount; i++)
        {
            Slot s = _cfg.Slots[i];
            if (!s.HasPassword) continue;
            for (int k = 0; k < s.InputCount; k++)
                if (s.SiteOf(k) is SiteLink l && s.ValueOf(k).Length > 0) list.Add((i * Slot.MaxInputs + k, l));
        }
        return list;
    }

    /// <summary>주기적 확인: 활성 창이 브라우저면 UIA 스레드에 페이지 확인을 올린다(하나씩). 결과는 WM_SITE_PROBE 로 온다.</summary>
    private void SiteTick()
    {
        if (!Unlocked) { UpdateSiteWatch(); return; }
        if (_fillRunning || _siteProbePending) return;
        nint fg = Native.GetForegroundWindow();
        if (!Uia.IsBrowser(fg)) { _siteLast = null; AppTick(fg); return; }
        var links = SiteLinks();
        if (links.Count == 0) { UpdateSiteWatch(); return; }
        _siteProbePending = true;
        int gen = _uiaGen, id = ++_probeSeq;
        _probePendingId = id;
        Uia.Post(_hwnd, job => Uia.Probe(fg, links), 2500, null, (ok, r) =>
        {
            if (_probePendingId == id) _siteProbePending = false;   // 자기가 건 대기 표시만 푼다 (R60-1)
            else if (_siteProbePending) _testProbeStaleKept++;      // 시험 관찰값: 늦게 온 옛 확인이 새 확인의 대기 표시를 지우지 않았다
            if (gen != _uiaGen) return;   // 그 사이 잠금·감시 끔: 이 결과는 버린다
            _testProbeDone++;
            OnSiteProbe(ok ? r : null, gen);
        });
    }

    /// <summary>확인 결과: 같은 브라우저 창이 아직 앞에 있고 연결한 입력란이 보이면 그 옆에 [채우기]를, 아니면 숨긴다.</summary>
    private void OnSiteProbe(Uia.ProbeResult? r, int gen)
    {
        if (r is null) { HideFill(); _siteLast = null; return; }
        _siteLast = r; _siteLastGen = gen;
        if (!Unlocked || _fillRunning || r.Url is null || Native.GetForegroundWindow() != r.Window) { HideFill(); return; }
        Native.GetWindowRect(r.Window, out Native.RECT wr);
        var visible = r.Hits.Where(h => !h.Field.Offscreen && h.Field.Rect.right > h.Field.Rect.left && h.Field.Rect.bottom > h.Field.Rect.top
                                        && h.Field.Rect.top >= wr.top && h.Field.Rect.bottom <= wr.bottom).ToList();
        if (visible.Count == 0) { HideFill(); return; }
        Uia.Hit anchor = visible.OrderBy(h => h.Field.Rect.top).ThenBy(h => h.Field.Rect.left).First();
        string names = string.Join(", ", r.Hits.Select(h => _cfg.Slots[h.Slot / Slot.MaxInputs].DisplayName(h.Slot / Slot.MaxInputs)).Distinct());
        names = FillEnter(visible.Select(h => h.Slot / Slot.MaxInputs)) ? T.FillTipEnter(names) : T.FillTip(names);
        // 같은 결과면 토큰 유지, 달라졌거나 숨겼다 다시 보이면 새 토큰 (R60-3)
        string sig = r.Window + "|" + r.Url + "|" + string.Join(";", r.Hits.Select(h => h.Slot + ":" + h.Field.Password + ":" + h.Field.AutomationId + ":" + h.Field.Name + ":" + h.Field.Index + ":" + h.Field.Offscreen));
        if (_fillToken == 0 || sig != _fillSignature || _fillTokenGen != gen || !FillButton.Visible)
        {
            _fillToken = ++_fillTokenSeq; if (_fillToken == 0) _fillToken = ++_fillTokenSeq;
            _fillSignature = sig; _fillTokenGen = gen;
        }
        FillButton.ShowAt(_hwnd, anchor.Field.Rect, wr, names, _fillToken);
    }

    /// <summary>
    /// [채우기] 뒤 Enter (2026-10-01 사용자 결정: 항목 설정을 따른다). 채우는 항목이 모두 "입력 후 Enter" 켜짐이고
    /// "브라우저에서는 Enter 보내지 않기"가 꺼져 있을 때만. 여러 항목을 한 번에 채우는데 하나라도 아니면 보내지 않는다.
    /// </summary>
    private bool FillEnter(IEnumerable<int> slots)
    {
        bool any = false;
        foreach (int i in slots.Distinct())
        {
            Slot s = _cfg.Slots[i];
            if (!s.PressEnter || s.NoEnterInBrowser) return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// [채우기]: 마지막 확인에서 찾은 입력란마다(화면 위에서 아래 순서) — 페이지 주소가 그대로인지, 그 입력란에 커서를 옮겨 정말 그 칸인지
    /// (같은 요소·같은 비밀번호 칸 여부) UIA 로 확인한 뒤 지금처럼 키 입력으로 넣는다. 끝에 항목 설정대로 Enter(FillEnter). 잠금·다른 창으로 가면 멈춘다.
    /// </summary>
    private void FillNow(int token)
    {
        // 클릭이 싣고 온 토큰이 지금 보이는 버튼의 것이고, 그 토큰을 만든 확인 결과의 세대가 지금과 같아야 한다 (R60-3)
        if (token == 0 || token != _fillToken || _fillTokenGen != _uiaGen) { if (!Unlocked) HideFill(); return; }
        if (!Unlocked) { HideFill(); ShowBalloon(AppTitle, T.FillLocked, Native.NIIF_WARNING); ShowMainWindow(); return; }
        if (_appFillSlot >= 0)   // 프로그램 칸 옆의 [채우기]
        {
            int slot = _appFillSlot;
            HideFill();
            if (_cfg.Slots[slot].App is AppLink app && !_fillRunning) AppLinkedFill(slot, new Injector.Request(Native.GetForegroundWindow(), 0, 0), app);
            return;
        }
        Uia.ProbeResult? r = _siteLast;
        if (_fillRunning || r is null || r.Url is null || r.Hits.Count == 0) return;
        if (_siteLastGen != _uiaGen) { HideFill(); return; }   // 잠금·해제 전의 결과로 누른 늦은 클릭 (V55-4)
        if (Native.GetForegroundWindow() != r.Window) { HideFill(); return; }
        // 범위: 이 페이지에 연결된 칸 가운데 **화면에 보이는** 칸만(툴팁의 이름들). 같은 칸에 항목이 둘 이상 연결돼 있으면 어느 값인지
        // 정할 수 없으므로 아무것도 넣지 않는다.
        var visible = r.Hits.Where(h => !h.Field.Offscreen).ToList();
        if (visible.GroupBy(h => (h.Field.Password, h.Field.AutomationId, h.Field.Name, h.Field.Index)).Any(g => g.Count() > 1))
        {
            HideFill();
            ShowBalloon(AppTitle, T.FillDuplicate, Native.NIIF_WARNING);
            return;
        }
        var jobs = new List<(string Label, SiteLink Link, Slot Copy)>();
        foreach (Uia.Hit h in visible.OrderBy(h => h.Field.Rect.top).ThenBy(h => h.Field.Rect.left))
        {
            int slot = h.Slot / Slot.MaxInputs, input = h.Slot % Slot.MaxInputs;
            Slot s = _cfg.Slots[slot];
            if (input >= s.InputCount || s.SiteOf(input) is not SiteLink link || link.Url != r.Url || s.ValueOf(input).Length == 0) continue;
            // 값은 누른 이 순간 한 번만 복사한다(그 입력의 값 하나). Enter 는 칸마다가 아니라 끝에 한 번(FillEnter).
            jobs.Add((FillLabel(s, slot, input), link, new Slot { Name = s.Name, Password = s.ValueOf(input), Method = s.Method, PressEnter = false, NoClick = s.NoClick }));
        }
        if (jobs.Count == 0) return;
        _fillRunning = true;
        HideFill();
        MarkActivity();
        RunFill(r.Window, jobs, enter: FillEnter(visible.Select(h => h.Slot / Slot.MaxInputs)), byButton: true);
    }

    /// <summary>넣지 않은 이유를 알린다: 트레이 풍선 + 앱 자체 안내 창(Windows 알림이 꺼져 있어도 보이게).</summary>
    private void FillNotice(string text) { ShowBalloon(AppTitle, text, Native.NIIF_WARNING); Toast.Show(_hwnd, text, 6000, warn: true); }

    /// <summary>실패 안내에 붙이는 이름: "항목 이름" 또는 입력이 여럿이면 "항목 이름 · 입력 2".</summary>
    private static string FillLabel(Slot s, int slot, int input) => s.DisplayName(slot) + (s.InputCount > 1 ? " · " + T.EditInput(input + 1) : "");

    /// <summary>
    /// 사이트 채우기 작업: 칸마다 커서를 옮기고(확인) 모두 고른 뒤 입력한다. [채우기] 버튼과, 연결한 페이지에서 누른 단축키가 같이 쓴다.
    /// enter = 항목의 "입력 후 Enter"(브라우저에서 보내지 않기가 꺼진 때, [채우기]는 채우는 항목이 모두 그럴 때): 마지막 칸이 그대로일 때만 Enter.
    /// 호출 전에 _fillRunning = true.
    /// </summary>
    private void RunFill(nint w, List<(string Label, SiteLink Link, Slot Copy)> jobs, bool enter, bool byButton)
    {
        int gen = Injector.Generation;
        Config cfg = _cfg;
        nint me = _hwnd;
        var thread = new Thread(() =>
        {
            string? err = null, notice = null;
            // 결과는 번역 문구가 아니라 종류로 보관한다(Codex C62-1). 모든 칸이 실제로 들어갔을 때만 Ok 로 남는다.
            var outcome = Injector.FillOutcome.Ok;
            long deadline = Environment.TickCount64 + 15000;   // 칸 여러 개를 돌아도 전체 15초 안 (V55-3)
            long started = Environment.TickCount64;
            bool Expired() => Environment.TickCount64 >= deadline || Injector.TestExpireNow;   // 시험 훅: 대기 경계에서 만료 (R75-2 시험만)
            bool Valid() => Injector.Generation == gen && cfg.IsUnlocked && Native.GetForegroundWindow() == w && !Expired();
            // 글자마다 페이지 안 포커스가 방금 확인한 그 칸인지 UIA 로 본다(다른 칸·다른 탭·요소 교체면 멈춤). 답이 늦으면(1초) 멈춘다.
            bool Guard() => Uia.Run(job => Uia.FocusIsFillTarget(job), 1000, null, out bool same) && same;
            string? label = null;                  // 실패 안내에 붙일 "항목 · 입력 n"
            int focusMethodMax = 0;                // 시험 관찰값: 이 채우기에서 커서를 옮긴 방법 가운데 가장 뒤 단계(1 SetFocus, 2 기본 동작, 3 클릭)
            Injector.Request? lastTarget = null;   // 마지막으로 채운 칸(단축키 경로의 Enter)
            int done = 0;                          // 최근 채우기 결과(FillLog): 다 들어간 칸 수
            bool focusFallback = false;            // 최근 채우기 결과: 포커스 2순위 판정을 쓴 적이 있는가
            int focusRetries = 0;                  // 최근 채우기 결과: 커서 옮기기를 다시 해 본 횟수
            bool tabUsed = false, clickMissed = false;   // 최근 채우기 결과: Tab 으로 옮김 / 클릭했는데 커서가 없었음
            var inputs = new List<(int Method, int Retries, bool Fallback, string Probe)>();
            Uia.Platform platform = Uia.Platform.Unknown;   // 최근 채우기 결과: 화면 종류(칸마다 다르면 확인 불가)
            bool mainFrameUsed = false;                     // MainFrame 출발 Shift+Tab 은 한 채우기에서 한 번(최대 2번)   // 최근 채우기 결과: 칸별 방법(실패면 마지막 확인 관찰값)
            // 멈춘 이유: 모든 경로가 같은 순서(잠금·취소 → 제한 시각 → 활성 창 변경 → 그 경로의 오류)로 정한다 (Codex C62-2)
            void Stop(string? other = null)
                => (outcome, err) = Injector.ClassifyStop(Injector.Generation != gen || !cfg.IsUnlocked, Expired(), Native.GetForegroundWindow() != w, other);
            // 검증 전용: 정한 단계에서 실제 잠금(Windows 잠금 알림)과 제한 시각 만료를 겹친다 (C62-2). 1 사용권 전, 2 UIA 대기 전, 3 Capture 직후
            int testStage = Program.IsTestMode ? Interlocked.Exchange(ref _testFillStage, 0) : 0;
            if (testStage == 4) Injector.TestForceElevationOnce = true;   // 권한 부족 결과 분류 (C62-1)
            void TestAt(int stage)
            {
                if (testStage != stage) return;
                deadline = Environment.TickCount64 - 1;
                Native.PostMessageW(me, 0x02B1, 7, 0);   // WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK: 사용자의 잠금과 같은 경로
                for (int i = 0; i < 60 && cfg.IsUnlocked; i++) Thread.Sleep(50);
            }
            try
            {
                int jobIndex = -1;
                foreach (var (jobLabel, link, copy) in jobs)
                {
                    jobIndex++;
                    label = jobLabel;
                    if (!Valid()) { Stop(); break; }
                    // 순서(Codex V55-4): ① 입력 사용권을 먼저 잡는다(기다리는 동안에도 잠금·창 확인) ② 그다음 UIA 로 칸을 확인하고 커서를 옮긴다
                    // ③ 곧바로 활성 창·포커스를 잡아 ④ Ctrl+A 와 ⑤ 입력. 확인과 첫 키 사이에 긴 대기가 없게 하고, 모든 키에 같은 잠금 세대를 쓴다.
                    TestAt(1);
                    int waited = 0;
                    bool began = false;
                    while (true)
                    {
                        if (!Valid()) { Stop(); break; }   // 잡기 전에도, 기다리는 동안에도 먼저 확인한다
                        if (Injector.TryBegin()) { began = true; break; }
                        if (waited >= 3000) { outcome = Injector.FillOutcome.Failed; err = T.FillBusy; break; }
                        Thread.Sleep(50); waited += 50;
                    }
                    if (!began) break;
                    bool owned = true;   // 사용권: Send 가 끝에서 놓는다. 그 전에 멈추면 여기서 놓는다.
                    try
                    {
                        TestAt(2);
                        // 커서 옮기기(2026-10-03 단순화, Codex 05:45). 화면 종류를 먼저 본다: Nexacro / 일반 / 확인 불가(채우기 직전 현재 문서에서).
                        // ① 클릭 없이(SetFocus·기본 동작) 한 번 → ② 두 번째 칸부터는 방금 채운 칸에 커서가 그대로 있을 때만 Tab 한 번 뒤 확인만
                        // (Nexacro 는 프로그램이 넣은 커서를 되돌려, 화면이 스스로 옮기게 한다: 0.2.107 비밀번호 칸 14/14)
                        // → ③ 커서가 이 항목의 다음 연결 칸에 있으면 Shift+Tab 한 번 뒤 확인만(0.2.109 3/3)
                        // → ④ 일반이 아니면 클릭 없이 두 번 더(사이 0.35초, Nexacro 의 되돌림 대비)
                        // → ⑤ 좌표 클릭은 **일반으로 판별된 화면**에서, 항목의 고급 설정이 허락할 때만, 한 번(Nexacro·확인 불가 화면에서는 하지 않는다:
                        // Nexacro 업무 사이트 오착 두 건). 클릭했는데 커서가 그 칸에 없으면(ClickMissed) 멈춘다. 어느 경로든 연결한 그 칸인지 확인한 뒤에만 넣는다.
                        // 체크박스·버튼에 커서가 있는 경우는 자동으로 다루지 않는다: Nexacro 는 그때 포커스를 MainFrame 으로만 알려 출발점을 알 수 없다
                        // (2026-10-03 읽기 확인) — 안내("그 칸을 한 번 클릭")로 끝낸다.
                        Uia.Platform plat = Uia.Run(job => Uia.DetectPlatform(w, link, job), 1500, Valid, out Uia.Platform pl) ? pl : Uia.Platform.Unknown;
                        if (jobIndex == 0) platform = plat;
                        else if (plat != platform) platform = Uia.Platform.Unknown;
                        Uia.FocusResult fr = Uia.FocusResult.FocusFailed;
                        bool uiaOk = true, viaTab = false, viaShiftTab = false;
                        int retriesHere = 0;
                        bool TryNoClick()
                        {
                            int uiaWait = (int)Math.Clamp(deadline - Environment.TickCount64, 1, 3000);
                            if (!Uia.Run(job => Uia.FocusField(w, link, job, allowClick: false), uiaWait, Valid, out fr)) { uiaOk = false; return false; }
                            return fr != Uia.FocusResult.FocusFailed;
                        }
                        bool done1 = TryNoClick();
                        if (!done1 && uiaOk && Valid() && lastTarget is Injector.Request prev && Injector.PressTabInField(prev, gen, deadline, Guard) is null)
                        {
                            viaTab = true;
                            Thread.Sleep(Math.Max(cfg.KeyDelayMs, 150));
                            int uiaWait = (int)Math.Clamp(deadline - Environment.TickCount64, 1, 3000);
                            if (!Uia.Run(job => Uia.FocusField(w, link, job, allowClick: false, verifyOnly: true), uiaWait, Valid, out fr)) uiaOk = false;
                            done1 = fr != Uia.FocusResult.FocusFailed;
                        }
                        // ③' MainFrame 에서 출발(Codex 06:30-as, 제한 허용): 첫 칸이고 판별이 Nexacro 이며, 조회가 완전하고 UIA 포커스가 정확히 그 문서의
                        // MainFrame 이며 덮개·팝업이 없을 때만(Uia.MainFrameStart). 체크박스 등 실제 어느 부품인지는 알 수 없다.
                        // Shift+Tab 은 한 채우기 전체에서 최대 2번: 첫 키 뒤 이 칸이면 끝, 다음 연결 칸(비밀번호)이면 두 번째, 그 밖(여전히 MainFrame·
                        // 다른 요소·확인 실패)이면 즉시 멈춘다. 멈추면 재시도·클릭 없이 안내로 끝낸다. 값은 도착 확인 뒤에만 보낸다.
                        int mainSteps = 0, mainReason = -1;   // mainReason: MainFrame 출발 첫 확인의 reason(-1 = 확인하지 않음), 최근 채우기 결과에 남긴다(Codex 07:45-aw)
                        bool mainTried = false;
                        if (!done1 && uiaOk && Valid() && jobIndex == 0 && plat == Uia.Platform.Nexacro && !mainFrameUsed)
                        {
                            bool AtMain() => Uia.Run(job => (Uia.MainFrameStart(w, link, job, out int why), why), 1500, null, out (bool On, int Why) m) && m.On;
                            bool atMain = AtMain();
                            mainReason = Uia.LastMainFrame;
                            if (atMain)
                            {
                                mainTried = true;
                                mainFrameUsed = true;   // 한 채우기에서 한 번만(상한 2번은 이 안에서)
                                SiteLink? nextLink = jobs.Count > 1 ? jobs[1].Link : null;
                                bool OnNext() => nextLink is not null && Uia.Run(job => Uia.IsFocusedOn(w, nextLink, job), 1000, null, out bool on) && on;
                                Func<bool> from = AtMain;
                                for (int step = 0; step < 2 && uiaOk && Valid(); step++)
                                {
                                    if (step == 1 && !Injector.TestHoldPoint("mf.second")) break;   // 검증 전용 경계(Codex 07:45-ay): 첫 Shift+Tab 뒤·두 번째 전
                                    string? se = Injector.PressShiftTabInField(Injector.Capture(), gen, deadline, from);
                                    if (se == Injector.ShiftReleaseFailed) { Stop(se); uiaOk = false; break; }
                                    if (se is not null) break;   // 보내기 전 확인 실패: 멈춤
                                    mainSteps++;
                                    Thread.Sleep(Math.Max(cfg.KeyDelayMs, 150));
                                    int uiaWait = (int)Math.Clamp(deadline - Environment.TickCount64, 1, 3000);
                                    if (!Uia.Run(job => Uia.FocusField(w, link, job, allowClick: false, verifyOnly: true), uiaWait, Valid, out fr)) { uiaOk = false; break; }
                                    done1 = fr == Uia.FocusResult.Ok;
                                    if (done1 || fr != Uia.FocusResult.FocusFailed) break;
                                    if (step == 0 && OnNext()) { from = OnNext; continue; }   // 다음 연결 칸에 도착: 두 번째 허용
                                    break;   // 여전히 MainFrame·다른 요소: 멈춤
                                }
                                if (!uiaOk && err is not null) break;   // Shift 를 떼지 못함
                            }
                        }
                        if (!done1 && uiaOk && Valid() && !mainTried && jobIndex + 1 < jobs.Count)
                        {
                            SiteLink next = jobs[jobIndex + 1].Link;
                            bool NextFocused() => Uia.Run(job => Uia.IsFocusedOn(w, next, job), 1000, null, out bool on) && on;
                            string? stErr = NextFocused() ? Injector.PressShiftTabInField(Injector.Capture(), gen, deadline, NextFocused) : "skip";
                            if (stErr == Injector.ShiftReleaseFailed) { Stop(stErr); break; }   // Shift 를 떼지 못함: 더 보내지 않는다
                            if (stErr is null)
                            {
                                viaShiftTab = true;
                                Thread.Sleep(Math.Max(cfg.KeyDelayMs, 150));
                                int uiaWait = (int)Math.Clamp(deadline - Environment.TickCount64, 1, 3000);
                                if (!Uia.Run(job => Uia.FocusField(w, link, job, allowClick: false, verifyOnly: true), uiaWait, Valid, out fr)) uiaOk = false;
                                done1 = fr != Uia.FocusResult.FocusFailed;
                            }
                        }
                        for (int attempt = 1; attempt < 3 && !done1 && uiaOk && plat != Uia.Platform.General && !mainTried; attempt++)   // MainFrame 출발이 실패했으면 재시도 없이
                        {
                            for (int t = 0; t < 7 && Valid(); t++) Thread.Sleep(50);   // 0.35초
                            if (!Valid()) break;
                            retriesHere++;
                            done1 = TryNoClick();
                        }
                        if (uiaOk && fr == Uia.FocusResult.FocusFailed && Valid() && !copy.NoClick && plat == Uia.Platform.General)
                        {
                            int uiaWait = (int)Math.Clamp(deadline - Environment.TickCount64, 1, 3000);
                            if (!Uia.Run(job => Uia.FocusField(w, link, job, allowClick: true), uiaWait, Valid, out fr)) uiaOk = false;
                        }
                        focusRetries += retriesHere;
                        tabUsed |= viaTab && fr == Uia.FocusResult.Ok && Uia.LastFocusMethod == 4;
                        // 칸별 기록(최근 채우기 결과): 들어간 방법(0 실패, 1 지정, 2 기본 동작, 3 클릭, 4 Tab, 5 Shift+Tab)·다시 한 횟수·보조 판정
                        inputs.Add((fr == Uia.FocusResult.Ok ? (mainSteps > 0 && Uia.LastFocusMethod == 4 ? 30 + mainSteps : viaShiftTab && Uia.LastFocusMethod == 4 ? 5 : Uia.LastFocusMethod) : mainTried ? 40 + mainSteps : 0, retriesHere, fr == Uia.FocusResult.Ok && Uia.LastFocusFallback,
                                    (fr == Uia.FocusResult.Ok ? "" : Uia.LastProbe) + (mainReason >= 0 ? "#mf=" + mainReason : "")));
                        if (!uiaOk) { Stop(T.DiagSiteUnavailable); break; }
                        if (!Valid()) { Stop(); break; }
                        if (fr == Uia.FocusResult.ClickMissed) { clickMissed = true; Stop(T.FillClickMissed); break; }
                        if (fr != Uia.FocusResult.Ok)
                        {
                            Stop(fr switch { Uia.FocusResult.PageChanged => T.FillPageChanged, Uia.FocusResult.NotFound => T.FillNotFound,
                                             _ => mainReason == 9 ? T.FillBrowserList : T.FillFocusFailed });   // 브라우저 목록이 열려 있었다: 닫고 아이디 칸 클릭 뒤 다시
                            break;
                        }
                        focusMethodMax = Math.Max(focusMethodMax, Uia.LastFocusMethod);
                        focusFallback |= Uia.LastFocusFallback;
                        Injector.Request target = Injector.Capture();
                        TestAt(3);
                        if (target.Window != w || !Valid()) { Stop(T.InjActiveChanged); break; }
                        // 전체 제한(Codex R60-4, C61-1)은 이 채우기의 Ctrl+A 와 글자 전송에만 건다: 제한 시각을 Injector 에 넘겨 다음 글자 전에 비교한다.
                        // 전역 취소 세대는 쓰지 않으므로 이 채우기의 제한이 다른 입력을 멈추게 할 수 없다. 이미 들어간 글자는 되돌리지 않아 안내한다.
                        // 미리 채워진 값(브라우저 자동 완성 등)이 있으면 바꿔 쓰도록 그 칸의 글을 먼저 모두 고른다(같은 잠금 세대·같은 제한 시각)
                        if (Injector.SelectAllInField(target, gen, deadline, Guard) is string selErr) { Stop(selErr); break; }
                        owned = false;   // 여기부터 Send 가 사용권을 놓는다
                        Injector.Result res = Injector.Send(copy, cfg, target, gen, deadline, Guard);
                        if (res.Status == Injector.Status.NeedsElevation)
                        {
                            outcome = Injector.FillOutcome.NeedsElevation;   // 성공이 아니다: 권한 안내만 띄운다 (C62-1)
                            Volatile.Write(ref _injectNeedsElevation, 1); Native.PostMessageW(me, WM_INJECT_RESULT, 0, 0);
                            break;
                        }
                        if (res.Status != Injector.Status.Ok) { Stop(res.Status == Injector.Status.Timeout ? null : res.Message); break; }
                        if (res.Message is not null) notice = res.Message;
                        lastTarget = target;
                        done++;
                    }
                    finally { if (owned) Injector.Abandon(); }
                    Thread.Sleep(Math.Max(cfg.KeyDelayMs, 60));
                }
                // 단축키 경로: 모든 칸이 들어간 뒤 항목 설정대로 Enter (마지막 칸·같은 창·같은 잠금 세대·제한 시각 안일 때만)
                if (enter && err is null && outcome == Injector.FillOutcome.Ok && lastTarget is Injector.Request lt)
                {
                    label = null;
                    if (!Injector.TryBegin()) { outcome = Injector.FillOutcome.Failed; err = T.FillBusy; }
                    else
                    {
                        try { if (Injector.PressEnterInField(lt, gen, deadline, Guard) is string enterErr) Stop(enterErr); }
                        finally { Injector.Abandon(); }
                    }
                }
            }
            catch (Exception ex) { outcome = Injector.FillOutcome.Failed; err = T.InjError(ex.Message); }
            finally
            {
                Uia.Run(job => Uia.ClearFillTarget(job), 1000, null, out bool _);
                // 최근 채우기 결과(메모리에만): 항목 이름(label)·값은 넣지 않고 안내 문구와 칸 수만
                FillLog.Add(new FillLog.Entry(DateTime.Now, byButton, done, jobs.Count, err is null && outcome == Injector.FillOutcome.Ok,
                    err ?? (outcome == Injector.FillOutcome.NeedsElevation ? T.DiagFillNeedsAdmin : ""), focusMethodMax, focusFallback, focusRetries, tabUsed, clickMissed, inputs.ToArray(), (int)platform));
                if (err is not null && label is not null) err = label + ": " + err;   // 어느 항목·입력에서 멈췄는지
                // 시험 관찰값: 결과 종류(FillOutcome)와 걸린 시간
                Volatile.Write(ref _testFillMs, (int)Math.Min(Environment.TickCount64 - started, 65535));
                Volatile.Write(ref _testFillResult, (int)outcome);
                Volatile.Write(ref _testFocusMethodMax, focusMethodMax);
                Volatile.Write(ref _fillError, err);
                Volatile.Write(ref _fillNotice, notice);
                Native.PostMessageW(me, Native.WM_FILL_DONE, 0, 0);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);   // 클립보드 방식이 쓰는 OLE 는 STA 가 필요하다(목록 입력과 같다)
        try { thread.Start(); }
        catch { _fillRunning = false; throw; }
    }

    private void OnFillDone()
    {
        _fillRunning = false;
        string? err = Interlocked.Exchange(ref _fillError, null), notice = Interlocked.Exchange(ref _fillNotice, null);
        if (err is not null) { ShowBalloon(AppTitle, err, Native.NIIF_WARNING); Toast.Show(_hwnd, err, 6000, warn: true); }   // 알림이 꺼져 있어도 보이게
        else if (notice is not null) ShowBalloon(AppTitle, notice, Native.NIIF_INFO);
    }

    /// <summary>고급 › 진단 › 브라우저 입력란 읽기 확인: 3초 뒤 활성 브라우저 페이지의 주소와 입력란 목록(값은 읽지 않음)을 보인다.</summary>
    private void StartSiteDiagnostics()
    {
        int r = Msg(T.DiagSiteGuide, T.DiagSiteTitle, Native.MB_OK | Native.MB_ICONINFORMATION);
        if (r != Native.IDOK || !Unlocked) return;
        _siteDiagPending = true;
        Native.SetTimer(_hwnd, TimerSiteDiag, 3000, 0);
    }

    private void TogglePasswordVisible()
    {
        _editPwVisible = !_editPwVisible;
        for (int k = 0; k < _editInputs; k++)   // 모든 입력을 함께(로그인 양식의 ID 는 늘 보이므로 건너뛴다)
        {
            if (_editForm == FormLogin && _editInputs == 2 && k == 0) continue;
            nint e = C(InTextId(k));
            if (e == 0) continue;
            Native.SendMessageW(e, Native.EM_SETPASSWORDCHAR, _editPwVisible ? 0 : 0x25CF, 0);
            Native.InvalidateRect(e, 0, true);
        }
        Native.SetText(C(IdEShow), _editPwVisible ? IcEyeOff : IcEye);
    }

    /// <summary>편집 화면을 다시 만들고 읽어 둔 값을 되돌린다(테마·DPI 변경, 입력 추가·삭제).</summary>
    private void RestoreEdit(Slot e, bool dirty, bool visible)
    {
        ShowScreen(Screen.Edit);
        if (_cur != Screen.Edit) return;   // 그 사이 잠겼다면 잠금 화면이 떠 있다
        Native.SetText(C(IdEName), e.Name);
        for (int k = 0; k < _editInputs; k++) Native.SetText(C(InTextId(k)), k < e.InputCount ? e.ValueOf(k) : "");
        HotkeyBox.Set(C(IdEHotkey), e.Mods, e.Vk); Dropdown.Set(C(IdEEnter), EnterNames, EnterIndex(e)); SetCheck(IdEAllowClick, !e.NoClick);
        Dropdown.Set(C(IdEMethod), MethodNames, (int)e.Method);
        UpdateEditNotes((int)e.Method);
        _editPwVisible = false; if (visible) TogglePasswordVisible();
        _editDirty = dirty;
    }

    /// <summary>
    /// 용도를 바꾼다. 연결 쪽이면 입력마다 [연결] 줄이 생긴다. 커서 쪽으로 바꿀 때 연결이 있으면 지운다고 먼저 묻는다
    /// (아니요면 그대로). 값·설정은 그대로 두고 화면만 다시 만든다.
    /// </summary>
    private void ChangeMode(bool link)
    {
        if (link == _editLinkMode) return;
        if (!link && (_editApp is not null || _editSites.Any(x => x is not null)))
        {
            int r = Msg(T.EditModeDropLinks, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (_cur != Screen.Edit) return;
            if (r != Native.IDYES) { Dropdown.Set(C(IdEMode), ModeNames, 1); return; }
            _editApp = null;
            for (int k = 0; k < Slot.MaxInputs; k++) _editSites[k] = null;
        }
        _editLinkMode = link;
        RestoreEdit(ReadEdit(), true, _editPwVisible);
    }

    /// <summary>[+ 입력 추가](remove &lt; 0) 또는 입력 remove 의 [×]. 그 입력의 연결도 함께 지운다. 값·설정은 그대로 두고 화면만 다시 만든다.</summary>
    private void ChangeInputs(int remove)
    {
        if (_cur != Screen.Edit) return;
        Slot e = ReadEdit();
        bool visible = _editPwVisible;
        if (remove < 0)
        {
            if (_editInputs >= Slot.MaxInputs) return;
            e.More.Add(new ExtraInput("", null));
        }
        else
        {
            if (remove < 1 || remove >= _editInputs) return;
            e.More.RemoveAt(remove - 1);
            for (int k = remove; k < Slot.MaxInputs; k++) _editSites[k] = k + 1 < Slot.MaxInputs ? _editSites[k + 1] : null;
        }
        _editInputs = e.InputCount;
        for (int k = _editInputs; k < Slot.MaxInputs; k++) _editSites[k] = null;
        RestoreEdit(e, true, visible);
        if (_cur == Screen.Edit) Native.SetFocus(C(InTextId(remove < 0 ? _editInputs - 1 : Math.Min(remove, _editInputs - 1))));
    }

    private void StartTestFromEdit()
    {
        Slot e = ReadEdit();
        if (!e.HasPassword) { Msg(T.EditNeedContent, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdEPw)); return; }
        e.PressEnter = false;   // 테스트는 글자만 넣는다. Enter 까지 보내면 진짜 결재·전송이 일어날 수 있다.
        int r = Msg(T.EditTestGuide, T.CommonTest, Native.MB_OK | Native.MB_ICONINFORMATION);
        if (r != Native.IDOK || _cur != Screen.Edit || !Unlocked) return;   // 안내가 떠 있는 동안 잠겨 닫혔거나 화면이 바뀌었다
        _testSlot = e;
        Native.SetTimer(_hwnd, TimerTest, 3000, 0);
    }
}
