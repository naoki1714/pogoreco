using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Pogoreco.App.Charts;

namespace Pogoreco.App;

/// <summary>画面の部品（カード・数字のタイル・表など）をコードで組み立てるための小さな道具。</summary>
internal static class Ui
{
    public static TextBlock Text(string text, double size = 14, Brush? brush = null, bool bold = false, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Theme.TextPrimary,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = margin ?? new Thickness(0),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>見出しと大きな数字は丸いフォント（Kiwi Maru。1 太さだけなので太字にしない）。</summary>
    public static TextBlock Display(string text, double size, Brush? brush = null, Thickness? margin = null)
    {
        var t = Text(text, size, brush, margin: margin);
        t.FontFamily = Theme.Display;
        return t;
    }

    public static TextBlock H1(string text) => Display(text, 26);
    public static TextBlock H2(string text) => Display(text, 18);
    public static TextBlock Muted(string text, double size = 12) => Text(text, size, Theme.TextMuted);

    public static StackPanel Stack(params UIElement?[] children) => Panel(new StackPanel(), children);
    public static StackPanel Row(params UIElement?[] children) => Panel(new StackPanel { Orientation = Orientation.Horizontal }, children);

    private static StackPanel Panel(StackPanel panel, UIElement?[] children)
    {
        foreach (var c in children) if (c is not null) panel.Children.Add(c);
        return panel;
    }

    public static Border Card(UIElement content) => new()
    {
        Background = Theme.Surface,
        // 枠線は無し。下だけ太い線にして、ぷくっとした影のかわりにする
        BorderBrush = Theme.CardEdge,
        BorderThickness = new Thickness(0, 0, 0, 3),
        CornerRadius = new CornerRadius(24),
        Padding = new Thickness(22, 18, 22, 18),
        Margin = new Thickness(0, 0, 0, 16),
        Child = content,
    };

    /// <summary>そのビューで一番大事な数字（1 画面に 1 つ）。</summary>
    public static UIElement Hero(string label, string value) => Stack(
        Text(label, 14, Theme.TextSecondary),
        Display(value, 48));

    public static UIElement Tile(string label, string value, string? note = null) => Stack(
        Text(label, 13, Theme.TextSecondary),
        Display(value, 22),
        note is null ? null : Muted(note));

    /// <summary>
    /// 数字のタイルを並べる。1 つずつ淡い地色の丸いタイルに入れる。
    /// 幅が足りなくなったら、1 行の数を減らして折り返す（文字が縦に割れないように）。
    /// </summary>
    public static Panel Tiles(params UIElement[] tiles)
    {
        var panel = new TilePanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var t in tiles)
            panel.Children.Add(new Border
            {
                Background = Theme.Tint,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 12, 16, 12),
                Child = t,
            });
        return panel;
    }
    public static Border Notice(string text, bool warn = false) => new()
    {
        Background = Theme.Surface,
        BorderBrush = warn ? Theme.Warning : Theme.Border,
        BorderThickness = warn ? new Thickness(4, 1, 1, 1) : new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(14, 10, 14, 10),
        Margin = new Thickness(0, 0, 0, 12),
        Child = Text(text, 13),
    };

    /// <summary>状態の表示。色だけに頼らず、点と文字を並べる。</summary>
    /// <summary>山の絵などの上に置く、白っぽい小さなラベル。</summary>
    public static Border Chip(string text) => new()
    {
        Background = Brushes.White,
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(10, 2, 10, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text, 12, Theme.TextSecondary),
    };

    public static Border Badge(string text, Brush dot) => new()
    {
        BorderBrush = Theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(8, 1, 8, 1),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = Row(
            new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = dot, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center },
            Text(text, 12, Theme.TextSecondary)),
    };

    public static Button Button(string text, Action onClick, bool primary = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, primary ? "PrimaryButton" : "ActionButton");
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>
    /// 絵だけの丸いボタン。pathData は 24×24 の枠の中に描いた線（太さ 2 の線で描かれる）。
    /// spin に渡した Canvas を回すと、処理中の動きにできる。
    /// </summary>
    public static Button IconButton(string pathData, string tooltip, Action onClick, out Canvas spin)
    {
        spin = new Canvas { Width = 24, Height = 24, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0) };
        spin.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = Theme.TextPrimary,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        var b = new Button
        {
            Content = new Viewbox { Width = 16, Height = 16, Child = spin },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = tooltip,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
        b.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>「進む」を表す &gt; の丸いボタン。</summary>
    public static Button ChevronButton(string tooltip, Action onClick) => IconButton("M9,5 L16,12 L9,19", tooltip, onClick, out _);

    public static TextBlock Link(string text, Action onClick)
    {
        var t = Text(text, 12, Theme.Link);
        t.Cursor = Cursors.Hand;
        t.MouseLeftButtonUp += (_, _) => onClick();
        return t;
    }

    /// <summary>表の行をクリックしたときの処理（型を Action? にそろえるため）。</summary>
    public static Action? Click(Action onClick) => onClick;

    /// <summary>表。onClick があれば行をクリックできる。numeric の列は右寄せ。</summary>
    public static UIElement Table(string[] headers, IEnumerable<(string[] Cells, Action? OnClick)> rows, bool[]? numeric = null, double[]? weights = null)
    {
        var grid = new Grid();
        for (int c = 0; c < headers.Length; c++)
        {
            var w = weights is not null && c < weights.Length ? weights[c] : c == 0 ? 1.4 : 1;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        }
        void AddRow(int r, string[] cells, bool header, Action? onClick)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bg = new Border { BorderBrush = Theme.Grid, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
            Grid.SetRow(bg, r);
            Grid.SetColumnSpan(bg, headers.Length);
            grid.Children.Add(bg);
            if (onClick is not null)
            {
                bg.Cursor = Cursors.Hand;
                bg.MouseEnter += (_, _) => bg.Background = Theme.Hover;
                bg.MouseLeave += (_, _) => bg.Background = Brushes.Transparent;
                bg.MouseLeftButtonUp += (_, _) => onClick();
            }
            for (int c = 0; c < headers.Length; c++)
            {
                var right = numeric is not null && c < numeric.Length && numeric[c];
                var t = Text(c < cells.Length ? cells[c] : "", header ? 12 : 13, header ? Theme.TextSecondary : Theme.TextPrimary, margin: new Thickness(8, 7, 8, 7));
                t.TextWrapping = TextWrapping.NoWrap;
                t.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
                t.IsHitTestVisible = false;
                Grid.SetRow(t, r);
                Grid.SetColumn(t, c);
                grid.Children.Add(t);
            }
        }
        AddRow(0, headers, true, null);
        int i = 1;
        foreach (var (cells, onClick) in rows) AddRow(i++, cells, false, onClick);
        return grid;
    }

    /// <summary>グラフのカード。どのグラフにも同じ内容の表を用意し、切り替えられるようにする。</summary>
    public static Border ChartCard(string title, string? desc, UIElement chart, Func<UIElement> table, UIElement? above = null)
    {
        var content = new ContentControl { Content = chart };
        bool showingTable = false;
        var toggle = Link("表で見る", () => { });
        toggle.MouseLeftButtonUp += (_, _) =>
        {
            showingTable = !showingTable;
            content.Content = showingTable ? table() : chart;
            toggle.Text = showingTable ? "グラフで見る" : "表で見る";
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        header.Children.Add(Row(H2(title), desc is null ? null : Muted(desc, 12).WithMargin(new Thickness(10, 4, 0, 0))));
        return Card(Stack(header, above, content));
    }

    public static T WithMargin<T>(this T element, Thickness margin) where T : FrameworkElement
    {
        element.Margin = margin;
        return element;
    }
}

/// <summary>同じ幅のタイルを、1 行にいくつ入るかを幅から決めて並べるパネル。</summary>
internal sealed class TilePanel : Panel
{
    private const double MinTileWidth = 140, Gap = 10;

    private static int Columns(double width, int count)
    {
        int cols = Math.Clamp((int)((double.IsInfinity(width) ? 1e6 : width + Gap) / (MinTileWidth + Gap)), 1, Math.Max(1, count));
        // 最後の行に 1 枚だけ残らないよう、割り切れる列数があればそれにする（4 枚なら 2×2）
        if (cols < count && count % cols != 0)
            for (int c = cols; c >= 2; c--)
                if (count % c == 0) { cols = c; break; }
        return cols;
    }

    protected override Size MeasureOverride(Size available)
    {
        int cols = Columns(available.Width, InternalChildren.Count);
        double tileW = double.IsInfinity(available.Width) ? MinTileWidth : (available.Width - Gap * (cols - 1)) / cols;
        double total = 0, rowH = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(tileW, double.PositiveInfinity));
            rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
            if (i % cols == cols - 1 || i == InternalChildren.Count - 1) { total += rowH + (total > 0 ? Gap : 0); rowH = 0; }
        }
        return new Size(double.IsInfinity(available.Width) ? tileW * cols + Gap * (cols - 1) : available.Width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int cols = Columns(finalSize.Width, InternalChildren.Count);
        double tileW = (finalSize.Width - Gap * (cols - 1)) / cols, y = 0;
        for (int start = 0; start < InternalChildren.Count; start += cols)
        {
            double rowH = 0;
            for (int j = start; j < Math.Min(start + cols, InternalChildren.Count); j++)
                rowH = Math.Max(rowH, InternalChildren[j].DesiredSize.Height);
            for (int j = start; j < Math.Min(start + cols, InternalChildren.Count); j++)
                InternalChildren[j].Arrange(new Rect((j - start) * (tileW + Gap), y, tileW, rowH));
            y += rowH + Gap;
        }
        return finalSize;
    }
}