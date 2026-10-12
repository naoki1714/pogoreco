using System.Windows;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

/// <summary>エリアごとの集計（滞在時間・落下の回数・うち斧）。</summary>
internal sealed record AreaRow(string Name, double DwellSec, int Falls, int AxeFalls);

internal static class AreaStats
{
    /// <summary>
    /// 1 本の高さの推移から、エリアごとに数える。areas は境界の高さの昇順。先頭は「スタート地点」（最初の境界より下）。
    /// 滞在時間は、点と点の間の時間を手前の点の高さのエリアに足す（落ちて戻った分も累計する）。
    /// 落下は、落ち始めた高さのエリアで数える。
    /// </summary>
    public static List<AreaRow> FromSeries(HeightSeries series, IReadOnlyList<(double Value, string Name)> areas)
    {
        var dwell = new double[areas.Count + 1];
        var falls = new int[areas.Count + 1];
        var axes = new int[areas.Count + 1];
        Add(series, areas, dwell, falls, axes);
        return Rows(areas, dwell, falls, axes);
    }

    /// <summary>そのマップの全ランを合計する。エリアの境界は、区間の記録のある全ランの中央値（RunsPage.LearnedAreas）。</summary>
    public static List<AreaRow> ForMap(AppData d, int map)
    {
        var items = RunsPage.Items(d).Where(i => i.Map == map).ToList();
        string? unit = null;
        var serieses = new List<HeightSeries>();
        foreach (var it in items)
        {
            var s = HeightTimeline.Build(it.Climb, it.Hud);
            if (s.Points.Count < 2 || s.Unit == "") continue;
            unit ??= s.Unit;
            if (s.Unit == unit) serieses.Add(s);
        }
        if (unit is null) return [];
        var areas = RunsPage.LearnedAreas(d, map, unit);
        if (areas.Count == 0) return [];
        var dwell = new double[areas.Count + 1];
        var falls = new int[areas.Count + 1];
        var axes = new int[areas.Count + 1];
        foreach (var s in serieses) Add(s, areas, dwell, falls, axes);
        return Rows(areas, dwell, falls, axes);
    }

    private static void Add(HeightSeries series, IReadOnlyList<(double Value, string Name)> areas, double[] dwell, int[] falls, int[] axes)
    {
        int AreaOf(double y) => areas.Count(a => a.Value <= y);
        for (int i = 1; i < series.Points.Count; i++)
        {
            var (x0, y0) = series.Points[i - 1];
            var dt = series.Points[i].X - x0;
            if (dt <= 0 || double.IsNaN(y0)) continue;
            dwell[AreaOf(y0)] += dt;
        }
        if (series.Unit == "") return;
        foreach (var f in Falls.Detect(series.Points, series.Unit))
        {
            var k = AreaOf(f.From);
            falls[k]++;
            if (f.Axe) axes[k]++;
        }
    }

    private static List<AreaRow> Rows(IReadOnlyList<(double Value, string Name)> areas, double[] dwell, int[] falls, int[] axes) =>
        [.. Enumerable.Range(0, dwell.Length).Select(k => new AreaRow(k == 0 ? "スタート地点" : areas[k - 1].Name, dwell[k], falls[k], axes[k]))];
}

/// <summary>エリア別の棒グラフ（縦向き・小さめ）。ラン・マップの詳細で共通に使う。</summary>
internal static class AreaCharts
{
    private const double ChartHeight = 200;

    /// <summary>エリアごとの累計滞在時間の棒グラフだけ（カードの中に埋め込む用）。</summary>
    public static BarChart DwellChart(IReadOnlyList<AreaRow> rows, double height = 150)
    {
        var total = rows.Sum(r => r.DwellSec);
        return new BarChart
        {
            Bars = [.. rows.Select(r => new Bar(r.Name, r.DwellSec, true, $"{r.Name}　{Fmt.Elapsed(r.DwellSec)}（{(total > 0 ? r.DwellSec / total : 0):P0}）"))],
            ValueFormat = Fmt.Elapsed,
            ValueName = "滞在時間",
            Height = height,
        };
    }

    /// <summary>エリアごとの累計滞在時間。</summary>
    public static UIElement Dwell(IReadOnlyList<AreaRow> rows, string title, string description)
    {
        var total = rows.Sum(r => r.DwellSec);
        string Share(AreaRow r) => total > 0 ? (r.DwellSec / total).ToString("P0") : "—";
        var chart = new BarChart
        {
            Bars = [.. rows.Select(r => new Bar(r.Name, r.DwellSec, true, $"{r.Name}　{Fmt.Elapsed(r.DwellSec)}（{Share(r)}）"))],
            ValueFormat = Fmt.Elapsed,
            ValueName = "滞在時間",
            Height = ChartHeight,
        };
        return Ui.ChartCard(title, description, chart,
            () => Ui.Table(["エリア", "累計滞在時間", "割合"],
                rows.Select(r => (new[] { r.Name, Fmt.Elapsed(r.DwellSec), Share(r) }, (Action?)null)),
                [false, true, true]));
    }

    /// <summary>エリアごとの落下回数（斧に当たった回数も表に出す）。</summary>
    public static UIElement Falls(IReadOnlyList<AreaRow> rows, string title, string description)
    {
        var chart = new BarChart
        {
            Bars = [.. rows.Select(r => new Bar(r.Name, r.Falls, true, $"{r.Name}　{r.Falls}回" + (r.AxeFalls > 0 ? $"（うち斧 {r.AxeFalls}回）" : "")))],
            ValueFormat = v => $"{v:0}回",
            ValueName = "落下の回数",
            Height = ChartHeight,
        };
        return Ui.ChartCard(title, description, chart,
            () => Ui.Table(["エリア", "落下の回数", "うち斧"],
                rows.Select(r => (new[] { r.Name, r.Falls.ToString(), r.AxeFalls > 0 ? r.AxeFalls.ToString() : "" }, (Action?)null)),
                [false, true, true]));
    }
}
