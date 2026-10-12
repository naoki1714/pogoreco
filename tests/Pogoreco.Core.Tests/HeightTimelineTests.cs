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
    public void 画面の記録があれば_軌跡は使わず画面の記録だけを描く()
    {
        var climb = Climb(ClimbStatus.InProgress, 0, 3);
        var hud = HeightTimeline.SplitRuns([S(1000, 2, 99, 99), S(2000, 4, 0.5, 1)]);
        var series = HeightTimeline.Build(climb, hud);
        Assert.Equal("%", series.Unit);
        Assert.Equal([2.0, 4], series.Points.Select(p => p.X)); // 0 秒からは描かない
    }

    [Fact]
    public void 画面の記録がまったく無いランだけ_軌跡を割合に換算して描く()
    {
        var series = HeightTimeline.Build(Climb(ClimbStatus.InProgress, 0, 3), []);
        Assert.Equal("%", series.Unit);
        Assert.Equal([1.0, 2, 3], series.Points.Select(p => p.X));
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

    [Fact]
    public void 画面の記録が長く途切れているときは_直線でつながず線を切る()
    {
        var hud = HeightTimeline.SplitRuns([S(1000, 5000, 50, 50), S(2000, 5001, 51, 51), S(1_000_000, 6000, 60, 60)]);
        var series = HeightTimeline.Build(null, hud);
        Assert.Equal([5000.0, 5001, 5002, 6000], series.Points.Select(p => p.X)); // 5001 と 6000 のあいだに切れ目
        Assert.Single(series.Points, p => double.IsNaN(p.Y));
    }

    [Fact]
    public void 点を間引いても_線の切れ目は残る()
    {
        var points = new List<(double X, double Y)>();
        for (var i = 0; i < 3000; i++) points.Add((i, i % 7));
        points.Add((3001, double.NaN));
        for (var i = 0; i < 3000; i++) points.Add((10_000 + i, i % 5));
        var down = Replay.Downsample(points, 600);
        Assert.Single(down, p => double.IsNaN(p.Y));
        Assert.True(down.Count < 1500);
        Assert.Contains(down, p => p.X >= 10_000);
    }

    [Fact]
    public void 数字の読み違いで小さく巻き戻った値や桁が欠けた値は_ランを分けず取り除く()
    {
        // 121648 を 13648 と読む、119838 を 119821 と読む、など（実データの Map 2）
        var samples = new List<HudSample>();
        double[] times = [121640, 121641, 121642, 121643, 121644, 121645, 121646, 121647, 13648, 121649, 121650, 121651, 121638, 121653, 121654, 121655, 121656, 121657];
        for (var i = 0; i < times.Length; i++) samples.Add(S(1_000_000 + i * 1000L, times[i], 600, 700, "m", 8));
        var runs = HeightTimeline.SplitRuns(samples);
        var run = Assert.Single(runs);
        Assert.All(run.Samples, s => Assert.InRange(s.RunTimeSec!.Value, 121640, 121660));
    }

    [Fact]
    public void 本当のリセットでラン時間が小さい値に戻ったら_別のランにする()
    {
        var samples = new List<HudSample>();
        for (var i = 0; i < 20; i++) samples.Add(S(1_000_000 + i * 1000L, 5000 + i, 300, 300, "m", 8));
        for (var i = 0; i < 20; i++) samples.Add(S(1_020_000 + i * 1000L, 3 + i, 20, 300, "m", 8));
        Assert.Equal(2, HeightTimeline.SplitRuns(samples).Count);
    }

    [Fact]
    public void ポーズ中にタイムが止まっていても_補った値より小さいだけでは別のランにしない()
    {
        // 誤読が続いてタイムを補ううちに、止まっている実際のタイム（119073）より先に進んだ値になり、そのあと読めた値が小さくなる
        var samples = new List<HudSample> { S(1_000_000, 119073, 700, 716, "m", 8) };
        for (var i = 1; i <= 12; i++) samples.Add(S(1_000_000 + i * 1000L, null, 700, 716, "m", 8));
        for (var i = 13; i <= 20; i++) samples.Add(S(1_000_000 + i * 1000L, 119073, 700, 716, "m", 8));
        Assert.Single(HeightTimeline.SplitRuns(samples));
    }

    [Fact]
    public void タイムがまったく読めなくても_実時間を代わりの時計にして捨てない()
    {
        // タイムの行を出していない設定など: 高さだけが読める
        var samples = Enumerable.Range(0, 30).Select(i => S(1_000_000 + i * 1000L, null, 600 + i, 700, "m", 8)).ToList();
        var run = Assert.Single(HeightTimeline.SplitRuns(samples));
        Assert.Equal(30, run.Samples.Count);
        Assert.Equal(0, run.FirstRunTime);
        Assert.Equal(29, run.LastRunTime);
    }

    [Fact]
    public void 前後にタイムが読めているところは_代わりの時計を使わない()
    {
        var samples = new List<HudSample>
        {
            S(1_000_000, null, 10, 10, "m", 8), S(1_001_000, null, 11, 11, "m", 8), S(1_002_000, 5002, 12, 12, "m", 8), S(1_003_000, 5003, 13, 13, "m", 8),
        };
        var run = Assert.Single(HeightTimeline.SplitRuns(samples));
        Assert.Equal([5002.0, 5003], run.Samples.Select(s => s.RunTimeSec!.Value)); // 先頭の 2 個はタイムが無いので捨てる（これまでどおり）
        Assert.All(run.Samples, s => Assert.False(s.ClockEstimated));
    }

    [Fact]
    public void 代わりの時計のあとにタイムが読めるようになったら_別のランにする()
    {
        var samples = Enumerable.Range(0, 400).Select(i => S(1_000_000 + i * 1000L, null, 600, 700, "m", 8)).ToList();
        samples.AddRange(Enumerable.Range(0, 30).Select(i => S(1_000_000 + (400 + i) * 1000L, 90_000 + i, 600, 700, "m", 8)));
        var runs = HeightTimeline.SplitRuns(samples);
        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Samples[0].ClockEstimated);
        Assert.False(runs[1].Samples[0].ClockEstimated);
    }

    [Fact]
    public void タイムが読めない記録が30分以上空いたら_別のランにする()
    {
        var samples = Enumerable.Range(0, 10).Select(i => S(1_000_000 + i * 1000L, null, 600, 700, "m", 8)).ToList();
        samples.AddRange(Enumerable.Range(0, 10).Select(i => S(1_000_000 + 40 * 60_000L + i * 1000L, null, 300, 700, "m", 8)));
        Assert.Equal(2, HeightTimeline.SplitRuns(samples).Count);
    }
}
