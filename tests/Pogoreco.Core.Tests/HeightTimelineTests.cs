namespace Pogoreco.Core.Tests;

public class HeightTimelineTests
{
    private static HudSample S(long at, double? runTime, double current, double best, string unit = "%", int? map = 0) =>
        new() { At = at, RunTimeSec = runTime, Current = current, Best = best, Unit = unit, Map = map };

    [Fact]
    public void ラン時間が巻き戻ったら別のランにし_読めなかったタイムは補う()
    {
        var runs = HeightTimeline.SplitRuns(
        [
            S(1000, 100, 10, 10),
            S(2000, null, 11, 11), // タイムが読めなかった → 101
            S(3000, 102, 12, 12),
            S(4000, 0, 0, 0), // リセットして最初から
            S(5000, 1, 1, 1),
        ]);
        Assert.Equal(2, runs.Count);
        Assert.Equal([100.0, 101, 102], runs[0].Samples.Select(s => s.RunTimeSec!.Value));
        Assert.Equal((0.0, 1.0), (runs[1].FirstRunTime, runs[1].LastRunTime));
    }

    [Fact]
    public void 実時間よりありえないほど先に進んだタイムは誤読として補う()
    {
        // OCR がノイズを数字と誤読し、1 秒しか経っていないのにラン時間が 54 万時間先に進んだ（実データで発生した例）
        var runs = HeightTimeline.SplitRuns(
        [
            S(1000, 100, 10, 10),
            S(2000, 1_949_158_624, 11, 11), // 誤読 → 読めなかった扱いで 101 に補う
            S(3000, 102, 12, 12),
        ]);
        Assert.Single(runs);
        Assert.Equal([100.0, 101, 102], runs[0].Samples.Select(s => s.RunTimeSec!.Value));
    }

    [Fact]
    public void 一瞬だけ巻き戻ってすぐ元に戻るタイムは誤読として捨てる()
    {
        // 実データで見つかった例: 118851 → 10856（誤読）→ 118858 と、1 回だけ大きく巻き戻ってすぐ元の値に戻る
        var runs = HeightTimeline.SplitRuns(
        [
            S(1000, 118850, 10, 10, "m", 8),
            S(2000, 118851, 11, 11, "m", 8),
            S(13000, 10856, 12, 12, "m", 8), // 誤読
            S(14000, null, 12, 12, "m", 8),
            S(15000, 118858, 13, 13, "m", 8), // 誤読の前の値からすぐ続く → 誤読と分かる
        ]);
        Assert.Single(runs);
        Assert.Equal([118850.0, 118851, 118858], runs[0].Samples.Select(s => s.RunTimeSec!.Value));
    }

    [Fact]
    public void 巻き戻った後も続けば本当のリセットとして別のランにする()
    {
        var runs = HeightTimeline.SplitRuns(
        [
            S(1000, 100, 10, 10),
            S(2000, 0, 0, 0), // リセット
            S(3000, 1, 1, 1),
            S(4000, 2, 2, 2),
        ]);
        Assert.Equal(2, runs.Count);
        Assert.Equal([0.0, 1, 2], runs[1].Samples.Select(s => s.RunTimeSec!.Value));
    }

    [Fact]
    public void マップが変わったら別のラン()
    {
        var runs = HeightTimeline.SplitRuns([S(1000, 100, 50, 60), S(2000, 5000, 600, 700, "m", 8)]);
        Assert.Equal([(int?)0, 8], runs.Select(r => r.Map));
    }

    private static ClimbRecord Climb(ClimbStatus status, int map, double recordedSec, double? finish = null, long firstSeenAt = 0) => new()
    {
        Id = "c",
        Map = map,
        Sources = ["rog"],
        Status = status,
        FirstSeenAt = firstSeenAt,
        RecordedSec = recordedSec,
        FinishTimeSec = finish,
        Heights = [.. Enumerable.Range(0, (int)recordedSec).Select(i => 1719 + i * 10)],
    };

    private static SessionRecord Session(long startedAt, params RunRecord[] runs) => new(
        1, "s", null, "8.47.1", startedAt, startedAt, 0, SessionStatus.Closed, null, [], runs.Length > 0, [], [], runs);

