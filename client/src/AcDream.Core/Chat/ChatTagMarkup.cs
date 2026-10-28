namespace AcDream.Core.Chat;

public readonly record struct ChatTextTag(string Type, string Format, string Data)
{
    public bool TryGetCoordinate(out double eastWest, out double northSouth)
    {
        eastWest = 0d;
        northSouth = 0d;
        if (!string.Equals(Type, "COORD", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Format, "World", StringComparison.OrdinalIgnoreCase))
            return false;
        return TryParseCoordinatePair(Data, out eastWest, out northSouth);
    }

    public static bool TryParseCoordinatePair(string? text, out double eastWest, out double northSouth)
    {
        eastWest = 0d;
        northSouth = 0d;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && TryParseCoordinate(parts[0], 'N', 'S', out northSouth)
            && TryParseCoordinate(parts[1], 'E', 'W', out eastWest);
    }

    private static bool TryParseCoordinate(string text, char positive, char negative, out double value)
    {
        value = 0d;
        if (text.Length < 2)
            return false;
        char suffix = char.ToUpperInvariant(text[^1]);
        if (suffix != positive && suffix != negative)
            return false;
        if (!double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double magnitude)
            || magnitude < 0d)
            return false;
        value = suffix == negative ? -magnitude : magnitude;
        return true;
    }

    /// <summary>
    /// Whether this is a tell tag, the kind that marks a speaker's name. It is
    /// the only kind drawn in the tag colour, and the only kind a click acts on.
    /// </summary>
    public bool IsTell => string.Equals(Type, ChatTagMarkup.TellTypeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The object id and name of an <c>IIDString</c> tag: an id, a colon and a
    /// name, neither empty. The name runs to the end of the data, colons and all.
    /// </summary>
    public bool TryGetIidString(out uint objectId, out string name)
    {
        objectId = 0;
        name = string.Empty;
        if (!string.Equals(Format, "IIDString", StringComparison.OrdinalIgnoreCase))
            return false;

        int split = Data.IndexOf(':');
        if (split <= 0 || split == Data.Length - 1)
            return false;
        if (!ChatTagMarkup.TryParseTagNumber(Data[..split], out objectId))
            return false;

        name = Data[(split + 1)..];
        return true;
    }
}

public enum ChatSpanRole
{
    Body,

    Timestamp,
}

public readonly record struct ChatTextSpan(
    string Text,
    ChatTextTag? Tag,
    ChatSpanRole Role = ChatSpanRole.Body);

public static class ChatTagMarkup
{
    /// <summary>
    /// Whether a plugin's line holds at least one link tag
    /// <see cref="ParseLinks"/> reads: a well-formed
    /// <c>&lt;COORD:World:42.1N, 33.5E&gt;</c>. Any other angle-bracket text,
    /// such as "Press &lt;Ctrl:F&gt;", and an escaped tag are not tags.
    /// </summary>
    public static bool ContainsTag(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.IndexOf('<') >= 0
        && ParseLinks(text).Any(static span => span.Tag is not null);

    /// <summary>
    /// Reads the link tags in a line a plugin posted. Only a well-formed
    /// <c>&lt;COORD:World:pair&gt;</c> opens a link, and only its own
    /// <c>&lt;\COORD&gt;</c> closes it; every other angle-bracket text is shown
    /// as written. A link left open runs to the end of the line. A backslash
    /// directly before <c>&lt;</c> is dropped and that <c>&lt;</c> is shown as
    /// written, never read as a tag, which is how
    /// <see cref="AcDream.Plugin.Abstractions.PluginChatMarkup.Escape"/> makes
    /// text show literally.
    /// </summary>
    public static IReadOnlyList<ChatTextSpan> ParseLinks(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSpan>();

        var spans = new List<ChatTextSpan>();
        var run = new System.Text.StringBuilder(text.Length);
        ChatTextTag? open = null;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '<')
            {
                run.Append('<');   // escaped: shown, never a tag
                i += 2;
                continue;
            }

            if (c == '<')
            {
                int close = text.IndexOf('>', i + 1);
                if (close >= 0)
                {
                    string inner = text[(i + 1)..close];
                    bool markup = true;
                    ChatTextTag? next = null;
                    if (TryParseLinkTag(inner) is { } start)
                        next = start;
                    else if (open is not { } current || !IsEndTag(inner, current))
                        markup = false;   // not markup: shown as written

                    if (markup)
                    {
                        if (run.Length > 0)
                            spans.Add(new ChatTextSpan(run.ToString(), open));
                        run.Clear();
                        open = next;
                        i = close + 1;
                        continue;
                    }
                }
            }

            run.Append(c);
            i++;
        }

        if (run.Length > 0)
            spans.Add(new ChatTextSpan(run.ToString(), open));

