using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Pogoreco.App.Charts;

namespace Pogoreco.App;

/// <summary>山の絵（遠い山・近い山・太陽・頂上の旗）を、角丸のカードとして組み立てる。</summary>
internal static class MountainArt
{
    private const double W = 960, H = 330;

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Path Shape(string data, string fill) => new() { Data = Geometry.Parse(data), Fill = B(fill) };

    /// <summary>山の絵の上に、ラベル・大きな数字・補足を重ねたカード。</summary>
    public static UIElement Hero(string label, string value, string? note, double height = 220)
    {
        var art = new Canvas { Width = W, Height = H };
        art.Children.Add(new Ellipse { Width = 88, Height = 88, Fill = B("#FBE3EA"), RenderTransform = new TranslateTransform(716, 46) });
        art.Children.Add(Shape("M0,330 L0,230 L140,150 L230,210 L330,120 L460,230 L580,90 L720,220 L820,160 L960,250 L960,330 Z", "#C9E6D6"));
        art.Children.Add(Shape("M0,330 L0,270 L180,200 L300,262 L440,180 L560,262 L700,210 L860,280 L960,240 L960,330 Z", "#9FD0B7"));
        art.Children.Add(Shape("M580,90 L612,142 L580,130 L550,142 Z", "#FFFFFF"));
        art.Children.Add(new Rectangle { Width = 2.5, Height = 40, RadiusX = 1.2, RadiusY = 1.2, Fill = B("#E8788F"), RenderTransform = new TranslateTransform(579, 52) });
        art.Children.Add(Shape("M581.5,52 L606,61 L581.5,70 Z", "#E8788F"));

        var text = Ui.Stack(
            Ui.Text(label, 13, Theme.TextSecondary),
            Ui.Display(value, 56, margin: new Thickness(0, 2, 0, 2)),
            note is null ? null : Ui.Text(note, 13, Theme.TextSecondary));
        text.Margin = new Thickness(28, 22, 0, 0);
        text.HorizontalAlignment = HorizontalAlignment.Left;
        text.VerticalAlignment = VerticalAlignment.Top;

        var grid = new Grid
        {
            Height = height,
            Margin = new Thickness(0, 0, 0, 16),
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#DCF0E4"), (Color)ColorConverter.ConvertFromString("#F2F9F4"), 90),
        };
        // 幅に合わせて山の絵を広げる（縦横比は保ち、はみ出た分は切る）
        grid.Children.Add(new Viewbox { Stretch = Stretch.UniformToFill, Child = art, IsHitTestVisible = false });
        grid.Children.Add(text);
        // 角を丸くする
        grid.SizeChanged += (_, e) => grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 28, 28);
        return grid;
    }
}
