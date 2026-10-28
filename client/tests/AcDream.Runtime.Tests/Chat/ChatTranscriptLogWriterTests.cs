using System;
using System.IO;
using AcDream.Runtime.Chat;
using AcDream.Core.Chat;

namespace AcDream.App.Tests.UI;

/// <summary>
/// CT-B4: what actually reaches the <c>@log</c> file.
/// </summary>
public sealed class ChatTranscriptLogWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "acdream-logwriter-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* nothing left to say */ }
    }

    private string Read() => File.ReadAllText(Path.Combine(_directory, "session.txt"));

    [Fact]
    public void TheComposedDisplayLineIsLoggedRatherThanTheRawMessage()
    {
        using var log = new ChatSessionLog(_directory);
        var transcript = new ChatLog();
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        writer.Attach(transcript);
        transcript.OnLocalSpeech("Dww", "hello", 0x02u, false, 0u);
        log.Close();

        Assert.Equal("Dww says, \"hello\"\n", Read());
    }

    [Fact]
    public void TheLineIsLoggedAsComposedWithItsTagsAsTheyArrived()
    {
        using var log = new ChatSessionLog(_directory);
        var transcript = new ChatLog();
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        writer.Attach(transcript);
        transcript.OnLocalSpeech("Bob", "hello", 0x5000000Au, false, 0x02u);
        transcript.OnSystemMessage("<Tell:IIDString:1:Ann>Ann<\\Tell> has logged on.", chatType: 0u);
        transcript.OnSystemMessage("<COORD:World:1N, 2E>here<\\COORD>", chatType: 0u, allowsMarkup: true);
        log.Close();

        Assert.Equal(
            "<Tell:IIDString:1342177290:Bob>Bob<\\Tell> says, \"hello\"\n"
            + "<Tell:IIDString:1:Ann>Ann<\\Tell> has logged on.\n"
            + "here\n",
            Read());
    }

    [Fact]
    public void TheTimestampFollowsTheSameOptionTheWindowUses()
    {
        using var log = new ChatSessionLog(_directory);
        bool stamps = false;
        var transcript = new ChatLog { DisplayTimestampsSource = () => stamps };
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        writer.Attach(transcript);
        transcript.OnLocalSpeech("Dww", "before", 0x02u, false, 0u);
        stamps = true;
        transcript.OnLocalSpeech("Dww", "after", 0x02u, false, 0u);
        log.Close();

        string[] lines = Read().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Dww says, \"before\"", lines[0]);
        Assert.Matches(@"^\d{1,2}:\d{2}:\d{2} Dww says, ""after""$", lines[1]);
    }

    [Fact]
    public void NothingIsLoggedBeforeAttachOrAfterDetach()
    {
        using var log = new ChatSessionLog(_directory);
        var transcript = new ChatLog();
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        transcript.OnLocalSpeech("Dww", "before attach", 0x02u, false, 0u);
        writer.Attach(transcript);
        transcript.OnLocalSpeech("Dww", "during", 0x02u, false, 0u);
        writer.Detach();
        transcript.OnLocalSpeech("Dww", "after detach", 0x02u, false, 0u);
        log.Close();

        Assert.Equal("Dww says, \"during\"\n", Read());
    }

    [Fact]
    public void AttachingTwiceDoesNotWriteEveryLineTwice()
    {
        using var log = new ChatSessionLog(_directory);
        var transcript = new ChatLog();
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        writer.Attach(transcript);
        writer.Attach(transcript);
        transcript.OnLocalSpeech("Dww", "once", 0x02u, false, 0u);
        log.Close();

        Assert.Equal("Dww says, \"once\"\n", Read());
    }

    [Fact]
    public void DetachReleasesTheTranscriptItActuallyAttachedTo()
    {
        using var log = new ChatSessionLog(_directory);
        var first = new ChatLog();
        var second = new ChatLog();
        var writer = new ChatTranscriptLogWriter(log);

        log.Open("session", out _);
        writer.Attach(first);
        writer.Attach(second);          // switches transcripts
        first.OnLocalSpeech("Dww", "stale", 0x02u, false, 0u);
        second.OnLocalSpeech("Dww", "live", 0x02u, false, 0u);
        log.Close();

        Assert.Equal("Dww says, \"live\"\n", Read());
    }
}
