using System.Buffers.Binary;

namespace Pogoreco.Core.Tests;

public class ReplayTests
{
    private static byte[] Save((short X, short Y)[] points, int gameModeId = 106)
    {
        int body = 4 + points.Length * 8;
        var b = new byte[264 + body];
        int[] head = [36, 62, 44, 80, 23, 264, body];
        for (int i = 0; i < head.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8 + i * 4), head[i]);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(52), gameModeId);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(264), points.Length);
        for (int s = 0; s < points.Length; s++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(268 + s * 8), points[s].X);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(268 + s * 8 + 2), points[s].Y);
        }
        return b;
    }

    internal static (short X, short Y)[] Climb(int steps, int startY = 1500) =>
        [.. Enumerable.Range(0, steps).Select(i => ((short)(1000 + i), (short)(startY + i)))];

    [Fact]
    public void 実ファイルの自己ベストのリプレイを読む()
    {
        var t = Replay.ParseReplayFile(Fixtures.Bytes("best-map1.replay"))!;
        Assert.Equal(5815, t.Ys.Length);
        Assert.Equal(726.781, t.FinishTimeSec);
        Assert.Equal(1407, t.Ys[0]);
        Assert.Equal(23054, t.Ys.Max());
    }

    [Fact]
    public void ゲーム内の割合とメートルに換算できる()
    {
        Assert.True(Math.Abs(Maps.ToDisplay(0, 14298).Value - 59.05) < 0.1);
        Assert.True(Math.Abs(Maps.ToDisplay(0, 14495).Value - 59.88) < 0.1);
        Assert.Equal(100, Maps.ToDisplay(0, 23055).Value, 6);
        Assert.Equal(new HeightDisplay(0, "%"), Maps.ToDisplay(0, 1407));
        Assert.Equal(new HeightDisplay(826.4, "m"), Maps.ProgressToDisplay(8, 82.64));
        Assert.Equal(new HeightDisplay(11280, ""), Maps.ToDisplay(8, 11280));
    }

    [Fact]
    public void セーブを読み_同じランは伸びてもキーが変わらない()
    {
        var early = Replay.ParseSaveFile(Save(Climb(70)))!;
        var later = Replay.ParseSaveFile(Save(Climb(100)))!;
        Assert.Equal(106, later.GameModeId);
        Assert.Equal([1500, 1501, 1502], later.Ys.Take(3).Select(y => (int)y));
        Assert.Equal(Replay.TrackKey(early), Replay.TrackKey(later));
        Assert.NotEqual(Replay.TrackKey(later), Replay.TrackKey(Replay.ParseSaveFile(Save(Climb(100, 1501)))!));
        Assert.Null(Replay.ParseSaveFile(new byte[300]));
        Assert.Null(Replay.ParseReplayFile(new byte[300]));
    }

    [Fact]
    public void ランのキーはTypeScript版と同じ値になる() =>
        // TypeScript 版の trackKey で同じ点列から計算した値。以前保存した climbs/*.json の id と一致させるため
        Assert.Equal("135bf14d", Replay.TrackKey(Replay.ParseSaveFile(Save(Climb(100)))!));

    [Fact]
    public void 間引いても最高点と落下の底は残る()
    {
        Assert.Equal([7, 15, 19], Replay.HeightsPerSecond([.. Enumerable.Range(0, 20).Select(i => (short)i)]));
        var pts = Enumerable.Range(0, 1000).Select(i => ((double)i, i == 500 ? 9999.0 : i == 700 ? -50.0 : i)).ToList();
        var d = Replay.Downsample(pts, 100);
        Assert.True(d.Count <= 100);
        Assert.Contains(d, p => p.Y == 9999);
        Assert.Contains(d, p => p.Y == -50);
    }
}

public class HudTests
{
    [Theory]
    [InlineData("4)Player123 @ 665m/ 826m", 665, 826, "m")]
    [InlineData("4)P1ayer123 @ 665m/ 826m", 665, 826, "m")]
    [InlineData("1)name @ 53.8%/ 97.5%", 53.8, 97.5, "%")]
    [InlineData("1)name @ 53,8% / 97,5%", 53.8, 97.5, "%")]
    public void 自分の行を読む(string text, double current, double best, string unit) =>
        Assert.Equal(new HudHeights(current, best, unit), Hud.ParseOwnLine(text));

    [Theory]
    [InlineData("4)P1ayer123 0 65m/ 2m")] // 最高が今より低い（読み間違い）
    [InlineData("m1234 685m 2")]
    [InlineData("1)name @ 153.8%/ 197.5%")] // 100% を超える
    public void ありえない値は捨てる(string text) => Assert.Null(Hud.ParseOwnLine(text));

    [Theory]
    [InlineData("32h 05m 47s", 32 * 3600 + 5 * 60 + 47)]
    [InlineData("32h 05m 4 7s", 32 * 3600 + 5 * 60 + 47)]
    [InlineData("32h 05m 475", 32 * 3600 + 5 * 60 + 47)]
    [InlineData("d 14 : 32h 05m 4 7s", 32 * 3600 + 5 * 60 + 47)]
    [InlineData("0h 12m 03s", 12 * 60 + 3)]
    public void タイムの行を読む(string text, double sec) => Assert.Equal(sec, Hud.ParseRunTime(text));

    [Fact]
    public void タイムが読めなければnull() => Assert.Null(Hud.ParseRunTime("(読めない)"));

    [Theory]
    [InlineData("541433h04m35s")] // OCR がノイズを数字と誤読し、ありえない時間になったもの
    [InlineData("1949158h62m04s")]
    public void 時間がありえないほど大きければ捨てる(string text) => Assert.Null(Hud.ParseRunTime(text));
}
