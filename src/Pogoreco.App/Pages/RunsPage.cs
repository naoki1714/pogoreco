using System.Windows;
using System.Windows.Controls;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

/// <summary>
/// ラン。1 回の挑戦（ラン）ごとに、ラン開始からの経過時間と高さをグラフにする。
/// 元データ: セーブ・リプレイの軌跡（3 時間まで）＋ 記録係が画面から読んだ高さ（それ以降も）＋ ログの到達%。
/// </summary>
internal static class RunsPage
{
    /// <summary>一覧の 1 行: セーブ・リプレイのラン（画面の記録つき）か、画面の記録だけのラン。</summary>
    internal sealed record Item(string Id, int? Map, ClimbRecord? Climb, List<HudRun> Hud, long LastActivity)
    {
        public string StatusText => Climb?.Status switch
        {
            ClimbStatus.Finished => "ゴール",
            ClimbStatus.Reset => "リセット",
            ClimbStatus.InProgress => "登っている途中",
            _ => "画面の記録のみ",
        };
        public double? HudBest => Hud.Count > 0 ? Hud.Max(r => r.Best) : null;
        public HudRun? LastHud => Hud.OrderBy(r => r.LastAt).LastOrDefault();
    }

    internal enum RunSort { Recent, Reach, Time }

    /// <summary>メニューの「ラン」: 全マップ横断のランの一覧。</summary>
    public static UIElement Build(Navigator nav) => List(nav, null, RunSort.Recent, null, ChartColorMode.None);

    /// <summary>マップで絞り込んだ一覧を開く（マップ詳細の「ランで見る」など）。</summary>
    public static UIElement ListForMap(Navigator nav, int map) => List(nav, map, RunSort.Recent, null, ChartColorMode.None);

    /// <summary>ランを 1 件選んだ状態で開く（そのランのマップで絞り込み、そのランの詳細を上に出す）。</summary>
    public static UIElement Detail(Navigator nav, string id) =>
        List(nav, Items(nav.Data).FirstOrDefault(i => i.Id == id)?.Map, RunSort.Recent, id, ChartColorMode.None);

    private static double ReachValue(Item i) =>
        i.HudBest ?? (i.Climb is { } c ? Maps.ToDisplay(c.Map, c.MaxY).Value : double.NegativeInfinity);

    internal static string ReachText(Item i) =>
        i.HudBest is double hb ? Shared.Height(hb, i.Hud[0].Unit) : i.Climb is { } c ? Maps.ToDisplay(c.Map, c.MaxY).ToString() : "—";

    private static double ElapsedOf(Item i) =>
        Math.Max(i.Climb?.RecordedSec ?? 0, i.Hud.Count > 0 ? i.Hud.Max(r => r.LastRunTime) : 0);

    /// <summary>セッションの 1 ランに対応する、高さの記録のあるラン。ゴールしたランはタイム、登っている途中のランは時刻で結びつける（リセットしたランは対応が取れない）。</summary>
    internal static Item? FindItem(AppData d, SessionRecord s, RunRecord r)
    {
        if (r.Map is not int map) return null;
        var items = Items(d).Where(i => i.Map == map);
        if (r.Outcome == RunOutcome.Finished && r.FinishTime is double t)
            return items.FirstOrDefault(i => i.Climb is { Status: ClimbStatus.Finished, FinishTimeSec: double f } && Math.Abs(f - t) <= 2);
        if (r.Outcome == RunOutcome.InProgress)
            return items.FirstOrDefault(i => i.Climb is { Status: ClimbStatus.InProgress } && i.LastActivity >= s.StartedAt && i.LastActivity <= s.EndedAt + 60_000);
        return null;
    }

    /// <summary>そのランを遊んだセッション。見つからなければ null。</summary>
    internal static SessionRecord? FindSession(AppData d, Item item)
    {
        if (item.Climb is { Status: ClimbStatus.Finished, FinishTimeSec: double f })
        {
            var hit = d.Sessions.OrderByDescending(s => s.StartedAt).FirstOrDefault(s =>
                s.Runs.Any(r => r.Map == item.Map && r.Outcome == RunOutcome.Finished && r.FinishTime is double t && Math.Abs(t - f) <= 2));
            if (hit is not null) return hit;
        }
        return d.Sessions.FirstOrDefault(s => item.LastActivity >= s.StartedAt && item.LastActivity <= s.EndedAt + 60_000);
    }

