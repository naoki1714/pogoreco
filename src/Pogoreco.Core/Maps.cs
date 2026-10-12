namespace Pogoreco.Core;

public enum HeightUnit { Percent, Meter }

/// <summary>
/// 高さ（リプレイの y）をゲーム内の表示に換算するための値。
/// Map 2 以外は割合（%）、Map 2 はメートルで表示される。
/// </summary>
/// <param name="StartY">0% / 0 m の y</param>
/// <param name="TopY">100% の y。分からなければ null</param>
/// <param name="TopMeters">頂上のメートル（Meter のとき）</param>
public sealed record HeightScale(HeightUnit Unit, double StartY, double? TopY, double? TopMeters);

public sealed record ModeInfo(int Key, string Name);

public sealed record MapInfo(
    int Index,
    string Name,
    IReadOnlyList<string> Splits,
    string GoalName,
    IReadOnlyList<ModeInfo> Modes,
    string? SaveFile = null,
    HeightScale? Height = null);

/// <summary>表示用の高さ。Unit は "%" / "m" / ""（換算できずゲーム内部の値のまま）。</summary>
public readonly record struct HeightDisplay(double Value, string Unit)
{
    public override string ToString() => Unit switch
    {
        "%" => $"{Value:0.0}%",
        "m" => $"{Math.Round(Value):#,0} m",
        _ => $"{Math.Round(Value):#,0}",
    };
}

/// <summary>マップ番号・モード番号・区間名の対応（derJunker/pogostuck-split-tracker の調査結果を元にしている）。</summary>
public static class Maps
{
    private static readonly string[] Map1Splits = ["Bones", "Wind", "Grapes", "Trees", "Pineapples", "Palm Trees", "Mushrooms", "Flowers", "Ice"];

    public static readonly IReadOnlyList<MapInfo> All =
    [
        new(0, "Map 1", Map1Splits, "Egg",
            [new(0, "通常"), new(1, "スキップなし"), new(2, "透明"), new(3, "氷"), new(4, "No Anvil Skip"), new(6, "上下逆さま"), new(7, "パズル")],
            SaveFile: "rog",
            // ゴール地点（リプレイの最後, y=23055）を 100% とし、ログに出た到達%（59.05%, 59.88%）と
            // その時点の最高到達 y（14298, 14495）が合うように 0% の位置を求めた（どちらも誤差 0.1% 以内）。
            // スタート地点（y≈1407）は 0% よりわずかに下になる
            Height: new(HeightUnit.Percent, 1719, 23055, null)),
        new(8, "Map 2", ["Ants", "Coconuts", "Slopes", "Wind", "Bees", "Vines", "Pillars", "Duck", "Plums", "Snake"], "Finale",
            [new(12, "通常"), new(13, "二段ジャンプ"), new(14, "720°"), new(15, "パズル"), new(16, "上下逆さま")],
            SaveFile: "mog",
            // 頂上は 1000 m（画面の最高到達 826 m と、同じ頃のログの到達 82.64% が一致）。
            // リプレイの y からメートルへの換算（TopY）はまだ分からない
            Height: new(HeightUnit.Meter, 19, null, 1000)),
        new(9, "Map 3", ["CP 1", "CP 2", "CP 3", "CP 4", "CP 5", "CP 6", "CP 7", "CP 8"], "Finale",
            [new(20, "通常"), new(21, "二段ジャンプ"), new(22, "パズル"), new(23, "上下逆さま")]),
        new(99, "Micro Map 1", Map1Splits, "Egg", [new(30, "通常"), new(31, "ブーストなし")]),
        new(100, "Drakula's Castle", ["Mushrooms", "Cellar", "Moving Blocks", "Ice", "Pillars"], "Tower", [new(27, "通常")]),
    ];

    public static MapInfo? Find(int map) => All.FirstOrDefault(m => m.Index == map);

    public static string MapName(int map) => Find(map)?.Name ?? $"マップ {map}";

    public static string ModeName(int map, int mode) => Find(map)?.Modes.FirstOrDefault(m => m.Key == mode)?.Name ?? $"モード {mode}";

    public static string MapModeName(MapModeKey key) => $"{MapName(key.Map)}・{ModeName(key.Map, key.Mode)}";

    /// <summary>チェックポイント番号の表示名。-1 はスタート地点。</summary>
    public static string CheckpointName(int map, int checkpoint) =>
        checkpoint < 0 ? "スタート" : Find(map)?.Splits.ElementAtOrDefault(checkpoint) ?? $"CP {checkpoint + 1}";

    /// <summary>モードが分からないときに使う、そのマップの通常モード。</summary>
    public static int? DefaultMode(int map) => Find(map)?.Modes.FirstOrDefault()?.Key;

    public static int? MapOfSaveFile(string file) => All.FirstOrDefault(m => m.SaveFile == file)?.Index;

    /// <summary>セーブのゲームモード番号（106 = Map 1, 200 = Map 2 を確認済み）からマップを引く。</summary>
    public static int? MapOfGameModeId(int? gameModeId) => (gameModeId / 100) switch
    {
        1 => 0,
        2 => 8,
        _ => null,
    };

    /// <summary>HUD の単位からマップの候補を引く（m なら Map 2）。</summary>
    public static int? MapOfHudUnit(string unit) => unit == "m" ? 8 : null;

    /// <summary>リプレイの y をゲーム内の表示（% または m）に換算する。換算できなければ y のまま。</summary>
    public static HeightDisplay ToDisplay(int? map, double y)
    {
        var scale = map is int m ? Find(m)?.Height : null;
        if (scale?.TopY is not double top) return new(y, "");
        var ratio = (y - scale.StartY) / (top - scale.StartY);
        // スタート地点は 0% より少し下になるので、0 未満は 0 として表示する
        if (scale.Unit == HeightUnit.Percent) return new(Math.Max(0, ratio * 100), "%");
        if (scale.TopMeters is double meters) return new(Math.Max(0, ratio * meters), "m");
        return new(y, "");
    }

    /// <summary>ログに出る到達%を、ゲーム内の表示（Map 2 はメートル）に換算する。</summary>
    public static HeightDisplay ProgressToDisplay(int? map, double percent)
    {
        var scale = map is int m ? Find(m)?.Height : null;
        return scale is { Unit: HeightUnit.Meter, TopMeters: double meters }
            ? new(percent / 100 * meters, "m")
            : new(percent, "%");
    }
}
