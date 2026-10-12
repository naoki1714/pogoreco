using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Pogoreco.App.Charts;

/// <summary>グラフと画面の色・文字（dataviz スキルの基準パレット・ライト）。</summary>
internal static class Theme
{
    // 見た目は「やさしい緑」。地は淡いミント、カードは白、文字は緑がかった墨。
    public static readonly Brush Page = Make("#F3F9F4");
    public static readonly Brush Surface = Make("#FFFFFF");
    /// <summary>白いカードの中に置く、淡い地色のタイルなど。</summary>
    public static readonly Brush Tint = Make("#F3F9F4");
    public static readonly Brush TextPrimary = Make("#23473A");
    public static readonly Brush TextSecondary = Make("#486155");
    public static readonly Brush TextMuted = Make("#5E766A");
    public static readonly Brush Grid = Make("#E1EFE6");
    public static readonly Brush Axis = Make("#B5CFC0");
    /// <summary>カードの下の線（影のかわり）。</summary>
    public static readonly Brush CardEdge = Make("#D5E9DC");
    public static readonly Brush Border = Make("#1A23473A");
    public static readonly Brush Hover = Make("#14239A6F");
    /// <summary>選んでいる行・メニュー。</summary>
    public static readonly Brush Selected = Make("#DAEFE3");
    public static readonly Brush Series = Make("#3A9873");
    public static readonly Brush SeriesWash = Make("#1A3A9873");
    public static readonly Brush Deemph = Make("#8CA096");
    public static readonly Brush Accent = Make("#C4506B");

    /// <summary>見出しと大きな数字の丸いフォント（Fonts/ に同梱の Kiwi Maru）。</summary>
    public static readonly FontFamily Display = new(new Uri("pack://application:,,,/"), "./Fonts/#Kiwi Maru, Yu Gothic UI");

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
    public static readonly Brush Link = Make("#1F7A58");
    public static readonly Brush Good = Make("#4FB88F");
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
