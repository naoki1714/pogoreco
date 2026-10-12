using System.Windows;
using Pogoreco.App.Charts;
using Pogoreco.Core;

namespace Pogoreco.App.Pages;

internal static class MapsPage
{
    public static UIElement Build(Navigator nav)
    {
        var maps = Stats.ListMapModes(nav.Data.Sessions);
        var page = Ui.Stack(Ui.H1("マップ").WithMargin(new Thickness(0, 0, 0, 16)));
        if (maps.Count == 0)
        {
            page.Children.Add(Ui.Card(Ui.Text("まだマップで遊んだ記録がありません。", 14, Theme.TextSecondary)));
            return page;
        }
        var rows = maps.Select(m => (new[]
        {
            Maps.MapName(m.Map),
            Maps.ModeName(m.Map, m.Mode),
            m.SessionCount.ToString(),
            m.RunCount.ToString(),
            m.FinishCount.ToString(),
            Shared.ReachLabel(m.Map, m.MaxCheckpoint, m.FinishCount > 0, m.MaxCheckpoint is not null),
            Fmt.RunTime(m.PersonalBest),
            Fmt.DateTime(m.LastPlayedAt),
        }, Ui.Click(() => nav.Push(n => Detail(n, m.Key)))));
        page.Children.Add(Ui.Card(Ui.Table(["マップ", "モード", "セッション", "挑戦", "ゴール", "最高到達", "自己ベスト", "最後のプレイ"], rows,
            [false, false, true, true, true, false, true, false])));
        return page;
    }

    public static UIElement Detail(Navigator nav, MapModeKey key)
    {
        var st = Stats.GetMapModeStats(nav.Data.Sessions, key);
        var page = Ui.Stack(Ui.Row(
            Ui.Link("← 戻る", nav.Back).WithMargin(new Thickness(0, 8, 12, 0)),
            Ui.H1(Maps.MapModeName(key)),
            Ui.Link("ランで見る", () => nav.Push(n => RunsPage.ListForMap(n, key.Map), "ラン")).WithMargin(new Thickness(12, 12, 0, 0))).WithMargin(new Thickness(0, 0, 0, 16)));
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
            page.Children.Add(Ui.Notice($"区間の記録が無いセッション {st.SessionsWithoutSplits} 件（ゲームのスプリット表示がオフ）は、区間ごとのグラフに含めていません。ゴールタイムには含めています。"));

        if (st.Checkpoints.Count > 0)
        {
            UIElement Table() => Ui.Table(["地点", "到達したラン", "ここで終わったラン", "最速通過"],
                st.Checkpoints.AsEnumerable().Reverse().Select(c => (new[] { Maps.CheckpointName(key.Map, c.Checkpoint), c.Reached.ToString(), c.EndedHere.ToString(), Fmt.RunTime(c.BestRunTime) }, (Action?)null)),
                [false, true, true, true]);
            // 上がゴール側（登山の向き）
            var ordered = st.Checkpoints.AsEnumerable().Reverse().ToList();
            page.Children.Add(Ui.ChartCard("どこまで登れたか", "その地点まで到達したラン数",
                new BarChart { Horizontal = true, Bars = [.. ordered.Select(c => new Bar(Maps.CheckpointName(key.Map, c.Checkpoint), c.Reached))], ValueFormat = v => $"{v:0}", ValueName = "到達したラン" }, Table));
            page.Children.Add(Ui.ChartCard("どこで終わったか", "ここを最後にリセット・中断したラン数",
                new BarChart { Horizontal = true, Bars = [.. ordered.Select(c => new Bar(Maps.CheckpointName(key.Map, c.Checkpoint), c.EndedHere))], ValueFormat = v => $"{v:0}", ValueName = "ここで終わったラン" }, Table));
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
