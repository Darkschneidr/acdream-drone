using AcDream.App.Input;
using AcDream.App.UI;

namespace AcDream.App.Tests.Input;

public sealed class RetainedUiInputCaptureSlotTests
{
    [Fact]
    public void FocusStateFollowsTheBoundRootsKeyboardFocus()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var field = new UiField { Width = 200, Height = 20 };
        root.AddChild(field);
        using IDisposable binding = slot.Bind(root);

        Assert.False(slot.HasKeyboardFocus);
        root.SetKeyboardFocus(field);
        Assert.True(slot.HasKeyboardFocus);
        root.SetKeyboardFocus(null);
        Assert.False(slot.HasKeyboardFocus);
    }

    [Fact]
    public void FocusStateFollowsTheBoundRootsModal()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var dialog = new UiPanel { Width = 300, Height = 150 };
        root.AddChild(dialog);
        using IDisposable binding = slot.Bind(root);

        Assert.False(slot.IsModalOpen);
        root.Modal = dialog;
        Assert.True(slot.IsModalOpen);
        root.Modal = null;
        Assert.False(slot.IsModalOpen);
    }

    [Fact]
    public void AnUnboundSlotReportsNoFocusAndNoModal()
    {
        var slot = new RetainedUiInputCaptureSlot();
        var root = new UiRoot { Width = 800, Height = 600 };
        var field = new UiField { Width = 200, Height = 20 };
        root.AddChild(field);
        root.SetKeyboardFocus(field);
        root.Modal = new UiPanel { Width = 300, Height = 150 };

        Assert.False(slot.HasKeyboardFocus);
        Assert.False(slot.IsModalOpen);

        // An interface rebuild disposes the old root's binding first.
        slot.Bind(root).Dispose();
        Assert.False(slot.HasKeyboardFocus);
        Assert.False(slot.IsModalOpen);
    }
}
