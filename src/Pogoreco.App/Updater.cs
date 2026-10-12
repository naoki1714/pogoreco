using Velopack;
using Velopack.Sources;

namespace Pogoreco.App;

/// <summary>
/// 自動更新（Velopack ＋ GitHub Releases）。インストーラーで入れた版だけが対象で、
/// zip を展開しただけの版・開発中のビルドでは何もしない。
/// </summary>
public static class Updater
{
    public const string RepoUrl = "https://github.com/naoki1714/pogoreco";

    private static readonly UpdateManager Manager = new(new GithubSource(RepoUrl, null, false));
    private static UpdateInfo? _pending;

    public static bool IsInstalled => Manager.IsInstalled;

    /// <summary>新しい版があれば、そのバージョンを返す（なければ・確認できなければ null）。</summary>
    public static async Task<string?> CheckAsync()
    {
        if (!Manager.IsInstalled) return null;
        try
        {
            _pending = await Manager.CheckForUpdatesAsync();
            return _pending?.TargetFullRelease.Version.ToString();
        }
        catch
        {
            return null; // 通信できないときは黙って何もしない
        }
    }

    /// <summary>ダウンロードして、アプリを閉じて入れ替え、再起動する。</summary>
    public static async Task ApplyAsync()
    {
        if (_pending is null) return;
        await Manager.DownloadUpdatesAsync(_pending);
        Manager.ApplyUpdatesAndRestart(_pending);
    }
}
