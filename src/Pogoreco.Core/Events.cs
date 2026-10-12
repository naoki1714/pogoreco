namespace Pogoreco.Core;

/// <summary>acklog.txt から読み取るイベント。Line は 1 始まりの行番号、Frame は「at frame N」の N。</summary>
public abstract record PogoEvent(int Line, int? Frame);

/// <summary>
/// マップ・モードの通知。マップに入ったとき（モード不明）、約 1 分ごとの位置の保存、
/// スプリット表示がオンのときの区間更新（Run はこの行にだけある）から作る。
/// </summary>
public sealed record MapModeEvent(int Line, int? Frame, int Map, int? Mode, int? Run) : PogoEvent(Line, Frame);

/// <summary>リプレイ記録の開始 ＝ ランの開始。</summary>
public sealed record RunStartEvent(int Line, int? Frame) : PogoEvent(Line, Frame);

/// <summary>チェックポイント（区間）通過。スプリット表示がオンのときだけ出る。RunTime はラン開始からの秒数。</summary>
public sealed record CheckpointEvent(int Line, int? Frame, int Checkpoint, int Previous, double RunTime, int? Map3Route) : PogoEvent(Line, Frame);

/// <summary>リセット。KeepsTime が true のときはタイマーが継続する（ランは終わらない）。</summary>
public sealed record ResetEvent(int Line, int? Frame, bool Died, bool KeepsTime) : PogoEvent(Line, Frame);

/// <summary>ゴール。BestTimeMs が 0 以下なら記録なし。Mode は直前の行から分かれば入る。</summary>
public sealed record FinishEvent(int Line, int? Frame, long TimeMs, long BestTimeMs, int? Mode) : PogoEvent(Line, Frame);

public sealed record PauseMenuEvent(int Line, int? Frame) : PogoEvent(Line, Frame);

public sealed record MainMenuEvent(int Line, int? Frame) : PogoEvent(Line, Frame);

public sealed record SteamStatsEvent(int Line, int? Frame, SteamStats Stats) : PogoEvent(Line, Frame);

/// <summary>ウィンドウを閉じた。AtSec はゲーム起動からの秒数。</summary>
public sealed record ExitEvent(int Line, int? Frame, double AtSec) : PogoEvent(Line, Frame);

public sealed record LevelUpEvent(int Line, int? Frame, int Level) : PogoEvent(Line, Frame);

/// <summary>登っている途中の到達%（セッション内の最高到達と思われる）。</summary>
public sealed record ProgressEvent(int Line, int? Frame, double Percent) : PogoEvent(Line, Frame);

/// <summary>「LevelInit at 秒」: ゲーム起動からの秒数が分かる行（時刻の目安）。</summary>
public sealed record ClockEvent(int Line, int? Frame, double Sec) : PogoEvent(Line, Frame);

/// <summary>約 1 分ごとの位置の自動保存（タイマーによる）。時刻の目安に使う。</summary>
public sealed record TimerSaveEvent(int Line, int? Frame) : PogoEvent(Line, Frame);

/// <summary>「UPLOAD ... time: HH:MM:SS」: 実際の時刻が分かる行。SecOfDay は 0 時からの秒数。</summary>
public sealed record WallClockEvent(int Line, int? Frame, int SecOfDay) : PogoEvent(Line, Frame);

public sealed record SteamStats(int Level, long ExpTotal, int Wins, long BestTimeMs);

/// <summary>StartedAt はログを書いた PC のローカル時刻として解釈した epoch ミリ秒。</summary>
public sealed record LogHeader(string EngineVersion, long StartedAt, string SessionId);

public sealed record ParsedLog(LogHeader? Header, string? SteamId, IReadOnlyList<PogoEvent> Events, int LineCount);
