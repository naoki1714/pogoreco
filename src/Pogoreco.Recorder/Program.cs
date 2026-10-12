using System.Diagnostics;
using System.Text;
using Pogoreco.Core;
using Pogoreco.Platform;

namespace Pogoreco.Recorder;

/// <summary>
/// 記録係。専用ショートカットから起動される。
/// ゲームが動いていなければ Steam 経由で -diag 付きで起動し、ゲームが動いている間だけ
/// 画面の HUD（1 秒ごと）・ログ・セーブ（30 秒ごと）を記録して、ゲームが終わったら自分も終わる（常駐しない）。
/// <para>引数: --no-launch（ゲームを起動しない）, --seconds N（N 秒で終わる。動作確認用）, --debug（HUD の切り抜きを保存）</para>
/// </summary>
internal static class Program
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ImportInterval = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\Pogoreco.Recorder", out var first);
        if (!first) return 0; // すでに記録中
        var store = DataStore.Default();
        Directory.CreateDirectory(store.Root);
        var log = new RecorderLog(Path.Combine(store.Root, "recorder.log"));
        try
        {
            return await RunAsync(args, store, log);
        }
        catch (Exception e)
        {
            log.Write($"エラーで終了: {e}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args, DataStore store, RecorderLog log)
    {
        var noLaunch = args.Contains("--no-launch");
        var debugDir = args.Contains("--debug") ? store.Root : null;
        var limit = args.SkipWhile(a => a != "--seconds").Skip(1).Select(a => double.TryParse(a, out var s) ? s : (double?)null).FirstOrDefault();

        var steamDir = SteamPaths.FindSteamDir();
        var gameDir = SteamPaths.FindGameDir(steamDir);
        var remoteDir = SteamPaths.FindRemoteDir(steamDir);
        if (steamDir is null || gameDir is null)
        {
            log.Write("Steam か Pogostuck が見つからない");
            Native.MessageBoxW(IntPtr.Zero, "Steam か Pogostuck のインストール先が見つかりませんでした。", "pogoreco", 0x30);
            return 1;
        }

        if (!GameProcess.IsRunning())
        {
            if (noLaunch) return 0;
            log.Write("Steam 経由で Pogostuck を起動");
            GameProcess.LaunchViaSteam(steamDir);
        }
        using var game = await WaitForGameWindowAsync(TimeSpan.FromMinutes(3));
        if (game is null)
        {
            log.Write("Pogostuck のウィンドウが出てこなかったので終了");
            return 1;
        }
        log.Write($"記録を開始（pid {game.Id}）");

        var climbs = new ClimbTracker(store.ClimbsDir);
        climbs.Load();
        string? sessionId = null;
        int? currentMap = null;
        var hudKey = $"game-{game.StartTime:yyyyMMdd-HHmmss}";

        void Import()
        {
            try
            {
                var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                store.ArchiveLog(gameDir);
                sessionId = DataStore.ActiveSessionId(gameDir) ?? sessionId;
                if (sessionId is not null)
                {
                    var text = Encoding.Latin1.GetString(DataStore.ReadShared(Path.Combine(store.LogsDir, sessionId + ".txt")));
                    currentMap = LogParser.CurrentMap(LogParser.ParseLog(text));
                }
                if (remoteDir is not null) climbs.Scan(remoteDir, now);
            }
            catch (Exception e)
            {
                // ログ・セーブが書き込み中で読めない、形式が想定外など。記録係は止めず、次の回にやり直す
                log.Write($"取り込みに失敗（次回やり直す）: {e.GetType().Name}: {e.Message}");
            }
        }

        Import();
        var pending = new List<HudSample>();
        void Flush()
        {
            if (pending.Count == 0) return;
            try
            {
                store.AppendHudSamples(sessionId ?? hudKey, pending);
                pending.Clear();
            }
            catch (Exception e)
            {
                // 書けなかった分は残して、次の回にもう一度書く
                log.Write($"高さの書き込みに失敗（次回やり直す）: {e.GetType().Name}: {e.Message}");
            }
        }

        HudReader? hud = null;
        var sinceOpen = Stopwatch.StartNew();
        var started = Stopwatch.StartNew();
        var sinceFlush = Stopwatch.StartNew();
        var sinceImport = Stopwatch.StartNew();
        int reads = 0, readable = 0, captureFailures = 0, errors = 0, reopened = 0, failStreak = 0;
        while (!game.HasExited && (limit is null || started.Elapsed.TotalSeconds < limit))
        {
            var tick = Stopwatch.StartNew();
            try
            {
                game.Refresh();
                var hwnd = game.MainWindowHandle;
                if (hwnd != IntPtr.Zero && !Native.IsIconic(hwnd))
                {
                    // 取得が無い・ウィンドウが変わった・新しい画面が 1 分来ない・取得に失敗し続けているときは、取得をやり直す
                    // （開き直しは 5 秒以上あける）
                    var needOpen = hud is null || hud.Hwnd != hwnd || hud.SecondsSinceFrame > 60 || failStreak >= 5;
                    if (needOpen && (hud is null || sinceOpen.Elapsed > TimeSpan.FromSeconds(5)))
                    {
                        hud?.Dispose();
                        hud = null;
                        sinceOpen.Restart();
                        failStreak = 0;
                        try
                        {
                            hud = new HudReader(hwnd);
                            if (reads > 0) { reopened++; log.Write($"画面の取得をやり直した（{reopened} 回目）"); }
                        }
                        catch (Exception e)
                        {
                            if (errors++ % 30 == 0) log.Write($"画面の取得を始められない（続けて試す）: {e.GetType().Name}: {e.Message}");
                        }
                    }
                    if (hud is not null)
                    {
                        var reading = await hud.ReadAsync(reads == 0 ? debugDir : null);
                        reads++;
                        if (!reading.Captured) { captureFailures++; failStreak++; }
                        else failStreak = 0;
                        if (reading.Heights is HudHeights h)
                        {
                            readable++;
                            pending.Add(new HudSample
                            {
                                At = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                                RunTimeSec = reading.RunTimeSec,
                                Current = h.Current,
                                Best = h.Best,
                                Unit = h.Unit,
                                Map = Maps.MapOfHudUnit(h.Unit) ?? currentMap,
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // 1 回の失敗で記録係を止めない。取得を作り直して続ける（ログは 30 回に 1 回だけ）
                if (errors++ % 30 == 0) log.Write($"読み取りでエラー（続けて試す）: {e.GetType().Name}: {e.Message}");
                hud?.Dispose();
                hud = null;
            }
            if (sinceFlush.Elapsed >= FlushInterval) { Flush(); sinceFlush.Restart(); }
            if (sinceImport.Elapsed >= ImportInterval) { Import(); sinceImport.Restart(); }
            var wait = ReadInterval - tick.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        }
        hud?.Dispose();

        // ゲームが終わった: ログとセーブの書き終わりを待ってから最後に取り込む
        if (game.HasExited) await Task.Delay(TimeSpan.FromSeconds(2));
        Flush();
        Import();
        var memory = Process.GetCurrentProcess();
        log.Write($"記録を終了: HUD を {reads} 回読み {readable} 回読めた（取得失敗 {captureFailures}・エラー {errors}・やり直し {reopened}） / メモリ private {memory.PrivateMemorySize64 / 1048576} MB, working set {memory.WorkingSet64 / 1048576} MB");
        return 0;
    }

    /// <summary>Pogostuck のウィンドウが出るまで待つ（Steam の起動を含めて数十秒かかることがある）。</summary>
    private static async Task<Process?> WaitForGameWindowAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var p = GameProcess.Find();
            if (p is not null)
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero) return p;
                p.Dispose();
            }
            await Task.Delay(1000);
        }
        return null;
    }
}

/// <summary>記録係の動作ログ（recorder.log）。1 MB を超えたら作り直す。</summary>
internal sealed class RecorderLog
{
    private readonly string _path;

    public RecorderLog(string path)
    {
        _path = path;
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
    }

    public void Write(string message) =>
        File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
}
