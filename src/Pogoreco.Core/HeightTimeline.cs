namespace Pogoreco.Core;

/// <summary>画面から読んだ高さの、1 回のラン分のまとまり（ラン時間が巻き戻ったら別のラン）。</summary>
public sealed record HudRun(int? Map, string Unit, IReadOnlyList<HudSample> Samples)
{
    public double FirstRunTime => Samples[0].RunTimeSec ?? 0;
    public double LastRunTime => Samples[^1].RunTimeSec ?? 0;
    public long FirstAt => Samples[0].At;
    public long LastAt => Samples[^1].At;
    public double Best => Samples.Max(s => s.Best);
    public double Current => Samples[^1].Current;
}

/// <summary>グラフ用の 1 本のラン: x はラン開始からの秒、y はゲーム内の表示（% / m / 換算できなければ内部の値）。</summary>
public sealed record HeightSeries(string Unit, IReadOnlyList<(double X, double Y)> Points);

/// <summary>区間（チェックポイント）通過。RunTime はラン開始からの秒（グラフの横軸と同じ単位）。</summary>
public sealed record Segment(int Checkpoint, double RunTime);

public static class HeightTimeline
{
    /// <summary>
    /// 画面から読んだ高さを、ランごとに分ける。タイムが読めなかったサンプルは、前のサンプルからの経過時間で補う。
    /// マップか単位が変わったとき、ラン時間が 5 秒以上巻き戻って次のサンプルもそこから続いているとき（本当のリセット）、
    /// 30 分以上間が空いてタイムが続いていないときに区切る。
    /// ラン時間は実時間より速くは進まないので、それより大きく先に進んだ値は OCR の誤読として読めなかった扱いにする。
    /// 巻き戻った値も、次のサンプルがそこから続いていなければ（1 回だけ変な値を読んだだけ）、誤読として捨てる。
    /// </summary>
    public static List<HudRun> SplitRuns(IEnumerable<HudSample> samples)
    {
        var ordered = FillMissingClock(DropTimeOutliers(samples.OrderBy(s => s.At).ToList()));
        var runs = new List<HudRun>();
        var current = new List<HudSample>();
        HudSample? prev = null;
        HudSample? lastRead = null; // 実際に読めたタイムのうち、一番新しいもの（補った値は含めない）
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            // 代わりの時計から読めたタイムに切り替わったときは、飛んだ値でも誤読とは扱わず、別のランにする
            var clockChanged = prev is not null && s.RunTimeSec is not null && prev.ClockEstimated != s.ClockEstimated;
            if (s.RunTimeSec is double t && prev?.RunTimeSec is double prevOkTime && !clockChanged
                && t - prevOkTime > (s.At - prev.At) / 1000.0 + 30)
                s = s with { RunTimeSec = null };
            var filled = false;
            if (s.RunTimeSec is null && prev?.RunTimeSec is double prevTime && s.At - prev.At < 5_000)
            {
                s = s with { RunTimeSec = prevTime + (s.At - prev.At) / 1000.0, ClockEstimated = prev.ClockEstimated };
                filled = true;
            }
            if (s.RunTimeSec is null) continue;

            // 巻き戻ったかは、実際に読めたタイムとだけ比べる（ポーズ中はタイムが止まるので、補った値は実際より進んでいることがある）
            var wentBack = !filled && !clockChanged && lastRead?.RunTimeSec is double pt && s.RunTimeSec < pt - 5;
            if (wentBack && !ConfirmsReset(ordered, i, s)) continue;

            var startsNew = prev is not null && (
                prev.Map != s.Map || prev.Unit != s.Unit || wentBack || clockChanged
                || (s.ClockEstimated && s.At - prev.At > 30 * 60_000)
                || (s.At - prev.At > 30 * 60_000 && s.RunTimeSec > prev.RunTimeSec + (s.At - prev.At) / 1000.0 + 60));
            if (startsNew && current.Count > 0)
            {
                runs.Add(new HudRun(current[0].Map, current[0].Unit, current));
                current = [];
            }
            current.Add(s);
            prev = s;
            if (!filled) lastRead = s;
        }
        if (current.Count > 0) runs.Add(new HudRun(current[0].Map, current[0].Unit, current));
        return runs;
    }

    /// <summary>
    /// タイムがまったく読めないとき（ゲームの設定でタイムの行を出していない、OCR が読めない、など）の代わりの時計を作る。
    /// 前後 5 分以内にタイムが 1 回でも読めているところは、読めた値を使う（あとの処理が補う）ので触らない。
    /// 5 分以内に 1 回も読めていないところは、「そこからの実時間」をタイムにする（グラフの横軸は、その記録の始まりからの経過時間）。
    /// 間が 30 分以上空いたら、その時計は 0 からやり直す。代わりの時計の値には ClockEstimated を付ける。
    /// </summary>
    internal static List<HudSample> FillMissingClock(List<HudSample> ordered)
    {
        const long Near = 5 * 60_000;
        var result = new List<HudSample>(ordered);
        var readAt = ordered.Where(x => x.RunTimeSec is not null).Select(x => x.At).ToList();
        bool NearRead(long at)
        {
            var k = readAt.BinarySearch(at);
            if (k < 0) k = ~k;
            return (k < readAt.Count && readAt[k] - at <= Near) || (k > 0 && at - readAt[k - 1] <= Near);
        }
        long? zeroAt = null;
        long? lastAt = null;
        for (var i = 0; i < result.Count; i++)
        {
            var s = result[i];
            if (s.RunTimeSec is null && !NearRead(s.At))
            {
                if (zeroAt is null || s.At - lastAt!.Value >= 30 * 60_000) zeroAt = s.At;
                result[i] = s with { RunTimeSec = (s.At - zeroAt.Value) / 1000.0, ClockEstimated = true };
            }
            lastAt = s.At;
        }
        return result;
    }

    /// <summary>
    /// OCR が数字を読み違えた「ラン時間」を、読めなかった扱い（null）にする。ラン時間は実時間と同じ速さで進むので、
    /// 「ラン時間 − 実時間」（ずれ）は、ポーズやリセットのあとを除いて一定のはず。前後の読めたサンプル（それぞれ最大 6 個、
    /// 60 秒以内）のうち、ずれが 8 秒以内でそろっている仲間が 3 個以上いる「まとまり」を探し、そのどれにも合わない値は誤読とする
    /// （例: 121648 を 13648、119838 を 119821 と読む。誤読が 2〜3 個続いても、まわりの正しい値が多数なので見つけられる）。
    /// ポーズ・リセットのあとは、ずれの新しい値が新しいまとまりになるので、誤読にはならない。
    /// </summary>
    internal static List<HudSample> DropTimeOutliers(List<HudSample> ordered)
    {
        const int Neighbors = 6;
        const long WindowMs = 60_000;
        const double Tolerance = 8;
        const int MinCluster = 3;
        var valid = Enumerable.Range(0, ordered.Count).Where(i => ordered[i].RunTimeSec is not null).ToList();
        double Offset(int i) => ordered[i].RunTimeSec!.Value - ordered[i].At / 1000.0;
        var result = new List<HudSample>(ordered);
        for (var v = 0; v < valid.Count; v++)
        {
            var i = valid[v];
            var near = new List<double>();
            for (var k = Math.Max(0, v - Neighbors); k <= Math.Min(valid.Count - 1, v + Neighbors); k++)
                if (k != v && Math.Abs(ordered[valid[k]].At - ordered[i].At) <= WindowMs) near.Add(Offset(valid[k]));
            if (near.Count < 5) continue;
            // 仲間が MinCluster 個以上いる値（まとまりの一員）を集める。自分もそのどれかに合っていれば、誤読ではない
            var supported = near.Where(o => near.Count(p => Math.Abs(p - o) <= Tolerance) >= MinCluster).ToList();
            if (supported.Count == 0) continue;
            var me = Offset(i);
            if (!supported.Any(o => Math.Abs(me - o) <= Tolerance)) result[i] = ordered[i] with { RunTimeSec = null };
        }
        return result;
    }

    /// <summary>
    /// 巻き戻った後の値から、次に読めたサンプルが本当に続いているか（本当のリセットなら続く。1 回だけの誤読なら元の値に戻る）。
    /// </summary>
    private static bool ConfirmsReset(List<HudSample> ordered, int index, HudSample reset)
    {
        for (var j = index + 1; j < ordered.Count && j <= index + 10; j++)
        {
            if (ordered[j].RunTimeSec is not double t) continue;
            var elapsed = (ordered[j].At - reset.At) / 1000.0;
            return t >= reset.RunTimeSec!.Value - 5 && t <= reset.RunTimeSec.Value + elapsed + 30;
        }
        return true; // 確かめられなければ、見逃さないようリセットとして扱う
    }

    /// <summary>
    /// セーブ・リプレイのランに、その続き（または同じラン）と考えられる画面の記録を結びつける。
    /// <list type="bullet">
    /// <item>登っている途中のラン: 同じマップで、ラン時間が記録の終わり（3 時間の上限など）より後まで続いている画面の記録</item>
    /// <item>ゴールしたラン: 同じマップで、最後のラン時間がゴールタイムと 10 秒以内の画面の記録</item>
    /// </list>
    /// </summary>
    public static List<HudRun> MatchHud(ClimbSummary climb, IReadOnlyList<HudRun> hudRuns) => climb.Status switch
    {
        ClimbStatus.Finished when climb.FinishTimeSec is double finish =>
            [.. hudRuns.Where(r => r.Map == climb.Map && Math.Abs(r.LastRunTime - finish) <= 10)],
        ClimbStatus.InProgress =>
            [.. hudRuns.Where(r => r.Map == climb.Map && r.LastRunTime >= climb.RecordedSec - 60 && r.LastAt >= climb.FirstSeenAt - 6 * 3600_000L)],
        _ => [],
    };

    /// <summary>
    /// グラフに描く点。セーブ・リプレイの軌跡を換算できるときはそれを使い、その後ろに画面の記録をつなぐ。
    /// 換算できない（Map 2 など）ときは、画面の記録だけ（単位をそろえるため）。
    /// </summary>
    public static HeightSeries Build(ClimbRecord? climb, IReadOnlyList<HudRun> hud)
    {
        var map = climb?.Map ?? hud.FirstOrDefault()?.Map;
        var points = new List<(double X, double Y)>();
        string? unit = null;
        if (climb is not null && climb.Heights.Count > 0)
        {
            var probe = Maps.ToDisplay(map, climb.Heights[0]);
            if (probe.Unit != "" || hud.Count == 0)
            {
                unit = probe.Unit;
                for (int i = 0; i < climb.Heights.Count; i++) points.Add((i + 1, Maps.ToDisplay(map, climb.Heights[i]).Value));
            }
        }
        var after = points.Count > 0 ? points[^1].X : double.NegativeInfinity;
        foreach (var s in hud.SelectMany(r => r.Samples).OrderBy(s => s.RunTimeSec))
        {
            if (unit is not null && unit != s.Unit) continue;
            unit ??= s.Unit;
            if (s.RunTimeSec is double t && t > after) points.Add((t, s.Current));
        }
        unit ??= "";
        return new HeightSeries(unit, BreakGaps(unit == "" ? points : RemoveSpikes(points, Falls.SpikeThreshold(unit))));
    }

    /// <summary>記録のない時間がこれより長く空いたら、その間は線でつながない（秒）。</summary>
    public const double MaxGapSec = 300;

    /// <summary>
    /// 記録が MaxGapSec より長く途切れているところに、Y が NaN の点を入れて線を切る。
    /// セーブの軌跡（3 時間まで）と画面の記録のあいだなど、データの無い長い時間を、1 本の直線で結んで
    /// 「その間ずっと同じ動きをした」ように見せないため。
    /// </summary>
    public static List<(double X, double Y)> BreakGaps(IReadOnlyList<(double X, double Y)> points)
    {
        var result = new List<(double X, double Y)>(points.Count + 4);
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0 && !double.IsNaN(points[i].Y) && !double.IsNaN(points[i - 1].Y) && points[i].X - points[i - 1].X > MaxGapSec)
                result.Add((points[i - 1].X + 1, double.NaN));
            result.Add(points[i]);
        }
        return result;
    }

    /// <summary>
    /// 画面の読み取り（OCR）の誤読で、1 回だけ前後から大きく外れた点（例: 364 → 4 → 363）を取り除く。
    /// 実際の落下・斧では、外れたあと 1 回で元の高さに戻ることはない。
    /// </summary>
    public static List<(double X, double Y)> RemoveSpikes(IReadOnlyList<(double X, double Y)> points, double threshold)
    {
        var result = new List<(double X, double Y)>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0 && i < points.Count - 1 && result.Count > 0)
            {
                var (prev, cur, next) = (result[^1].Y, points[i].Y, points[i + 1].Y);
                if (!double.IsNaN(prev) && !double.IsNaN(cur) && !double.IsNaN(next)
                    && Math.Abs(cur - prev) >= threshold && Math.Abs(cur - next) >= threshold && Math.Abs(prev - next) < threshold)
                    continue;
            }
            result.Add(points[i]);
        }
        return result;
    }

    /// <summary>
    /// そのランの区間通過を、ログ（スプリット表示オンのときだけ出る `playerCheckpointDo` 行）から探す。
    /// ゴールしたランはゴールタイムが一致するものを使う。登っている途中・リセットしたランは、同じマップで一番
    /// 新しいセッションのものを使う（通算のラン時間でログを跨いで正確に紐づける手段が無いための見た目合わせ）。
    /// 見つからなければ（スプリット表示オフだったときなど）空。
    /// </summary>
    public static IReadOnlyList<Segment> FindSegments(ClimbRecord? climb, IReadOnlyList<SessionRecord> sessions)
    {
        if (climb is null) return [];
        var candidates = sessions
            .SelectMany(s => s.Runs.Select(r => (Session: s, Run: r)))
            .Where(x => x.Run.Map == climb.Map && x.Run.Splits.Count > 0);
        if (climb.Status == ClimbStatus.Finished && climb.FinishTimeSec is double finish)
            candidates = candidates.Where(x => x.Run.Outcome == RunOutcome.Finished && x.Run.FinishTime is double t && Math.Abs(t - finish) <= 2);
        var match = candidates.OrderByDescending(x => x.Session.StartedAt).ThenByDescending(x => x.Run.Index).FirstOrDefault();
        return match.Run?.Splits.Select(s => new Segment(s.Checkpoint, s.RunTime)).ToList() ?? [];
    }
}
