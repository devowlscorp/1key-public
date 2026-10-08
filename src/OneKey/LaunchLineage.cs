namespace OneKey;

/// <summary>
/// 한 실행이 만든 프로세스들의 판단(Codex C28-1·R29-1). 프로세스 신원 = PID + 생성 시각. 스냅샷·생성 시각·실행 파일·핸들 고정은 부르는 쪽이
/// 넣는다(<see cref="Launcher"/> 의 감시가 실제 값을, 창 없는 단위 시험 LL01– 이 꾸민 값을).
/// - 뿌리: 셸이 돌려준 프로세스(주어진 뿌리). 없으면(관리자 1Key 의 탐색기 경로) 요청 시각 뒤에 생긴 그 실행 파일의 프로세스가
///   **하나뿐일 때만**. 둘째 후보가 보이면 연계를 모름(Ambiguous)으로 두고 아무 창도 자식으로 보지 않는다. 주어진 뿌리가 있으면
///   같은 실행 파일의 다른 프로세스를 뿌리로 더하지 않는다.
/// - 자식: 스냅샷에서 부모 PID 가 구성원이고, 부모 생성 시각 ≤ 자식 생성 시각 ≤ 부모 신원이 확인된 마지막 시각(Until).
///   Until = 고정(핸들을 쥐고 있어 PID 가 재사용될 수 없음)이면 끝없음, 아니면 스냅샷에서 같은 생성 시각으로 마지막 본 시각.
///   부모가 끝나 신원을 확인할 수 없게 된 뒤에 생긴 프로세스는 잇지 않는다(PID 재사용된 부모의 새 자식을 잇지 않게).
/// - 구성원의 PID 가 스냅샷에서 다른 생성 시각으로 보이면 그 구성원을 뺀다(PID 재사용).
/// - 창의 프로세스가 자식인가 = 구성원이고 뿌리가 아니며 지금 생성 시각이 저장한 값과 같다.
/// 3대까지, 구성원 MaxMembers 개까지.
/// </summary>
internal sealed class LaunchLineage
{
    /// <summary>스냅샷의 프로세스 하나(PID·부모 PID·실행 파일 이름).</summary>
    public readonly record struct Proc(uint Pid, uint Parent, string File);

    private sealed class Member { public long Created, Until; public bool Root; }

    public const int MaxMembers = 32;
    private readonly Dictionary<uint, Member> _m = new();
    private readonly string _want, _file;
    private readonly long _since;
    private readonly bool _given;
    private bool _hadRoot;

    /// <summary>연계를 모른다(뿌리 후보가 둘 이상). 그 뒤로는 아무것도 자식으로 보지 않는다.</summary>
    public bool Ambiguous { get; private set; }
    public int Count => _m.Count;

    /// <param name="want">대상 실행 파일(정규화된 전체 경로)</param>
    /// <param name="file">그 파일 이름(스냅샷 비교용)</param>
    /// <param name="since">이 뒤에 생긴 프로세스만 뿌리 후보(주어진 뿌리가 없을 때)</param>
    /// <param name="rootPid">셸이 돌려준 프로세스(0 = 없음)</param>
    /// <param name="rootCreated">그 생성 시각</param>
    /// <param name="rootPinned">그 프로세스 핸들을 감시 동안 쥐고 있다(PID 재사용 불가)</param>
    /// <param name="now">지금(FILETIME)</param>
    public LaunchLineage(string want, string file, long since, uint rootPid, long rootCreated, bool rootPinned, long now)
    {
        _want = want; _file = file; _since = since;
        if (rootPid != 0 && rootCreated != 0)
        {
            _given = true;
            _m[rootPid] = new Member { Created = rootCreated, Until = rootPinned ? long.MaxValue : now, Root = true };
        }
    }

    /// <summary>창의 프로세스(pid, 지금 생성 시각)가 이 실행의 자식·손자인가.</summary>
    public bool IsDescendant(uint pid, long? createdNow) =>
        !Ambiguous && createdNow is long c && _m.TryGetValue(pid, out Member? m) && !m.Root && m.Created == c;

    /// <param name="now">스냅샷을 찍은 뒤, 생성 시각을 읽기 전의 시각(FILETIME)</param>
    /// <param name="procs">스냅샷</param>
    /// <param name="created">PID 의 지금 생성 시각(못 읽으면 null)</param>
    /// <param name="image">PID 의 실행 파일(정규화된 전체 경로, 못 읽으면 null)</param>
    /// <param name="pin">(PID, 생성 시각)의 핸들을 감시 동안 쥔다 — 같은 신원일 때만 true</param>
    public void Update(long now, IReadOnlyList<Proc> procs, Func<uint, long?> created, Func<uint, string?> image, Func<uint, long, bool> pin)
    {
        if (Ambiguous) return;
        var byPid = new Dictionary<uint, Proc>(procs.Count);
        foreach (Proc p in procs) byPid[p.Pid] = p;
        // 1) 구성원 신원: 고정되지 않은 구성원이 스냅샷에 있으면 생성 시각이 같아야 한다(다르면 PID 재사용 — 뺀다)
        foreach (uint pid in _m.Keys.ToList())
        {
            Member m = _m[pid];
            if (m.Until == long.MaxValue || !byPid.ContainsKey(pid)) continue;
            long? c = created(pid);
            if (c == m.Created) m.Until = now;
            else if (c is not null) _m.Remove(pid);
        }
        Link(now, procs, created, pin);
        // 2) 주어진 뿌리가 없을 때만: 요청 뒤에 생긴 대상 실행 파일의 프로세스(이미 구성원인 자식은 빼고). 둘째가 보이면 모름.
        if (_given) return;
        var cands = new List<(Proc P, long C)>();
        foreach (Proc p in procs)
        {
            if (!string.Equals(p.File, _file, StringComparison.OrdinalIgnoreCase)) continue;
            if (created(p.Pid) is not long c || c < _since) continue;
            if (!string.Equals(image(p.Pid), _want, StringComparison.OrdinalIgnoreCase)) continue;
            cands.Add((p, c));
        }
        // 먼저 생긴 것부터: 뿌리로 삼은 뒤 잇고, 그 뿌리의 자식(같은 실행 파일)으로 이어진 것은 후보에서 빠진다
        foreach (var (p, c) in cands.OrderBy(x => x.C))
        {
            if (_m.TryGetValue(p.Pid, out Member? known) && known.Created == c) continue;
            if (_hadRoot) { Ambiguous = true; _m.Clear(); return; }   // 둘째 후보(앞 후보가 끝났거나 PID 가 재사용됐어도): 어느 것이 이 실행인지 모른다
            _hadRoot = true;
            _m[p.Pid] = new Member { Created = c, Until = pin(p.Pid, c) ? long.MaxValue : now, Root = true };
            Link(now, procs, created, pin);
        }
    }

    private void Link(long now, IReadOnlyList<Proc> procs, Func<uint, long?> created, Func<uint, long, bool> pin)
    {
        for (int depth = 0; depth < 3; depth++)
        {
            bool grew = false;
            foreach (Proc p in procs)
            {
                if (_m.Count >= MaxMembers) return;
                if (_m.ContainsKey(p.Pid) || !_m.TryGetValue(p.Parent, out Member? par)) continue;
                if (created(p.Pid) is not long c || c < par.Created || c > par.Until) continue;
                _m[p.Pid] = new Member { Created = c, Until = pin(p.Pid, c) ? long.MaxValue : now };
                grew = true;
            }
            if (!grew) break;
        }
    }
}
