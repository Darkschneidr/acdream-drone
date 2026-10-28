using System.Reflection;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class SilkRetainedMouseScrollTests
{
    [Theory]
    [InlineData(-0.01f)]
    [InlineData(-0.5f)]
    [InlineData(-1f)]
    [InlineData(-3f)]
    public void DownwardWheelEvent_revealsEighthSlotThroughMouseSurface(float delta)
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var list = new UiItemList { Width = 36, Height = 252, CellWidth = 36, CellHeight = 36 };
        list.Flush();
        for (int i = 0; i < 8; i++) list.AddItem(new UiItemSlot());
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(list);
        root.OnMouseMove(18, 234);
        var surface = new SilkRetainedMouseSurface(mouse);
        Action<int> onScroll = root.OnScroll;
        surface.AddScroll(onScroll);

        ((WheelMouseProxy)mouse).Raise(mouse, delta);
        list.LayoutCells();

        Assert.Equal(36, list.Scroll.ScrollY);
        Assert.True(list.GetItem(7)!.Visible);
        surface.RemoveScroll(onScroll);
    }

    [Theory]
    [InlineData(0.01f, 1)]
    [InlineData(0.5f, 1)]
    [InlineData(1f, 1)]
    [InlineData(3f, 1)]
    [InlineData(-0.01f, -1)]
    [InlineData(-3f, -1)]
    public void WheelEvent_emitsOneDirectionalStep(float delta, int expected)
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var surface = new SilkRetainedMouseSurface(mouse);
        var steps = new List<int>();
        surface.AddScroll(steps.Add);
        ((WheelMouseProxy)mouse).Raise(mouse, delta);
        Assert.Equal(new[] { expected }, steps);
    }

    [Fact]
    public void ZeroVerticalDelta_emitsNoVerticalStep()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var surface = new SilkRetainedMouseSurface(mouse);
        var steps = new List<int>();
        surface.AddScroll(steps.Add);
        ((WheelMouseProxy)mouse).Raise(mouse, 0f);
        Assert.Empty(steps);
    }

    public class WheelMouseProxy : DispatchProxy
    {
        private Action<IMouse, ScrollWheel>? _scroll;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "add_Scroll": _scroll += (Action<IMouse, ScrollWheel>)args![0]!; return null;
                case "remove_Scroll": _scroll -= (Action<IMouse, ScrollWheel>)args![0]!; return null;
                default: throw new NotSupportedException(method?.Name);
            }
        }

        public void Raise(IMouse mouse, float delta) => _scroll?.Invoke(mouse, new ScrollWheel(0, delta));
    }
}
