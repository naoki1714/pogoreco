using System.Buffers.Binary;

namespace Pogoreco.Core;

/// <summary>プレイヤーの軌跡。Ys が高さ。</summary>
public sealed record Track(string Source, double? FinishTimeSec, int? GameModeId, short[] Xs, short[] Ys);

/// <summary>
/// リプレイ・セーブファイルから、プレイヤーの軌跡（位置の連続データ）を読む。
/// どちらも公式の仕様は無く、2026-10-11 に実ファイルを調べて分かった形式。
/// <list type="bullet">
/// <item>*.replay（ゴールしたラン）: ヘッダー 116 バイト。int32[0]=ステップ数, int32[10]=タイム(ms), int32[12]=ヘッダーサイズ(116)</item>
/// <item>セーブ（rog = Map 1, mog = Map 2 など。途中のラン）: ヘッダー 264 バイト。int32[7]=ヘッダーサイズ(264), int32[8]=本体サイズ, int32[13]=ゲームモード番号。本体は int32 ステップ数 + ステップ</item>
/// <item>ステップは 0.125 秒ごとに 8 バイト: int16 x, int16 y（高さ）, int16, int16</item>
/// <item>セーブの記録はラン開始から 86400 ステップ（3 時間）で止まる</item>
/// </list>
/// </summary>
public static class Replay
{
    public const double StepSec = 0.125;
    public const int StepBytes = 8;
    /// <summary>セーブに記録されるステップ数の上限（3 時間）</summary>
    public const int SaveStepCap = 86_400;

    private const int ReplayHeader = 116;
    private const int SaveHeader = 264;

    private static int I32(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b[offset..]);

    private static (short[] Xs, short[] Ys) ReadSteps(ReadOnlySpan<byte> b, int offset, int steps)
    {
        var xs = new short[steps];
        var ys = new short[steps];
        for (int s = 0; s < steps; s++)
        {
            xs[s] = BinaryPrimitives.ReadInt16LittleEndian(b[(offset + s * StepBytes)..]);
            ys[s] = BinaryPrimitives.ReadInt16LittleEndian(b[(offset + s * StepBytes + 2)..]);
        }
        return (xs, ys);
    }

    /// <summary>ゴールしたランのリプレイ（*.replay）を読む。形式が違えば null。</summary>
    public static Track? ParseReplayFile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ReplayHeader) return null;
        int steps = I32(bytes, 0), timeMs = I32(bytes, 40), headerSize = I32(bytes, 48);
        if (headerSize != ReplayHeader || steps < 0 || (long)headerSize + (long)steps * StepBytes != bytes.Length) return null;
        var (xs, ys) = ReadSteps(bytes, headerSize, steps);
        return new Track("replay", timeMs / 1000.0, null, xs, ys);
    }

    /// <summary>登っている途中のセーブ（rog, mog など）を読む。形式が違えば null。</summary>
    public static Track? ParseSaveFile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < SaveHeader) return null;
        int headerSize = I32(bytes, 28), bodySize = I32(bytes, 32);
        if (headerSize != SaveHeader || (long)headerSize + bodySize != bytes.Length) return null;
        int gameModeId = I32(bytes, 52);
        if (bodySize < 4) return new Track("save", null, gameModeId, [], []);
        int steps = I32(bytes, headerSize);
        if (steps < 0 || headerSize + 4 + (long)steps * StepBytes > bytes.Length) return null;
        var (xs, ys) = ReadSteps(bytes, headerSize + 4, steps);
        return new Track("save", null, gameModeId, xs, ys);
    }

    /// <summary>
    /// ランを見分けるキー。ランの最初の部分は後から変わらないので、先頭のステップから作る。
    /// 同じランは、途中経過のセーブでもゴール後のリプレイでも同じキーになる（TypeScript 版と同じ値）。
    /// </summary>
    public static string? TrackKey(Track track)
    {
        int n = Math.Min(64, track.Ys.Length);
        if (n < 8) return null;
        uint h = 0x811c9dc5;
        for (int s = 0; s < n; s++)
        {
            foreach (int v in new int[] { track.Xs[s], track.Ys[s] })
            {
                h ^= (uint)(v & 0xff);
                h *= 0x01000193;
                h ^= (uint)((v >> 8) & 0xff);
                h *= 0x01000193;
            }
        }
        return h.ToString("x8");
    }

    /// <summary>1 秒ごとの高さ（その 1 秒の最後の値）に間引く。</summary>
    public static List<int> HeightsPerSecond(short[] ys)
    {
        const int perSec = 8;
        var output = new List<int>(ys.Length / perSec + 1);
        for (int s = perSec - 1; s < ys.Length; s += perSec) output.Add(ys[s]);
        if (ys.Length % perSec != 0 && ys.Length > 0) output.Add(ys[^1]);
        return output;
    }

    /// <summary>グラフ用に点を減らす。区間ごとに最小・最大を残すので、落下や最高点が消えない。戻り値は (秒, 値)。</summary>
    public static List<(double X, double Y)> Downsample(IReadOnlyList<(double X, double Y)> points, int maxPoints)
    {
        if (points.Count <= maxPoints) return [.. points];
        int buckets = maxPoints / 2;
        double size = (double)points.Count / buckets;
        var output = new List<(double, double)>(maxPoints);
        for (int b = 0; b < buckets; b++)
        {
            int from = (int)(b * size), to = Math.Min(points.Count, (int)((b + 1) * size));
            if (from >= to) continue;
            int minI = from, maxI = from;
            for (int i = from; i < to; i++)
            {
                if (points[i].Y < points[minI].Y) minI = i;
                if (points[i].Y > points[maxI].Y) maxI = i;
            }
            if (minI == maxI) output.Add(points[minI]);
            else if (minI < maxI) { output.Add(points[minI]); output.Add(points[maxI]); }
            else { output.Add(points[maxI]); output.Add(points[minI]); }
        }
        return output;
    }
}
