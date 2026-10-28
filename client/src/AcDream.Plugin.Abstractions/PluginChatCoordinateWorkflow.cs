using System.Globalization;
using System.Text.RegularExpressions;

namespace AcDream.Plugin.Abstractions;

/// <summary>Parses compass coordinate pairs from ordinary chat text.</summary>
public static partial class PluginChatCoordinateParser
{
    // A number and a compass letter, twice. A number may not continue a word
    // or another number and a letter may not start a word, so "10n 20east"
    // and "v2.1N 3E" are not pairs.
    [GeneratedRegex(@"(?<![\w.])(?<a>\d+(?:\.\d+)?)\s*(?<ad>[NSEW])(?!\w)\s*(?:[,;/|]|\s+)\s*(?<b>\d+(?:\.\d+)?)\s*(?<bd>[NSEW])(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    /// <summary>Finds every unambiguous coordinate pair in a message.</summary>
    /// <param name="text">The chat text to search.</param>
    /// <returns>
    /// Each pair found, in the order it appears; empty for none. A pair is one
    /// north-or-south value and one east-or-west value, in either order, such
    /// as "42.1N 33.5E" or "33.5E, 42.1N". South and west come back negative.
    /// Neither half may sit inside a longer word or number: "10n 20east" and
    /// "v2.1N 3E" hold no pair.
    /// </returns>
    public static IReadOnlyList<PluginChatCoordinate> ParseAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<PluginChatCoordinate>();
        var result = new List<PluginChatCoordinate>();
        foreach ((_, _, PluginChatCoordinate coordinate) in Find(text))
            result.Add(coordinate);
        return result;
    }

    /// <summary>
    /// Every pair <see cref="ParseAll"/> reads, with where it stands in the
    /// text, so the client's chat links exactly the pairs a plugin would read.
    /// </summary>
    internal static IEnumerable<(int Index, int Length, PluginChatCoordinate Coordinate)> Find(string text)
    {
        foreach (Match match in PairPattern().Matches(text))
        {
            if (!double.TryParse(match.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                || !double.TryParse(match.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)) continue;
            char ad = char.ToUpperInvariant(match.Groups["ad"].Value[0]);
            char bd = char.ToUpperInvariant(match.Groups["bd"].Value[0]);
            if (ad is not ('N' or 'S') || bd is not ('E' or 'W'))
            {
                if (ad is not ('E' or 'W') || bd is not ('N' or 'S')) continue;
                (a, b) = (b, a); (ad, bd) = (bd, ad);
            }
            yield return (match.Index, match.Length, new PluginChatCoordinate(bd == 'W' ? -b : b, ad == 'S' ? -a : a));
        }
    }

    /// <summary>Parses exactly one coordinate pair and rejects ambiguous text.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="coordinate">The pair, when the text holds exactly one.</param>
    /// <returns>True when the text holds exactly one pair; false for none or for more than one.</returns>
    public static bool TryParse(string? text, out PluginChatCoordinate coordinate)
    {
        IReadOnlyList<PluginChatCoordinate> values = ParseAll(text);
        coordinate = values.Count == 1 ? values[0] : default;
        return values.Count == 1;
    }
}

/// <summary>Routes coordinate chat clicks to a plugin-owned destination callback.</summary>
public sealed class PluginChatCoordinateLinkRouter : IDisposable
{
    private readonly IPluginChat _chat;
    private readonly Action<PluginChatCoordinate> _destination;
    private bool _disposed;

    /// <summary>Starts routing coordinate clicks from a chat surface.</summary>
    /// <param name="chat">The chat surface whose link clicks are watched.</param>
    /// <param name="destination">Called with the coordinate each time the player clicks one.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public PluginChatCoordinateLinkRouter(IPluginChat chat, Action<PluginChatCoordinate> destination)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _chat.LinkClicked += OnLinkClicked;
    }
    private void OnLinkClicked(PluginChatLinkClicked link)
    {
        if (!_disposed && link.Kind == PluginChatLinkKind.Coordinate && link.Coordinate is { } coordinate)
            _destination(coordinate);
    }

    /// <summary>Stops routing. Clicks after this reach nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _chat.LinkClicked -= OnLinkClicked;
    }
}
