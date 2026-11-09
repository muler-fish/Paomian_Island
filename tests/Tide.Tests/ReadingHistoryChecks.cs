using Tide.Core;

/// <summary>阅读历史的纯数据规则：后退/前进只移动位置、打开新串截断前进部分、同串不重复、上限与失效项移除。</summary>
internal static class ReadingHistoryChecks
{
    internal static void Run()
    {
        const long A = 69711052, B = 69711012, C = 69711070, D = 69711080;
        var history = new ReadingNavigationHistory();
        Require(history.Current is null && !history.CanGoBack && !history.CanGoForward && history.Index == -1, "空历史状态");
        history.Visit(new(A)); history.Visit(new(B)); history.Visit(new(C));
        Require(Ids(history).SequenceEqual([A, B, C]) && history.Index == 2 && history.CanGoBack && !history.CanGoForward, "A→B→C 记录");
        Require(history.MoveTo(1) && history.Current!.ThreadId == B && history.MoveTo(0) && history.Current!.ThreadId == A, "两次后退");
        Require(!history.CanGoBack && history.CanGoForward && history.Count == 3, "回到最早一项后按钮状态或记录数量");
        Require(history.MoveTo(1) && history.MoveTo(2) && history.Current!.ThreadId == C && !history.CanGoForward, "两次前进");
        Require(!history.MoveTo(3) && !history.MoveTo(-1) && history.Index == 2, "越界移动改变位置");

        history.MoveTo(1); history.Visit(new(D));
        Require(Ids(history).SequenceEqual([A, B, D]) && history.Index == 2 && !history.CanGoForward, "后退后打开新串没有截断前进历史");

        // 刷新、跳页和重复打开当前串只更新当前项。
        history.Visit(new(D, 3, new(D * 10, 4, 120, false)));
        Require(history.Count == 3 && history.Current!.Page == 3 && history.Current.Position!.Offset == 120, "同串打开产生重复记录或没有更新页码");
        // 不连续的重复是正常的浏览路径，应保留。
        history.Visit(new(A));
        Require(Ids(history).SequenceEqual([A, B, D, A]), "非连续重复被错误合并");

        // 保存位置只写当前项，并且只接受同一串，避免迟到的位置写错项。
        history.UpdateCurrent(B, 5, new(1, 1, 1, false));
        Require(history.Current!.Page == 1 && history.Current.Position is null, "其他串的位置写进当前项");
        history.UpdateCurrent(A, 2, new(A + 1, 7, 33, true));
        Require(history.Current!.Page == 2 && history.Current.Position is { Index: 7, OnlyPo: true }, "当前项位置没有保存");
        history.MoveTo(1);
        Require(history.At(3)!.Page == 2 && history.At(2)!.Page == 3, "移动位置后其他项保存的页码丢失");

        // 移除失效项：当前项保持不变，前后相同的串合并为一项。
        history = new ReadingNavigationHistory();
        foreach (var id in new[] { A, B, A, C }) history.Visit(new(id));
        history.MoveTo(3); history.RemoveAt(1);
        Require(Ids(history).SequenceEqual([A, C]) && history.Current!.ThreadId == C && history.Index == 1, "移除失效项后没有合并相邻重复项或当前项改变");
        history = new ReadingNavigationHistory();
        foreach (var id in new[] { A, B, C }) history.Visit(new(id, 1, new(id, 0, 0, false)));
        history.MoveTo(2); history.RemoveAt(1);
        Require(Ids(history).SequenceEqual([A, C]) && history.Current!.ThreadId == C && history.CanGoBack, "移除中间项后后退目标不正确");
        history.RemoveAt(0);
        Require(Ids(history).SequenceEqual([C]) && history.Index == 0 && !history.CanGoBack, "移除最早项后位置不正确");
        history.RemoveAt(0);
        Require(history.Count == 0 && history.Index == -1 && history.Current is null, "移除最后一项后状态不正确");
        history.RemoveAt(5);

        // 上限：超出时丢弃最早的记录，当前项仍是最新打开的串。
        history = new ReadingNavigationHistory(5);
        for (var i = 1; i <= 8; i++) history.Visit(new(i));
        Require(Ids(history).SequenceEqual([4L, 5, 6, 7, 8]) && history.Index == 4 && history.Count == history.Capacity, "历史上限");
        Require(new ReadingNavigationHistory().Capacity == ReadingNavigationHistory.DefaultCapacity && ReadingNavigationHistory.DefaultCapacity == 100, "默认上限");
        history.Clear();
        Require(history.Count == 0 && history.Current is null && !history.CanGoBack, "清空历史");
        Console.WriteLine("PASS: reading history back/forward, truncation, same-thread updates, removal and capacity.");
    }

    private static IEnumerable<long> Ids(ReadingNavigationHistory history) => history.Entries.Select(e => e.ThreadId);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
