using System.Globalization;

namespace Pogoreco.App;

/// <summary>表示用の書式。</summary>
internal static class Fmt
{
    private static readonly string[] Weekdays = ["日", "月", "火", "水", "木", "金", "土"];

    private static DateTime Local(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    /// <summary>プレイ時間など（例: 3時間 5分 / 12分 / 45秒）</summary>
    public static string Duration(double sec)
    {
        var s = (long)Math.Round(Math.Max(0, sec));
        if (s < 60) return $"{s}秒";
        var minutes = s / 60;
        if (minutes < 60) return $"{minutes}分";
        return minutes % 60 == 0 ? $"{minutes / 60}時間" : $"{minutes / 60}時間 {minutes % 60}分";
    }

    /// <summary>ランのタイム（例: 13:39.702 / 1:02:03.400）</summary>
    public static string RunTime(double? sec)
    {
        if (sec is not double v) return "—";
        var t = TimeSpan.FromMilliseconds(Math.Round(v * 1000));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}" : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    /// <summary>ラン開始からの経過時間（例: 32:05:47 / 12:34）</summary>
    public static string Elapsed(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Max(0, sec)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>経過時間の目盛り（例: 32時間 / 1時間30分 / 45分）</summary>
    public static string ElapsedTick(double sec)
    {
        var totalMin = (long)Math.Round(sec / 60);
        long h = totalMin / 60, m = totalMin % 60;
        if (h == 0) return $"{m}分";
        return m == 0 ? $"{h}時間" : $"{h}時間{m}分";
    }

    /// <summary>例: 10/10(土) 21:47</summary>
    public static string DateTime(long ms)
    {
        var d = Local(ms);
        return $"{d.Month}/{d.Day}({Weekdays[(int)d.DayOfWeek]}) {d:HH:mm}";
    }

    /// <summary>例: 2026/10/10</summary>
    public static string Date(long ms) => Local(ms).ToString("yyyy/M/d", CultureInfo.InvariantCulture);

    public static string Count(int n) => n.ToString("#,0", CultureInfo.InvariantCulture);
}
