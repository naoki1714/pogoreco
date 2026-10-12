using System.Text.Json;
using Pogoreco.Core;

namespace Pogoreco.Platform;

/// <summary>
/// Steam クラウドのフォルダにあるセーブ・リプレイから、ランごとの高さの推移を記録する。
/// セーブはリセットするとゲームに消されるので、取り込みのたびに中身を残しておく。
/// </summary>
public sealed class ClimbTracker
{
    /// <summary>ゴールしたランのリプレイ。ゴールのたびに上書きされるので、見つけたら記録しておく。</summary>
    private static readonly (string File, int Map, bool PersonalBest)[] ReplayFiles =
    [
        ("map1last.replay", 0, false),
        ("best.replay", 0, true),
    ];

    private sealed class State
    {
        /// <summary>セーブファイルごとの、いま入っているランの id</summary>
        public Dictionary<string, string> Slots { get; set; } = [];
        /// <summary>読んだときのファイルの更新時刻（変わっていなければ読み直さない）</summary>
        public Dictionary<string, double> Seen { get; set; } = [];
    }

    private readonly string _dir;
    private readonly string _rawDir;
    private readonly Dictionary<string, ClimbRecord> _climbs = [];
    private State _state = new();

    public ClimbTracker(string dir)
    {
        _dir = dir;
        _rawDir = Path.Combine(dir, "raw");
    }

    public void Load()
    {
        Directory.CreateDirectory(_rawDir);
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                if (Path.GetFileName(file) == "state.json") _state = JsonSerializer.Deserialize<State>(json, DataStore.Json) ?? new();
                else if (JsonSerializer.Deserialize<ClimbRecord>(json, DataStore.Json) is { } climb) _climbs[climb.Id] = climb;
            }
            catch (JsonException) { }
        }
    }

    /// <summary>新しく更新された順。</summary>
    public IReadOnlyList<ClimbRecord> All => [.. _climbs.Values.OrderByDescending(c => c.UpdatedAt)];

    public ClimbRecord? Get(string id) => _climbs.GetValueOrDefault(id);

    private static double? Mtime(string path) =>
        File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds() : null;

    /// <summary>フォルダを読み、記録が変わったら true。</summary>
    public bool Scan(string remoteDir, long now)
    {
        bool changed = false;

        // ゴールしたランを先に読む（ゴールと同時にセーブが消えるので、リセット扱いにしないため）
        foreach (var (file, map, pb) in ReplayFiles)
        {
            var path = Path.Combine(remoteDir, file);
            if (Mtime(path) is not double mtime || _state.Seen.GetValueOrDefault(file) == mtime) continue;
            _state.Seen[file] = mtime;
            var bytes = DataStore.ReadShared(path);
            if (Replay.ParseReplayFile(bytes) is { } track)
                changed |= Upsert(track, file, map, now, bytes, ((long)mtime, pb));
        }

        var saveFiles = Directory.Exists(remoteDir)
            ? Directory.EnumerateFiles(remoteDir).Select(Path.GetFileName).OfType<string>().Where(f => f.Length == 3 && f.EndsWith("og")).ToList()
            : [];
        foreach (var file in saveFiles)
        {
            var path = Path.Combine(remoteDir, file);
            if (Mtime(path) is not double mtime || _state.Seen.GetValueOrDefault(file) == mtime) continue;
            _state.Seen[file] = mtime;
            var bytes = DataStore.ReadShared(path);
            var track = Replay.ParseSaveFile(bytes);
            var id = track is null ? null : Replay.TrackKey(track);
            // 別のランに入れ替わった（または空になった）→ 前のランはゴールしていなければリセット
            if (_state.Slots.TryGetValue(file, out var previous) && previous != id)
            {
                changed |= EndIfUnfinished(previous, now);
                _state.Slots.Remove(file);
            }
            if (track is null || id is null) continue;
            _state.Slots[file] = id;
            changed |= Upsert(track, file, Maps.MapOfSaveFile(file) ?? Maps.MapOfGameModeId(track.GameModeId), now, bytes, null);
        }

        // セーブ自体が消えた（ゴールやリセットで削除された）
        foreach (var (file, id) in _state.Slots.ToList())
        {
            if (saveFiles.Contains(file)) continue;
            changed |= EndIfUnfinished(id, now);
            _state.Slots.Remove(file);
        }

        Directory.CreateDirectory(_dir);
        DataStore.WriteAtomic(Path.Combine(_dir, "state.json"), JsonSerializer.SerializeToUtf8Bytes(_state, DataStore.Json));
        return changed;
    }

    private bool Upsert(Track track, string file, int? map, long now, byte[] raw, (long EndedAt, bool PersonalBest)? finished)
    {
        var id = Replay.TrackKey(track);
        if (id is null) return false;
        _climbs.TryGetValue(id, out var existing);
        var recordedSec = track.Ys.Length * Replay.StepSec;
        // 途中経過は伸びたときだけ差し替える（古いセーブで上書きしない）。ゴールのリプレイは常に完全版
        var kept = existing is not null && finished is null && recordedSec < existing.RecordedSec ? existing : null;
        var heights = kept?.Heights ?? Replay.HeightsPerSecond(track.Ys);
        var maxY = kept?.MaxY ?? (track.Ys.Length > 0 ? track.Ys.Max() : 0);

        var record = new ClimbRecord
        {
            Id = id,
            Map = existing?.Map ?? map,
            Sources = [.. (existing?.Sources ?? []).Append(file).Distinct()],
            Status = finished is not null || existing?.Status == ClimbStatus.Finished ? ClimbStatus.Finished : ClimbStatus.InProgress,
            FirstSeenAt = existing?.FirstSeenAt ?? now,
            UpdatedAt = existing?.UpdatedAt ?? now,
            EndedAt = finished?.EndedAt ?? (existing?.Status == ClimbStatus.Finished ? existing.EndedAt : null),
            RecordedSec = kept?.RecordedSec ?? recordedSec,
            FinishTimeSec = track.FinishTimeSec ?? existing?.FinishTimeSec,
            IsPersonalBest = (finished?.PersonalBest ?? false) || (existing?.IsPersonalBest ?? false),
            Capped = (track.Source == "save" && track.Ys.Length >= Replay.SaveStepCap) || (existing?.Capped ?? false),
            MaxY = maxY,
            LastY = heights.Count > 0 ? heights[^1] : 0,
            Heights = heights,
        };
        if (existing is not null && SameContent(existing, record)) return false;
        record = record with { UpdatedAt = now };
        Save(record);
        // 元のファイルも残しておく（読み方を直したときに作り直せるように）
        if (kept is null) File.WriteAllBytes(Path.Combine(_rawDir, $"{id}-{file}"), raw);
        return true;
    }

    private static bool SameContent(ClimbRecord a, ClimbRecord b) =>
        JsonSerializer.Serialize(a with { UpdatedAt = 0 }, DataStore.Json) == JsonSerializer.Serialize(b with { UpdatedAt = 0 }, DataStore.Json);

    private bool EndIfUnfinished(string id, long now)
    {
        if (!_climbs.TryGetValue(id, out var climb) || climb.Status != ClimbStatus.InProgress) return false;
        Save(climb with { Status = ClimbStatus.Reset, EndedAt = now, UpdatedAt = now });
        return true;
    }

    private void Save(ClimbRecord record)
    {
        _climbs[record.Id] = record;
        Directory.CreateDirectory(_dir);
        DataStore.WriteAtomic(Path.Combine(_dir, record.Id + ".json"), JsonSerializer.SerializeToUtf8Bytes(record, DataStore.Json));
    }
}
