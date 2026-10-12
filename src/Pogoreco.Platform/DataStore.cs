using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pogoreco.Core;

namespace Pogoreco.Platform;

/// <summary>
/// データの保存場所（%APPDATA%\pogoreco。環境変数 POGORECO_DATA_DIR で変えられる）。
/// <list type="bullet">
/// <item>logs\&lt;起動時刻&gt;.txt — 取り込んだ acklog.txt（元データ。セッションはここから毎回集計する）</item>
/// <item>climbs\ — ランごとの高さ（セーブ・リプレイから）。raw\ に元ファイルの写し</item>
/// <item>hud\&lt;起動時刻&gt;.jsonl — 画面から読んだ高さ（1 行 1 回分）</item>
/// </list>
/// </summary>
public sealed class DataStore(string root)
{
    public string Root { get; } = root;
    public string LogsDir => Path.Combine(Root, "logs");
    public string ClimbsDir => Path.Combine(Root, "climbs");
    public string HudDir => Path.Combine(Root, "hud");

    public static DataStore Default() => new(
        Environment.GetEnvironmentVariable("POGORECO_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pogoreco"));

    /// <summary>以前の TypeScript 版と同じ JSON の形（camelCase、列挙は "inProgress" など）。</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>ゲームが書き込み中のファイルも読めるように開く。</summary>
    public static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// acklog.txt を logs\&lt;セッションID&gt;.txt に保存する。同じ回のログは同じ名前になるので、何度保存しても重複しない。
    /// 戻り値はセッション ID（ログが無い・形式が違うときは null）。
    /// </summary>
    public string? ArchiveLog(string gameDir)
    {
        var path = Path.Combine(gameDir, SteamPaths.LogFileName);
        if (!File.Exists(path)) return null;
        var bytes = ReadShared(path);
        var firstLine = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 200)).Split('\n')[0].TrimEnd('\r');
        var header = LogParser.ParseHeader(firstLine);
        if (header is null) return null;
        Directory.CreateDirectory(LogsDir);
        var target = Path.Combine(LogsDir, header.SessionId + ".txt");
        if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length) WriteAtomic(target, bytes);
        return header.SessionId;
    }

    /// <summary>
    /// 保存したログからセッションを集計する。activeSessionId は、いまゲームが書き込んでいる回（プレイ中として扱う）。
    /// </summary>
    public List<SessionRecord> LoadSessions(string? activeSessionId, long now)
    {
        if (!Directory.Exists(LogsDir)) return [];
        var sessions = new List<SessionRecord>();
        foreach (var file in Directory.EnumerateFiles(LogsDir, "*.txt"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var log = LogParser.ParseLog(Encoding.Latin1.GetString(ReadShared(file)));
            var session = SessionAggregator.Aggregate(log, new AggregateOptions
            {
                GameRunning = id == activeSessionId,
                FallbackEndedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds(),
                Now = now,
            });
            if (session is not null) sessions.Add(session);
        }
        return sessions;
    }

    /// <summary>
    /// いまゲームが書き込んでいる回のセッション ID。ゲームが動いていて、ゲームの起動時刻とログの起動時刻が近いときだけ
    /// （-diag なしで起動した場合など、ログが別の回のものであることがあるため）。
    /// </summary>
    public static string? ActiveSessionId(string? gameDir)
    {
        if (gameDir is null || GameProcess.StartTime() is not long started) return null;
        var path = Path.Combine(gameDir, SteamPaths.LogFileName);
        if (!File.Exists(path)) return null;
        var bytes = ReadShared(path);
        var header = LogParser.ParseHeader(Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 200)).Split('\n')[0].TrimEnd('\r'));
        return header is not null && Math.Abs(header.StartedAt - started) < 5 * 60_000 ? header.SessionId : null;
    }

    // ---- 画面から読んだ高さ ----

    public void AppendHudSamples(string fileKey, IEnumerable<HudSample> samples)
    {
        Directory.CreateDirectory(HudDir);
        var lines = samples.Select(s => JsonSerializer.Serialize(s, Json));
        File.AppendAllLines(Path.Combine(HudDir, fileKey + ".jsonl"), lines);
    }

    public List<HudSample> LoadHudSamples()
    {
        if (!Directory.Exists(HudDir)) return [];
        var samples = new List<HudSample>();
        foreach (var file in Directory.EnumerateFiles(HudDir, "*.jsonl"))
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { if (JsonSerializer.Deserialize<HudSample>(line, Json) is { } s) samples.Add(s); }
                catch (JsonException) { }
            }
        return [.. samples.OrderBy(s => s.At)];
    }
}
