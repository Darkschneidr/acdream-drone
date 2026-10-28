using AcDream.Core.Chat;
using AcDream.Core.Combat;
using AcDream.Runtime.Chat;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Chat;

public sealed class RuntimeChatFeedTests
{
    private const uint OtherPlayerGuid = 0x5000000Au;

    // -- Wording (the same goldens the chat box is pinned to) -------------

    [Fact]
    public void EachKindIsWordedTheWayTheChatBoxReadsIt()
    {
        static string Word(ChatEntry entry) => RuntimeChatFeed.Format(entry);

        Assert.Equal(
            "Bob says, \"hi there\"",
            Word(new ChatEntry(
                ChatKind.LocalSpeech, "Bob", "hi there", OtherPlayerGuid, 0u)));
        Assert.Equal(
            "You say, \"hi there\"",
            Word(new ChatEntry(ChatKind.LocalSpeech, "You", "hi there", 0u, 0u)));
        Assert.Equal(
            "Bob says, \"hi there\"",
            Word(new ChatEntry(
                ChatKind.RangedSpeech, "Bob", "hi there", OtherPlayerGuid, 0u)));
        Assert.Equal(
            "[Fellowship] Bob says, \"group up\"",
            Word(new ChatEntry(ChatKind.Channel, "Bob", "group up", OtherPlayerGuid, 7u)
            {
                ChannelName = "Fellowship",
            }));
        Assert.Equal(
            "Bob says on the <unknown> channel, \"group up\"",
            Word(new ChatEntry(
                ChatKind.Channel, "Bob", "group up", OtherPlayerGuid, 7u)));
        Assert.Equal(
            "Bob tells you, \"meet me\"",
            Word(new ChatEntry(ChatKind.Tell, "Bob", "meet me", OtherPlayerGuid, 0u)));
        Assert.Equal(
            "You tell Bob, \"meet me\"",
            Word(new ChatEntry(ChatKind.Tell, "Bob", "meet me", 0u, 0u)));
        Assert.Equal(
            "[Popup] You have died.",
            Word(new ChatEntry(ChatKind.Popup, "", "You have died.", 0u, 0u)));
        Assert.Equal(
            "Bob waves.",
            Word(new ChatEntry(ChatKind.Emote, "Bob", "waves.", OtherPlayerGuid, 0u)));
        Assert.Equal(
            "Bob waves.",
            Word(new ChatEntry(
                ChatKind.SoulEmote, "Bob", "waves.", OtherPlayerGuid, 0u)));
        Assert.Equal(
            "Welcome to Dereth.",
            Word(new ChatEntry(ChatKind.System, "", "Welcome to Dereth.", 0u, 0u)));
    }

    [Fact]
    public void AnotherPlayersNameIsMarkedUpAndTheVisibleTextIsUnchanged()
    {
        var entry = new ChatEntry(
            ChatKind.LocalSpeech, "Bob", "hi there", OtherPlayerGuid, 0u);

        Assert.Equal(
            "<Tell:IIDString:1342177290:Bob>Bob<\\Tell> says, \"hi there\"",
            RuntimeChatFeed.FormatTagged(entry));
        Assert.Equal(
            RuntimeChatFeed.Format(entry),
            string.Concat(ChatTagMarkup
                .Parse(RuntimeChatFeed.FormatTagged(entry))
                .Select(span => span.Text)));
    }

    // -- Coordinate links: only a plugin's own line carries one ----------

    private static ChatTextSpan CoordinateSpan(RuntimeChatLine line) =>
        Assert.Single(line.Spans!, span => span.Tag is { Type: "COORD" });

    private static bool HasCoordinateLink(RuntimeChatLine line) =>
        line.Spans?.Any(span => span.Tag is { } tag
            && string.Equals(tag.Type, "COORD", StringComparison.OrdinalIgnoreCase)) == true;

    [Fact]
    public void ACoordinateAPlayerTypesIsNotALink()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech("Bob", "meet at 42.1N 33.5E", OtherPlayerGuid, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
        log.OnTellReceived("Bob", "meet at 42.1N 33.5E", OtherPlayerGuid, (uint)RetailLogTextType.Tell);
        log.OnChannelBroadcast(7u, "Bob", "meet at 42.1N 33.5E", channelName: "Fellowship");

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal(3, lines.Count);
        Assert.Equal("Bob says, \"meet at 42.1N 33.5E\"", lines[0].Text);
        foreach (RuntimeChatLine line in lines)
        {
            Assert.False(HasCoordinateLink(line));
            // The speaker's name is still the tell link it always was.
            Assert.Equal("Bob", Assert.Single(line.Spans!, span => span.Tag is not null).Text);
        }
    }

