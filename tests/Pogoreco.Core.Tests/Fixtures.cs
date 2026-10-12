using System.Text;

namespace Pogoreco.Core.Tests;

internal static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>acklog は ASCII 以外が混じることがあるので Latin-1 で読む（バイト位置が崩れない）</summary>
    public static string Text(string name) => File.ReadAllText(Path(name), Encoding.Latin1);

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    public static long Local(int y, int mo, int d, int h, int mi, int s) =>
        new DateTimeOffset(new DateTime(y, mo, d, h, mi, s, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    public static SessionRecord Session(string name, AggregateOptions? options = null) =>
        SessionAggregator.Aggregate(LogParser.ParseLog(Text(name)), options)!;
}
