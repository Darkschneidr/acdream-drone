using System;
using AcDream.Core.Chat;

namespace AcDream.Runtime.Chat;

public sealed class ChatTranscriptLogWriter
{
    private readonly ChatSessionLog _log;
    private ChatLog? _source;

    public ChatTranscriptLogWriter(ChatSessionLog log)
        => _log = log ?? throw new ArgumentNullException(nameof(log));

    public void Attach(ChatLog source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Detach();
        _source = source;
        source.EntryAppended += Write;
    }

    public void Detach()
    {
        if (_source is null)
            return;

        _source.EntryAppended -= Write;
        _source = null;
    }

    private void Write(ChatEntry entry)
    {
        ChatLog? source = _source;
        if (source is null)
            return;

        bool stamped = source.DisplayTimestampsSource?.Invoke() == true;

        // The file gets the line as composed, before the chat window reads its
        // tags: a speaker's name is written inside its tell tag, and any tag
        // in the words is written as it arrived. A plugin's own link markup is
        // ours rather than the chat window's; its line is written as shown.
        _log.Write(
            stamped ? ChatLog.FormatTimestampPrefix(entry.Received) : null,
            entry.AllowsMarkup ? RuntimeChatFeed.Format(entry) : RuntimeChatFeed.FormatTagged(entry));
    }
}
