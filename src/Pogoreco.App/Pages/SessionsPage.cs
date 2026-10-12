using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

internal static class SessionsPage
{
    /// <summary>セッションの一覧。日付のバッジ・プレイ時間・最高到達のバーが並ぶ行。上に直近 7 日のまとめ。</summary>
    public static UIElement Build(Navigator nav)
    {
        var d = nav.Data;
        var sessions = Stats.ListSessionSummaries(d.Sessions);
        var page = Ui.Stack(Ui.H1("セッション").WithMargin(new Thickness(0, 0, 0, 16)));
        if (sessions.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Text("まだ記録がありません。", 14, Theme.TextSecondary)));
            return page;
        }

        var weekAgo = d.Now - 7L * 24 * 3600 * 1000;
        var week = sessions.Where(s => s.StartedAt >= weekAgo).ToList();
        page.Children.Add(Ui.Card(Ui.Tiles(
            Ui.Tile("直近 7 日のプレイ時間", Fmt.Duration(week.Sum(s => s.DurationSec))),
            Ui.Tile("直近 7 日の挑戦（ラン）", $"{week.Sum(s => s.RunCount)}回"),
            Ui.Tile("直近 7 日のゴール", $"{week.Sum(s => s.FinishCount)}回"))));

        var list = Ui.Stack();
        foreach (var s in sessions)
        {
            var id = s.Id;
            list.Children.Add(Row(d, s, () => nav.Push(n => Detail(n, id))));
        }
        page.Children.Add(Ui.Card(list));
        return page;
    }

    /// <summary>1 行: 日付のバッジ（先頭のマップの色）、プレイ時間と挑戦数、時間帯とマップ、最高到達のバー。</summary>
    private static UIElement Row(AppData d, SessionSummary s, Action onClick)
    {
        var record = d.Sessions.FirstOrDefault(x => x.Id == s.Id);
        var started = DateTimeOffset.FromUnixTimeMilliseconds(s.StartedAt).LocalDateTime;
        var palette = MountainArt.PaletteFor(s.Furthest?.Map ?? (s.MapModes.Count > 0 ? s.MapModes[0].Map : null));
        var color = (Color)ColorConverter.ConvertFromString(palette.Far);

        var badge = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Ui.Text($"{started.Month}月", 11, Theme.TextSecondary).WithAlign(TextAlignment.Center),
                    Ui.Display($"{started.Day}", 22).WithAlign(TextAlignment.Center),
                },
            },
        };

        var range = record is null ? "" : $"{started:HH:mm} – {DateTimeOffset.FromUnixTimeMilliseconds(record.EndedAt).LocalDateTime:HH:mm}";
        var maps = s.MapModes.Count > 0 ? string.Join("、", s.MapModes.Select(Maps.MapModeName)) : "メニューのみ";
        var status = Shared.SessionStatus(s.Status) is string st ? $"（{st}）" : "";
        var middle = Ui.Stack(
            Ui.Text($"{Fmt.Duration(s.DurationSec)} プレイ ・ {s.RunCount} ラン{status}", 15, bold: true),
            Ui.Muted($"{range} ・ {maps}"));
        middle.VerticalAlignment = VerticalAlignment.Center;
        middle.Margin = new Thickness(16, 0, 16, 0);

        var right = ReachBar(s);
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(badge);
        grid.Children.Add(middle);
        grid.Children.Add(right);

        var row = new Border { Padding = new Thickness(10, 10, 10, 10), CornerRadius = new CornerRadius(20), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid };
        row.MouseEnter += (_, _) => row.Background = Theme.Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => onClick();
        return row;
    }

    /// <summary>最高到達の名前と、そこまでの進み具合（スタート 0 → ゴール 1）のバー。</summary>
    private static FrameworkElement ReachBar(SessionSummary s)
    {
        if (s.Furthest is not { } f) return Ui.Muted("—");
        var label = Shared.ReachLabel(f.Map, f.MaxCheckpoint, f.Finished, s.SplitsRecorded);
        var goal = (Maps.Find(f.Map)?.Splits.Count ?? 0) + 1;
        double fraction = !f.Finished && !s.SplitsRecorded ? 0 : Math.Clamp((double)Shared.ProgressValue(f.Map, f.MaxCheckpoint, f.Finished) / goal, 0, 1);

        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.Grid, Margin = new Thickness(0, 6, 0, 0) };
        var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.Series, HorizontalAlignment = HorizontalAlignment.Left };
        var bar = new Grid { Children = { track, fill } };
        // バーの長さは、トラックの幅に対する割合で決める
        bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * fraction;
        return Ui.Stack(
            Ui.Row(Ui.Muted("最高到達　"), Ui.Text(label, 13, bold: true)),
            bar).WithMargin(new Thickness(0, 0, 8, 0));
    }

    private static T WithAlign<T>(this T text, TextAlignment align) where T : TextBlock
    {
        text.TextAlignment = align;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        return text;
    }

    public static UIElement Detail(Navigator nav, string id)
    {
        var s = nav.Data.Sessions.FirstOrDefault(x => x.Id == id);
        var page = Ui.Stack(Ui.Row(
            Ui.Link("← 戻る", nav.Back).WithMargin(new Thickness(0, 8, 12, 0)),
            Ui.H1(s is null ? "セッションが見つかりません" : $"{Fmt.DateTime(s.StartedAt)} のセッション"),
            s is not null && Shared.SessionStatus(s.Status) is string st ? Ui.Badge(st, s.Status == SessionStatus.InProgress ? Theme.Good : Theme.Warning).WithMargin(new Thickness(12, 4, 0, 0)) : null)
            .WithMargin(new Thickness(0, 0, 0, 16)));
        if (s is null) return page;

        var finishes = s.Runs.Where(r => r.Outcome == RunOutcome.Finished).ToList();
        var deaths = s.Runs.Sum(r => r.Deaths);
        page.Children.Add(Ui.Card(Ui.Tiles(
            Ui.Tile("プレイ時間", Fmt.Duration(s.DurationSec), $"{Fmt.DateTime(s.StartedAt)} 〜 {Fmt.DateTime(s.EndedAt)}"),
            Ui.Tile("挑戦（ラン）", $"{s.Runs.Count}回"),
            Ui.Tile("ゴール", $"{finishes.Count}回", finishes.Count > 0 ? $"最速 {Fmt.RunTime(finishes.Min(r => r.FinishTime))}" : null),
            Ui.Tile("タイマーが続くリセット", $"{deaths}回"))));

        if (s.Runs.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Text("このセッションではマップで遊んでいません（メニューのみ）。", 14, Theme.TextSecondary)));
            return page;
        }
        if (!s.SplitsRecorded)
            page.Children.Add(Ui.Notice("このセッションは区間の記録がありません（ゲームのスプリット表示がオフ）。ゴールしなかったランの到達地点は分かりません。高さは「ラン」で見られます。"));

        // 左: ランの一覧、右: ランごとの到達地点（マップ・モードごと）
        var left = Ui.Card(Ui.Stack(Ui.H2("ランの一覧").WithMargin(new Thickness(0, 0, 0, 8)), RunsTable(nav, s, s.Runs),
            Ui.Muted("* 区間を通過した時点のタイム。高さの記録のあるラン（ゴールしたラン・登っている途中のラン）は、行を選ぶと「ラン」で高さのグラフを見られます。").WithMargin(new Thickness(0, 8, 0, 0))));
        var right = Ui.Stack();
        if (s.SplitsRecorded)
        {
            foreach (var key in s.MapModes)
            {
                var runs = s.Runs.Where(r => r.Map == key.Map && r.Mode == key.Mode).ToList();
                var chart = new BarChart
                {
                    Bars = [.. runs.Select(r => new Bar($"#{r.Index + 1}", Shared.ProgressValue(key.Map, r.MaxCheckpoint, r.Outcome == RunOutcome.Finished),
                        r.Outcome == RunOutcome.Finished, $"#{r.Index + 1}　{Shared.Outcome(r.Outcome)}"))],
                    ValueFormat = v => Shared.ProgressLabel(key.Map, (int)Math.Round(v)),
                    ValueName = "到達",
                    Height = 240,
                };
                right.Children.Add(Ui.ChartCard($"{Maps.MapModeName(key)}：ランごとの到達地点", "緑の棒はゴールしたラン", chart, () => RunsTable(nav, s, runs)));
            }
        }

        if (right.Children.Count == 0)
        {
            page.Children.Add(left);
            return page;
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        page.Children.Add(grid);
        return page;
    }

    private static UIElement RunsTable(Navigator nav, SessionRecord s, IEnumerable<RunRecord> runs) => Ui.Table(
        ["#", "マップ", "結果", "到達", "区間", "タイム"],
        runs.Select(r => (new[]
        {
            (r.Index + 1).ToString(),
            r is { Map: int m, Mode: int mode } ? Maps.MapModeName(new MapModeKey(m, mode)) : "—",
            Shared.Outcome(r.Outcome),
            r.Map is int map ? Shared.ReachLabel(map, r.MaxCheckpoint, r.Outcome == RunOutcome.Finished, s.SplitsRecorded) : "—",
            s.SplitsRecorded ? r.Splits.Count.ToString() : "—",
            r.FinishTime is double t ? Fmt.RunTime(t) : r.Splits.Count > 0 ? $"{Fmt.RunTime(r.Splits[^1].RunTime)} *" : "—",
        }, RunsPage.FindItem(nav.Data, s, r) is { } item ? Ui.Click(() => nav.Push(n => RunsPage.Detail(n, item.Id), "ラン")) : null)),
        [true, false, false, false, true, true], [0.5, 2.2, 1, 1.6, 0.7, 1.6]);
}
