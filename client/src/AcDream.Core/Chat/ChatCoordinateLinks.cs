using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Chat;

/// <summary>
/// Turns the coordinate pairs written in a plugin's chat line ("meet at
/// 42.1N 33.5E") into the same <c>COORD:World</c> links the line's own markup
/// makes, so a click on one reaches plugins as
/// <see cref="PluginChatLinkKind.Coordinate"/>. Only a plugin's own line is
/// linked; what players and the server write is shown as it arrived.
/// </summary>
public static class ChatCoordinateLinks
{
    /// <summary>
    /// Splits every plain body span at the coordinate pairs it holds, each pair
    /// becoming a span of its own tagged as a coordinate. Tagged spans and the
    /// timestamp are kept as they are. A pair is found exactly when
    /// <see cref="PluginChatCoordinateParser.ParseAll"/> reads it.
    /// </summary>
    /// <returns>The new spans, which rejoin to the same text; null when the
    /// line holds no pair.</returns>
    public static IReadOnlyList<ChatTextSpan>? Link(IReadOnlyList<ChatTextSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        List<ChatTextSpan>? result = null;
        for (int i = 0; i < spans.Count; i++)
        {
            ChatTextSpan span = spans[i];
            if (span.Tag is not null || span.Role != ChatSpanRole.Body)
            {
                result?.Add(span);
                continue;
            }

            int runStart = 0;
            foreach ((int index, int length, PluginChatCoordinate coordinate)
                in PluginChatCoordinateParser.Find(span.Text))
            {
                if (result is null)
                {
                    result = new List<ChatTextSpan>(spans.Count + 2);
                    for (int j = 0; j < i; j++)
                        result.Add(spans[j]);
                }
                if (index > runStart)
                    result.Add(span with { Text = span.Text[runStart..index] });
                result.Add(span with
                {
                    Text = span.Text.Substring(index, length),
                    Tag = TagFor(coordinate),
                });
                runStart = index + length;
            }

            if (result is null)
                continue;
            if (runStart < span.Text.Length)
                result.Add(span with { Text = span.Text[runStart..] });
        }
        return result;
    }

    /// <summary>The link a coordinate is carried by, in the chat markup's own form.</summary>
    public static ChatTextTag TagFor(PluginChatCoordinate coordinate) =>
        new(
            "COORD",
            "World",
            $"{Axis(coordinate.NorthSouth, 'N', 'S')}, {Axis(coordinate.EastWest, 'E', 'W')}");

    /// <summary>
    /// How a clicked coordinate is named to plugins: "42.1N, 33.5W", to at most
    /// three decimals, written the same in every locale.
    /// </summary>
    public static string Describe(double eastWest, double northSouth) =>
        $"{Axis(northSouth, 'N', 'S', "0.###")}, {Axis(eastWest, 'E', 'W', "0.###")}";

    private static string Axis(double value, char positive, char negative, string format = "R") =>
        Math.Abs(value).ToString(format, CultureInfo.InvariantCulture)
            + (value < 0d ? negative : positive);
}
