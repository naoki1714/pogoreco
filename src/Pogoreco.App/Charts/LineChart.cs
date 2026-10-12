using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pogoreco.App.Charts;

public enum XAxisKind { Number, Duration, Time }

/// <summary>
/// 色分けの種類。Time はラン開始からの経過時間（Segments）、Height は高さ（Areas）で塗り分ける。
/// Height は、落下して前のエリアに戻ったところが（時間は先に進んでいても）元の色に戻るので分かる。
/// </summary>
public enum ChartColorMode { None, Time, Height }

/// <summary>
/// 折れ線（面）グラフ。外部ライブラリを使わず WPF の描画だけで描く（軽さのため）。
/// 見た目は dataviz スキルの決まりごと: 2px の線、面は 10% の塗り、8px の点（背景色の縁取り）、
/// 実線の細い目盛り線、ホバーで縦線と値（値が太字・名前は控えめ）。Y が NaN の点で線を切る。
/// ドラッグで範囲を選んで X を拡大できる（見たい範囲を直接囲む）。ホイールでも拡大・縮小できる（カーソルの位置を中心に）。
/// 拡大中は Y も見えている範囲に合わせる。ダブルクリックか、右上に出る「全体を表示」で元に戻る。
/// 区間（Segments）を渡すと、縦の目盛りとホバーに区間名を出す（ColorMode に関わらず常に出す）。
/// ColorMode で、線・面を時間の区間ごと（Time）か、高さのエリアごと（Height、Areas を使う）に色分けできる。
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public IReadOnlyList<(double X, double Y)> Points { get; set; } = [];
    public XAxisKind XKind { get; set; }
    /// <summary>X の目盛りの書き方</summary>
    public Func<double, string> XFormat { get; set; } = x => x.ToString("0.##");
    /// <summary>ホバーで出す X の書き方（null なら XFormat）</summary>
    public Func<double, string>? HoverXFormat { get; set; }
    public Func<double, string> YFormat { get; set; } = y => y.ToString("0.##");
    public double? YMin { get; set; }
    public double? YMax { get; set; }
    public IReadOnlyList<double>? YTicks { get; set; }
    public bool Area { get; set; }
    public bool Step { get; set; }
    public string ValueName { get; set; } = "値";
    /// <summary>区間の境界（ラン開始からの秒・区間名）。X の昇順。縦の目盛りとホバーに常に使う。</summary>
    public IReadOnlyList<(double At, string Name)>? Segments { get; set; }
    /// <summary>エリアの境界（高さ・名前）。Y の昇順。ColorMode が Height のときの色分けに使う。</summary>
    public IReadOnlyList<(double Value, string Name)>? Areas { get; set; }
    public ChartColorMode ColorMode { get; set; }

    private const double Top = 10, Right = 14, Bottom = 24;
    private static readonly Pen SegmentTickPen = MakeDashedPen();
    private static Pen MakeDashedPen()
    {
        var pen = new Pen(Theme.Axis, 1) { DashStyle = new DashStyle([3, 2], 0) };
        pen.Freeze();
        return pen;
    }
    private int _hover = -1;
    private Rect _plot;
    private double _x0, _x1, _y0, _y1;
    private double _dataX0, _dataX1;
    private double? _zoomX0, _zoomX1;
    private Rect _resetLabelRect;
    private bool _dragging;
    private double _dragStartX, _dragCurrentX;

    public LineChart()
    {
        Height = 260;
        ClipToBounds = true;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { if (!_dragging) { _hover = -1; InvalidateVisual(); } };
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += (_, _) => { _dragging = false; InvalidateVisual(); };
        Cursor = Cursors.Cross;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragging)
        {
            _dragCurrentX = Math.Clamp(pos.X, _plot.Left, _plot.Right);
            InvalidateVisual();
            return;
        }
        Cursor = _resetLabelRect.Contains(pos) ? Cursors.Hand : Cursors.Cross;
        UpdateHover(pos);
    }

    /// <summary>ドラッグで選んだ範囲にズームする。ほぼ動かさずに離したらクリックとして扱い、何もしない。</summary>
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var endX = Math.Clamp(e.GetPosition(this).X, _plot.Left, _plot.Right);
        if (Math.Abs(endX - _dragStartX) < 4) { InvalidateVisual(); return; }
        var a = InvPx(Math.Min(_dragStartX, endX));
        var b = InvPx(Math.Max(_dragStartX, endX));
        var full = _dataX1 - _dataX0;
        var span = Math.Max(b - a, full * 0.001);
        _zoomX0 = Math.Clamp(a, _dataX0, _dataX1 - span);
        _zoomX1 = _zoomX0 + span;
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>ホイールで拡大・縮小（カーソルの位置を中心に）。全体の範囲まで縮めたらズーム解除。</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var full = _dataX1 - _dataX0;
        if (full <= 0 || !_plot.Contains(e.GetPosition(this))) return;
        var center = _x0 + (e.GetPosition(this).X - _plot.Left) / _plot.Width * (_x1 - _x0);
        var factor = e.Delta > 0 ? 0.8 : 1.25;
        var span = Math.Clamp((_x1 - _x0) * factor, full * 0.001, full);
        if (span >= full) { _zoomX0 = null; _zoomX1 = null; }
        else
        {
            var x0 = Math.Clamp(center - span / 2, _dataX0, _dataX1 - span);
            _zoomX0 = x0;
            _zoomX1 = x0 + span;
        }
        _hover = -1;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// ダブルクリック、または「全体を表示」ラベルのクリックでズームを解除。
    /// それ以外でプロット内を押したら、範囲選択ズームのドラッグを始める。
    /// </summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            if (_zoomX0 is not null) { _zoomX0 = null; _zoomX1 = null; _hover = -1; InvalidateVisual(); }
            return;
        }
        if (_resetLabelRect.Contains(pos))
        {
            _zoomX0 = null;
            _zoomX1 = null;
            _hover = -1;
            InvalidateVisual();
            return;
        }
        if (!_plot.Contains(pos)) return;
        _dragging = true;
        _dragStartX = _dragCurrentX = pos.X;
        _hover = -1;
        CaptureMouse();
        e.Handled = true;
    }

    private double Px(double x) => _plot.Left + (_x1 == _x0 ? 0.5 : (x - _x0) / (_x1 - _x0)) * _plot.Width;
    private double Py(double y) => _plot.Bottom - (_y1 == _y0 ? 0.5 : (y - _y0) / (_y1 - _y0)) * _plot.Height;
    private double InvPx(double px) => _x0 + (_plot.Width == 0 ? 0 : (px - _plot.Left) / _plot.Width) * (_x1 - _x0);

    /// <summary>x がどの区間に入るか（Segments の何番目を過ぎたか）。まだ最初の区間に届いていなければ -1。</summary>
    private int SegmentIndexAt(double x)
    {
        if (Segments is not { Count: > 0 } segs) return -1;
        int idx = -1;
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i].At > x) break;
            idx = i;
        }
        return idx;
    }

    /// <summary>y（高さ）がどのエリアに入るか（Areas の何番目を超えたか）。まだ最初のエリアに届いていなければ -1。</summary>
    private int AreaIndexAt(double y)
    {
        if (Areas is not { Count: > 0 } areas) return -1;
        int idx = -1;
        for (int i = 0; i < areas.Count; i++)
        {
            if (areas[i].Value > y) break;
            idx = i;
        }
        return idx;
    }

    private int RegionAt(double x, double y) => ColorMode switch
    {
        ChartColorMode.Time => SegmentIndexAt(x) + 1,
        ChartColorMode.Height => AreaIndexAt(y) + 1,
        _ => 0,
    };

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var valid = Points.Where(p => !double.IsNaN(p.Y)).ToList();
        if (valid.Count == 0)
        {
            var empty = Theme.Text(this, "データがありません", 13, Theme.TextMuted);
            dc.DrawText(empty, new Point((ActualWidth - empty.Width) / 2, (ActualHeight - empty.Height) / 2));
            return;
        }

        _dataX0 = valid.Min(p => p.X);
        _dataX1 = valid.Max(p => p.X);
        // ズーム範囲が今のデータと重ならない（別のランに切り替わったなど）ときは解除する
        if (_zoomX0 is double zx0 && _zoomX1 is double zx1 && (zx1 <= _dataX0 || zx0 >= _dataX1)) { _zoomX0 = null; _zoomX1 = null; }
        _x0 = Math.Clamp(_zoomX0 ?? _dataX0, _dataX0, _dataX1);
        _x1 = Math.Clamp(_zoomX1 ?? _dataX1, _x0, _dataX1);
        var zoomed = _zoomX0 is not null;

        var visible = zoomed ? valid.Where(p => p.X >= _x0 && p.X <= _x1).ToList() : valid;
        if (visible.Count == 0) visible = valid;
        double dataMin = visible.Min(p => p.Y), dataMax = visible.Max(p => p.Y);
        // ズーム中は見えている範囲に Y も合わせる（外から渡された固定の軸は、全体表示のときだけ使う）
        var yTicks = !zoomed ? YTicks ?? Ticks.Nice(YMin ?? dataMin, YMax ?? dataMax, 5) : Ticks.Nice(dataMin, dataMax, 5);
        _y0 = !zoomed && YMin is double ymin ? ymin : Math.Min(dataMin, yTicks[0]);
        _y1 = !zoomed && YMax is double ymax ? ymax : Math.Max(dataMax, yTicks[^1]);

        var labels = yTicks.Select(t => Theme.Text(this, YFormat(t), 12, Theme.TextMuted)).ToList();
        double left = labels.Max(l => l.Width) + 10;
        _plot = new Rect(left, Top, Math.Max(10, ActualWidth - left - Right), Math.Max(10, ActualHeight - Top - Bottom));

        // 目盛り線（横・実線・細い）と Y の値
        var gridPen = Theme.Pen(Theme.Grid, 1);
        for (int i = 0; i < yTicks.Count; i++)
        {
            double y = Math.Round(Py(yTicks[i])) + 0.5;
            if (y < _plot.Top - 1 || y > _plot.Bottom + 1) continue;
            dc.DrawLine(gridPen, new Point(_plot.Left, y), new Point(_plot.Right, y));
            dc.DrawText(labels[i], new Point(left - 8 - labels[i].Width, y - labels[i].Height / 2));
        }
        // X の目盛り
        double lastRight = double.NegativeInfinity;
        foreach (var t in Ticks.ForX(XKind, _x0, _x1, _plot.Width))
        {
            var label = Theme.Text(this, XFormat(t), 12, Theme.TextMuted);
            double x = Px(t) - label.Width / 2;
            x = Math.Clamp(x, _plot.Left - 4, ActualWidth - label.Width);
            if (x < lastRight + 12) continue;
            dc.DrawText(label, new Point(x, _plot.Bottom + 4));
            lastRight = x + label.Width;
        }
        dc.DrawLine(Theme.Pen(Theme.Axis, 1), new Point(_plot.Left, Math.Round(_plot.Bottom) + 0.5), new Point(_plot.Right, Math.Round(_plot.Bottom) + 0.5));

        // 区間の境界（点線）。詳しい区間名はホバーで出す
        if (Segments is { Count: > 0 } segsForTicks)
            foreach (var (at, _) in segsForTicks)
            {
                if (at <= _x0 || at >= _x1) continue;
                double tx = Math.Round(Px(at)) + 0.5;
                dc.DrawLine(SegmentTickPen, new Point(tx, _plot.Top), new Point(tx, _plot.Bottom));
            }

        // ズーム中だけ、右上に戻すためのラベルを出す
        if (zoomed)
        {
            var resetText = Theme.Text(this, "全体を表示", 12, Theme.Link);
            var resetBox = new Rect(_plot.Right - resetText.Width - 16, _plot.Top + 2, resetText.Width + 16, resetText.Height + 8);
            dc.DrawRoundedRectangle(Theme.Surface, Theme.Pen(Theme.Border, 1), resetBox, 6, 6);
            dc.DrawText(resetText, new Point(resetBox.Left + 8, resetBox.Top + 4));
            _resetLabelRect = resetBox;
        }
        else _resetLabelRect = Rect.Empty;

        // 線と面（NaN で区切る。色分けが有効なときは、区間・エリアの境目でも分ける）
        var colored = ColorMode switch
        {
            ChartColorMode.Time => Segments is { Count: > 0 },
            ChartColorMode.Height => Areas is { Count: > 0 },
            _ => false,
        };
        var runs = new List<(List<Point> Pts, int Region)>();
        {
            List<Point>? cur = null;
            int curRegion = int.MinValue;
            foreach (var (px, py) in Points)
            {
                if (double.IsNaN(py)) { cur = null; continue; }
                var pt = new Point(Px(px), Py(py));
                var region = colored ? RegionAt(px, py) : 0;
                if (cur is null || region != curRegion)
                {
                    var bridge = cur?[^1];
                    cur = [];
                    runs.Add((cur, region));
                    if (bridge is Point b) cur.Add(b);
                }
                cur.Add(pt);
                curRegion = region;
            }
        }
        foreach (var (pts, region) in runs)
        {
            if (pts.Count < 2) continue;
            var lineBrush = !colored ? Theme.Series : Theme.SegmentLine(region);
            var fillBrush = !Area ? null : !colored ? Theme.SeriesWash : Theme.SegmentWash(region);

            var line = new StreamGeometry();
            var area = fillBrush is not null ? new StreamGeometry() : null;
            using (var lc = line.Open())
            using (var ac = area?.Open())
            {
                lc.BeginFigure(pts[0], false, false);
                ac?.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Count; i++)
                {
                    if (Step)
                    {
                        lc.LineTo(new Point(pts[i].X, pts[i - 1].Y), true, true);
                        ac?.LineTo(new Point(pts[i].X, pts[i - 1].Y), true, true);
                    }
                    lc.LineTo(pts[i], true, true);
                    ac?.LineTo(pts[i], true, true);
                }
                ac?.LineTo(new Point(pts[^1].X, _plot.Bottom), false, false);
                ac?.LineTo(new Point(pts[0].X, _plot.Bottom), false, false);
            }
            line.Freeze();
            if (area is not null)
            {
                area.Freeze();
                dc.DrawGeometry(fillBrush, null, area);
            }
            dc.DrawGeometry(null, Theme.Pen(lineBrush, 2), line);
        }

        // 点が少ないときだけ点を描く（多いと線が点で埋まる）。ズーム中は見えている範囲だけで数える
        if (visible.Count <= 40)
            foreach (var p in visible)
            {
                var dot = !colored ? Theme.Series : Theme.SegmentLine(RegionAt(p.X, p.Y));
                dc.DrawEllipse(dot, Theme.Pen(Theme.Surface, 2), new Point(Px(p.X), Py(p.Y)), 4, 4);
            }

        // ドラッグで範囲を選んでいる間は、選んでいる範囲を塗って見せる（ホバーは出さない）
        if (_dragging && Math.Abs(_dragCurrentX - _dragStartX) >= 2)
        {
            var sel = new Rect(new Point(Math.Min(_dragStartX, _dragCurrentX), _plot.Top), new Point(Math.Max(_dragStartX, _dragCurrentX), _plot.Bottom));
            dc.DrawRectangle(Theme.SeriesWash, Theme.Pen(Theme.Series, 1), sel);
        }
        else DrawHover(dc);
    }

    private void UpdateHover(Point pos)
    {
        if (Points.Count == 0 || !_plot.Contains(new Point(pos.X, _plot.Top + 1))) { _hover = -1; InvalidateVisual(); return; }
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < Points.Count; i++)
        {
            if (double.IsNaN(Points[i].Y) || Points[i].X < _x0 || Points[i].X > _x1) continue;
            double d = Math.Abs(Px(Points[i].X) - pos.X);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best != _hover) { _hover = best; InvalidateVisual(); }
    }

    private void DrawHover(DrawingContext dc)
    {
        if (_hover < 0 || _hover >= Points.Count) return;
        var (hx, hy) = Points[_hover];
        double x = Px(hx), y = Py(hy);
        dc.DrawLine(Theme.Pen(Theme.Axis, 1), new Point(Math.Round(x) + 0.5, _plot.Top), new Point(Math.Round(x) + 0.5, _plot.Bottom));
        dc.DrawEllipse(Theme.Series, Theme.Pen(Theme.Surface, 2), new Point(x, y), 5, 5);

        var label = Theme.Text(this, (HoverXFormat ?? XFormat)(hx), 12, Theme.TextSecondary);
        var value = Theme.Text(this, YFormat(hy), 13, Theme.TextPrimary, bold: true);
        var name = Theme.Text(this, ValueName, 12, Theme.TextSecondary);
        var segIdx = SegmentIndexAt(hx);
        var segText = segIdx >= 0 ? Theme.Text(this, "区間: " + Segments![segIdx].Name, 12, Theme.TextSecondary) : null;
        var areaIdx = AreaIndexAt(hy);
        var areaText = areaIdx >= 0 ? Theme.Text(this, "エリア: " + Areas![areaIdx].Name, 12, Theme.TextSecondary) : null;

        double extraH = (segText?.Height ?? 0) + (areaText?.Height ?? 0);
        double extraW = Math.Max(segText?.Width ?? 0, areaText?.Width ?? 0);
        double w = Math.Max(Math.Max(label.Width, extraW), 12 + 8 + value.Width + 8 + name.Width) + 20;
        double h = label.Height + extraH + value.Height + 16;
        double bx = x + 12 + w > ActualWidth ? x - 12 - w : x + 12, by = Math.Clamp(y - h / 2, 0, Math.Max(0, ActualHeight - h));
        var box = new Rect(bx, by, w, h);
        dc.DrawRoundedRectangle(Theme.Surface, Theme.Pen(Theme.Border, 1), box, 8, 8);
        double ty = by + 8;
        dc.DrawText(label, new Point(bx + 10, ty));
        ty += label.Height;
        if (segText is not null) { dc.DrawText(segText, new Point(bx + 10, ty)); ty += segText.Height; }
        if (areaText is not null) { dc.DrawText(areaText, new Point(bx + 10, ty)); ty += areaText.Height; }
        double ry = ty + value.Height / 2;
        dc.DrawLine(Theme.Pen(Theme.Series, 2), new Point(bx + 10, ry), new Point(bx + 22, ry));
        dc.DrawText(value, new Point(bx + 30, ry - value.Height / 2));
        dc.DrawText(name, new Point(bx + 38 + value.Width, ry - name.Height / 2));
    }
}

