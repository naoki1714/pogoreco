namespace Pogoreco.Core;

public sealed record Overview(int SessionCount, double TotalPlaySec, int RunCount, int FinishCount, long? FirstPlayedAt, long? LastPlayedAt, int? SteamLevel);

/// <summary>Date はローカル日付。</summary>
public sealed record DailyPlay(DateOnly Date, double PlaySec, int Sessions, int Runs);

public sealed record Furthest(int Map, int Mode, int? MaxCheckpoint, bool Finished);

public sealed record SessionSummary(
    string Id, long StartedAt, double DurationSec, SessionStatus Status, int RunCount, int FinishCount,
    IReadOnlyList<MapModeKey> MapModes, bool SplitsRecorded, Furthest? Furthest);

public sealed record MapModeSummary(
    int Map, int Mode, int SessionCount, int RunCount, int FinishCount,
    double? BestFinishTime, double? PersonalBest, int? MaxCheckpoint, long LastPlayedAt)
{
    public MapModeKey Key => new(Map, Mode);
}

/// <summary>Checkpoint が -1 のときはスタート地点。</summary>
public sealed record CheckpointStat(int Checkpoint, int Reached, int EndedHere, double? BestRunTime);

public sealed record FinishPoint(string SessionId, long StartedAt, double Time, bool IsPersonalBest);

public sealed record SessionProgressPoint(string SessionId, long StartedAt, int RunCount, int? MaxCheckpoint, bool Finished);

public sealed record MapModeStats(
    MapModeSummary Summary, IReadOnlyList<CheckpointStat> Checkpoints, IReadOnlyList<FinishPoint> Finishes,
    IReadOnlyList<SessionProgressPoint> Sessions, int SessionsWithoutSplits);

public sealed record ProgressPoint(string SessionId, long At, double Percent);

public static class Stats
{
    private static bool Is(RunRecord r, MapModeKey key) => r.Map == key.Map && r.Mode == key.Mode;

    public static Overview GetOverview(IReadOnlyCollection<SessionRecord> sessions)
    {
        var newestFirst = sessions.OrderByDescending(s => s.StartedAt).ToList();
        var runs = sessions.SelectMany(s => s.Runs).ToList();
        return new Overview(
            sessions.Count,
            sessions.Sum(s => s.DurationSec),
            runs.Count,
            runs.Count(r => r.Outcome == RunOutcome.Finished),
            newestFirst.LastOrDefault()?.StartedAt,
            newestFirst.FirstOrDefault()?.StartedAt,
            // 最新のレベル（新しいセッションから順に、レベルアップ後か開始時のレベルを探す）
            newestFirst.Select(s => s.LevelUps.Count > 0 ? s.LevelUps[^1] : s.SteamStats?.Level).FirstOrDefault(l => l is not null));
    }

