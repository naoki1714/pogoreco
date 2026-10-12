namespace Pogoreco.Core.Tests;

public class FallsTests
{
    private static List<(double X, double Y)> P(params double[] ys) => [.. ys.Select((y, i) => ((double)i, y))];

    [Fact]
    public void しきい値以上の下降だけを落下とし_小さな揺れは数えない()
    {
        var falls = Falls.Detect(P(100, 110, 105, 108, 60, 40, 45, 50), "m");
        var f = Assert.Single(falls);
        Assert.Equal(110, f.From);
        Assert.Equal(40, f.To);
        Assert.Equal(70, f.Distance);
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
        Assert.Single(Falls.Detect(P(10, 80, 20), "m"));
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
