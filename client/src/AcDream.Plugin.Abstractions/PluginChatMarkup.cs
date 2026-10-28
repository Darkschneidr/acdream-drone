namespace AcDream.Plugin.Abstractions;

/// <summary>
/// Helpers for the markup a plugin's own chat line may carry
/// (<see cref="IPluginChat.PostSystemMessage"/>, <see cref="IPluginChat.PostMessage"/>).
/// </summary>
public static class PluginChatMarkup
{
    /// <summary>
    /// Escapes text so that it shows exactly as written in a line a plugin
    /// posts, with no tag in it read as a link. Use it on every piece of text
    /// the plugin did not write itself, such as a player's tell it repeats:
    /// otherwise a <c>&lt;COORD:World:…&gt;</c> tag that player typed becomes
    /// a working link under the plugin's name, showing one place and reporting
    /// another. A coordinate pair the escaped text shows in plain words is
    /// still a link to exactly the place it shows.
    /// </summary>
    /// <param name="text">The text to escape; null gives an empty string.</param>
    /// <returns>
    /// The text with a backslash put before every <c>&lt;</c>. The chat drops
    /// that backslash when it shows the line, so the reader sees the original
    /// text.
    /// </returns>
    public static string Escape(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "\\<", StringComparison.Ordinal);
}
