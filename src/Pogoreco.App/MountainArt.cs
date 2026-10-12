using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Pogoreco.Core;

namespace Pogoreco.App;

/// <summary>山の絵の配色。マップごとに色を変える。</summary>
internal sealed record MountainPalette(string SkyTop, string SkyBottom, string Far, string Near, string Sun, string Flag);

/// <summary>山の絵（遠い山・近い山・太陽・頂上の旗）を、角丸のバナーとして組み立てる。</summary>
internal static class MountainArt
{
    // 絵の 1 枚分の大きさ。横に長いバナーでは、左右を入れ替えた絵をつなげて幅を埋める
    private const double W = 960, H = 330;

    // Map 1 は緑（既存）、Map 2 は赤、そのほかのマップは別の色
    private static readonly MountainPalette Green = new("#DCF0E4", "#F2F9F4", "#C9E6D6", "#9FD0B7", "#FBE3EA", "#E8788F");
    private static readonly MountainPalette Red = new("#FBE0DC", "#FDF1EE", "#F2C6BE", "#E59A8E", "#FFF1D0", "#A63A50");
    private static readonly MountainPalette Blue = new("#DCEBF8", "#F1F7FC", "#CFE2F2", "#A9C6E3", "#FBE3EA", "#E8788F");
    private static readonly MountainPalette Yellow = new("#FBF0CC", "#FDF8E8", "#F3E2A6", "#E6CC72", "#FBE3EA", "#E8788F");
    private static readonly MountainPalette Purple = new("#E6DFF4", "#F4F1FA", "#D3C8EA", "#B5A4DA", "#FFF1D0", "#E8788F");

    public static MountainPalette PaletteFor(int? map) => map switch
    {
        0 => Green,
        8 => Red,
        9 => Blue,
        99 => Yellow,
        100 => Purple,
        _ => Green,
    };

    /// <summary>「上下逆さま」のモードは、絵も上下逆さまにする。</summary>
    public static bool IsUpsideDown(int map, int mode) => Maps.ModeName(map, mode) == "上下逆さま";

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush B(string hex)
    {
        var b = new SolidColorBrush(C(hex));
        b.Freeze();
        return b;
    }

    private static Path Shape(string data, string fill) => new() { Data = Geometry.Parse(data), Fill = B(fill) };

    /// <summary>山の絵の上に、重ねたい内容（見出しなど）を置いたバナー。</summary>
    public static UIElement Banner(UIElement overlay, MountainPalette palette, double height, bool upsideDown = false, bool flatBottom = false)
    {
        var host = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        // 上下逆さま: 絵ごと上下を反転する（空が下になる）。見出しは空の側に置く
        if (upsideDown)
        {
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.RenderTransform = new ScaleTransform(1, -1);
        }
        var grid = new Grid
        {
            Height = height,
            Background = new LinearGradientBrush(C(palette.SkyTop), C(palette.SkyBottom), upsideDown ? 270 : 90),
        };
        grid.Children.Add(host);
        grid.SizeChanged += (_, e) =>
        {
            Draw(host, palette, e.NewSize);
            // 角を丸くする（カードの上半分に置くときは、下の角が丸くならないよう切り抜きを下に伸ばす）
            grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height + (flatBottom ? 30 : 0)), 24, 24);
        };

        if (overlay is FrameworkElement fe)
        {
            fe.Margin = new Thickness(24, upsideDown ? 0 : 18, 0, upsideDown ? 18 : 0);
            fe.HorizontalAlignment = HorizontalAlignment.Left;
            fe.VerticalAlignment = upsideDown ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        }
        grid.Children.Add(overlay);
        return grid;
    }

    /// <summary>バナーの大きさに合わせて絵を描く。縦横比は保ち、絵の下側（山のある側）を残して上を少し切る。</summary>
    private static void Draw(Canvas host, MountainPalette p, Size size)
    {
        host.Children.Clear();
        if (size.Width <= 0 || size.Height <= 0) return;
        var scale = size.Height * 1.1 / H;
        var tiles = (int)Math.Ceiling(size.Width / (W * scale)) + 1;
        var art = new Canvas
        {
            Width = tiles * W,
            Height = H,
            RenderTransform = new TransformGroup
            {
                Children = [new ScaleTransform(scale, scale), new TranslateTransform(0, size.Height - H * scale)],
            },
        };
        art.Children.Add(new Ellipse { Width = 88, Height = 88, Fill = B(p.Sun), RenderTransform = new TranslateTransform(716, 46) });
        for (int i = 0; i < tiles; i++)
        {
            // 左右を入れ替えた絵を交互につなぐと、つなぎ目の高さが合う
            var tile = new Canvas
            {
                Width = W,
                Height = H,
                RenderTransform = i % 2 == 0 ? new TranslateTransform(i * W, 0) : new MatrixTransform(-1, 0, 0, 1, (i + 1) * W, 0),
            };
            tile.Children.Add(Shape("M0,330 L0,230 L140,150 L230,210 L330,120 L460,230 L580,90 L720,220 L820,160 L960,250 L960,330 Z", p.Far));
            tile.Children.Add(Shape("M0,330 L0,270 L180,200 L300,262 L440,180 L560,262 L700,210 L860,280 L960,240 L960,330 Z", p.Near));
            art.Children.Add(tile);
        }
        // 頂上の雪と旗（最初の 1 枚だけ）
        art.Children.Add(Shape("M580,90 L612,142 L580,130 L550,142 Z", "#FFFFFF"));
        art.Children.Add(new Rectangle { Width = 2.5, Height = 40, RadiusX = 1.2, RadiusY = 1.2, Fill = B(p.Flag), RenderTransform = new TranslateTransform(579, 52) });
        art.Children.Add(Shape("M581.5,52 L606,61 L581.5,70 Z", p.Flag));
        host.Children.Add(art);
    }
}
