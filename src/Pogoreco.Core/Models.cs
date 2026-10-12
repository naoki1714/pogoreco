namespace Pogoreco.Core;

// 時刻はすべて epoch ミリ秒（long）。保存する JSON は camelCase（以前の TypeScript 版と同じ形）。

public readonly record struct MapModeKey(int Map, int Mode);

public sealed record SplitPass(int Checkpoint, double RunTime);

public enum RunOutcome { Finished, Reset, Abandoned, InProgress }

public sealed record RunRecord(
    int Index,
    int? Map,
    int? Mode,
    RunOutcome Outcome,
    IReadOnlyList<SplitPass> Splits,
    int? MaxCheckpoint,
    double? FinishTime,
    double? BestTimeBefore,
    bool IsPersonalBest,
    int Deaths,
    int StartLine,
    int? EndLine);

public enum SessionStatus { Closed, InProgress, Interrupted }

public sealed record ProgressSample(int Map, double Percent, double AtSec);

public sealed record SessionRecord(
    int ParserVersion,
    string Id,
    string? SteamId,
    string EngineVersion,
    long StartedAt,
    long EndedAt,
    double DurationSec,
    SessionStatus Status,
    SteamStats? SteamStats,
    IReadOnlyList<int> LevelUps,
    bool SplitsRecorded,
    IReadOnlyList<ProgressSample> Progress,
    IReadOnlyList<MapModeKey> MapModes,
    IReadOnlyList<RunRecord> Runs);

// ---- ランごとの高さ（セーブ・リプレイから） ----

public enum ClimbStatus { InProgress, Finished, Reset }

public record ClimbSummary
{
    /// <summary>ランの先頭部分から作ったキー（途中経過とゴール後で同じになる）</summary>
    public required string Id { get; init; }
    public int? Map { get; init; }
    /// <summary>読み取ったファイル（rog, mog, map1last.replay, best.replay など）</summary>
    public required List<string> Sources { get; init; }
    public ClimbStatus Status { get; init; }
    public long FirstSeenAt { get; init; }
    public long UpdatedAt { get; init; }
    public long? EndedAt { get; init; }
    /// <summary>記録されている長さ（秒、ラン開始から）</summary>
    public double RecordedSec { get; init; }
    public double? FinishTimeSec { get; init; }
    public bool IsPersonalBest { get; init; }
    /// <summary>3 時間の上限に達し、それ以降の軌跡はゲームに記録されていない</summary>
    public bool Capped { get; init; }
    public int MaxY { get; init; }
    public int LastY { get; init; }
}

public sealed record ClimbRecord : ClimbSummary
{
    /// <summary>1 秒ごとの高さ（y）</summary>
    public required List<int> Heights { get; init; }
}

// ---- 画面（HUD）から読んだ高さ ----

/// <summary>画面左上の HUD を読んだ 1 回分。</summary>
public sealed record HudSample
{
    /// <summary>読んだ時刻（epoch ミリ秒）</summary>
    public long At { get; init; }
    /// <summary>HUD の「タイム」（ラン開始からの秒）。読めなければ null</summary>
    public double? RunTimeSec { get; init; }
    /// <summary>今の高さ（% か m）</summary>
    public double Current { get; init; }
    /// <summary>最高到達（% か m）</summary>
    public double Best { get; init; }
    /// <summary>"%" か "m"</summary>
    public required string Unit { get; init; }
    /// <summary>そのときのマップ（ログから分かれば）</summary>
    public int? Map { get; init; }
}
