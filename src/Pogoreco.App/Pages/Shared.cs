using Pogoreco.Core;

namespace Pogoreco.App.Pages;

/// <summary>ページ共通の表示。</summary>
internal static class Shared
{
    /// <summary>
    /// 登山の高さとしての到達地点の値。0 = スタート、k = k 番目のチェックポイント（0 始まりの k-1）、区間数 + 1 = ゴール。
    /// </summary>
    public static int ProgressValue(int map, int? maxCheckpoint, bool finished) =>
        finished ? (Maps.Find(map)?.Splits.Count ?? (maxCheckpoint ?? -1) + 1) + 1 : (maxCheckpoint ?? -1) + 1;

    public static string ProgressLabel(int map, int value)
    {
        if (value <= 0) return "スタート";
        var info = Maps.Find(map);
        if (info is not null && value > info.Splits.Count) return $"{info.GoalName}（ゴール）";
        return Maps.CheckpointName(map, value - 1);
    }

    /// <summary>到達地点の表示名。区間の記録が無いとき（スプリット表示オフ）は、ゴールしていなければ分からない。</summary>
    public static string ReachLabel(int map, int? maxCheckpoint, bool finished, bool splitsRecorded = true) =>
        !finished && !splitsRecorded ? "記録なし" : ProgressLabel(map, ProgressValue(map, maxCheckpoint, finished));

    public static string Outcome(RunOutcome o) => o switch
    {
        RunOutcome.Finished => "ゴール",
        RunOutcome.Reset => "リセット",
        RunOutcome.InProgress => "プレイ中",
        _ => "中断",
    };

    public static string? SessionStatus(Core.SessionStatus s) => s switch
    {
        Core.SessionStatus.InProgress => "プレイ中",
        Core.SessionStatus.Interrupted => "終了の記録なし",
        _ => null,
    };

    /// <summary>高さの値を単位つきで書く。</summary>
    public static string Height(double value, string unit) => new HeightDisplay(value, unit).ToString();

    /// <summary>高さのグラフの縦軸（% は 0〜100、m は 0〜1000、換算できなければ自動）。</summary>
    public static (double? Min, double? Max, double[]? Ticks) HeightAxis(string unit) => unit switch
    {
        "%" => (0, 100, [0, 25, 50, 75, 100]),
        "m" => (0, 1000, [0, 250, 500, 750, 1000]),
        _ => (null, null, null),
    };
}
