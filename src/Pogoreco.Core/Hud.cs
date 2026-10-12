using System.Globalization;
using System.Text.RegularExpressions;

namespace Pogoreco.Core;

public readonly record struct HudHeights(double Current, double Best, string Unit);

/// <summary>
/// 画面左上の HUD を OCR した文字を解釈する。
/// 自分の行（黄色）は「4)名前 @ 665m/ 826m」（Map 2）や「@ 53.8%/ 97.5%」（それ以外のマップ）、
/// タイムの行は「タイム: 32h 05m 47s」の形。
/// </summary>
public static partial class Hud
{
    [GeneratedRegex(@"(\d+(?:[.,]\d)?)\s*([m%])\s*/\s*(\d+(?:[.,]\d)?)\s*([m%])")]
    private static partial Regex HeightsRe();
    [GeneratedRegex(@"(?<!\d)(\d{1,3})h(\d{1,2})m(\d{1,3})s?")]
    private static partial Regex TimeRe();

    private static double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>自分の行から「今 / 最高」を読む。読めなかったり、ありえない値なら null。</summary>
    public static HudHeights? ParseOwnLine(string text)
    {
        var m = HeightsRe().Match(text);
        if (!m.Success || m.Groups[2].Value != m.Groups[4].Value) return null;
        double current = Num(m.Groups[1].Value), best = Num(m.Groups[3].Value);
        var unit = m.Groups[2].Value;
        double max = unit == "%" ? 100 : 1000;
        if (current < 0 || best < 0 || current > max || best > max || current > best + 0.05) return null;
        return new HudHeights(current, best, unit);
    }

    /// <summary>
    /// タイムの行からラン時間（秒）を読む。OCR が数字の間に空白を入れたり、末尾の s を 5 と読んだりするのを補正する。
    /// </summary>
    public static double? ParseRunTime(string text)
    {
        var compact = Regex.Replace(text, @"\s+", "");
        var m = TimeRe().Match(compact);
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var secText = m.Groups[3].Value;
        // 「47s」の s を 5 と読んで「475」になったとき
        if (secText.Length == 3 && secText.EndsWith('5')) secText = secText[..2];
        int sec = int.Parse(secText, CultureInfo.InvariantCulture);
        if (min > 59 || sec > 59) return null;
        return h * 3600 + min * 60 + sec;
    }
}
