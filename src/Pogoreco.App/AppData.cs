using System.Diagnostics;
using System.IO;
using Pogoreco.Core;
using Pogoreco.Platform;

namespace Pogoreco.App;

/// <summary>画面が使うデータ。開いたとき・更新ボタン・記録中は定期的に読み直す。</summary>
public sealed class AppData
{
    public DataStore Store { get; } = DataStore.Default();
    public string? SteamDir { get; private set; }
    public string? GameDir { get; private set; }
    public string? RemoteDir { get; private set; }

    public List<SessionRecord> Sessions { get; private set; } = [];
    public IReadOnlyList<ClimbRecord> Climbs { get; private set; } = [];
    public List<HudRun> HudRuns { get; private set; } = [];
    public bool GameRunning { get; private set; }
    public bool RecorderRunning { get; private set; }
    public bool? SplitsEnabled { get; private set; }
    public long Now { get; private set; }

    public static bool IsRecorderRunning() => Mutex.TryOpenExisting(@"Local\Pogoreco.Recorder", out var m) && Dispose(m);

    private static bool Dispose(IDisposable d)
    {
        d.Dispose();
        return true;
    }

    public void Refresh()
    {
        SteamDir ??= SteamPaths.FindSteamDir();
        GameDir ??= SteamPaths.FindGameDir(SteamDir);
        RemoteDir ??= SteamPaths.FindRemoteDir(SteamDir);
        Now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        GameRunning = GameProcess.IsRunning();
        RecorderRunning = IsRecorderRunning();

        var tracker = new ClimbTracker(Store.ClimbsDir);
        tracker.Load();
        // 記録係が動いている間は記録係が書き込むので、こちらは読むだけにする
        if (!RecorderRunning)
        {
            try
            {
                if (GameDir is not null) Store.ArchiveLog(GameDir);
                if (RemoteDir is not null) tracker.Scan(RemoteDir, Now);
            }
            catch (IOException) { /* ゲームが書き込み中など。次の更新で取り込む */ }
        }
        Climbs = tracker.All;
        Sessions = Store.LoadSessions(DataStore.ActiveSessionId(GameDir), Now);
        HudRuns = HeightTimeline.SplitRuns(Store.LoadHudSamples());
        SplitsEnabled = SteamPaths.ReadSplitsEnabled(RemoteDir);
    }

    /// <summary>記録係（Pogoreco.Recorder.exe）の場所。同じフォルダになければ、開発中のビルド先を探す。</summary>
    public static string? RecorderPath()
    {
        var here = Path.Combine(AppContext.BaseDirectory, "Pogoreco.Recorder.exe");
        if (File.Exists(here)) return here;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src", "Pogoreco.Recorder", "bin");
            if (Directory.Exists(src))
                return Directory.EnumerateFiles(src, "Pogoreco.Recorder.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        return null;
    }

    /// <summary>記録つきで Pogostuck を起動する（記録係がゲームを起動し、ゲームが終わると一緒に終わる）。</summary>
    public static bool StartRecorder()
    {
        if (RecorderPath() is not string path) return false;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
        return true;
    }

    private static string ShortcutPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pogostuck（記録つき）.lnk");

    /// <summary>デスクトップに「Pogostuck（記録つき）」のショートカットがすでにあるか。</summary>
    public static bool ShortcutExists() => File.Exists(ShortcutPath());

    /// <summary>デスクトップに「Pogostuck（記録つき）」のショートカットを作る。</summary>
    public string? CreateShortcut()
    {
        if (RecorderPath() is not string recorder) return null;
        var link = ShortcutPath();
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("ショートカットを作れません");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = recorder;
        shortcut.WorkingDirectory = Path.GetDirectoryName(recorder);
        shortcut.Description = "Pogostuck を起動して、プレイを記録します（pogoreco）";
        if (GameDir is not null) shortcut.IconLocation = Path.Combine(GameDir, "Pogostuck.exe") + ",0";
        shortcut.Save();
        return link;
    }
}
