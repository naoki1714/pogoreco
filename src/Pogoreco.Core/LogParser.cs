using System.Globalization;
using System.Text.RegularExpressions;

namespace Pogoreco.Core;

/// <summary>
/// acklog.txt（Pogostuck を -diag 付きで起動すると出るログ）を読む。
/// 実プレイのログ（2026-10-11）で確認済み: ラン開始・リセット・ゴール・マップ入場・位置の保存・レベルアップ・到達%。
/// 区間通過（playerCheckpointDo）と update splits はゲームの「スプリット表示」がオンのときだけ出る。
/// この 2 つは derJunker/pogostuck-split-tracker の調査結果によるもので、実物では未確認。
/// </summary>
public static partial class LogParser
{
    [GeneratedRegex(@"^Log of A8 Engine (\S+) run at \w{3} (\w{3})\s+(\d{1,2}) (\d{2}):(\d{2}):(\d{2}) (\d{4})")]
    private static partial Regex HeaderRe();
    [GeneratedRegex(@"Local User: CSteamID\((\d+)\)")]
    private static partial Regex SteamUserRe();
    [GeneratedRegex(@"at frame (\d+)")]
    private static partial Regex FrameRe();

    [GeneratedRegex(@"update splits at frame \d+: level_current\((-?\d+)\)m\((-?\d+)\) run\((-?\d+)\)")]
    private static partial Regex UpdateSplitsRe();
    [GeneratedRegex(@"PLAYER ENT CREATE AT FRAME \d+, outroPlaying\((\d+)\), level_current\((-?\d+)\)")]
    private static partial Regex PlayerCreateRe();
    // 位置の保存。map(マップ)(?) の 2 つ目はモードではない（Map 2 でも 0、マップを抜けるときは 2）
    [GeneratedRegex(@"calling playerSavePosition\(\) at frame \d+,.*?map\((-?\d+)\)\(-?\d+\)")]
    private static partial Regex SavePositionRe();
    [GeneratedRegex(@"playerSavePositionRequest to 1 in playerMove because of timer")]
    private static partial Regex TimerSaveRe();
    // time(0) のときが登っている途中。ゴール時は time にタイムが入り progress は 0
    [GeneratedRegex(@"uploadCurrentStateToFriendInfoLeaderboard\(\) at frame \d+: mode\(\d+\) progress\((\d+(?:\.\d+)?)\), time\(0\)")]
    private static partial Regex ProgressRe();
    [GeneratedRegex(@"^LevelInit at (\d+(?:\.\d+)?)")]
    private static partial Regex ClockRe();
    [GeneratedRegex(@"UPLOAD: calling menuUploadStats.*time: (\d{2}):(\d{2}):(\d{2})")]
    private static partial Regex WallClockRe();
    [GeneratedRegex(@"playerCheckpointDo\(\) at frame \d+: new checkpoint\((\d+)\).*?old\((-?\d+)\)(?:, map3RouteCurrent\((\d+)\))?.*?runTimeCurrent\((\d+(?:\.\d+)?)\)")]
    private static partial Regex CheckpointRe();
    [GeneratedRegex(@"playerReset\(\) .*?playerLocalDead\((\d+)\) dontResetTime\((\d+)\)")]
    private static partial Regex ResetRe();
    [GeneratedRegex(@"playerRunFinish at frame \d+.*?requestProgressUploadTime\((-?\d+)\) <\? bestTime\((-?\d+)\)")]
    private static partial Regex FinishRe();
    // ゴールの直前の行「playerRunFinish at frame N | runState(1) m(モード)」
    [GeneratedRegex(@"playerRunFinish at frame \d+ \| runState\(\d+\) m\((-?\d+)\)")]
    private static partial Regex FinishModeRe();
    [GeneratedRegex(@"playerExpUnlockStuff\(newLevel (\d+)\)")]
    private static partial Regex LevelUpRe();
    [GeneratedRegex(@"replayInit at frame \d+")]
    private static partial Regex RunStartRe();
    [GeneratedRegex(@"OPEN menu at frame \d+")]
    private static partial Regex PauseMenuRe();
    [GeneratedRegex(@"levelLoadMenu - START at frame \d+")]
    private static partial Regex MainMenuRe();
    [GeneratedRegex(@"menuGetSteamStats OK at frame \d+ stats\(wins (\d+) \| \d+, expTotal (\d+), expLevel \d+, level (\d+), bestTime (-?\d+)\)")]
    private static partial Regex SteamStatsRe();
    [GeneratedRegex(@"^Close window at (\d+(?:\.\d+)?)")]
    private static partial Regex ExitRe();

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static int I(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
    private static long L(Group g) => long.Parse(g.Value, CultureInfo.InvariantCulture);
    private static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>1 行目のヘッダーを読む。日時はログを書いた PC のローカル時刻として解釈する。</summary>
    public static LogHeader? ParseHeader(string line)
    {
        var m = HeaderRe().Match(line);
        if (!m.Success) return null;
        var month = Array.IndexOf(Months, m.Groups[2].Value);
        if (month < 0) return null;
        int day = I(m.Groups[3]), hour = I(m.Groups[4]), minute = I(m.Groups[5]), second = I(m.Groups[6]), year = I(m.Groups[7]);
        var local = new DateTime(year, month + 1, day, hour, minute, second, DateTimeKind.Local);
        return new LogHeader(
            m.Groups[1].Value,
            new DateTimeOffset(local).ToUnixTimeMilliseconds(),
            $"{year:D4}{month + 1:D2}{day:D2}-{hour:D2}{minute:D2}{second:D2}");
    }

    /// <summary>1 行をイベントに変換する。関係ない行は null。</summary>
    public static PogoEvent? ParseLine(string line, int lineNumber)
    {
        var fm = FrameRe().Match(line);
        int? frame = fm.Success ? I(fm.Groups[1]) : null;
        Match m;

        if ((m = UpdateSplitsRe().Match(line)).Success)
            return new MapModeEvent(lineNumber, frame, I(m.Groups[1]), I(m.Groups[2]), I(m.Groups[3]));
        if ((m = PlayerCreateRe().Match(line)).Success)
            // outroPlaying(1) はゴール後のムービーなので数えない
            return m.Groups[1].Value == "0" && I(m.Groups[2]) >= 0 ? new MapModeEvent(lineNumber, frame, I(m.Groups[2]), null, null) : null;
        if ((m = SavePositionRe().Match(line)).Success)
            return I(m.Groups[1]) >= 0 ? new MapModeEvent(lineNumber, frame, I(m.Groups[1]), null, null) : null;
        if (TimerSaveRe().IsMatch(line)) return new TimerSaveEvent(lineNumber, frame);
        if ((m = ProgressRe().Match(line)).Success) return new ProgressEvent(lineNumber, frame, D(m.Groups[1]));
        if ((m = ClockRe().Match(line)).Success) return new ClockEvent(lineNumber, frame, D(m.Groups[1]));
        if ((m = WallClockRe().Match(line)).Success)
            return new WallClockEvent(lineNumber, frame, I(m.Groups[1]) * 3600 + I(m.Groups[2]) * 60 + I(m.Groups[3]));
        if ((m = CheckpointRe().Match(line)).Success)
            return new CheckpointEvent(lineNumber, frame, I(m.Groups[1]), I(m.Groups[2]), D(m.Groups[4]), m.Groups[3].Success ? I(m.Groups[3]) : null);
        if ((m = ResetRe().Match(line)).Success)
            return new ResetEvent(lineNumber, frame, m.Groups[1].Value != "0", m.Groups[2].Value != "0");
        if ((m = FinishRe().Match(line)).Success) return new FinishEvent(lineNumber, frame, L(m.Groups[1]), L(m.Groups[2]), null);
        if ((m = LevelUpRe().Match(line)).Success) return new LevelUpEvent(lineNumber, frame, I(m.Groups[1]));
        if (RunStartRe().IsMatch(line)) return new RunStartEvent(lineNumber, frame);
        if (PauseMenuRe().IsMatch(line)) return new PauseMenuEvent(lineNumber, frame);
        if (MainMenuRe().IsMatch(line)) return new MainMenuEvent(lineNumber, frame);
        if ((m = SteamStatsRe().Match(line)).Success)
            return new SteamStatsEvent(lineNumber, frame, new SteamStats(I(m.Groups[3]), L(m.Groups[2]), I(m.Groups[1]), L(m.Groups[4])));
        if ((m = ExitRe().Match(line)).Success) return new ExitEvent(lineNumber, frame, D(m.Groups[1]));
        return null;
    }

    /// <summary>ログの最後の時点で遊んでいるマップ（メインメニューにいれば null）。</summary>
    public static int? CurrentMap(ParsedLog log)
    {
        int? map = null;
        foreach (var e in log.Events)
        {
            if (e is MapModeEvent m) map = m.Map;
            else if (e is MainMenuEvent or ExitEvent) map = null;
        }
        return map;
    }

    /// <summary>acklog.txt の全文を読む。</summary>
    public static ParsedLog ParseLog(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);

        var header = lines.Count > 0 ? ParseHeader(lines[0]) : null;
        string? steamId = null;
        var events = new List<PogoEvent>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (steamId is null)
            {
                var sm = SteamUserRe().Match(line);
                if (sm.Success) steamId = sm.Groups[1].Value;
            }
            var e = ParseLine(line, i + 1);
            if (e is null) continue;
            if (e is FinishEvent f && i > 0)
            {
                var mm = FinishModeRe().Match(lines[i - 1]);
                if (mm.Success) e = f with { Mode = I(mm.Groups[1]) };
            }
            events.Add(e);
        }
        return new ParsedLog(header, steamId, events, lines.Count);
    }
}