internal static class Ticks
{
    /// <summary>きりのよい目盛り（約 count 本）。</summary>
    public static List<double> Nice(double min, double max, int count)
    {
        if (max <= min) max = min + 1;
        double raw = (max - min) / count, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / mag;
        double step = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * mag;
        var ticks = new List<double>();
        for (double t = Math.Floor(min / step) * step; t <= max + step * 0.001; t += step) ticks.Add(Math.Round(t, 6));
        if (ticks[^1] < max) ticks.Add(ticks[^1] + step);
        return ticks;
    }

    private static readonly double[] DurationSteps = [10, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 432000];
    private static readonly double[] TimeSteps = [60_000, 300_000, 600_000, 1_800_000, 3_600_000, 10_800_000, 21_600_000, 43_200_000, 86_400_000, 172_800_000, 604_800_000];

    /// <summary>X の目盛り。経過時間（秒）と時刻（epoch ミリ秒）は、分・時間・日のきりのよい値にする。</summary>
    public static IEnumerable<double> ForX(XAxisKind kind, double min, double max, double width)
    {
        int count = Math.Max(2, (int)(width / 110));
        if (kind == XAxisKind.Number) return Nice(min, max, count).Where(t => t >= min && t <= max);
        var steps = kind == XAxisKind.Duration ? DurationSteps : TimeSteps;
        double step = steps.FirstOrDefault(s => (max - min) / s <= count, steps[^1]);
        double offset = kind == XAxisKind.Time ? TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMilliseconds : 0;
        double first = Math.Ceiling((min + offset) / step) * step - offset;
        var ticks = new List<double>();
        for (double t = first; t <= max; t += step) ticks.Add(t);
        return ticks;
    }
}