        return spans;
    }

    /// <summary>A plugin's line with its link tags removed: what <see cref="ParseLinks"/> shows.</summary>
    public static string ToPlainText(string? text) =>
        string.Concat(ParseLinks(text).Select(static span => span.Text));

    /// <summary>
    /// The spans with every coordinate tag dropped and its caption kept as
    /// plain text. What a player or the server writes may hold a coordinate
    /// tag; it is shown in the line's colour and is not a link.
    /// </summary>
    public static IReadOnlyList<ChatTextSpan> WithoutCoordinateTags(IReadOnlyList<ChatTextSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ChatTextSpan[]? result = null;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Tag is not { } tag || !IsCoordinateType(tag.Type))
                continue;
            result ??= spans.ToArray();
            result[i] = spans[i] with { Tag = null };
        }
        return result ?? spans;
    }

    private static bool IsCoordinateType(string type) =>
        string.Equals(type, "COORD", StringComparison.OrdinalIgnoreCase);

    private static ChatTextTag? TryParseLinkTag(string inner) =>
        TryParseStartTag(inner) is { } tag
            && IsCoordinateType(tag.Type)
            && tag.TryGetCoordinate(out _, out _)
            ? tag
            : null;

    private static bool IsEndTag(string inner, ChatTextTag open) =>
        inner.Length == open.Type.Length + 1
        && inner[0] == '\\'
        && string.Equals(inner[1..], open.Type, StringComparison.OrdinalIgnoreCase);

    /// <summary>The type name of a tell tag, the tag around a speaker's name.</summary>
    public const string TellTypeName = "Tell";

    // The names a tag's type and format may take, and the number each stands
    // for, as the game data lists them. Case does not matter. A format is one
    // of the four payload shapes, 1 to 4.
    private static readonly Dictionary<string, uint> TagNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Invalid"] = 0u,
            ["DID"] = 1u,
            ["IID"] = 2u,
            ["IIDEnum"] = 3u,
            ["IIDString"] = 4u,
            [TellTypeName] = 0x10000001u,
        };

    /// <summary>
    /// Reads the tags in a chat line, character by character, the way the
    /// chat window's text does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>&lt;</c> is matched with the first <c>&gt;</c> after it. With no
    /// tag open, the bracketed text opens one when it is a well-formed
    /// <c>&lt;TYPE:FORMAT:DATA&gt;</c> start tag, and is then not shown;
    /// any other bracketed text is shown as written. With a tag open, any
    /// bracketed text at all ends it and is not shown, even text that is
    /// itself a start tag (that one does not open). A <c>&lt;</c> with no
    /// <c>&gt;</c> after it is shown.
    /// </para>
    /// <para>
    /// The character straight after a hidden tag is always shown, even when
    /// it is a <c>&lt;</c>: the reading resumes one character later.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChatTextSpan> Parse(string? text)
        => Read(text, readCoordinateTags: false);

    /// <summary>
    /// A chat line that is not a plugin's own, as it is shown: its tags read
    /// by <see cref="Parse"/>, and a coordinate tag someone wrote in it kept
    /// as plain text. That tag is hidden and its caption shown, but it is not
    /// a link.
    /// </summary>
    public static IReadOnlyList<ChatTextSpan> ParseLine(string? text)
        => WithoutCoordinateTags(Read(text, readCoordinateTags: true));

    /// <summary>The text <see cref="ParseLine"/> shows: the line with its tags hidden.</summary>
    public static string ToShownText(string? text)
        => string.Concat(ParseLine(text).Select(static span => span.Text));

    private static IReadOnlyList<ChatTextSpan> Read(string? text, bool readCoordinateTags)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSpan>();
        if (text.IndexOf('<') < 0)
            return new[] { new ChatTextSpan(text, null) };

        var spans = new List<ChatTextSpan>();
        var run = new System.Text.StringBuilder(text.Length);
        ChatTextTag? open = null;
        int i = 0;

        // The first ">" at or after the reading position, or -1 when none is
        // left. Kept across brackets so a line full of "<" is read in one pass.
        int nextClose = text.IndexOf('>');

        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                if (nextClose >= 0 && nextClose < i)
                    nextClose = text.IndexOf('>', i);
                int close = nextClose;
                if (close >= 0)
                {
                    ChatTextTag? next = null;
                    bool hidden = open is not null;   // an open tag ends at any bracketed text
                    if (!hidden && MayBeStartTag(text, i + 1, close, readCoordinateTags))
                    {
                        string inner = text[(i + 1)..close];
                        next = ReadStartTag(inner)
                            ?? (readCoordinateTags ? TryParseLinkTag(inner) : null);
                        hidden = next is not null;
                    }

                    if (hidden)
                    {
                        if (run.Length > 0)
                            spans.Add(new ChatTextSpan(run.ToString(), open));
                        run.Clear();
                        open = next;
                        i = close + 1;
                        if (i < text.Length)
                            run.Append(text[i++]);   // shown whatever it is
                        continue;
                    }
                }
            }

            run.Append(text[i++]);
        }

        if (run.Length > 0)
            spans.Add(new ChatTextSpan(run.ToString(), open));

        return spans;
    }

    // The longest name in the tag table ("IIDString").
    private const int LongestTagName = 9;

    /// <summary>
    /// Whether the bracketed text <c>text[start..end)</c> begins with a type a
    /// tag can have: a name in the tag table (or COORD, when coordinate tags
    /// are read) followed by a colon. Only such text is worth copying out and
    /// reading; every other bracket is decided without looking further.
    /// </summary>
    private static bool MayBeStartTag(string text, int start, int end, bool readCoordinateTags)
    {
        int window = Math.Min(end - start, LongestTagName + 1);
        int colon = text.IndexOf(':', start, window);
        if (colon < 0)
            return false;
        ReadOnlySpan<char> type = text.AsSpan(start, colon - start);
        foreach (string name in TagNames.Keys)
        {
            if (type.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return readCoordinateTags && type.Equals("COORD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tag a bracketed <c>TYPE:FORMAT:DATA</c> stands for, or null when it
    /// is not one. TYPE and FORMAT must be names in the tag table, FORMAT one
    /// of the four payload shapes, and DATA must fit that shape.
    /// </summary>
    private static ChatTextTag? ReadStartTag(string inner)
    {
        int first = inner.IndexOf(':');
        if (first < 0)
            return null;
        string type = inner[..first];
        if (!TagNames.ContainsKey(type))
            return null;

        int second = inner.IndexOf(':', first + 1);
        if (second < 0)
            return null;
        string format = inner[(first + 1)..second];
        if (!TagNames.TryGetValue(format, out uint shape) || shape - 1u > 3u)
            return null;

        string data = inner[(second + 1)..];
        if (data.Length == 0)
            return null;

        bool fits = shape switch
        {
            1u or 2u => TryParseTagNumber(data, out _),        // DID, IID: one number
            3u => IdAndName(data, out string name)             // IIDEnum: number:a name in the table
                && TagNames.ContainsKey(name),
            _ => IdAndName(data, out _),                       // IIDString: number:any name
        };
        return fits ? new ChatTextTag(type, format, data) : null;

        static bool IdAndName(string data, out string name)
        {
            name = string.Empty;
            int split = data.IndexOf(':');
            if (split <= 0 || split == data.Length - 1 || !TryParseTagNumber(data[..split], out _))
                return false;
            name = data[(split + 1)..];
            return true;
        }
    }

    /// <summary>
    /// Reads a tag's number the way the C library reads an unsigned long in
    /// base 0, and accepts it only when that reading uses the whole text and
    /// does not overflow: optional leading white space, an optional sign (a
    /// minus wraps), then decimal, <c>0x</c> hexadecimal or <c>0</c> octal
    /// digits.
    /// </summary>
    internal static bool TryParseTagNumber(string text, out uint value)
    {
        value = 0;
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r')
            i++;

        bool negative = false;
        if (i < text.Length && text[i] is '+' or '-')
            negative = text[i++] == '-';

        uint radix = 10;
        if (i < text.Length && text[i] == '0')
        {
            if (i + 1 < text.Length && text[i + 1] is 'x' or 'X')
            {
                radix = 16;
                i += 2;
            }
            else
            {
                radix = 8;
            }
        }

        int firstDigit = i;
        ulong magnitude = 0;
        for (; i < text.Length; i++)
        {
            char c = text[i];
            uint digit = c switch
            {
                >= '0' and <= '9' => (uint)(c - '0'),
                >= 'a' and <= 'z' => (uint)(c - 'a' + 10),
                >= 'A' and <= 'Z' => (uint)(c - 'A' + 10),
                _ => uint.MaxValue,
            };
            if (digit >= radix)
                return false;   // the reading stops short of the end
            magnitude = magnitude * radix + digit;
            if (magnitude > uint.MaxValue)
                return false;   // out of range
        }

        if (i == firstDigit)
            return false;       // no digits at all

        value = negative ? unchecked(0u - (uint)magnitude) : (uint)magnitude;
        return true;
    }

    private static ChatTextTag? TryParseStartTag(string inner)
    {
        int first = inner.IndexOf(':');
        if (first <= 0 || first == inner.Length - 1)
            return null;

        int second = inner.IndexOf(':', first + 1);
        if (second < 0)
        {
            // TYPE:FORMAT with no payload. Still a tag — the data is empty.
            return new ChatTextTag(inner[..first], inner[(first + 1)..], string.Empty);
        }

        return new ChatTextTag(
            inner[..first],
            inner[(first + 1)..second],
            inner[(second + 1)..]);
    }
}