    /// <summary>ランの一覧（新しい順）。ダッシュボードの「現在のラン」でも使う。</summary>
    internal static List<Item> Items(AppData d)
    {
        var used = new HashSet<HudRun>();
        var items = new List<Item>();
        foreach (var c in d.Climbs)
        {
            var hud = HeightTimeline.MatchHud(c, d.HudRuns);
            used.UnionWith(hud);
            items.Add(new Item(c.Id, c.Map, c, hud, Math.Max(c.UpdatedAt, hud.Count > 0 ? hud.Max(r => r.LastAt) : 0)));
        }
        foreach (var r in d.HudRuns.Where(r => !used.Contains(r)))
            items.Add(new Item($"hud-{r.FirstAt}", r.Map, null, [r], r.LastAt));
        return [.. items.OrderByDescending(i => i.LastActivity)];
    }

    /// <summary>
    /// 一覧。マップを絞ったときは、そのマップを完走したかで見せ方を自動で切り替える:
    /// 完走済み → ゴールタイム・自己ベストとの差、未完走 → 到達・経過時間。
    /// </summary>
    private static UIElement List(Navigator nav, int? map, RunSort sort, string? selectedId, ChartColorMode colorMode)
    {
        var d = nav.Data;
        var all = Items(d);
        var progressMaps = d.Sessions.SelectMany(s => s.Progress).Select(p => (int?)p.Map);
        var maps = all.Select(i => i.Map).Concat(progressMaps).OfType<int>().Distinct().Order().ToList();
        var title = Ui.Row(Ui.H1("ラン"));
        if (nav.CanGoBack) title.Children.Insert(0, Ui.Link("← 戻る", nav.Back).WithMargin(new Thickness(0, 8, 12, 0)));
        var page = Ui.Stack(title.WithMargin(new Thickness(0, 0, 0, 12)));

        var filter = Ui.Row(Ui.Muted("マップ:").WithMargin(new Thickness(0, 7, 8, 0)));
        filter.Children.Add(Ui.Button("すべて", () => nav.Replace(n => List(n, null, sort, null, colorMode)), primary: map is null));
        foreach (var m in maps)
            filter.Children.Add(Ui.Button(Maps.MapName(m), () => nav.Replace(n => List(n, m, sort, null, colorMode)), primary: m == map));
        page.Children.Add(filter.WithMargin(new Thickness(0, 0, 0, 8)));

        var sortRow = Ui.Row(Ui.Muted("並べ替え:").WithMargin(new Thickness(0, 7, 8, 0)));
        foreach (var (v, label) in new[] { (RunSort.Recent, "新しい順"), (RunSort.Reach, "最高到達順"), (RunSort.Time, "ゴールタイム順") })
            sortRow.Children.Add(Ui.Button(label, () => nav.Replace(n => List(n, map, v, selectedId, colorMode)), primary: v == sort));
        page.Children.Add(sortRow.WithMargin(new Thickness(0, 0, 0, 16)));

        var items = all.Where(i => map is null || i.Map == map).ToList();
        if (items.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Stack(
                Ui.Text("まだ高さの記録がありません。", 14, Theme.TextSecondary),
                Ui.Text("デスクトップの「Pogostuck（記録つき）」から遊ぶと、画面の高さを 1 秒ごとに記録します。", 14, Theme.TextSecondary))));
            return page;
        }

        // 一番上には、選んだラン（なければ一番新しいラン）の詳細を出す
        var selected = items.FirstOrDefault(i => i.Id == selectedId) ?? items[0];
        if (selected.Map is int selMap)
        {
            var head = Ui.Row(Ui.H2("ランの詳細"), Ui.Muted($"{Maps.MapName(selMap)}　{Fmt.DateTime(selected.LastActivity)}").WithMargin(new Thickness(8, 4, 0, 0)));
            if (FindSession(d, selected) is { } session)
                head.Children.Add(Ui.Link("このセッションを見る", () => nav.Push(n => SessionsPage.Detail(n, session.Id), "セッション")).WithMargin(new Thickness(12, 5, 0, 0)));
            page.Children.Add(head.WithMargin(new Thickness(0, 0, 0, 8)));
            page.Children.Add(ClimbCard(d, selected, selMap, colorMode, cm => nav.Replace(n => List(n, map, sort, selected.Id, cm))));
        }

        items = sort switch
        {
            RunSort.Reach => [.. items.OrderByDescending(ReachValue)],
            RunSort.Time => [.. items.OrderBy(i => i.Climb?.FinishTimeSec ?? double.MaxValue).ThenByDescending(i => i.LastActivity)],
            _ => items,
        };
        // 自己ベスト = そのマップで記録の残っているゴールタイムの最速
        double? Best(int? m) => all.Where(i => i.Map == m && i.Climb is { Status: ClimbStatus.Finished, FinishTimeSec: not null })
            .Select(i => i.Climb!.FinishTimeSec).Min();
        string Diff(Item i) => i.Climb?.FinishTimeSec is double t && Best(i.Map) is double b ? (t - b < 0.0005 ? "ベスト" : "+" + Fmt.RunTime(t - b)) : "";

        bool timeView = map is int mm && Best(mm) is not null;
        string[] headers;
        bool[] numeric;
        Func<Item, string[]> cells;
        if (map is null)
        {
            headers = ["マップ", "状態", "最後の記録", "ラン時間", "最高到達", "ゴールタイム"];
            numeric = [false, false, false, true, true, true];
            cells = i => [i.Map is int m ? Maps.MapName(m) : "—", i.StatusText, Fmt.DateTime(i.LastActivity), Fmt.Elapsed(ElapsedOf(i)), ReachText(i), Fmt.RunTime(i.Climb?.FinishTimeSec)];
        }
        else if (timeView)
        {
            headers = ["状態", "最後の記録", "ゴールタイム", "自己ベストとの差", "ラン時間", "最高到達"];
            numeric = [false, false, true, true, true, true];
            cells = i => [i.StatusText, Fmt.DateTime(i.LastActivity), Fmt.RunTime(i.Climb?.FinishTimeSec), Diff(i), Fmt.Elapsed(ElapsedOf(i)), ReachText(i)];
        }
        else
        {
            headers = ["状態", "最後の記録", "最高到達", "前のランとの差", "ラン時間", "備考"];
            numeric = [false, false, true, true, true, false];
            cells = i => [i.StatusText, Fmt.DateTime(i.LastActivity), ReachText(i), PreviousDelta(d, i)?.Replace("前のランより ", "") ?? "", Fmt.Elapsed(ElapsedOf(i)), i.Hud.Count > 0 ? "画面の記録あり" : ""];
        }

        if (map is not null)
            page.Children.Add(Ui.Muted(timeView
                ? "ゴールしたマップのため、ゴールタイムと自己ベストとの差を表示しています。"
                : "まだゴールしていないマップのため、到達と経過時間を表示しています。").WithMargin(new Thickness(0, 0, 0, 8)));
        var rows = items.Select(i =>
        {
            var row = cells(i);
            if (i.Id == selected.Id) row[0] = "▶ " + row[0];
            return (row, Ui.Click(() => nav.Replace(n => List(n, map, sort, i.Id, colorMode))));
        });
        page.Children.Add(Ui.Card(Ui.Stack(
            Ui.H2("ランの一覧").WithMargin(new Thickness(0, 0, 0, 8)),
            Ui.Table(headers, rows, numeric))));
        if (map is int sm && SessionReachCard(d, sm) is { } sessionCard) page.Children.Add(sessionCard);
        return page;
    }

    /// <summary>
    /// ランのカード（タイル＋高さのグラフ）。onChangeColorMode を渡すと「単色／時間で色分け／高さで色分け」の
    /// 切り替えを出す（ダッシュボードの「現在のラン」など、簡潔に見せたい場面では渡さない）。
    /// </summary>
    internal static UIElement ClimbCard(AppData d, Item item, int map, ChartColorMode colorMode = ChartColorMode.None, Action<ChartColorMode>? onChangeColorMode = null, bool compact = false)
    {
        var series = HeightTimeline.Build(item.Climb, item.Hud);
        var segments = HeightTimeline.FindSegments(item.Climb, d.Sessions);
        var segmentNames = segments.Select(s => (s.RunTime, Maps.CheckpointName(map, s.Checkpoint))).ToList();
        // エリア（高さ）の境界: 区間を通過した瞬間の高さを使う。時間は先に進んでいても、
        // 落下して前のエリアの高さに戻ったところが分かるように、高さの昇順で並べる
        var areaNames = segments.Select(s => (Value: HeightNear(series, s.RunTime), Name: Maps.CheckpointName(map, s.Checkpoint)))
            .OrderBy(a => a.Value).ToList();
        // このランに区間の記録がなくても、同じマップの別のランで区間の記録があれば、その境界の高さからエリアを推定する
        bool areasEstimated = false;
        if (areaNames.Count == 0 && series.Unit != "")
        {
            areaNames = LearnedAreas(d, map, series.Unit);
            areasEstimated = areaNames.Count > 0;
        }
        var (yMin, yMax, yTicks) = Shared.HeightAxis(series.Unit);
        var chart = new LineChart
        {
            Points = Replay.Downsample(series.Points, 2000),
            XKind = XAxisKind.Duration,
            // 目盛りは「2時間」のような短い書き方、ホバーは「ラン時間 1:23:45」
            XFormat = Fmt.ElapsedTick,
            HoverXFormat = x => "ラン時間 " + Fmt.Elapsed(x),
            YFormat = y => Shared.Height(y, series.Unit),
            YMin = yMin,
            YMax = yMax,
            YTicks = yTicks,
            Area = true,
            ValueName = "高さ",
            Height = 300,
            Segments = segmentNames,
            Areas = areaNames,
            ColorMode = colorMode,
        };

        var lastHud = item.LastHud;
        var current = lastHud is not null ? Shared.Height(lastHud.Current, lastHud.Unit)
            : item.Climb is { } c && c.Status != ClimbStatus.Finished ? Maps.ToDisplay(c.Map, c.LastY).ToString() : "—";
        var best = item.HudBest is double hb ? Shared.Height(hb, item.Hud[0].Unit)
            : item.Climb is { } c2 ? Maps.ToDisplay(c2.Map, c2.MaxY).ToString() : "—";
        var runTime = series.Points.Count > 0 ? series.Points[^1].X : item.Climb?.RecordedSec ?? 0;

        var tiles = Ui.Tiles(
            Ui.Tile("状態", item.StatusText),
            Ui.Tile("ラン時間", Fmt.Elapsed(runTime), item.Climb?.FinishTimeSec is double f ? $"ゴール {Fmt.RunTime(f)}" : null),
            Ui.Tile("最後の高さ", item.Climb?.Status == ClimbStatus.Finished ? "ゴール" : current),
            Ui.Tile("最高到達", best, PreviousDelta(d, item)));

        var notes = Ui.Stack();
        if (item.Climb is { Capped: true } && item.Hud.Count == 0)
            notes.Children.Add(Ui.Muted("ゲームが記録する軌跡はラン開始から 3 時間までです。それ以降は、記録係で遊ぶと画面から記録します。").WithMargin(new Thickness(0, 8, 0, 0)));
        if (onChangeColorMode is not null && areaNames.Count > 0)
        {
            var modeRow = Ui.Row(Ui.Muted("色分け:").WithMargin(new Thickness(0, 7, 8, 0)));
            var modes = segmentNames.Count > 0
                ? new[] { (ChartColorMode.None, "単色"), (ChartColorMode.Time, "時間（区間）"), (ChartColorMode.Height, "高さ（エリア）") }
                : [(ChartColorMode.None, "単色"), (ChartColorMode.Height, "高さ（エリア）")];
            foreach (var (cm, label) in modes)
                modeRow.Children.Add(Ui.Button(label, () => onChangeColorMode(cm), primary: cm == colorMode));
            notes.Children.Add(modeRow.WithMargin(new Thickness(0, 8, 0, 0)));
        }
        if (areasEstimated)
            notes.Children.Add(Ui.Muted("このランは区間の記録がないため、エリアは同じマップの他のランの区間の記録から推定した高さの境界を使っています（時間での色分けは出せません）。").WithMargin(new Thickness(0, 8, 0, 0)));
        else if (areaNames.Count == 0)
            notes.Children.Add(Ui.Muted("このマップは区間の記録がまだ無いため、エリアの色分けと累計滞在時間は出せません。ゲームの Options → Misc → Show speedrun splits を Minimal にして一度遊ぶと、過去のランにも使えます。").WithMargin(new Thickness(0, 8, 0, 0)));

        var title = item.Climb?.Status switch
        {
            ClimbStatus.InProgress => "登っている途中のラン",
            ClimbStatus.Finished => "ゴールしたラン",
            ClimbStatus.Reset => "リセットしたラン",
            _ => "画面から記録したラン",
        };
        // 表にも区間・エリア名を出す（色は背景とのコントラストが低い色もあるため、色だけに頼らず表・ホバーでも分かるようにする）
        string? SegmentNameAt(double x)
        {
            string? name = null;
            foreach (var (at, n) in segmentNames) { if (at <= x) name = n; else break; }
            return name;
        }
        string? AreaNameAt(double y)
        {
            string? name = null;
            foreach (var (value, n) in areaNames) { if (value <= y) name = n; else break; }
            return name;
        }
        var card = Ui.ChartCard(title, "ラン開始からの経過時間（プレイしていた時間）と高さ", chart,
            () => segmentNames.Count == 0
                ? Ui.Table(["ラン時間", "高さ"],
                    series.Points.Where((_, i) => i % Math.Max(1, series.Points.Count / 60) == 0)
                        .Select(p => (new[] { Fmt.Elapsed(p.X), Shared.Height(p.Y, series.Unit) }, (Action?)null)),
                    [true, true])
                : Ui.Table(["ラン時間", "高さ", "区間", "エリア"],
                    series.Points.Where((_, i) => i % Math.Max(1, series.Points.Count / 60) == 0)
                        .Select(p => (new[] { Fmt.Elapsed(p.X), Shared.Height(p.Y, series.Unit), SegmentNameAt(p.X) ?? "", AreaNameAt(p.Y) ?? "" }, (Action?)null)),
                    [true, true, false, false]),
            above: Ui.Stack(tiles, notes).WithMargin(new Thickness(0, 0, 0, 12)));

        if (compact) return card;
        var stack = Ui.Stack(card);
        if (areaNames.Count > 0 && series.Points.Count > 1)
        {
            // 点と点の間の時間を、手前の点の高さのエリアに足す（落ちて戻った分も累計する）
            var spent = new double[areaNames.Count + 1];
            for (int i = 1; i < series.Points.Count; i++)
            {
                var (x0, y0) = series.Points[i - 1];
                var dt = series.Points[i].X - x0;
                if (dt <= 0 || double.IsNaN(y0)) continue;
                spent[areaNames.Count(a => a.Value <= y0)] += dt;
            }
            var total = spent.Sum();
            // 下から（スタート側）順に並べ、横棒グラフでは上がゴール側になるよう逆順で描く
            var rowsArea = Enumerable.Range(0, spent.Length)
                .Select(k => (Name: k == 0 ? "スタート地点" : areaNames[k - 1].Name, Sec: spent[k])).Reverse().ToList();
            stack.Children.Add(Ui.ChartCard("エリアごとの累計滞在時間", "ラン中にそのエリアにいた時間の合計（落ちて戻った分も足します）",
                new BarChart
                {
                    Horizontal = true,
                    Bars = [.. rowsArea.Select(r => new Bar(r.Name, r.Sec, true, $"{r.Name}　{Fmt.Elapsed(r.Sec)}（{(total > 0 ? r.Sec / total : 0):P0}）"))],
                    ValueFormat = Fmt.Elapsed,
                    ValueName = "滞在時間",
                },
                () => Ui.Table(["エリア", "累計滞在時間", "割合"],
                    rowsArea.Select(r => (new[] { r.Name, Fmt.Elapsed(r.Sec), total > 0 ? (r.Sec / total).ToString("P0") : "—" }, (Action?)null)),
                    [false, true, true])));
        }
        var fallCard = FallCard(series, areaNames);
        if (fallCard is not null) stack.Children.Add(fallCard);
        return stack;
    }

    private static double HeightNear(HeightSeries series, double runTime) =>
        series.Points.Where(p => !double.IsNaN(p.Y)).OrderBy(p => Math.Abs(p.X - runTime)).FirstOrDefault().Y;

    /// <summary>区間の記録のある同じマップの全ランから、区間ごとの境界の高さ（通過時の高さの中央値）を求める。</summary>
    private static List<(double Value, string Name)> LearnedAreas(AppData d, int map, string unit)
    {
        var heights = new Dictionary<int, List<double>>();
        foreach (var it in Items(d).Where(i => i.Map == map && i.Climb is not null))
        {
            var segs = HeightTimeline.FindSegments(it.Climb, d.Sessions);
            if (segs.Count == 0) continue;
            var ser = HeightTimeline.Build(it.Climb, it.Hud);
            if (ser.Unit != unit) continue;
            foreach (var sg in segs)
            {
                var h = HeightNear(ser, sg.RunTime);
                if (double.IsNaN(h)) continue;
                if (!heights.TryGetValue(sg.Checkpoint, out var list)) heights[sg.Checkpoint] = list = [];
                list.Add(h);
            }
        }
        return [.. heights.Select(kv =>
        {
            var v = kv.Value.Order().ToList();
            return (Value: v[v.Count / 2], Name: Maps.CheckpointName(map, kv.Key));
        }).OrderBy(a => a.Value)];
    }

    private static double? ComparableReach(Item i) =>
        i.HudBest ?? (i.Climb is { } c && Maps.ToDisplay(c.Map, 0).Unit != "" ? Maps.ToDisplay(c.Map, c.MaxY).Value : null);

    private static string UnitOf(Item i) => i.Hud.Count > 0 ? i.Hud[0].Unit : i.Climb is { } c ? Maps.ToDisplay(c.Map, 0).Unit : "";

    /// <summary>同じマップの 1 つ前のランと、最高到達を比べた差（例: 「前のランより +12 m」）。比べられなければ null。</summary>
    internal static string? PreviousDelta(AppData d, Item item)
    {
        var prev = Items(d).Where(i => i.Map == item.Map && i.LastActivity < item.LastActivity).FirstOrDefault();
        if (prev is null || ComparableReach(item) is not double now || ComparableReach(prev) is not double before) return null;
        var unit = UnitOf(item);
        if (unit == "" || unit != UnitOf(prev)) return null;
        var diff = now - before;
        return Math.Abs(diff) < 0.05 ? "前のランと同じ" : $"前のランより {(diff > 0 ? "+" : "−")}{Shared.Height(Math.Abs(diff), unit)}";
    }

    /// <summary>落下の記録。高さのグラフから、落下の回数・大きな落下・（Map 2 で）斧に当たった回数を推定する。</summary>
    private static UIElement? FallCard(HeightSeries series, List<(double Value, string Name)> areaNames)
    {
        if (series.Points.Count < 2 || series.Unit == "") return null;
        var falls = Falls.Detect(series.Points, series.Unit);
        var axes = falls.Count(f => f.Axe);
        var biggest = falls.OrderByDescending(f => f.Distance).FirstOrDefault();
        var thr = Shared.Height(Falls.Threshold(series.Unit), series.Unit);
        var tiles = new List<UIElement>
        {
            Ui.Tile("落下の回数", $"{falls.Count}回", $"{thr} 以上の下降"),
            Ui.Tile("最大の落下", biggest is null ? "—" : Shared.Height(biggest.Distance, series.Unit), biggest is null ? null : $"ラン時間 {Fmt.Elapsed(biggest.StartTime)}"),
            Ui.Tile("落ちた距離の合計", Shared.Height(falls.Sum(f => f.Distance), series.Unit)),
        };
        if (series.Unit == "m") tiles.Add(Ui.Tile("斧に当たった回数", $"{axes}回", "650 m 付近の斧。500 m 以上から 10 m 以下へ戻された回数（推定）"));
        var top = falls.OrderByDescending(f => f.Distance).Take(10).OrderBy(f => f.StartTime).ToList();
        var card = Ui.Card(Ui.Stack(
            Ui.H2("落下の記録").WithMargin(new Thickness(0, 0, 0, 8)),
            Ui.Tiles([.. tiles]),
            top.Count == 0 ? Ui.Muted("大きな落下はありません。") : Ui.Stack(
                Ui.Muted("大きな落下（上位 10 件、時間順）").WithMargin(new Thickness(0, 12, 0, 4)),
                Ui.Table(["ラン時間", "高さ", "落ちた距離", "種類"],
                    top.Select(f => (new[] { Fmt.Elapsed(f.StartTime), $"{Shared.Height(f.From, series.Unit)} → {Shared.Height(f.To, series.Unit)}", Shared.Height(f.Distance, series.Unit), f.Axe ? "斧" : "落下" }, (Action?)null)),
                    [true, false, true, false]))));
        if (areaNames.Count == 0) return card;

        // 落ち始めた高さのエリアごとに数える（スタート地点 → 最初の区間の手前）
        var byArea = new int[areaNames.Count + 1];
        var axeByArea = new int[areaNames.Count + 1];
        foreach (var f in falls)
        {
            var k = areaNames.Count(a => a.Value <= f.From);
            byArea[k]++;
            if (f.Axe) axeByArea[k]++;
        }
        var rowsArea = Enumerable.Range(0, byArea.Length)
            .Select(k => (Name: k == 0 ? "スタート地点" : areaNames[k - 1].Name, Count: byArea[k], Axe: axeByArea[k])).Reverse().ToList();
        var chart = new BarChart
        {
            Horizontal = true,
            Bars = [.. rowsArea.Select(r => new Bar(r.Name, r.Count, true, $"{r.Name}　{r.Count}回" + (r.Axe > 0 ? $"（うち斧 {r.Axe}回）" : "")))],
            ValueFormat = v => $"{v:0}回",
            ValueName = "落下の回数",
        };
        var areaCard = Ui.ChartCard("エリア別の落下回数", $"{thr} 以上落ちた回数。落ち始めた高さのエリアで数えます", chart,
            () => Ui.Table(["エリア", "落下の回数", "うち斧"],
                rowsArea.Select(r => (new[] { r.Name, r.Count.ToString(), r.Axe > 0 ? r.Axe.ToString() : "" }, (Action?)null)),
                [false, true, true]));
        return Ui.Stack(card, areaCard);
    }

    /// <summary>セッションごとの最高到達の推移（そのセッション中に画面で読んだ一番高い高さ。画面の記録が無い古いランは、ゲームの軌跡の最高値）。</summary>
    private static UIElement? SessionReachCard(AppData d, int map)
    {
        var items = Items(d).Where(i => i.Map == map).ToList();
        string? unit = null;
        var points = new List<(SessionRecord Session, double Value)>();
        foreach (var session in d.Sessions.OrderBy(x => x.StartedAt))
        {
            double? best = null;
            void Take(double v, string u)
            {
                if (unit is not null && unit != u) return;
                unit = u;
                best = Math.Max(best ?? double.NegativeInfinity, v);
            }
            foreach (var smp in items.SelectMany(i => i.Hud).SelectMany(r => r.Samples).Where(x => x.At >= session.StartedAt && x.At <= session.EndedAt + 60_000))
                Take(smp.Current, smp.Unit);
            foreach (var it in items.Where(i => i.Hud.Count == 0 && i.Climb is not null && FindSession(d, i)?.Id == session.Id))
            {
                var disp = Maps.ToDisplay(it.Map, it.Climb!.MaxY);
                if (disp.Unit != "") Take(disp.Value, disp.Unit);
            }
            if (best is double b) points.Add((session, b));
        }
        if (points.Count == 0 || unit is null) return null;
        var u = unit;
        var shown = points.TakeLast(30).ToList();
        string Delta(int i) => i == 0 ? "" : (shown[i].Value - shown[i - 1].Value) switch
        {
            var x when Math.Abs(x) < 0.05 => "±0",
            var x => (x > 0 ? "+" : "−") + Shared.Height(Math.Abs(x), u),
        };
        var chart = new BarChart
        {
            Bars = [.. shown.Select((p, i) => new Bar(Fmt.DateTime(p.Session.StartedAt)[..5], p.Value, true, $"{Fmt.DateTime(p.Session.StartedAt)}　{Shared.Height(p.Value, u)}"))],
            ValueFormat = v => Shared.Height(v, u),
            ValueName = "最高到達",
            Height = 240,
        };
        return Ui.ChartCard("セッションごとの最高到達", "そのセッションで到達した一番高い高さ（直近 30 セッション）", chart,
            () => Ui.Table(["セッション開始", "最高到達", "前のセッションとの差"],
                shown.Select((p, i) => (new[] { Fmt.DateTime(p.Session.StartedAt), Shared.Height(p.Value, u), Delta(i) }, (Action?)null)),
                [false, true, true]));
    }
}
