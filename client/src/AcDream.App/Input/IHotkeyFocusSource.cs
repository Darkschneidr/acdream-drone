namespace AcDream.App.Input;

/// <summary>
/// The interface's focus state as plugin hotkeys see it, read at each key
/// press.
/// </summary>
public interface IHotkeyFocusSource
{
    /// <summary>Whether any interface element holds keyboard focus.</summary>
    bool HasKeyboardFocus { get; }

    /// <summary>Whether a modal dialog is open.</summary>
    bool IsModalOpen { get; }
}
