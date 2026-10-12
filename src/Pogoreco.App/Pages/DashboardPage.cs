using System.Windows;
using System.Windows.Controls;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

internal static class DashboardPage
{
    private const int Days = 30;

    public static UIElement Build(Navigator nav) => Build(nav, ChartColorMode.None);

    private static UIElement Build(Navigator nav, ChartColorMode colorMode)
    {
        var d = nav.Data;
        var o = Stats.GetOverview(d.Sessions);
        var page = Ui.Stack(Ui.Row(
            Ui.H1("ダッシュボード"),
            o.LastPlayedAt is long last ? Ui.Text($"最後のプレイ: {Fmt.DateTime(last)}", 13, Theme.TextSecondary).WithMargin(new Thickness(12, 8, 0, 0)) : null)
            .WithMargin(new Thickness(0, 0, 0, 16)));

        if (o.SessionCount == 0)
        {
            page.Children.Add(Ui.Card(Ui.Stack(
                Ui.Text("まだ記録がありません。", 14, Theme.TextSecondary),
                Ui.Text("上の「記録つきで Pogostuck を起動」か、デスクトップの「Pogostuck（記録つき）」から遊ぶと、ここに記録が表示されます。", 14, Theme.TextSecondary))));
            return page;
        }

        // 一番見たいのは現在のラン（経過時間と高さの推移）なので、他の数字より先に出す
        if (RunsPage.Items(d).FirstOrDefault() is { } current && current.Map is int currentMap)
        {
            page.Children.Add(Ui.Row(
                Ui.H2("現在のラン"),
                Ui.Muted(Maps.MapName(currentMap)).WithMargin(new Thickness(8, 4, 0, 0)),
                Ui.Button("ランで見る", () => nav.Push(n => RunsPage.Detail(n, current.Id), "ラン")).WithMargin(new Thickness(12, 0, 0, 0)))
                .WithMargin(new Thickness(0, 0, 0, 8)));
            page.Children.Add(RunsPage.ClimbCard(d, current, currentMap, colorMode, cm => nav.Replace(n => Build(n, cm))));
        }

        page.Children.Add(Ui.Card(Ui.Stack(
            Ui.Hero("総プレイ時間", Fmt.Duration(o.TotalPlaySec)),
            Ui.Tiles(
                Ui.Tile("セッション", $"{Fmt.Count(o.SessionCount)}回"),
                Ui.Tile("挑戦（ラン）", $"{Fmt.Count(o.RunCount)}回"),
                Ui.Tile("ゴール", $"{Fmt.Count(o.FinishCount)}回"),
                Ui.Tile("Steam レベル", o.SteamLevel?.ToString() ?? "—", "最後に記録された値")))));

        var daily = Stats.GetDailyPlay(d.Sessions, Days, d.Now);
        bool inHours = daily.Max(x => x.PlaySec) > 2 * 3600;
        var chart = new BarChart
        {
            Bars = [.. daily.Select(x => new Bar($"{x.Date.Month}/{x.Date.Day}", inHours ? x.PlaySec / 3600 : x.PlaySec / 60, Tooltip: $"{x.Date.Month}/{x.Date.Day}　セッション {x.Sessions} 回・挑戦 {x.Runs} 回"))],
            ValueFormat = v => inHours ? $"{v:0.#}時間" : $"{v:0}分",
            ValueName = "プレイ時間",
            Height = 220,
            LabelEvery = 5,
        };
        page.Children.Add(Ui.ChartCard("日別プレイ時間", $"直近 {Days} 日", chart, () => Ui.Table(
            ["日付", "プレイ時間", "セッション", "挑戦"],
            daily.Where(x => x.Sessions > 0).Select(x => (new[] { $"{x.Date.Month}/{x.Date.Day}", Fmt.Duration(x.PlaySec), x.Sessions.ToString(), x.Runs.ToString() }, (Action?)null)),
            [false, true, true, true])));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var recent = Stats.ListSessionSummaries(d.Sessions).Take(6).Select(s => (new[]
        {
            Fmt.DateTime(s.StartedAt) + (Shared.SessionStatus(s.Status) is string st ? $"（{st}）" : ""),
            Fmt.Duration(s.DurationSec),
            s.RunCount.ToString(),
            s.Furthest is { } f ? Shared.ReachLabel(f.Map, f.MaxCheckpoint, f.Finished, s.SplitsRecorded) : "メニューのみ",
        }, Ui.Click(() => nav.Push(n => SessionsPage.Detail(n, s.Id)))));
        var left = Ui.Card(Ui.Stack(Ui.H2("最近のセッション").WithMargin(new Thickness(0, 0, 0, 8)),
            Ui.Table(["開始", "時間", "挑戦", "最高到達"], recent, [false, true, true, false])));

        var maps = Stats.ListMapModes(d.Sessions).Select(m => (new[]
        {
            Maps.MapModeName(m.Key),
            m.RunCount.ToString(),
            Shared.ReachLabel(m.Map, m.MaxCheckpoint, m.FinishCount > 0, m.MaxCheckpoint is not null),
            Fmt.RunTime(m.PersonalBest),
        }, Ui.Click(() => nav.Push(n => MapsPage.Detail(n, m.Key)))));
        var right = Ui.Card(Ui.Stack(Ui.H2("マップ別の記録").WithMargin(new Thickness(0, 0, 0, 8)),
            Ui.Table(["マップ", "挑戦", "最高到達", "自己ベスト"], maps, [false, true, false, true])));

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        page.Children.Add(grid);
        return page;
    }
}
