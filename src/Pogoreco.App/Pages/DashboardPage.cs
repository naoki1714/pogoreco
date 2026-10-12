using System.Windows;
using System.Windows.Controls;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

/// <summary>ダッシュボード。項目は絞ってあり、詳しくはそれぞれのページで見る（現在のラン・セッション・マップ）。</summary>
internal static class DashboardPage
{
    public static UIElement Build(Navigator nav)
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

        // 一番見たいのは現在のラン（経過時間と高さの推移）
        if (RunsPage.Items(d).FirstOrDefault() is { } current && current.Map is int currentMap)
        {
            page.Children.Add(Ui.Row(
                Ui.H2("現在のラン"),
                Ui.Muted(Maps.MapName(currentMap)).WithMargin(new Thickness(8, 4, 0, 0)),
                Ui.ChevronButton("ランで見る", () => nav.Push(n => RunsPage.Detail(n, current.Id), "ラン")).WithMargin(new Thickness(12, 0, 0, 0)))
                .WithMargin(new Thickness(0, 0, 0, 8)));
            page.Children.Add(RunsPage.ClimbCard(d, current, currentMap, compact: true));
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var recent = Stats.ListSessionSummaries(d.Sessions).Take(5).Select(s => (new[]
        {
            Fmt.DateTime(s.StartedAt) + (Shared.SessionStatus(s.Status) is string st ? $"（{st}）" : ""),
            Fmt.Duration(s.DurationSec),
            s.RunCount.ToString(),
            s.Furthest is { } f ? Shared.ReachLabel(f.Map, f.MaxCheckpoint, f.Finished, s.SplitsRecorded) : "メニューのみ",
        }, Ui.Click(() => nav.Push(n => SessionsPage.Detail(n, s.Id), "セッション"))));
        var left = Ui.Card(Ui.Stack(
            SectionHeader("セッション", "セッションをすべて見る", () => nav.Go("セッション", SessionsPage.Build)),
            Ui.Table(["開始", "時間", "挑戦", "最高到達"], recent, [false, true, true, false], [2, 1, 0.8, 1.2])));

        var maps = Stats.ListMapModes(d.Sessions).Select(m => (new[]
        {
            Maps.MapModeName(m.Key),
            m.RunCount.ToString(),
            Shared.ReachLabel(m.Map, m.MaxCheckpoint, m.FinishCount > 0, m.MaxCheckpoint is not null),
            Fmt.RunTime(m.PersonalBest),
        }, Ui.Click(() => nav.Push(n => MapsPage.Detail(n, m.Key), "マップ"))));
        var right = Ui.Card(Ui.Stack(
            SectionHeader("マップ", "マップをすべて見る", () => nav.Go("マップ", MapsPage.Build)),
            Ui.Table(["マップ", "挑戦", "最高到達", "自己ベスト"], maps, [false, true, false, true])));

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        page.Children.Add(grid);
        return page;
    }

    /// <summary>カードの見出しと、そのページへ進む &gt; ボタン。</summary>
    private static UIElement SectionHeader(string title, string tooltip, Action onGo)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var go = Ui.ChevronButton(tooltip, onGo);
        DockPanel.SetDock(go, Dock.Right);
        header.Children.Add(go);
        header.Children.Add(Ui.H2(title));
        return header;
    }
}