    public static DateOnly ToLocalDate(long ms) => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime);

    /// <summary>直近 days 日分の日別プレイ時間（プレイしなかった日も 0 で埋める）。セッションは開始日に数える。</summary>
    public static List<DailyPlay> GetDailyPlay(IEnumerable<SessionRecord> sessions, int days, long now)
    {
        var today = ToLocalDate(now);
        var result = Enumerable.Range(0, days).Select(i => today.AddDays(i - days + 1)).ToDictionary(d => d, d => new DailyPlay(d, 0, 0, 0));
        foreach (var s in sessions)
        {
            var date = ToLocalDate(s.StartedAt);
            if (result.TryGetValue(date, out var e)) result[date] = e with { PlaySec = e.PlaySec + s.DurationSec, Sessions = e.Sessions + 1, Runs = e.Runs + s.Runs.Count };
        }
        return [.. result.Values.OrderBy(d => d.Date)];
    }

    /// <summary>a のほうが先まで進んだラン（ゴール > 到達チェックポイント）なら true。</summary>
    private static bool IsFurther(RunRecord a, RunRecord? b)
    {
        if (b is null) return true;
        bool af = a.Outcome == RunOutcome.Finished, bf = b.Outcome == RunOutcome.Finished;
        if (af != bf) return af;
        return (a.MaxCheckpoint ?? -1) > (b.MaxCheckpoint ?? -1);
    }

    public static SessionSummary Summarize(SessionRecord s)
    {
        RunRecord? furthest = null;
        foreach (var r in s.Runs) if (r.Map is not null && r.Mode is not null && IsFurther(r, furthest)) furthest = r;
        return new SessionSummary(
            s.Id, s.StartedAt, s.DurationSec, s.Status, s.Runs.Count, s.Runs.Count(r => r.Outcome == RunOutcome.Finished), s.MapModes, s.SplitsRecorded,
            furthest is { Map: int fm, Mode: int fmode } ? new Furthest(fm, fmode, furthest.MaxCheckpoint, furthest.Outcome == RunOutcome.Finished) : null);
    }

    public static List<SessionSummary> ListSessionSummaries(IEnumerable<SessionRecord> sessions) =>
        [.. sessions.OrderByDescending(s => s.StartedAt).Select(Summarize)];

    private static MapModeSummary? SummarizeMapMode(IEnumerable<SessionRecord> sessions, MapModeKey key)
    {
        int sessionCount = 0, runCount = 0, finishCount = 0;
        long lastPlayedAt = 0;
        double? bestFinish = null, personalBest = null;
        int? maxCheckpoint = null;
        foreach (var s in sessions)
        {
            var runs = s.Runs.Where(r => Is(r, key)).ToList();
            if (runs.Count == 0) continue;
            sessionCount++;
            lastPlayedAt = Math.Max(lastPlayedAt, s.StartedAt);
            runCount += runs.Count;
            foreach (var r in runs)
            {
                if (r.FinishTime is double t)
                {
                    finishCount++;
                    bestFinish = bestFinish is null ? t : Math.Min(bestFinish.Value, t);
                    // ゲーム内の自己ベスト: ゴール時にログに出る自己ベストと、記録したゴールの速いほう
                    foreach (var v in new[] { r.FinishTime, r.BestTimeBefore })
                        if (v is double x) personalBest = personalBest is null ? x : Math.Min(personalBest.Value, x);
                }
                if (r.MaxCheckpoint is int c) maxCheckpoint = Math.Max(maxCheckpoint ?? -1, c);
            }
        }
        return runCount == 0 ? null : new MapModeSummary(key.Map, key.Mode, sessionCount, runCount, finishCount, bestFinish, personalBest, maxCheckpoint, lastPlayedAt);
    }

    /// <summary>遊んだことのあるマップ・モードの一覧（最後に遊んだ順）。</summary>
    public static List<MapModeSummary> ListMapModes(IReadOnlyCollection<SessionRecord> sessions) =>
        [.. sessions.SelectMany(s => s.MapModes).Distinct()
            .Select(k => SummarizeMapMode(sessions, k))
            .OfType<MapModeSummary>()
            .OrderByDescending(m => m.LastPlayedAt)];

    public static MapModeStats? GetMapModeStats(IReadOnlyCollection<SessionRecord> sessions, MapModeKey key)
    {
        var summary = SummarizeMapMode(sessions, key);
        if (summary is null) return null;

        var checkpoints = new SortedDictionary<int, CheckpointStat>();
        CheckpointStat Stat(int c) => checkpoints.TryGetValue(c, out var v) ? v : checkpoints[c] = new CheckpointStat(c, 0, 0, null);
        void Update(int c, Func<CheckpointStat, CheckpointStat> f) => checkpoints[c] = f(Stat(c));

        var finishes = new List<FinishPoint>();
        var progress = new List<SessionProgressPoint>();
        int sessionsWithoutSplits = 0;

        foreach (var s in sessions.OrderBy(s => s.StartedAt))
        {
            var runs = s.Runs.Where(r => Is(r, key)).ToList();
            if (runs.Count == 0) continue;
            if (!s.SplitsRecorded)
            {
                // 区間の記録が無いセッションは、ゴールだけ数える（到達地点が分からないので区間の統計には入れない）
                sessionsWithoutSplits++;
                foreach (var r in runs)
                    if (r is { Outcome: RunOutcome.Finished, FinishTime: double t }) finishes.Add(new FinishPoint(s.Id, s.StartedAt, t, r.IsPersonalBest));
                continue;
            }
            int? sessionMax = null;
            bool sessionFinished = false;
            foreach (var r in runs)
            {
                Update(-1, c => c with { Reached = c.Reached + 1 });
                foreach (var split in r.Splits)
                    Update(split.Checkpoint, c => c with { BestRunTime = c.BestRunTime is null ? split.RunTime : Math.Min(c.BestRunTime.Value, split.RunTime) });
                // 到達数は「そのチェックポイント以上まで進んだラン」で数える（スキップした区間も到達扱い）
                if (r.MaxCheckpoint is int max) for (int c = 0; c <= max; c++) Update(c, x => x with { Reached = x.Reached + 1 });

                if (r is { Outcome: RunOutcome.Finished, FinishTime: double t })
                {
                    sessionFinished = true;
                    finishes.Add(new FinishPoint(s.Id, s.StartedAt, t, r.IsPersonalBest));
                }
                else if (r.Outcome is RunOutcome.Reset or RunOutcome.Abandoned)
                {
                    Update(r.MaxCheckpoint ?? -1, c => c with { EndedHere = c.EndedHere + 1 });
                }
                if (r.MaxCheckpoint is int m) sessionMax = Math.Max(sessionMax ?? -1, m);
            }
            progress.Add(new SessionProgressPoint(s.Id, s.StartedAt, runs.Count, sessionMax, sessionFinished));
        }
        return new MapModeStats(summary, [.. checkpoints.Values], finishes, progress, sessionsWithoutSplits);
    }

    /// <summary>ログに出た到達%の推移（そのマップ、古い順）。</summary>
    public static List<ProgressPoint> GetProgressTimeline(IEnumerable<SessionRecord> sessions, int map) =>
        [.. sessions
            .SelectMany(s => s.Progress.Where(p => p.Map == map).Select(p => new ProgressPoint(s.Id, s.StartedAt + (long)Math.Round(p.AtSec * 1000), p.Percent)))
            .OrderBy(p => p.At)];
}