    private static RunRecord Run(int index, int map, RunOutcome outcome, double? finish, params (int Checkpoint, double RunTime)[] splits) => new(
        index, map, 0, outcome, [.. splits.Select(s => new SplitPass(s.Checkpoint, s.RunTime))], splits.Length > 0 ? splits[^1].Checkpoint : null, finish, null, false, 0, 0, null);

    [Fact]
    public void 区間通過はゴールタイムが一致するランから探す()
    {
        var sessions = new[]
        {
            Session(1000, Run(0, 0, RunOutcome.Finished, 100.5, (0, 30), (1, 70))),
            Session(2000, Run(0, 0, RunOutcome.Finished, 200.2, (0, 40), (1, 90))), // 別のゴール（タイムが違う）
        };
        var climb = Climb(ClimbStatus.Finished, 0, 1, finish: 100.879); // ms の端数はゲームと記録係で丸めが違いうる
        var segments = HeightTimeline.FindSegments(climb, sessions);
        Assert.Equal([(0, 30.0), (1, 70.0)], segments.Select(s => (s.Checkpoint, s.RunTime)));
    }

    [Fact]
    public void 登っている途中のランの区間通過は_同じマップで一番新しいセッションから探す()
    {
        var sessions = new[]
        {
            Session(1000, Run(0, 0, RunOutcome.Abandoned, null, (0, 30))),
            Session(2000, Run(0, 0, RunOutcome.InProgress, null, (0, 40), (1, 90))), // 一番新しい
        };
        var climb = Climb(ClimbStatus.InProgress, 0, 1000);
        var segments = HeightTimeline.FindSegments(climb, sessions);
        Assert.Equal([(0, 40.0), (1, 90.0)], segments.Select(s => (s.Checkpoint, s.RunTime)));
    }

    [Fact]
    public void スプリット表示がオフだと区間通過は空()
    {
        var climb = Climb(ClimbStatus.Finished, 0, 1, finish: 100);
        Assert.Empty(HeightTimeline.FindSegments(climb, [Session(1000, Run(0, 0, RunOutcome.Finished, 100, []))]));
    }

    [Fact]
    public void 登っている途中のランには_記録の終わりより後まで続く画面の記録をつなぐ()
    {
        var hud = HeightTimeline.SplitRuns([S(10_000, 290, 1.5, 1.5), S(11_000, 291, 1.6, 1.6)]);
        Assert.Single(HeightTimeline.MatchHud(Climb(ClimbStatus.InProgress, 0, 300), hud));
        Assert.Empty(HeightTimeline.MatchHud(Climb(ClimbStatus.InProgress, 8, 300), hud)); // マップが違う
        Assert.Empty(HeightTimeline.MatchHud(Climb(ClimbStatus.InProgress, 0, 900), hud)); // まだ記録の途中
    }

    [Fact]
    public void ゴールしたランには_最後のラン時間がゴールタイムに近い画面の記録をつなぐ()
    {
        var hud = HeightTimeline.SplitRuns([S(1000, 1940, 99, 99), S(2000, 1950, 100, 100)]);
        Assert.Single(HeightTimeline.MatchHud(Climb(ClimbStatus.Finished, 0, 1950, finish: 1950.879), hud));
        Assert.Empty(HeightTimeline.MatchHud(Climb(ClimbStatus.Finished, 0, 700, finish: 726.781), hud));
    }

    [Fact]
    public void Map1は軌跡を割合に換算し_その後ろに画面の記録をつなぐ()
    {
        var climb = Climb(ClimbStatus.InProgress, 0, 3);
        var hud = HeightTimeline.SplitRuns([S(1000, 2, 99, 99), S(2000, 4, 0.5, 1)]);
        var series = HeightTimeline.Build(climb, hud);
        Assert.Equal("%", series.Unit);
        Assert.Equal([1.0, 2, 3, 4], series.Points.Select(p => p.X)); // 2 秒目の画面の値は軌跡と重なるので使わない
        Assert.Equal(0.5, series.Points[^1].Y);
    }

    [Fact]
    public void Map2は軌跡を換算できないので画面の記録だけ()
    {
        var climb = Climb(ClimbStatus.InProgress, 8, 3);
        var hud = HeightTimeline.SplitRuns([S(1000, 115_000, 665, 826, "m", 8)]);
        var series = HeightTimeline.Build(climb, hud);
        Assert.Equal("m", series.Unit);
        Assert.Equal([(115_000.0, 665.0)], series.Points);
    }
}
