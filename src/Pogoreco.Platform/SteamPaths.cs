using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pogoreco.Platform;

/// <summary>Steam・Pogostuck・Steam クラウドのフォルダを探す。</summary>
public static partial class SteamPaths
{
    public const string AppId = "688130";
    public const string LogFileName = "acklog.txt";

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRe();
    [GeneratedRegex(@"^ShowSplits\s+(-?\d+)", RegexOptions.Multiline)]
    private static partial Regex ShowSplitsRe();

    /// <summary>Steam のインストール先（steam.exe のあるフォルダ）。</summary>
    public static string? FindSteamDir()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path)
        {
            var dir = Path.GetFullPath(path);
            if (File.Exists(Path.Combine(dir, "steam.exe"))) return dir;
        }
        foreach (var dir in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (File.Exists(Path.Combine(dir, "steam.exe"))) return dir;
        return null;
    }

    public static bool IsGameDir(string dir) => File.Exists(Path.Combine(dir, "Pogostuck.exe"));

    /// <summary>Pogostuck のインストール先（Steam のライブラリフォルダを順に探す）。</summary>
    public static string? FindGameDir(string? steamDir)
    {
        if (steamDir is null) return null;
        var libraries = new List<string> { steamDir };
        try
        {
            var vdf = File.ReadAllText(Path.Combine(steamDir, "steamapps", "libraryfolders.vdf"));
            libraries.AddRange(LibraryPathRe().Matches(vdf).Select(m => Path.GetFullPath(m.Groups[1].Value.Replace(@"\\", @"\"))));
        }
        catch (IOException) { }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(lib => Path.Combine(lib, "steamapps", "common", "Pogostuck"))
            .FirstOrDefault(IsGameDir);
    }

    /// <summary>
    /// Steam クラウドのフォルダ（userdata\&lt;アカウント&gt;\688130\remote）。
    /// アカウントが複数あるときは、settings.txt が最後に更新されたもの。
    /// </summary>
    public static string? FindRemoteDir(string? steamDir)
    {
        if (steamDir is null) return null;
        var userdata = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(userdata)) return null;
        return Directory.EnumerateDirectories(userdata)
            .Select(account => Path.Combine(account, AppId, "remote"))
            .Where(remote => File.Exists(Path.Combine(remote, "settings.txt")))
            .OrderByDescending(remote => File.GetLastWriteTimeUtc(Path.Combine(remote, "settings.txt")))
            .FirstOrDefault();
    }

    /// <summary>ゲームの「Show speedrun splits」がオンか（オフだと区間通過がログに出ない）。読めなければ null。</summary>
    public static bool? ReadSplitsEnabled(string? remoteDir)
    {
        if (remoteDir is null) return null;
        try
        {
            var m = ShowSplitsRe().Match(File.ReadAllText(Path.Combine(remoteDir, "settings.txt")));
            return m.Success ? m.Groups[1].Value != "0" : null;
        }
        catch (IOException) { return null; }
    }
}
