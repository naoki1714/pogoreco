using System.Runtime.InteropServices.WindowsRuntime;
using Pogoreco.Core;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Pogoreco.Recorder;

internal readonly record struct HudReading(HudHeights? Heights, double? RunTimeSec);

/// <summary>
/// ゲームのウィンドウを Windows Graphics Capture で取得し、左上の HUD を Windows OCR で読む。
/// ゲームには触らない（配信ソフトと同じ方法で、表示されている画面を見るだけ）。
/// </summary>
internal sealed class HudReader : IDisposable
{
    /// <summary>HUD が出ている範囲（ウィンドウの幅・高さに対する割合）</summary>
    private const double RegionWidth = 0.25, RegionHeight = 0.35, TimeLinesHeight = 0.08;
    /// <summary>Windows OCR は小さい文字を読まないので拡大する</summary>
    private const int OwnLineScale = 3;

    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly OcrEngine _ocr;
    private readonly object _lock = new();
    private SizeInt32 _size;
    private TaskCompletionSource<SoftwareBitmap?>? _waiting;
    private byte[] _frame = [];

    public HudReader(IntPtr hwnd)
    {
        _device = Native.CreateDevice();
        var item = Native.CreateItemForWindow(hwnd);
        _size = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, _size);
        _session = _pool.CreateCaptureSession(item);
        TrySet(() => _session.IsCursorCaptureEnabled = false);
        // 取得中に出る黄色い枠を消す
        TrySet(() => _session.IsBorderRequired = false);
        // ゲーム画面の受け取りを 0.5 秒に 1 回までにする（Windows 11 24H2 以降）
        TrySet(() => _session.MinUpdateInterval = TimeSpan.FromMilliseconds(500));
        _ocr = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? throw new InvalidOperationException("Windows の英語 OCR が使えません");
        _pool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
    }

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Exception) { /* 古い Windows では使えない設定 */ }
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
    {
        var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        try
        {
            if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
            {
                // ウィンドウの大きさが変わった
                _size = frame.ContentSize;
                pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, _size);
                return;
            }
            TaskCompletionSource<SoftwareBitmap?>? waiting;
            lock (_lock) { waiting = _waiting; _waiting = null; }
            if (waiting is null) return;
            try { waiting.TrySetResult(await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)); }
            catch (Exception) { waiting.TrySetResult(null); }
        }
        finally
        {
            frame.Dispose();
        }
    }

    /// <summary>次の画面を取得して HUD を読む。読めなかった値は null。</summary>
    public async Task<HudReading> ReadAsync(string? debugDir = null)
    {
        var tcs = new TaskCompletionSource<SoftwareBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _waiting = tcs;
        var done = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        if (done != tcs.Task) { lock (_lock) _waiting = null; return default; }
        using var bitmap = await tcs.Task;
        if (bitmap is null) return default;

        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        if (_frame.Length != width * height * 4) _frame = new byte[width * height * 4];
        bitmap.CopyToBuffer(_frame.AsBuffer());

        int cw = (int)(width * RegionWidth), ch = (int)(height * RegionHeight);
        var heights = await ReadOwnLineAsync(width, cw, ch, debugDir);
        var runTime = await ReadRunTimeAsync(width, cw, (int)(height * TimeLinesHeight), debugDir);
        return new HudReading(heights, runTime);
    }

    /// <summary>黄色らしさ（0〜1）: 赤・緑が高く青が低い</summary>
    private static double Yellowness(byte r, byte g, byte b) => Math.Clamp((Math.Min(r, g) - b - 40) / 120.0, 0, 1);

    /// <summary>自分の行（黄色）を見つけて、黒い文字・白い背景にして拡大し、OCR する。</summary>
    private async Task<HudHeights?> ReadOwnLineAsync(int stride, int cw, int ch, string? debugDir)
    {
        // 行ごとに黄色の画素を数え、いちばん多い帯を自分の行とする
        var counts = new int[ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int k = (y * stride + x) * 4;
                if (Yellowness(_frame[k + 2], _frame[k + 1], _frame[k]) > 0.5) counts[y]++;
            }
        int bestTop = -1, bestBottom = -1, bestSum = 0;
        for (int y = 0; y < ch;)
        {
            if (counts[y] <= 3) { y++; continue; }
            int top = y, sum = 0;
            while (y < ch && counts[y] > 3) sum += counts[y++];
            if (sum > bestSum) (bestTop, bestBottom, bestSum) = (top, y - 1, sum);
        }
        if (bestTop < 0) return null;

        int y0 = Math.Max(0, bestTop - 4), y1 = Math.Min(ch - 1, bestBottom + 4), lh = y1 - y0 + 1;
        int ow = cw * OwnLineScale, oh = lh * OwnLineScale;
        var pixels = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int k = ((y0 + y / OwnLineScale) * stride + x / OwnLineScale) * 4;
                byte c = (byte)(255 - Yellowness(_frame[k + 2], _frame[k + 1], _frame[k]) * 255);
                int o = (y * ow + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = c;
                pixels[o + 3] = 255;
            }
        using var line = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, ow, oh, BitmapAlphaMode.Premultiplied);
        if (debugDir is not null) await SavePngAsync(line, Path.Combine(debugDir, "debug-own-line.png"));
        var result = await _ocr.RecognizeAsync(line);
        return Hud.ParseOwnLine(string.Join(" ", result.Lines.Select(l => l.Text)));
    }

    /// <summary>上の数行（FPS・タイム・ジャンプ回数）を元の色のまま OCR して、タイムを読む。</summary>
    private async Task<double?> ReadRunTimeAsync(int stride, int cw, int lines, string? debugDir)
    {
        var pixels = new byte[cw * lines * 4];
        for (int y = 0; y < lines; y++) Buffer.BlockCopy(_frame, y * stride * 4, pixels, y * cw * 4, cw * 4);
        using var top = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, cw, lines, BitmapAlphaMode.Premultiplied);
        if (debugDir is not null) await SavePngAsync(top, Path.Combine(debugDir, "debug-top-lines.png"));
        var result = await _ocr.RecognizeAsync(top);
        return result.Lines.Select(l => Hud.ParseRunTime(l.Text)).FirstOrDefault(t => t is not null);
    }

    private static async Task SavePngAsync(SoftwareBitmap bitmap, string path)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, Windows.Storage.Streams.InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }

    public void Dispose()
    {
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        _device.Dispose();
    }
}
