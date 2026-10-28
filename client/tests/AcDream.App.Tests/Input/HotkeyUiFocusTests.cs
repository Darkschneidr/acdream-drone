using AcDream.App.Input;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

// The registry bound the way GameWindow binds it: to the retained
// interface's capture slot, before the interface exists, so the real
// UiRoot's focus and modal decide which hotkeys fire.
public sealed class HotkeyUiFocusTests
{
    [Fact]
    public void AFocusedInterfaceElementLetsOnlyCtrlAndAltChordsFire()
    {
        var harness = new Harness();
        var field = new UiField { Width = 200, Height = 20 };
        harness.Root.AddChild(field);
        int plainFired = 0;
        int ctrlFired = 0;
        harness.Registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => plainFired++);
        harness.Registry.Register(
            "quick-heal", "Quick Heal", new PluginKeyChord(PluginKey.H, Ctrl: true), () => ctrlFired++);

        harness.Root.SetKeyboardFocus(field);
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(0, plainFired);
        Assert.Equal(1, ctrlFired);

        harness.Root.SetKeyboardFocus(null);
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        Assert.Equal(1, plainFired);
    }

    [Fact]
    public void AnOpenModalLetsNoHotkeyFire()
    {
        var harness = new Harness();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);
        int fired = 0;
        harness.Registry.Register(
            "quick-heal", "Quick Heal", new PluginKeyChord(PluginKey.H, Ctrl: true), () => fired++);

        harness.Root.Modal = dialog;
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(0, fired);

        harness.Root.Modal = null;
        harness.Keyboard.Fire(Key.H, ModifierMask.Ctrl);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void HotkeysFireAgainOnceTheFocusedInterfaceIsTornDown()
    {
        var harness = new Harness();
        var field = new UiField { Width = 200, Height = 20 };
        harness.Root.AddChild(field);
        int fired = 0;
        harness.Registry.Register(
            "plain", "Plain", new PluginKeyChord(PluginKey.F9), () => fired++);

        harness.Root.SetKeyboardFocus(field);
        harness.UiBinding.Dispose();
        harness.Keyboard.Fire(Key.F9, ModifierMask.None);
        Assert.Equal(1, fired);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var bindings = new KeyBindings();
            InputDispatcher dispatcher = InputDispatcher.CreateDetached(
                Keyboard, new FakeMouse(), bindings);
            var slot = new RetainedUiInputCaptureSlot();
            Registry.Bind(Keyboard, bindings, dispatcher, slot);
            UiBinding = slot.Bind(Root);
        }

        public AppHotkeyRegistry Registry { get; } = new(overridesFilePath: null);
        public FakeKeyboard Keyboard { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800, Height = 600 };
        public IDisposable UiBinding { get; }
    }

    private sealed class FakeKeyboard : IKeyboardSource
    {
        public event Action<Key, ModifierMask>? KeyDown;
#pragma warning disable CS0067
        public event Action<Key, ModifierMask>? KeyUp;
#pragma warning restore CS0067
        public bool IsHeld(Key key) => false;
        public ModifierMask CurrentModifiers => ModifierMask.None;

        public void Fire(Key key, ModifierMask modifiers) =>
            KeyDown?.Invoke(key, modifiers);
    }

    private sealed class FakeMouse : IMouseSource
    {
#pragma warning disable CS0067
        public event Action<MouseButton, ModifierMask>? MouseDown;
        public event Action<MouseButton, ModifierMask>? MouseUp;
        public event Action<float, float>? MouseMove;
        public event Action<float>? Scroll;
#pragma warning restore CS0067
        public bool WantCaptureKeyboard { get; set; }
        public bool WantCaptureMouse { get; set; }
        public bool IsHeld(MouseButton button) => false;
    }
}
