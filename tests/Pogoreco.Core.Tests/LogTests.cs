namespace Pogoreco.Core.Tests;

public class LogParserTests
{
    [Fact]
    public void ヘッダーをローカル時刻として読みセッションIDを作る()
    {
        var h = LogParser.ParseHeader("Log of A8 Engine 8.47.1 run at Sat Oct 10 21:47:28 2026")!;
        Assert.Equal("8.47.1", h.EngineVersion);
        Assert.Equal(Fixtures.Local(2026, 10, 10, 21, 47, 28), h.StartedAt);
        Assert.Equal("20261010-214728", h.SessionId);
    }

    [Fact]
    public void 一桁の日付も読める() =>
        Assert.Equal("20261009-070005", LogParser.ParseHeader("Log of A8 Engine 8.47.1 run at Fri Oct  9 07:00:05 2026")!.SessionId);

    [Fact]
    public void 区間通過を読む() =>
        Assert.Equal(
            new CheckpointEvent(9, 4000, 3, 2, 255.646, 1),
            LogParser.ParseLine("playerCheckpointDo() at frame 4000: new checkpoint(3) old(2), map3RouteCurrent(1) runTimeCurrent(255.646)", 9));

    [Fact]
    public void 関係ない行はnull() => Assert.Null(LogParser.ParseLine("Mouse found", 1));

    [Fact]
    public void 実ログ_メニューのみ()
    {
        var log = LogParser.ParseLog(Fixtures.Text("menu-only.txt"));
        Assert.Equal("20261010-214728", log.Header!.SessionId);
        Assert.Equal("76561190000000001", log.SteamId);
        Assert.Equal(["MainMenuEvent", "ClockEvent", "SteamStatsEvent", "PauseMenuEvent", "ExitEvent"], log.Events.Select(e => e.GetType().Name));

        var s = SessionAggregator.Aggregate(log)!;
        Assert.Equal(SessionStatus.Closed, s.Status);
        Assert.Equal(1097.012, s.DurationSec, 3);
        Assert.Empty(s.Runs);
        Assert.Equal(new SteamStats(181, 1647100, 12, 819702), s.SteamStats);
    }
}

public class AggregatorTests
{
    [Fact]
    public void 実ログ_Map1登頂_マップとモードを取りゴールとリセットでランを区切る()
    {
        var s = Fixtures.Session("map1-finish.txt", new AggregateOptions { GameRunning = true, Now = Fixtures.Local(2026, 10, 11, 5, 55, 0) });
        Assert.Equal("20261011-051513", s.Id);
        Assert.Equal(SessionStatus.InProgress, s.Status);
        Assert.Equal([new MapModeKey(0, 0)], s.MapModes);
        Assert.Equal(
            [(0, 0, RunOutcome.Finished, (double?)1950.879, false, 0), (0, 0, RunOutcome.Reset, null, false, 0), (0, 0, RunOutcome.InProgress, null, false, 1)],
            s.Runs.Select(r => (r.Map!.Value, r.Mode!.Value, r.Outcome, r.FinishTime, r.IsPersonalBest, r.Deaths)));
        Assert.Equal(726.781, s.Runs[0].BestTimeBefore);
        Assert.Equal([182], s.LevelUps);
        Assert.False(s.SplitsRecorded);
    }

    [Fact]
    public void 暫定ログ_リセット_ゴール_メニュー_マップ変更でランを区切る()
    {
        var s = Fixtures.Session("gameplay-provisional.txt");
        Assert.Equal(1800, s.DurationSec);
        Assert.Equal(
            [(0, 0, RunOutcome.Reset, (int?)1), (0, 0, RunOutcome.Finished, 2), (0, 0, RunOutcome.Abandoned, 0), (8, 12, RunOutcome.Abandoned, 0)],
            s.Runs.Select(r => (r.Map!.Value, r.Mode!.Value, r.Outcome, r.MaxCheckpoint)));
        var finished = s.Runs[1];
        Assert.Equal(1, finished.Deaths);
        Assert.Equal([new SplitPass(0, 33), new SplitPass(2, 150.5)], finished.Splits);
        Assert.Equal(800.5, finished.FinishTime);
        Assert.True(finished.IsPersonalBest);
        Assert.True(s.SplitsRecorded);
    }