    [Fact]
    public void ACoordinateInTheCharactersOwnLineOrAServerLineIsNotALink()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech("You", "here 1N 2E", 0u, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
        log.OnSystemMessage("Your corpse is located at (42.1N, 33.5E).", chatType: 0u);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Null(lines[0].Spans);
        Assert.Null(lines[1].Spans);
        Assert.Equal("Your corpse is located at (42.1N, 33.5E).", lines[1].Text);
    }

    [Fact]
    public void ACoordinateTagAPlayerTypesIsNotALink()
    {
        // A player who types the tag must not be able to show one place and
        // hand plugins another: the caption is plain text, the tag goes nowhere.
        const string Typed = "<COORD:World:0N, 0E>42.1N 33.5E<\\COORD>";
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech("Bob", Typed, OtherPlayerGuid, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
        log.OnTellReceived("Bob", Typed, OtherPlayerGuid, (uint)RetailLogTextType.Tell);
        log.OnChannelBroadcast(7u, "Bob", Typed, channelName: "Fellowship");

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal(3, lines.Count);
        Assert.Equal("Bob says, \"42.1N 33.5E\"", lines[0].Text);
        foreach (RuntimeChatLine line in lines)
        {
            Assert.False(HasCoordinateLink(line));
            Assert.Equal("Bob", Assert.Single(line.Spans!, span => span.Tag is not null).Text);
            Assert.Equal(line.Text, string.Concat(line.Spans!.Select(span => span.Text)));
        }
    }

    [Fact]
    public void ACombatLineIsNotSearchedForCoordinates()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnCombatLine("You hit 3N 4E for 9 points.", (uint)RetailLogTextType.CombatSelf);

        Assert.Null(Assert.Single(feed.Snapshot()).Spans);
    }

    // -- A plugin's own line ----------------------------------------------

