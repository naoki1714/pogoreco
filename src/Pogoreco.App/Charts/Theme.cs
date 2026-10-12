using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Pogoreco.App.Charts;

/// <summary>グラフと画面の色・文字（dataviz スキルの基準パレット・ライト）。</summary>
internal static class Theme
{
    public static readonly Brush Page = Make("#F9F9F7");
    public static readonly Brush Surface = Make("#FCFCFB");
    public static readonly Brush TextPrimary = Make("#0B0B0B");
    public static readonly Brush TextSecondary = Make("#52514E");
    public static readonly Brush TextMuted = Make("#898781");
    public static readonly Brush Grid = Make("#E1E0D9");
    public static readonly Brush Axis = Make("#C3C2B7");
    public static readonly Brush Border = Make("#1A0B0B0B");
    public static readonly Brush Hover = Make("#0A0B0B0B");
    public static readonly Brush Series = Make("#2A78D6");
    public static readonly Brush SeriesWash = Make("#1A2A78D6");
    public static readonly Brush Deemph = Make("#898781");

    /// <summary>
    /// 区間ごとの色分け用。dataviz スキルのカテゴリカル配色 8色（固定順、`validate_palette.js` で検証済み）。
    /// 8 区間を超えたら色を使い回さず、9 区間目以降は Other（Deemph のグレー）にする。
    /// </summary>
    private static readonly string[] CategoricalHex =
        ["#2A78D6", "#EB6834", "#1BAF7A", "#EDA100", "#E87BA4", "#008300", "#4A3AA7", "#E34948"];
    public static readonly IReadOnlyList<Brush> Categorical = [.. CategoricalHex.Select(Make)];
    public static readonly IReadOnlyList<Brush> CategoricalWash = [.. CategoricalHex.Select(h => Make("#1A" + h.TrimStart('#')))];
    public static readonly Brush SeriesOther = Make("#898781");
    public static readonly Brush SeriesOtherWash = Make("#1A898781");

    /// <summary>区間の番号（0 始まり）から線の色。8 区間までは固定順の色、それ以降は Other。</summary>
    public static Brush SegmentLine(int region) => region < Categorical.Count ? Categorical[region] : SeriesOther;
    public static Brush SegmentWash(int region) => region < CategoricalWash.Count ? CategoricalWash[region] : SeriesOtherWash;
    public static readonly Brush Link = Make("#256ABF");
    public static readonly Brush Good = Make("#0CA30C");
    public static readonly Brush Warning = Make("#FAB219");

    public static readonly Typeface Font = new(new FontFamily("Yu Gothic UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface FontBold = new(new FontFamily("Yu Gothic UI, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static Pen Pen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    public static FormattedText Text(Visual visual, string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, bold ? FontBold : Font, size, brush,
            VisualTreeHelper.GetDpi(visual).PixelsPerDip);
}