    [Fact]
    public void 終了行が無くゲームも動いていなければ中断()
    {
        var text = string.Join('\n', Fixtures.Text("gameplay-provisional.txt").Split('\n').Take(33));
        var startedAt = Fixtures.Local(2026, 10, 11, 9, 5, 0);
        var s = SessionAggregator.Aggregate(LogParser.ParseLog(text), new AggregateOptions { FallbackEndedAt = startedAt + 300_000 })!;
        Assert.Equal(SessionStatus.Interrupted, s.Status);
        Assert.Equal(300, s.DurationSec);
    }

    [Fact]
    public void 到達の推移_マップごとに記録し時刻は単調に増える()
    {
        var log = LogParser.ParseLog(Fixtures.Text("map1-map2-progress.txt"));
        var s = SessionAggregator.Aggregate(log, new AggregateOptions { GameRunning = true, Now = log.Header!.StartedAt + 70 * 60_000 })!;
        Assert.Equal([59.05, 59.81, 59.81, 59.81, 59.81, 59.88, 59.88, 59.88], s.Progress.Where(p => p.Map == 0).Select(p => p.Percent));
        Assert.Equal([73.35, 73.45, 80.74, 82.64, 82.64, 82.64], s.Progress.Where(p => p.Map == 8).Select(p => p.Percent));
        var secs = s.Progress.Select(p => p.AtSec).ToList();
        Assert.Equal(secs.Order(), secs);
        Assert.True(s.Progress.Where(p => p.Map == 0).Max(p => p.AtSec) < 2694);
        var last = Stats.GetProgressTimeline([s], 8)[^1];
        Assert.Equal("15:58:01", DateTimeOffset.FromUnixTimeMilliseconds(last.At).LocalDateTime.ToString("HH:mm:ss"));
        Assert.Equal([(0, (int?)0), (8, 12)], s.Runs.Select(r => (r.Map!.Value, r.Mode)));
    }
}

public class StatsTests
{
    private static List<SessionRecord> Sessions() => [Fixtures.Session("menu-only.txt"), Fixtures.Session("gameplay-provisional.txt")];

    [Fact]
    public void 概要()
    {
        var o = Stats.GetOverview(Sessions());
        Assert.Equal(2, o.SessionCount);
        Assert.Equal(1097.012 + 1800, o.TotalPlaySec, 3);
        Assert.Equal((4, 1, (int?)181), (o.RunCount, o.FinishCount, o.SteamLevel));
    }

    [Fact]
    public void 日別プレイ時間はプレイしなかった日も0で埋める()
    {
        var daily = Stats.GetDailyPlay(Sessions(), 3, Fixtures.Local(2026, 10, 11, 12, 0, 0));
        Assert.Equal([("2026-10-09", 0, 0), ("2026-10-10", 1, 0), ("2026-10-11", 1, 4)], daily.Select(d => (d.Date.ToString("yyyy-MM-dd"), d.Sessions, d.Runs)));
    }

    [Fact]
    public void マップ別_どこまで到達しどこで終わったか()
    {
        var st = Stats.GetMapModeStats(Sessions(), new MapModeKey(0, 0))!;
        Assert.Equal((1, 3, 1, (double?)800.5, (int?)2), (st.Summary.SessionCount, st.Summary.RunCount, st.Summary.FinishCount, st.Summary.BestFinishTime, st.Summary.MaxCheckpoint));
        Assert.Equal(
            [new CheckpointStat(-1, 3, 0, null), new CheckpointStat(0, 3, 1, 33), new CheckpointStat(1, 2, 1, 118.128), new CheckpointStat(2, 1, 0, 150.5)],
            st.Checkpoints);
    }

    [Fact]
    public void 区間の記録が無いセッションはゴールだけ数え_自己ベストはゲーム内の値も使う()
    {
        var map1 = Fixtures.Session("map1-finish.txt", new AggregateOptions { GameRunning = true, Now = Fixtures.Local(2026, 10, 11, 5, 55, 0) });
        var st = Stats.GetMapModeStats([map1, Fixtures.Session("gameplay-provisional.txt")], new MapModeKey(0, 0))!;
        Assert.Equal(1, st.SessionsWithoutSplits);
        Assert.Equal(3, st.Checkpoints.Single(c => c.Checkpoint == -1).Reached);
        Assert.Equal([1950.879, 800.5], st.Finishes.Select(f => f.Time));
        var only = Stats.GetMapModeStats([map1], new MapModeKey(0, 0))!.Summary;
        Assert.Equal((1950.879, 726.781), (only.BestFinishTime!.Value, only.PersonalBest!.Value));
    }
}
