using AcDream.Core.Chat;

namespace AcDream.Core.Tests.Chat;

public sealed class ChatTagMarkupTests
{
    private const string TellLine =
        @"<Tell:IIDString:1342177290:Dww>Dww<\Tell> tells you, ""hello""";

    [Fact]
    public void ARetailTellLineSplitsIntoATaggedNameAndPlainRemainder()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.Parse(TellLine);

        Assert.Equal(2, spans.Count);

        Assert.Equal("Dww", spans[0].Text);
        Assert.NotNull(spans[0].Tag);
        Assert.Equal("Tell", spans[0].Tag!.Value.Type);
        Assert.Equal("IIDString", spans[0].Tag!.Value.Format);

        Assert.Equal(@" tells you, ""hello""", spans[1].Text);
        Assert.Null(spans[1].Tag);
    }

    [Fact]
    public void TheTagCarriesTheSpeakersIdAndName()
    {
        ChatTextTag tag = Assert
            .Single(ChatTagMarkup.Parse(TellLine), s => s.Tag is not null)
            .Tag!.Value;

        Assert.True(tag.TryGetIidString(out uint objectId, out string name));
        Assert.Equal(1342177290u, objectId);
        Assert.Equal("Dww", name);
    }

    [Fact]
    public void AnyBracketedTextThatIsNotATagClosesTheOpenOne()
    {
        foreach (string closer in new[] { @"<\Tell>", "<Tell>", "<anything>" })
        {
            IReadOnlyList<ChatTextSpan> spans =
                ChatTagMarkup.Parse($"<Tell:IIDString:1:A>A{closer}B");

            Assert.Equal(2, spans.Count);
            Assert.Equal("A", spans[0].Text);
            Assert.NotNull(spans[0].Tag);
            Assert.Equal("B", spans[1].Text);
            Assert.Null(spans[1].Tag);
        }
    }

    [Fact]
    public void PlainTextIsOneUntaggedSpan()
    {
        ChatTextSpan span = Assert.Single(ChatTagMarkup.Parse("You say, \"hi\""));
        Assert.Equal("You say, \"hi\"", span.Text);
        Assert.Null(span.Tag);
    }

    [Fact]
    public void AnUnterminatedBracketIsOrdinaryText()
    {
        ChatTextSpan span = Assert.Single(ChatTagMarkup.Parse("is 3 < 4 really"));
        Assert.Equal("is 3 < 4 really", span.Text);
        Assert.Null(span.Tag);
    }

    [Fact]
    public void TheConcatenatedSpansAlwaysReproduceTheVisibleLine()
    {
        string visible = string.Concat(
            ChatTagMarkup.Parse(TellLine).Select(s => s.Text));

        Assert.Equal(@"Dww tells you, ""hello""", visible);
    }

    [Theory]
    [InlineData("<Tell:IIDString:notanumber:Dww>x", "a non-numeric id")]
    [InlineData("<Tell:IIDString:1>x", "no name")]
    [InlineData("<Tell:IIDString:>x", "empty payload")]
    [InlineData("<Tell:DID:1:x>x", "a DID payload that is not one number")]
    [InlineData("<Tell:IIDEnum:1:Nope>x", "an IIDEnum name not in the tag table")]
    [InlineData("<Tell:Tell:1:Bob>x", "a format that is not a payload shape")]
    [InlineData("<COORD:World:1N, 2E>x", "a type not in the tag table")]
    [InlineData("<Tell:IIDString:4294967296:Bob>x", "an id that overflows")]
    public void AMalformedOrUnknownTagIsNotATagAndIsShownAsWritten(
        string markup, string why)
    {
        ChatTextSpan span = Assert.Single(ChatTagMarkup.Parse(markup));
        Assert.True(span.Tag is null, why);
        Assert.Equal(markup, span.Text);
    }

    // -- The reading rule, case by case ---------------------------------

    [Fact]
    public void BracketedTextThatIsNotATagIsShownWhenNoTagIsOpen()
    {
        ChatTextSpan span = Assert.Single(ChatTagMarkup.Parse("a <hello> b <> c <x:y> d"));
        Assert.Equal("a <hello> b <> c <x:y> d", span.Text);
        Assert.Null(span.Tag);
    }

    [Fact]
    public void ALineFullOfBracketsIsReadInOnePassAndShownAsWritten()
    {
        string brackets = new string('<', 20000) + "Tell:IIDString:1:Bob";
        string late = new string('<', 20000) + "a>";

        Assert.Equal(brackets, Assert.Single(ChatTagMarkup.Parse(brackets)).Text);
        Assert.Equal(late, Assert.Single(ChatTagMarkup.Parse(late)).Text);
        Assert.Equal(late, ChatTagMarkup.ToShownText(late));
    }

    [Fact]
    public void AStartTagWhileATagIsOpenOnlyClosesIt()
    {
        IReadOnlyList<ChatTextSpan> spans =
            ChatTagMarkup.Parse("<Tell:IIDString:1:A>A<Tell:IIDString:2:B>B");

        Assert.Equal(new[] { "A", "B" }, spans.Select(span => span.Text));
        Assert.Equal("1:A", spans[0].Tag!.Value.Data);
        Assert.Null(spans[1].Tag);
    }

    [Fact]
    public void AnEmptyPairOfBracketsClosesAnOpenTag()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.Parse("<Tell:IIDString:1:A>A<>B");

        Assert.Equal(new[] { "A", "B" }, spans.Select(span => span.Text));
        Assert.NotNull(spans[0].Tag);
        Assert.Null(spans[1].Tag);
    }

    [Fact]
    public void AnUnterminatedBracketInsideATagIsShownInThatTag()
    {
        ChatTextSpan span = Assert.Single(ChatTagMarkup.Parse("<Tell:IIDString:1:A>A < b"));
        Assert.Equal("A < b", span.Text);
        Assert.True(span.Tag!.Value.IsTell);
    }

    [Fact]
    public void TheCharacterRightAfterAHiddenTagIsShownEvenWhenItIsABracket()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.Parse(
            @"<Tell:IIDString:1:A>A<\Tell><Tell:IIDString:2:B>B");

        Assert.Equal("A<Tell:IIDString:2:B>B", string.Concat(spans.Select(span => span.Text)));
        Assert.Equal("A", spans[0].Text);
        Assert.All(spans.Skip(1), span => Assert.Null(span.Tag));
    }

    [Fact]
    public void ABracketIsMatchedWithTheFirstClosingBracketAfterIt()
    {
        // "<a<Tell:IIDString:1:B>" is not a tag, so its "<" is shown; the scan
        // then finds the start tag inside it.
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.Parse("<a<Tell:IIDString:1:B>B");

        Assert.Equal(new[] { "<a", "B" }, spans.Select(span => span.Text));
        Assert.Null(spans[0].Tag);
        Assert.True(spans[1].Tag!.Value.IsTell);
    }

    [Theory]
    [InlineData("<tell:iidstring:1:Bob>Bob", true)]
    [InlineData("<Tell:IID:0x5000000A>Bob", true)]
    [InlineData("<DID:DID:0x06001234>icon", false)]
    [InlineData("<IID:IIDEnum:7:Tell>x", false)]
    [InlineData("<Invalid:IIDString: 12:Bob>Bob", false)]
    public void EveryTypeInTheTagTableOpensATagButOnlyATellTagIsATell(string markup, bool tell)
    {
        ChatTextSpan span = ChatTagMarkup.Parse(markup)[0];
        Assert.NotNull(span.Tag);
        Assert.Equal(tell, span.Tag!.Value.IsTell);
    }

    [Theory]
    [InlineData("1342177290", true, 1342177290u)]
    [InlineData("0x5000000A", true, 0x5000000Au)]
    [InlineData("017", true, 15u)]
    [InlineData("  +7", true, 7u)]
    [InlineData("-1", true, 0xFFFFFFFFu)]
    [InlineData("4294967295", true, 0xFFFFFFFFu)]
    [InlineData("4294967296", false, 0u)]
    [InlineData("08", false, 0u)]
    [InlineData("0x", false, 0u)]
    [InlineData("12 ", false, 0u)]
    [InlineData("", false, 0u)]
    [InlineData("-", false, 0u)]
    public void ATagNumberIsReadLikeTheCLibrary(string text, bool ok, uint expected)
    {
        Assert.Equal(ok, ChatTagMarkup.TryParseTagNumber(text, out uint value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ANameContainingAColonSurvivesIntact()
    {
        ChatTextTag tag = ChatTagMarkup.Parse("<Tell:IIDString:5:Odd:Name>x")[0].Tag!.Value;

        Assert.True(tag.TryGetIidString(out uint id, out string name));
        Assert.Equal(5u, id);
        Assert.Equal("Odd:Name", name);
    }

    [Theory]
    [InlineData("Press <Ctrl:F> to follow", false)]
    [InlineData("<a:b:c>", false)]
    [InlineData(@"<Tell:IIDString:1:Bob>Bob<\Tell>", false)]
    [InlineData(@"<COORD:World:somewhere>x<\COORD>", false)]
    [InlineData(@"<COORD:World:1N, 2E>x<\COORD>", true)]
    [InlineData(@"Press <Ctrl:F>, then <coord:world:1N, 2E>x<\coord>", true)]
    public void OnlyAWellFormedCoordinateTagIsALinkTag(string text, bool expected)
    {
        Assert.Equal(expected, ChatTagMarkup.ContainsTag(text));
    }

    [Fact]
    public void ParseLinksKeepsEveryOtherAngleBracketTextAsWritten()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.ParseLinks(
            @"<Ctrl:F> <COORD:World:1N, 2E>go <b:c> there<\Other><\COORD> <\COORD>end");

        Assert.Equal(
            new[] { "<Ctrl:F> ", @"go <b:c> there<\Other>", @" <\COORD>end" },
            spans.Select(span => span.Text));
        Assert.Null(spans[0].Tag);
        Assert.Equal("COORD", spans[1].Tag!.Value.Type);
        Assert.Null(spans[2].Tag);
    }

    [Theory]
    [InlineData(@"<COORD:World:0N, 0E>the bridge<\COORD>")]
    [InlineData(@"<coord:world:1N, 2E>x")]
    [InlineData(@"a\<COORD:World:1N, 2E>b<\COORD>")]
    [InlineData(@"trailing \ and \\< and <<COORD:World:1N, 2E>>")]
    [InlineData(@"Press <Ctrl:F>")]
    [InlineData("no markup at all")]
    public void EscapedTextShowsAsWrittenAndHoldsNoTag(string text)
    {
        string escaped = AcDream.Plugin.Abstractions.PluginChatMarkup.Escape(text);

        Assert.False(ChatTagMarkup.ContainsTag(escaped));
        Assert.Equal(text, ChatTagMarkup.ToPlainText(escaped));
        Assert.All(ChatTagMarkup.ParseLinks(escaped), span => Assert.Null(span.Tag));
    }

    [Fact]
    public void EscapeOfNothingIsEmpty()
    {
        Assert.Equal(string.Empty, AcDream.Plugin.Abstractions.PluginChatMarkup.Escape(null));
        Assert.Equal(@"\<b>", AcDream.Plugin.Abstractions.PluginChatMarkup.Escape("<b>"));
    }

    [Fact]
    public void WithoutCoordinateTagsKeepsTheCaptionAndOtherTags()
    {
        IReadOnlyList<ChatTextSpan> spans = ChatTagMarkup.ParseLine(
            @"<Tell:IIDString:1:Bob>Bob<\Tell> says, ""<COORD:World:0N, 0E>42.1N 33.5E<\COORD>""");

        Assert.Equal(
            new[] { "Bob", " says, \"", "42.1N 33.5E", "\"" },
            spans.Select(span => span.Text));
        Assert.Equal("Tell", spans[0].Tag!.Value.Type);
        Assert.All(spans.Skip(1), span => Assert.Null(span.Tag));
    }
}
