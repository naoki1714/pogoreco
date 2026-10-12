using System.Windows;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

internal static class SessionsPage
{
    public static UIElement Build(Navigator nav)
    {
        var sessions = Stats.ListSessionSummaries(nav.Data.Sessions);
        var page = Ui.Stack(Ui.H1("セッション").WithMargin(new Thickness(0, 0, 0, 16)));
        if (sessions.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Text("まだ記録がありません。", 14, Theme.TextSecondary)));
            return page;
        }
        var rows = sessions.Select(s => (new[]
        {
            Fmt.DateTime(s.StartedAt) + (Shared.SessionStatus(s.Status) is string st ? $"（{st}）" : ""),
            Fmt.Duration(s.DurationSec),
            s.MapModes.Count > 0 ? string.Join("、", s.MapModes.Select(Maps.MapModeName)) : "メニューのみ",
            s.RunCount.ToString(),
            s.FinishCount.ToString(),
            s.Furthest is { } f ? Shared.ReachLabel(f.Map, f.MaxCheckpoint, f.Finished, s.SplitsRecorded) : "—",
        }, Ui.Click(() => nav.Push(n => Detail(n, s.Id)))));
        page.Children.Add(Ui.Card(Ui.Table(["開始", "プレイ時間", "マップ", "挑戦", "ゴール", "最高到達"], rows, [false, true, false, true, true, false], [1.4, 1, 3, 0.8, 0.8, 1.2])));
        return page;
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
                page.Children.Add(Ui.ChartCard($"{Maps.MapModeName(key)}：ランごとの到達地点", "青はゴールしたラン", chart, () => RunsTable(nav, s, runs)));
            }
        }
        page.Children.Add(Ui.Card(Ui.Stack(Ui.H2("ランの一覧").WithMargin(new Thickness(0, 0, 0, 8)), RunsTable(nav, s, s.Runs),
            Ui.Muted("高さの記録のあるラン（ゴールしたラン・登っている途中のラン）は、行を選ぶと「ラン」で高さのグラフを見られます。").WithMargin(new Thickness(0, 8, 0, 0)))));
        return page;
    }

    private static UIElement RunsTable(Navigator nav, SessionRecord s, IEnumerable<RunRecord> runs) => Ui.Table(
        ["#", "マップ", "結果", "到達", "通過した区間", "タイム"],
        runs.Select(r => (new[]
        {
            (r.Index + 1).ToString(),
            r is { Map: int m, Mode: int mode } ? Maps.MapModeName(new MapModeKey(m, mode)) : "—",
            Shared.Outcome(r.Outcome),
            r.Map is int map ? Shared.ReachLabel(map, r.MaxCheckpoint, r.Outcome == RunOutcome.Finished, s.SplitsRecorded) : "—",
            s.SplitsRecorded ? r.Splits.Count.ToString() : "—",
            r.FinishTime is double t ? Fmt.RunTime(t) : r.Splits.Count > 0 ? $"{Fmt.RunTime(r.Splits[^1].RunTime)}（区間通過時）" : "—",
        }, RunsPage.FindItem(nav.Data, s, r) is { } item ? Ui.Click(() => nav.Push(n => RunsPage.Detail(n, item.Id), "ラン")) : null)),
        [true, false, false, false, true, true]);
}
