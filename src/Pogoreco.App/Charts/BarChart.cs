using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pogoreco.App.Charts;

/// <param name="Label">目盛りの名前</param>
/// <param name="Emphasis">false なら控えめな灰色（強調したい棒だけ色をつける）</param>
/// <param name="Tooltip">ホバーで出す見出し（null なら Label）</param>
public sealed record Bar(string Label, double Value, bool Emphasis = true, string? Tooltip = null);

/// <summary>
/// 棒グラフ（縦・横）。dataviz スキルの決まりごと: 24px 以下の細い棒、先端だけ 4px の角丸、棒どうしは離す、
/// 横棒は先端に値を書く、ホバーで棒を少し明るくして値を出す。
/// </summary>
public sealed class BarChart : FrameworkElement
{
    public IReadOnlyList<Bar> Bars { get; set; } = [];
    public bool Horizontal { get; set; }
    public Func<double, string> ValueFormat { get; set; } = v => v.ToString("0.##");
    public string ValueName { get; set; } = "値";
    /// <summary>縦棒の X ラベルを間引くとき、何本おきに出すか（0 なら自動）</summary>
    public int LabelEvery { get; set; }

    private readonly List<Rect> _hit = [];
    private int _hover = -1;

    public BarChart()
    {
        MouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            int i = _hit.FindIndex(r => r.Contains(p));
            if (i != _hover) { _hover = i; InvalidateVisual(); }
        };
        MouseLeave += (_, _) => { _hover = -1; InvalidateVisual(); };
        Cursor = Cursors.Hand;
    }

    protected override Size MeasureOverride(Size available) =>
        new(0, Horizontal ? Math.Max(120, Bars.Count * 28 + 30) : (double.IsNaN(Height) ? 220 : Height));

    /// <summary>先端だけ角丸の長方形（根元は四角）。</summary>
    private static Geometry RoundedEnd(Rect r, bool horizontal)
    {
        double rad = Math.Min(4, Math.Min(r.Width, r.Height) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            if (horizontal)
            {
                c.BeginFigure(r.TopLeft, true, true);
                c.LineTo(new Point(r.Right - rad, r.Top), true, false);
                c.ArcTo(new Point(r.Right, r.Top + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right, r.Bottom - rad), true, false);
                c.ArcTo(new Point(r.Right - rad, r.Bottom), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(r.BottomLeft, true, false);
            }
            else
            {
                c.BeginFigure(r.BottomLeft, true, true);
                c.LineTo(new Point(r.Left, r.Top + rad), true, false);
                c.ArcTo(new Point(r.Left + rad, r.Top), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right - rad, r.Top), true, false);
                c.ArcTo(new Point(r.Right, r.Top + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(r.BottomRight, true, false);
            }
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _hit.Clear();
        if (Bars.Count == 0) return;
        double max = Math.Max(Bars.Max(b => b.Value), 0);
        var ticks = Ticks.Nice(0, max <= 0 ? 1 : max, 4);
        double top = ticks[^1];
        var grid = Theme.Pen(Theme.Grid, 1);

        if (Horizontal)
        {
            var labels = Bars.Select(b => Theme.Text(this, b.Label, 12, Theme.TextMuted)).ToList();
            double left = labels.Max(l => l.Width) + 10, right = 44, rowH = (ActualHeight - 22) / Bars.Count;
            double plotW = Math.Max(10, ActualWidth - left - right);
            foreach (var t in ticks)
            {
                double x = Math.Round(left + t / top * plotW) + 0.5;
                dc.DrawLine(grid, new Point(x, 0), new Point(x, ActualHeight - 22));
                var tl = Theme.Text(this, ValueFormat(t), 12, Theme.TextMuted);
                dc.DrawText(tl, new Point(x - tl.Width / 2, ActualHeight - 18));
            }
            for (int i = 0; i < Bars.Count; i++)
            {
                double cy = i * rowH + rowH / 2, thick = Math.Min(18, rowH * 0.7), w = Bars[i].Value / top * plotW;
                dc.DrawText(labels[i], new Point(left - 8 - labels[i].Width, cy - labels[i].Height / 2));
                var rect = new Rect(left, cy - thick / 2, Math.Max(0, w), thick);
                if (w > 0) dc.DrawGeometry(Fill(i), null, RoundedEnd(rect, true));
                var vl = Theme.Text(this, ValueFormat(Bars[i].Value), 12, Theme.TextSecondary);
                dc.DrawText(vl, new Point(left + w + 6, cy - vl.Height / 2));
                _hit.Add(new Rect(0, i * rowH, ActualWidth, rowH));
            }
        }
        else
        {
            var yLabels = ticks.Select(t => Theme.Text(this, ValueFormat(t), 12, Theme.TextMuted)).ToList();
            double left = yLabels.Max(l => l.Width) + 10, bottom = 24, plotH = Math.Max(10, ActualHeight - bottom - 8);
            double plotW = Math.Max(10, ActualWidth - left - 8), slot = plotW / Bars.Count, thick = Math.Min(24, slot * 0.7);
            for (int i = 0; i < ticks.Count; i++)
            {
                double y = Math.Round(8 + plotH - ticks[i] / top * plotH) + 0.5;
                dc.DrawLine(grid, new Point(left, y), new Point(left + plotW, y));
                dc.DrawText(yLabels[i], new Point(left - 8 - yLabels[i].Width, y - yLabels[i].Height / 2));
            }
            int every = LabelEvery > 0 ? LabelEvery : Math.Max(1, (int)Math.Ceiling(Bars.Count / Math.Max(1, plotW / 56)));
            for (int i = 0; i < Bars.Count; i++)
            {
                double cx = left + slot * i + slot / 2, h = Bars[i].Value / top * plotH;
                var rect = new Rect(cx - thick / 2, 8 + plotH - h, thick, Math.Max(0, h));
                if (h > 0) dc.DrawGeometry(Fill(i), null, RoundedEnd(rect, false));
                if (i % every == 0 || i == Bars.Count - 1)
                {
                    var xl = Theme.Text(this, Bars[i].Label, 12, Theme.TextMuted);
                    if (i % every == 0) dc.DrawText(xl, new Point(cx - xl.Width / 2, 8 + plotH + 4));
                }
                _hit.Add(new Rect(left + slot * i, 0, slot, ActualHeight));
            }
        }
        DrawTooltip(dc);
    }

    private Brush Fill(int i)
    {
        var baseBrush = Bars[i].Emphasis ? Theme.Series : Theme.Deemph;
        if (i != _hover) return baseBrush;
        var c = ((SolidColorBrush)baseBrush).Color;
        return new SolidColorBrush(Color.FromArgb(255, (byte)Math.Min(255, c.R + 30), (byte)Math.Min(255, c.G + 30), (byte)Math.Min(255, c.B + 30)));
    }

    private void DrawTooltip(DrawingContext dc)
    {
        if (_hover < 0 || _hover >= Bars.Count) return;
        var b = Bars[_hover];
        var label = Theme.Text(this, b.Tooltip ?? b.Label, 12, Theme.TextSecondary);
        var value = Theme.Text(this, ValueFormat(b.Value), 13, Theme.TextPrimary, bold: true);
        var name = Theme.Text(this, ValueName, 12, Theme.TextSecondary);
        double w = Math.Max(label.Width, value.Width + 8 + name.Width) + 20, h = label.Height + value.Height + 16;
        var pos = Mouse.GetPosition(this);
        double bx = pos.X + 14 + w > ActualWidth ? pos.X - 14 - w : pos.X + 14, by = Math.Clamp(pos.Y - h - 6, 0, Math.Max(0, ActualHeight - h));
        dc.DrawRoundedRectangle(Theme.Surface, Theme.Pen(Theme.Border, 1), new Rect(bx, by, w, h), 8, 8);
        dc.DrawText(label, new Point(bx + 10, by + 8));
        dc.DrawText(value, new Point(bx + 10, by + 8 + label.Height));
        dc.DrawText(name, new Point(bx + 18 + value.Width, by + 8 + label.Height + (value.Height - name.Height) / 2));
    }
}
