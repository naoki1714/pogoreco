namespace Pogoreco.Core;

public sealed record AggregateOptions
{
    /// <summary>ゲームがまだ動いているか（終了行が無くてもプレイ中として扱う）</summary>
    public bool GameRunning { get; init; }
    /// <summary>終了行が無いときに使う終了時刻（ログファイルの最終更新時刻など）</summary>
    public long? FallbackEndedAt { get; init; }
    /// <summary>プレイ中の経過時間の計算に使う現在時刻</summary>
    public long? Now { get; init; }
}

/// <summary>解析済みのログ 1 回分を、セッション・ラン・区間通過に集計する。</summary>
public static class SessionAggregator
{
    /// <summary>集計ロジックを変えたら上げる。保存済みのセッションは生ログから作り直される。</summary>
    public const int ParserVersion = 4;

    private sealed class OpenRun
    {
        public int? Map;
        public int? Mode;
        public List<SplitPass> Splits = [];
        public int Deaths;
        public int StartLine;
    }

    public static SessionRecord? Aggregate(ParsedLog log, AggregateOptions? options = null)
    {
        options ??= new AggregateOptions();
        if (log.Header is null) return null;

        var runs = new List<RunRecord>();
        OpenRun? open = null;
        int? map = null, mode = null;
        SteamStats? steamStats = null;
        var levelUps = new List<int>();
        double? exitAtSec = null;
        var pendingProgress = new List<(int Index, int Map, double Percent)>();

        OpenRun StartRun(int line) => new() { Map = map, Mode = mode, StartLine = line };

        void CloseRun(RunOutcome outcome, int? endLine, FinishEvent? finish = null)
        {
            if (open is null) return;
            // モードが最後まで分からなければ通常モードとみなす（スプリット表示オフだとモードはゴール時にしか出ない）
            if (open.Mode is null && open.Map is int m) open.Mode = Maps.DefaultMode(m);
            int? maxCheckpoint = open.Splits.Count > 0 ? open.Splits.Max(s => s.Checkpoint) : null;
            double? finishTime = finish is null ? null : finish.TimeMs / 1000.0;
            double? bestBefore = finish is { BestTimeMs: > 0 } ? finish.BestTimeMs / 1000.0 : null;
            runs.Add(new RunRecord(
                runs.Count, open.Map, open.Mode, outcome, open.Splits, maxCheckpoint, finishTime, bestBefore,
                finishTime is double t && (bestBefore is null || t < bestBefore), open.Deaths, open.StartLine, endLine));
            open = null;
        }

        for (int index = 0; index < log.Events.Count; index++)
        {
            switch (log.Events[index])
            {
                case MapModeEvent e:
                {
                    // マップかモードが変わったら、途中のランは中断扱い（モードが分からない通知では判断しない）
                    var modeChanged = e.Mode is not null && open?.Mode is not null && open.Mode != e.Mode;
                    if (open?.Map is not null && (open.Map != e.Map || modeChanged)) CloseRun(RunOutcome.Abandoned, e.Line);
                    // マップに入っただけの通知（モード不明）は、同じマップならモードを引き継ぐ
                    if (e.Mode is not null) mode = e.Mode;
                    else if (map != e.Map) mode = null;
                    map = e.Map;
                    if (open is not null)
                    {
                        open.Map ??= map;
                        if (open.Map == map) open.Mode ??= mode;
                    }
                    break;
                }
                case RunStartEvent e:
                    if (open is { Splits.Count: > 0 }) CloseRun(RunOutcome.Abandoned, e.Line);
                    open ??= StartRun(e.Line);
                    break;
                case CheckpointEvent e:
                    open ??= StartRun(e.Line);
                    open.Splits.Add(new SplitPass(e.Checkpoint, e.RunTime));
                    break;
                case ResetEvent e:
                    if (e.KeepsTime) { if (open is not null) open.Deaths++; }
                    else CloseRun(RunOutcome.Reset, e.Line);
                    break;
                case FinishEvent e:
                    open ??= StartRun(e.Line);
                    open.Map ??= map;
                    open.Mode ??= e.Mode ?? mode;
                    CloseRun(RunOutcome.Finished, e.Line, e);
                    break;
                case LevelUpEvent e:
                    levelUps.Add(e.Level);
                    break;
                case MainMenuEvent e:
                    CloseRun(RunOutcome.Abandoned, e.Line);
                    map = null;
                    mode = null;
                    break;
                case SteamStatsEvent e:
                    steamStats ??= e.Stats;
                    break;
                case ExitEvent e:
                    CloseRun(RunOutcome.Abandoned, e.Line);
                    exitAtSec = e.AtSec;
                    break;
                case ProgressEvent e:
                    if (map is int current) pendingProgress.Add((index, current, e.Percent));
                    break;
            }
        }

        var startedAt = log.Header.StartedAt;
        SessionStatus status;
        long endedAt;
        if (exitAtSec is double exit)
        {
            status = SessionStatus.Closed;
            endedAt = startedAt + (long)Math.Round(exit * 1000);
        }
        else if (options.GameRunning)
        {
            status = SessionStatus.InProgress;
            endedAt = Math.Max(startedAt, options.Now ?? options.FallbackEndedAt ?? startedAt);
        }
        else
        {
            status = SessionStatus.Interrupted;
            endedAt = Math.Max(startedAt, options.FallbackEndedAt ?? startedAt);
        }
        CloseRun(status == SessionStatus.InProgress ? RunOutcome.InProgress : RunOutcome.Abandoned, null);

        var secAt = MakeTimeEstimator(log.Events, startedAt, (endedAt - startedAt) / 1000.0);
        var progress = pendingProgress.Select(p => new ProgressSample(p.Map, p.Percent, secAt(p.Index))).ToList();

        return new SessionRecord(
            ParserVersion,
            log.Header.SessionId,
            log.SteamId,
            log.Header.EngineVersion,
            startedAt,
            endedAt,
            (endedAt - startedAt) / 1000.0,
            status,
            steamStats,
            levelUps,
            log.Events.Any(e => e is CheckpointEvent),
            progress,
            runs.Where(r => r.Map is not null && r.Mode is not null).Select(r => new MapModeKey(r.Map!.Value, r.Mode!.Value)).Distinct().ToList(),
            runs);
    }

