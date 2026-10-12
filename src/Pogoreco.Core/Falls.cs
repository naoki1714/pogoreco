namespace Pogoreco.Core;

/// <summary>落下 1 回分。時刻はラン開始からの秒、高さは表示の単位（% か m）。</summary>
public sealed record Fall(double StartTime, double EndTime, double From, double To, bool Axe)
{
    public double Distance => From - To;
}

/// <summary>高さの推移（ラン時間, 高さ）から、落下と「斧に当たった」回数を推定する。</summary>
public static class Falls
{
    /// <summary>この距離以上の下降を「大幅な落下」1 回とする（% は 10%、m は 100 m）。</summary>
    public static double Threshold(string unit) => unit == "%" ? 10 : 100;

    /// <summary>OCR の誤読（1 回だけ前後から外れた点）を取り除くときの外れ幅（% は 3、m は 30）。落下の基準とは別。</summary>
    public static double SpikeThreshold(string unit) => unit == "%" ? 3 : 30;

    /// <summary>斧（Map 2、650 m 付近にあり、当たるとスタートの 0 m に戻される）: 1 回の読み取りの間に、この高さ以上から 10 m 以下まで戻された</summary>
    private const double AxeFrom = 500, AxeTo = 10, AxeMaxGapSec = 3;

    public static List<Fall> Detect(IReadOnlyList<(double X, double Y)> points, string unit)
    {
        var thr = Threshold(unit);
        var eps = thr / 6;            // これ以上登り返したら、下降は終わり
        var result = new List<Fall>();
        bool descending = false, axe = false;
        (double X, double Y) peak = default, low = default, prev = default;
        bool hasPrev = false;

        void End()
        {
            if (peak.Y - low.Y >= thr) result.Add(new Fall(peak.X, low.X, peak.Y, low.Y, axe));
            descending = false;
            axe = false;
        }

        foreach (var p in points)
        {
            if (double.IsNaN(p.Y)) continue;
            if (!hasPrev) { prev = p; hasPrev = true; continue; }
            if (descending)
            {
                if (p.Y < low.Y) low = p;
                else if (p.Y > low.Y + eps) End();
            }
            if (!descending && p.Y < prev.Y) { descending = true; peak = prev; low = p; axe = false; }
            if (descending && unit == "m" && prev.Y >= AxeFrom && p.Y <= AxeTo && p.X - prev.X <= AxeMaxGapSec) axe = true;
            prev = p;
        }
        if (descending) End();
        return result;
    }
}
