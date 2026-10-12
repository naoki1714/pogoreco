namespace Pogoreco.Core.Tests;

public class FallsTests
{
    private static List<(double X, double Y)> P(params double[] ys) => [.. ys.Select((y, i) => ((double)i, y))];

    [Fact]
    public void しきい値以上の下降だけを落下とし_小さな揺れは数えない()
    {
        var falls = Falls.Detect(P(300, 330, 315, 322, 180, 150, 160), "m");
        var f = Assert.Single(falls);
        Assert.Equal(330, f.From);
        Assert.Equal(150, f.To);
        Assert.Equal(180, f.Distance);
        Assert.False(f.Axe);
    }

    [Fact]
    public void 一度に0mへ戻されたら斧に当たったとみなす()
    {
        var falls = Falls.Detect(P(600, 640, 655, 3, 20, 60), "m");
        var f = Assert.Single(falls);
        Assert.True(f.Axe);
        Assert.Equal(3, f.To);
    }

    [Fact]
    public void ゆっくり降りて低い所に着いただけでは斧にならない()
    {
        var falls = Falls.Detect(P(200, 150, 100, 60, 30, 5), "m");
        Assert.All(falls, f => Assert.False(f.Axe));
    }

    [Fact]
    public void パーセントの単位では斧を数えない()
    {
        var falls = Falls.Detect(P(50, 55, 0), "%");
        Assert.All(falls, f => Assert.False(f.Axe));
    }

    [Fact]
    public void 最後まで下降が続いても数える()
    {
        Assert.Single(Falls.Detect(P(10, 150, 20), "m"));
    }

    [Fact]
    public void 基準は_メートルなら100m以上_パーセントなら10パーセント以上()
    {
        Assert.Equal(100, Falls.Threshold("m"));
        Assert.Equal(10, Falls.Threshold("%"));
        Assert.Empty(Falls.Detect(P(500, 420, 410), "m"));      // 90 m は大幅ではない
        Assert.Single(Falls.Detect(P(500, 400, 395), "m"));     // 105 m
        Assert.Empty(Falls.Detect(P(50, 42, 41), "%"));         // 9%
        Assert.Single(Falls.Detect(P(50, 40, 39), "%"));        // 11%
    }
}

public class SpikeTests
{
    private static List<(double X, double Y)> P(params double[] ys) => [.. ys.Select((y, i) => ((double)i, y))];

    [Fact]
    public void 一回だけ外れた読み取りは取り除く()
    {
        var r = HeightTimeline.RemoveSpikes(P(360, 362, 4, 363, 360), 30);
        Assert.Equal([360.0, 362, 363, 360], r.Select(p => p.Y));
    }

    [Fact]
    public void 外れたまま続く変化は残す()
    {
        var r = HeightTimeline.RemoveSpikes(P(650, 655, 3, 4, 20), 30);
        Assert.Equal(5, r.Count);
    }
}