    /// <summary>
    /// イベントの時刻（セッション開始からの秒）を推定する関数を作る。
    /// ログの行には時刻が無いので、時刻が分かる行（LevelInit の経過秒・UPLOAD の時刻・終了）を目印にし、
    /// 目印の間は約 1 分ごとの自動保存の回数で按分する（自動保存はメニュー中は止まるようなので、回数だけでは決めない）。
    /// </summary>
    private static Func<int, double> MakeTimeEstimator(IReadOnlyList<PogoEvent> events, long startedAt, double endSec)
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(startedAt).ToLocalTime();
        var startSecOfDay = start.Hour * 3600 + start.Minute * 60 + start.Second;
        var anchors = new List<(int Index, double Sec)> { (-1, 0) };
        var savesBefore = new int[events.Count + 1];
        int saves = 0;

        void AddAnchor(int index, double sec)
        {
            if (sec >= anchors[^1].Sec) anchors.Add((index, sec));
        }

        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] is TimerSaveEvent) saves++;
            savesBefore[i] = saves;
            switch (events[i])
            {
                case ClockEvent c: AddAnchor(i, c.Sec); break;
                case ExitEvent x: AddAnchor(i, x.AtSec); break;
                case WallClockEvent w:
                    var sec = (double)(w.SecOfDay - startSecOfDay);
                    while (sec < 0) sec += 86_400;
                    AddAnchor(i, sec);
                    break;
            }
        }
        savesBefore[events.Count] = saves;
        AddAnchor(events.Count, Math.Max(endSec, anchors[^1].Sec));

        int SavesAt(int index) => index < 0 ? 0 : savesBefore[index];

        return index =>
        {
            var a = anchors[0];
            var b = anchors[^1];
            foreach (var anchor in anchors)
            {
                if (anchor.Index <= index) a = anchor;
                if (anchor.Index >= index) { b = anchor; break; }
            }
            if (a.Index == b.Index) return a.Sec;
            int sa = SavesAt(a.Index), sb = SavesAt(b.Index);
            var frac = sb > sa ? (double)(SavesAt(index) - sa) / (sb - sa) : (double)(index - a.Index) / (b.Index - a.Index);
            return a.Sec + (b.Sec - a.Sec) * frac;
        };
    }
}