    [Fact]
    public void ACoordinateAPluginWritesIsALink()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("meet at 42.1N 33.5E", chatType: 0u, allowsMarkup: true);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal("meet at 42.1N 33.5E", line.Text);
        ChatTextSpan coordinate = CoordinateSpan(line);
        Assert.Equal("42.1N 33.5E", coordinate.Text);
        Assert.True(coordinate.Tag!.Value.TryGetCoordinate(out double eastWest, out double northSouth));
        Assert.Equal(33.5, eastWest);
        Assert.Equal(42.1, northSouth);
    }

    [Fact]
    public void ATimestampedPluginLineKeepsItsStampAndLinksTheCoordinate()
    {
        var log = new ChatLog { DisplayTimestampsSource = static () => true };
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("at 5S 6W", chatType: 0u, allowsMarkup: true);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal(ChatSpanRole.Timestamp, line.Spans![0].Role);
        Assert.Equal("5S 6W", CoordinateSpan(line).Text);
        Assert.Equal(line.Text, string.Concat(line.Spans.Select(span => span.Text)));
    }

    [Fact]
    public void APluginLinesMarkupIsReadAsALink()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage(
            "Waypoint: <COORD:World:12.5N, 3.25W>the bridge<\\COORD>.",
            chatType: 0u,
            allowsMarkup: true);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal("Waypoint: the bridge.", line.Text);
        ChatTextSpan coordinate = CoordinateSpan(line);
        Assert.Equal("the bridge", coordinate.Text);
        Assert.True(coordinate.Tag!.Value.TryGetCoordinate(out double eastWest, out double northSouth));
        Assert.Equal(-3.25, eastWest);
        Assert.Equal(12.5, northSouth);
        Assert.Equal("Waypoint: the bridge.", RuntimeChatFeed.Format(log.Snapshot()[0]));
    }

    [Fact]
    public void AServerLinesTagIsHiddenAndItsTellNameIsTagged()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("<Tell:IIDString:1:Bob>Bob<\\Tell> has logged on.", chatType: 0u);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal("Bob has logged on.", line.Text);
        ChatTextSpan name = Assert.Single(line.Spans!, span => span.Tag is not null);
        Assert.Equal("Bob", name.Text);
        Assert.True(name.Tag!.Value.IsTell);
        Assert.Equal("Bob has logged on.", RuntimeChatFeed.Format(log.Snapshot()[0]));
    }

    [Fact]
    public void TheCharactersOwnAndACombatLinesTagsAreHiddenToo()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech(
            "You", "ask <Tell:IIDString:7:Ann>Ann<\\Tell> please", 0u,
            isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
        log.OnCombatLine(
            "You hit <DID:DID:0x06001234>the drudge<\\DID>.",
            (uint)RetailLogTextType.CombatSelf);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal("You say, \"ask Ann please\"", lines[0].Text);
        Assert.Equal("Ann", Assert.Single(lines[0].Spans!, span => span.Tag is not null).Text);
        Assert.Equal("You hit the drudge.", lines[1].Text);
        ChatTextSpan other = Assert.Single(lines[1].Spans!, span => span.Tag is not null);
        Assert.Equal("the drudge", other.Text);
        Assert.False(other.Tag!.Value.IsTell);
    }

    [Fact]
    public void AnUnmatchedBracketWithNoTagOpenIsShownAsWritten()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("3 < 4, and <Tell:IIDString:1:Bob", chatType: 0u);
        log.OnLocalSpeech(
            "You", "x <y", 0u, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal("3 < 4, and <Tell:IIDString:1:Bob", lines[0].Text);
        Assert.Null(lines[0].Spans);
        Assert.Equal("You say, \"x <y\"", lines[1].Text);
        Assert.Null(lines[1].Spans);
    }

    [Fact]
    public void ACoordinateTagInAServerLineIsClosedByTheNextBracketedTextWhateverItIs()
    {
        // The coordinate tag is read as a tag that is never a link, so the
        // next bracketed text only closes it: "<gate>" is hidden, and the tell
        // tag after the caption opens nothing, which leaves its end tag shown.
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("<COORD:World:1N, 2E> meet at <gate>", chatType: 0u);
        log.OnSystemMessage(
            "<COORD:World:1N, 2E>x <Tell:IIDString:5:Bob>Bob<\\Tell>", chatType: 0u);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal(" meet at ", lines[0].Text);
        Assert.Null(lines[0].Spans);
        Assert.Equal("x Bob<\\Tell>", lines[1].Text);
        Assert.Null(lines[1].Spans);
    }

    [Fact]
    public void AServerLinesBracketedTextThatIsNotATagIsShownAsWritten()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("Use <item> on <target>, or 3 < 4.", chatType: 0u);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal("Use <item> on <target>, or 3 < 4.", line.Text);
        Assert.Null(line.Spans);
    }

    [Fact]
    public void APluginLineWithNoTagKeepsItsAngleBrackets()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("a < b > c <none>", chatType: 0u, allowsMarkup: true);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal("a < b > c <none>", line.Text);
        Assert.Null(line.Spans);
    }

    [Fact]
    public void APlayersTagAPluginRepostsEscapedIsShownAsWrittenAndIsNotALink()
    {
        // A plugin echoing a captured tell escapes the player's words; the
        // tag they typed is shown literally and reports nothing hidden.
        const string Typed = "<COORD:World:0N, 0E>the bridge<\\COORD>";
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage(
            "Bob told you: " + AcDream.Plugin.Abstractions.PluginChatMarkup.Escape(Typed),
            chatType: 0u,
            allowsMarkup: true);
        log.OnSystemMessage(
            AcDream.Plugin.Abstractions.PluginChatMarkup.Escape("press <Ctrl:F>"),
            chatType: 0u,
            allowsMarkup: true);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal("Bob told you: " + Typed, lines[0].Text);
        Assert.Equal("Bob told you: " + Typed, RuntimeChatFeed.Format(log.Snapshot()[0]));
        // The caption is not a link. The tag's own text, now shown, holds a
        // plain pair, and that pair links to exactly what it shows.
        Assert.DoesNotContain(lines[0].Spans!, span => span.Tag is not null && span.Text.Contains("bridge"));
        ChatTextSpan shown = CoordinateSpan(lines[0]);
        Assert.Equal("0N, 0E", shown.Text);
        Assert.True(shown.Tag!.Value.TryGetCoordinate(out double eastWest, out double northSouth));
        Assert.Equal((0d, 0d), (eastWest, northSouth));

        Assert.Equal("press <Ctrl:F>", lines[1].Text);
        Assert.Null(lines[1].Spans);
    }

    [Fact]
    public void APluginLinesOtherAngleBracketTextIsShownAsWritten()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnSystemMessage("Press <Ctrl:F> to follow", chatType: 0u, allowsMarkup: true);
        log.OnSystemMessage(
            "Press <Ctrl:F>, then <COORD:World:1N, 2E>go<\\COORD> <a:b:c>",
            chatType: 0u,
            allowsMarkup: true);

        IReadOnlyList<RuntimeChatLine> lines = feed.Snapshot();

        Assert.Equal("Press <Ctrl:F> to follow", lines[0].Text);
        Assert.Null(lines[0].Spans);
        Assert.Equal("Press <Ctrl:F>, then go <a:b:c>", lines[1].Text);
        Assert.Equal("go", CoordinateSpan(lines[1]).Text);
        Assert.Equal(
            "Press <Ctrl:F>, then go <a:b:c>",
            RuntimeChatFeed.Format(log.Snapshot()[1]));
    }

    // -- Pull -------------------------------------------------------------

    [Fact]
    public void ASnapshotCarriesTheFinishedTextAndTheEntrysClassification()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnCombatLine(
            "A Drudge Slinker slashes you for 9 points of damage!",
            (uint)RetailLogTextType.CombatEnemy,
            CombatLineKind.Warning);

        RuntimeChatLine line = Assert.Single(feed.Snapshot());

        Assert.Equal(
            "A Drudge Slinker slashes you for 9 points of damage!", line.Text);
        Assert.Equal(ChatKind.Combat, line.Kind);
        Assert.Equal((uint)RetailLogTextType.CombatEnemy, line.LogTextType);
        Assert.Equal(CombatLineKind.Warning, line.CombatKind);
        Assert.Equal(1L, line.Sequence);
        Assert.Null(line.Spans);
    }

    [Fact]
    public void ASnapshotReturnsTheTailOldestFirst()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        for (int i = 1; i <= 5; i++)
            log.OnSystemMessage($"line {i}", chatType: 0u);

        Assert.Equal(
            ["line 3", "line 4", "line 5"],
            feed.Snapshot(limit: 3).Select(line => line.Text).ToArray());
        Assert.Equal(5, feed.Snapshot(limit: 50).Count);
    }

    [Fact]
    public void ASnapshotOfAnEmptyLogIsEmptyAndTheRevisionTracksTheLog()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);

        Assert.Empty(feed.Snapshot());
        Assert.Equal(log.Revision, feed.Revision);

        log.OnSystemMessage("Welcome to Dereth.", chatType: 0u);
        Assert.Equal(log.Revision, feed.Revision);
    }

    [Fact]
    public void ALimitBelowOneIsRefused()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);

        Assert.Throws<ArgumentOutOfRangeException>(() => feed.Snapshot(limit: 0));
    }

    // -- Push -------------------------------------------------------------

    [Fact]
    public void EveryAppendedLineIsPushedOnceInArrivalOrderWithTheSameText()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        var pushed = new List<RuntimeChatLine>();
        feed.LineAppended += pushed.Add;

        log.OnLocalSpeech("Bob", "hi there", OtherPlayerGuid, false, 2u);
        log.OnSystemMessage("Welcome to Dereth.", chatType: 0u);

        Assert.Equal(
            ["Bob says, \"hi there\"", "Welcome to Dereth."],
            pushed.Select(line => line.Text).ToArray());
        Assert.Equal(
            feed.Snapshot().Select(line => line.Text).ToArray(),
            pushed.Select(line => line.Text).ToArray());
        Assert.Equal(
            feed.Snapshot().Select(line => line.Sequence).ToArray(),
            pushed.Select(line => line.Sequence).ToArray());
    }

    [Fact]
    public void ADisposedFeedStopsPushing()
    {
        var log = new ChatLog();
        var feed = new RuntimeChatFeed(log);
        var pushed = new List<RuntimeChatLine>();
        feed.LineAppended += pushed.Add;

        log.OnSystemMessage("first", chatType: 0u);
        feed.Dispose();
        log.OnSystemMessage("second", chatType: 0u);

        Assert.Equal(["first"], pushed.Select(line => line.Text).ToArray());

        // Disposing twice is harmless.
        feed.Dispose();
    }

    // -- Per-window membership --------------------------------------------

    [Fact]
    public void WithoutWindowFiltersEveryLineBelongsToEveryWindow()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);

        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.Speech));
        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.Allegiance));
    }

    [Fact]
    public void AWindowShowsOnlyTheTextClassesItsFilterIsSetTo()
    {
        var log = new ChatLog();
        var windows = new ChatWindowState();
        using var feed = new RuntimeChatFeed(log, windows);

        // Speech only, nothing else.
        windows.SetFilter(0, 1UL << (int)RetailLogTextType.Speech);
        log.OnLocalSpeech(
            "Bob", "hi there", OtherPlayerGuid, false,
            (uint)RetailLogTextType.Speech);
        log.OnSystemMessage(
            "Your Cooking skill is now trained!",
            (uint)RetailLogTextType.Advancement);

        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.Speech));
        Assert.False(feed.BelongsTo(0, (uint)RetailLogTextType.Advancement));
        Assert.Equal(2, feed.Snapshot().Count);
        Assert.Equal(
            ["Bob says, \"hi there\""],
            feed.SnapshotForWindow(0).Select(line => line.Text).ToArray());
    }

    [Fact]
    public void TheMainWindowsDefaultFilterShowsOrdinarySpeechAndCombat()
    {
        var log = new ChatLog();
        var windows = new ChatWindowState();
        using var feed = new RuntimeChatFeed(log, windows);

        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.Speech));
        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.Tell));
        Assert.True(feed.BelongsTo(0, (uint)RetailLogTextType.CombatSelf));
    }

    // -- Timestamps -------------------------------------------------------

    [Fact]
    public void TimestampsOffLeaveTheLineAsWorded()
    {
        var log = new ChatLog { DisplayTimestampsSource = () => false };
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech("Bob", "hi there", OtherPlayerGuid, false, 2u);

        Assert.Equal("Bob says, \"hi there\"", feed.Snapshot()[0].Text);
    }

    [Fact]
    public void TimestampsOnPutThePrefixInFrontAndInItsOwnSpan()
    {
        var log = new ChatLog { DisplayTimestampsSource = () => true };
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech("Bob", "hi there", OtherPlayerGuid, false, 2u);
        string prefix = ChatLog.FormatTimestampPrefix(log.Snapshot()[0].Received);

        RuntimeChatLine line = feed.Snapshot()[0];
        Assert.Equal(prefix + "Bob says, \"hi there\"", line.Text);
        Assert.Equal(prefix, line.Spans![0].Text);
        Assert.Equal(ChatSpanRole.Timestamp, line.Spans[0].Role);
        Assert.Equal(line.Text, string.Concat(line.Spans.Select(span => span.Text)));
    }

    [Fact]
    public void APushedLineCarriesTheTimestampToo()
    {
        var log = new ChatLog { DisplayTimestampsSource = () => true };
        using var feed = new RuntimeChatFeed(log);
        RuntimeChatLine? pushed = null;
        feed.LineAppended += line => pushed = line;

        log.OnSystemMessage("Welcome to Dereth.", chatType: 0u);
        string prefix = ChatLog.FormatTimestampPrefix(log.Snapshot()[0].Received);

        Assert.Equal(prefix + "Welcome to Dereth.", pushed?.Text);
    }

    // -- The session owner supplies one ------------------------------------

    [Fact]
    public void TheCommunicationOwnerCarriesAFeedWiredToItsLogAndItsWindowFilters()
    {
        using var state = new RuntimeCommunicationState();
        state.ChatWindows.SetFilter(0, 1UL << (int)RetailLogTextType.Speech);
        state.Chat.OnLocalSpeech(
            "Bob", "hi there", OtherPlayerGuid, false,
            (uint)RetailLogTextType.Speech);
        state.Chat.OnSystemMessage(
            "Your Cooking skill is now trained!",
            (uint)RetailLogTextType.Advancement);

        Assert.Equal(
            ["Bob says, \"hi there\""],
            state.ChatFeed.SnapshotForWindow(0).Select(line => line.Text).ToArray());
    }

    // -- The short tag a colourless front end puts in front of a line ------

    [Fact]
    public void ALineThatAlreadyNamesItsChannelIsNotTaggedWithTheChannelAgain()
    {
        // Before this the console printed the label twice:
        // "[fellowship] [Fellowship] Bob says, ...".
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnChannelBroadcast(
            7u, "Bob", "group up",
            (uint)RetailLogTextType.Fellowship, "Fellowship");

        RuntimeChatLine line = feed.Snapshot()[0];

        Assert.Equal(string.Empty, RuntimeChatLineTags.For(line));
        Assert.Equal("[Fellowship] Bob says, \"group up\"", line.Text);
    }

    [Fact]
    public void EveryOtherLineStillCarriesTheTagForItsTextClass()
    {
        var log = new ChatLog();
        using var feed = new RuntimeChatFeed(log);
        log.OnLocalSpeech(
            "Bob", "hi there", OtherPlayerGuid, false,
            (uint)RetailLogTextType.Speech);

        Assert.Equal("[say] ", RuntimeChatLineTags.For(feed.Snapshot()[0]));
    }

    [Fact]
    public void DisposingTheCommunicationOwnerStopsItsFeed()
    {
        var state = new RuntimeCommunicationState();
        var pushed = new List<RuntimeChatLine>();
        state.ChatFeed.LineAppended += pushed.Add;

        state.Chat.OnSystemMessage("first", chatType: 0u);
        state.Dispose();
        state.Chat.OnSystemMessage("second", chatType: 0u);

        Assert.Equal(["first"], pushed.Select(line => line.Text).ToArray());
    }
}
