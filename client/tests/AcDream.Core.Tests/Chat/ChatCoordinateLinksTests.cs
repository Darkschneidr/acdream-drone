using AcDream.Core.Chat;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Chat;

public sealed class ChatCoordinateLinksTests
{
    private static IReadOnlyList<ChatTextSpan> Plain(string text) =>
        new[] { new ChatTextSpan(text, null) };

    [Fact]
    public void APairInPlainTextBecomesACoordinateLink()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatCoordinateLinks.Link(
            Plain("meet at 42.1N 33.5E now"))!;

        Assert.Equal(new[] { "meet at ", "42.1N 33.5E", " now" }, spans.Select(s => s.Text));
        Assert.Null(spans[0].Tag);
        Assert.Null(spans[2].Tag);
        ChatTextTag tag = spans[1].Tag!.Value;
        Assert.True(tag.TryGetCoordinate(out double eastWest, out double northSouth));
        Assert.Equal(33.5, eastWest);
        Assert.Equal(42.1, northSouth);
    }

    [Fact]
    public void SouthAndWestAndEitherOrderAreRead()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatCoordinateLinks.Link(
            Plain("go 12.5W, 3.25S"))!;

        ChatTextTag tag = Assert.Single(spans, s => s.Tag is not null).Tag!.Value;
        Assert.True(tag.TryGetCoordinate(out double eastWest, out double northSouth));
        Assert.Equal(-12.5, eastWest);
        Assert.Equal(-3.25, northSouth);
    }

    [Fact]
    public void EveryPairOnTheLineIsLinked()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatCoordinateLinks.Link(
            Plain("from 1N 2E to 3S 4W"))!;

        Assert.Equal(2, spans.Count(s => s.Tag is not null));
        Assert.Equal("from 1N 2E to 3S 4W", string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void ALineWithNoPairIsLeftAlone()
    {
        Assert.Null(ChatCoordinateLinks.Link(Plain("hello there")));
        Assert.Null(ChatCoordinateLinks.Link(Plain("42N 33N is not a pair")));
    }

    [Theory]
    [InlineData("10n 20east")]
    [InlineData("10north 20e")]
    [InlineData("v2.1N 3E")]
    [InlineData("x10n 20e")]
    public void APairInsideAWordIsNotLinked(string text)
    {
        Assert.Null(ChatCoordinateLinks.Link(Plain(text)));
    }

    [Fact]
    public void AnAlreadyTaggedSpanAndTheTimestampAreNotSearched()
    {
        var tell = new ChatTextTag("Tell", "IIDString", "1:Bob 1N 2E");
        IReadOnlyList<ChatTextSpan> spans = new[]
        {
            new ChatTextSpan("[10:00] 1N 2E ", null, ChatSpanRole.Timestamp),
            new ChatTextSpan("Bob 1N 2E", tell),
            new ChatTextSpan(" says, \"hi\"", null),
        };

        Assert.Null(ChatCoordinateLinks.Link(spans));
    }

    [Fact]
    public void ATaggedSpanIsKeptAndThePlainSpanAfterItIsLinked()
    {
        var tell = new ChatTextTag("Tell", "IIDString", "1:Bob");
        IReadOnlyList<ChatTextSpan> spans = ChatCoordinateLinks.Link(new[]
        {
            new ChatTextSpan("Bob", tell),
            new ChatTextSpan(" says, \"at 5N 6E\"", null),
        })!;

        Assert.Equal(new[] { "Bob", " says, \"at ", "5N 6E", "\"" }, spans.Select(s => s.Text));
        Assert.Equal(tell, spans[0].Tag);
        Assert.Equal("COORD", spans[2].Tag!.Value.Type);
    }

    [Theory]
    [InlineData("42.1N 33.5E")]
    [InlineData("42.1n, 33.5e")]
    [InlineData("33.5E; 42.1N")]
    [InlineData("0.5S/101.9W")]
    [InlineData("a 1N 2E b 3N 4W c")]
    [InlineData("no pair 1N here")]
    [InlineData("1E 2W")]
    [InlineData("10n 20east and (42.1N, 33.5E).")]
    public void ItLinksExactlyThePairsThePluginParserReads(string text)
    {
        IReadOnlyList<PluginChatCoordinate> parsed = PluginChatCoordinateParser.ParseAll(text);
        IReadOnlyList<ChatTextSpan> spans = ChatCoordinateLinks.Link(Plain(text)) ?? Plain(text);

        PluginChatCoordinate[] linked = spans
            .Where(s => s.Tag is not null)
            .Select(s =>
            {
                Assert.True(s.Tag!.Value.TryGetCoordinate(out double ew, out double ns));
                return new PluginChatCoordinate(ew, ns);
            })
            .ToArray();
        Assert.Equal(parsed, linked);
    }
}
