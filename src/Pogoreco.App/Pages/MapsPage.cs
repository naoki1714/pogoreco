using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

internal static class MapsPage
{
    /// <summary>マップの一覧。マップ・モードごとのカード（山の絵はマップごとに色が違う）に、数字とエリアごとの累計滞在時間。</summary>
    public static UIElement Build(Navigator nav)
    {
        var d = nav.Data;
        var maps = Stats.ListMapModes(d.Sessions);
        var page = Ui.Stack(Ui.H1("マップ").WithMargin(new Thickness(0, 0, 0, 16)));
        if (maps.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Text("まだマップで遊んだ記録がありません。", 14, Theme.TextSecondary)));
            return page;
        }

        // エリアの集計はマップ単位（モードをまたいだ合計）。同じマップのカードで使い回す
        var areaCache = new Dictionary<int, List<AreaRow>>();
        var grid = new UniformGrid { Columns = 2 };
        foreach (var m in maps)
        {
            if (!areaCache.TryGetValue(m.Map, out var rows)) areaCache[m.Map] = rows = AreaStats.ForMap(d, m.Map);
            var key = m.Key;
            grid.Children.Add(Card(m, rows, () => nav.Push(n => Detail(n, key), "マップ")));
        }
        page.Children.Add(grid);
        return page;
    }

    private static UIElement Card(MapModeSummary m, List<AreaRow> areas, Action onClick)
    {
        var palette = MountainArt.PaletteFor(m.Map);
        var banner = MountainArt.Banner(
            Ui.Row(Ui.Display(Maps.MapModeName(m.Key), 22), Ui.Chip($"{m.RunCount} ラン").WithMargin(new Thickness(10, 0, 0, 0))),
            palette, 120, MountainArt.IsUpsideDown(m.Map, m.Mode), flatBottom: true);

        var stats = Ui.Row(
            Stat("最高到達", Shared.ReachLabel(m.Map, m.MaxCheckpoint, m.FinishCount > 0, m.MaxCheckpoint is not null)),
            Stat("自己ベスト", Fmt.RunTime(m.PersonalBest)),
            Stat("ゴール", $"{m.FinishCount}回"));

        FrameworkElement chart = areas.Count > 0
            ? Ui.Stack(Ui.Muted("エリアごとの累計滞在時間（このマップの全ラン）").WithMargin(new Thickness(0, 0, 0, 4)), AreaCharts.DwellChart(areas))
            : Ui.Muted("区間の記録がないため、エリアごとの滞在時間は出せません。");

        var card = new Border
        {
            Background = Theme.Surface,
            BorderBrush = Theme.CardEdge,
            BorderThickness = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(24),
            Margin = new Thickness(0, 0, 16, 16),
            Cursor = Cursors.Hand,
            Child = Ui.Stack(banner, new Border { Padding = new Thickness(22, 16, 22, 18), Child = Ui.Stack(stats, chart.WithMargin(new Thickness(0, 14, 0, 0))) }),
        };
        card.MouseLeftButtonUp += (_, _) => onClick();
        return card;
    }

    private static UIElement Stat(string label, string value)
    {
        var s = Ui.Stack(Ui.Text(label, 12, Theme.TextSecondary), Ui.Display(value, 20));
        s.Margin = new Thickness(0, 0, 28, 0);
        return s;
    }

    public static UIElement Detail(Navigator nav, MapModeKey key)
    {
        var d = nav.Data;
        var st = Stats.GetMapModeStats(d.Sessions, key);
        var page = Ui.Stack(Ui.Row(
            Ui.Link("← 戻る", nav.Back).WithMargin(new Thickness(0, 0, 12, 0)),
            Ui.Link("ランで見る", () => nav.Push(n => RunsPage.ListForMap(n, key.Map), "ラン"))).WithMargin(new Thickness(0, 0, 0, 10)));
        var banner = MountainArt.Banner(
            Ui.Display(Maps.MapModeName(key), 28), MountainArt.PaletteFor(key.Map), 140, MountainArt.IsUpsideDown(key.Map, key.Mode));
        banner.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 16));
        page.Children.Add(banner);
        if (st is null)
        {
            page.Children.Add(Ui.Card(Ui.Text("このマップの記録はありません。", 14, Theme.TextSecondary)));
            return page;
        }
        var s = st.Summary;
        page.Children.Add(Ui.Card(Ui.Tiles(
            Ui.Tile("挑戦（ラン）", $"{s.RunCount}回", $"{s.SessionCount} セッション"),
            Ui.Tile("ゴール", $"{s.FinishCount}回"),
            Ui.Tile("自己ベスト", Fmt.RunTime(s.PersonalBest), s.BestFinishTime is double b ? $"記録したゴールの最速 {Fmt.RunTime(b)}" : null),
            Ui.Tile("最高到達", Shared.ReachLabel(key.Map, s.MaxCheckpoint, s.FinishCount > 0, s.MaxCheckpoint is not null)))));

        if (st.SessionsWithoutSplits > 0)
            page.Children.Add(Ui.Notice($"区間の記録が無いセッション {st.SessionsWithoutSplits} 件（ゲームのスプリット表示がオフ）は、ゴールタイムには含めていますが、到達地点には含めていません。"));

        // エリアごとの累計滞在時間と落下回数（斧に当たった回数は表で）。このマップの全ラン（モードをまたいだ合計）
        var areas = AreaStats.ForMap(d, key.Map);
        if (areas.Count > 0)
        {
            page.Children.Add(AreaCharts.Dwell(areas, "エリアごとの累計滞在時間", "このマップの全ランで、そのエリアにいた時間の合計（落ちて戻った分も足します）"));
            var axes = areas.Sum(a => a.AxeFalls);
            page.Children.Add(AreaCharts.Falls(areas, "エリア別の落下回数",
                "大きく落ちた回数。落ち始めた高さのエリアで数えます" + (axes > 0 ? $"（斧に当たった回数 {axes} 回を含む。内訳は「表で見る」）" : "")));
        }
        else
        {
            page.Children.Add(Ui.Notice("このマップは区間の記録がまだ無いため、エリアごとの滞在時間と落下回数は出せません。ゲームの Options → Misc → Show speedrun splits を Minimal にして一度遊ぶと出せます。"));
        }

        if (st.Finishes.Count > 1)
        {
            var points = st.Finishes.Select((f, i) => ((double)(i + 1), f.Time)).ToList();
            page.Children.Add(Ui.ChartCard("ゴールタイムの推移", "下にいくほど速い",
                new LineChart { Points = points, XKind = XAxisKind.Number, XFormat = x => $"{x:0}回目", YFormat = y => Fmt.RunTime(y)[..^4], ValueName = "タイム", Height = 240 },
                () => Ui.Table(["#", "日時", "タイム", "自己ベスト"],
                    st.Finishes.Select((f, i) => (new[] { (i + 1).ToString(), Fmt.DateTime(f.StartedAt), Fmt.RunTime(f.Time), f.IsPersonalBest ? "更新" : "" }, (Action?)null)),
                    [true, false, true, false])));
        }
        else if (st.Finishes.Count == 1)
        {
            page.Children.Add(Ui.Card(Ui.Text($"ゴール 1 回：{Fmt.RunTime(st.Finishes[0].Time)}（{Fmt.DateTime(st.Finishes[0].StartedAt)}）。2 回以上ゴールすると推移のグラフが出ます。", 14)));
        }
        return page;
    }
}
