using System.Diagnostics;

namespace Pogoreco.Platform;

/// <summary>Pogostuck のプロセス。ゲームには触らず、名前と起動時刻を見るだけ。</summary>
public static class GameProcess
{
    public static Process? Find() => Process.GetProcessesByName("Pogostuck").FirstOrDefault();

    public static bool IsRunning()
    {
        using var p = Find();
        return p is not null;
    }

    /// <summary>動いている Pogostuck の起動時刻（epoch ミリ秒）。動いていなければ null。</summary>
    public static long? StartTime()
    {
        using var p = Find();
        try { return p is null ? null : new DateTimeOffset(p.StartTime).ToUnixTimeMilliseconds(); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Steam 経由で -diag（ログ出力）付きで起動する。</summary>
    public static void LaunchViaSteam(string steamDir)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(steamDir, "steam.exe"),
            ArgumentList = { "-applaunch", SteamPaths.AppId, "-diag" },
            UseShellExecute = false,
        })?.Dispose();
    }
}
