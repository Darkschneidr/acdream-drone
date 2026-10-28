using System.Numerics;
using System.Reflection;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class SilkRetainedMousePressTests
{
    [Fact]
    public void Press_landsWhereTheLastMoveEventLeftThePointer_notWhereItIsNow()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, ButtonMouseProxy>();
        var proxy = (ButtonMouseProxy)mouse;
        var surface = new SilkRetainedMouseSurface(mouse);
        var presses = new List<(int, int)>();
        surface.AddMouseMove((_, _) => { });
        surface.AddMouseDown((_, x, y) => presses.Add((x, y)));

        proxy.RaiseMove(mouse, new Vector2(100, 200));
        // The pointer has moved on by the time the press is handled; the move
        // that says so is still queued behind it.
        proxy.LivePosition = new Vector2(140, 200);
        proxy.RaiseDown(mouse, MouseButton.Left);

        Assert.Equal(new[] { (100, 200) }, presses);
    }

    [Fact]
    public void Release_landsWhereTheLastMoveEventLeftThePointer()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, ButtonMouseProxy>();
        var proxy = (ButtonMouseProxy)mouse;
        var surface = new SilkRetainedMouseSurface(mouse);
        var releases = new List<(int, int)>();
        surface.AddMouseMove((_, _) => { });
        surface.AddMouseUp((_, x, y) => releases.Add((x, y)));

        proxy.RaiseMove(mouse, new Vector2(300, 50));
        proxy.LivePosition = new Vector2(300, 90);
        proxy.RaiseUp(mouse, MouseButton.Left);

        Assert.Equal(new[] { (300, 50) }, releases);
    }

    [Fact]
    public void PressBeforeAnyMove_usesTheCurrentPointer()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, ButtonMouseProxy>();
        var proxy = (ButtonMouseProxy)mouse;
        var surface = new SilkRetainedMouseSurface(mouse);
        var presses = new List<(int, int)>();
        surface.AddMouseMove((_, _) => { });
        surface.AddMouseDown((_, x, y) => presses.Add((x, y)));

        proxy.LivePosition = new Vector2(12, 34);
        proxy.RaiseDown(mouse, MouseButton.Left);

        Assert.Equal(new[] { (12, 34) }, presses);
    }

    public class ButtonMouseProxy : DispatchProxy
    {
        private Action<IMouse, MouseButton>? _down;
        private Action<IMouse, MouseButton>? _up;
        private Action<IMouse, Vector2>? _move;

        public Vector2 LivePosition { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "add_MouseDown": _down += (Action<IMouse, MouseButton>)args![0]!; return null;
                case "remove_MouseDown": _down -= (Action<IMouse, MouseButton>)args![0]!; return null;
                case "add_MouseUp": _up += (Action<IMouse, MouseButton>)args![0]!; return null;
                case "remove_MouseUp": _up -= (Action<IMouse, MouseButton>)args![0]!; return null;
                case "add_MouseMove": _move += (Action<IMouse, Vector2>)args![0]!; return null;
                case "remove_MouseMove": _move -= (Action<IMouse, Vector2>)args![0]!; return null;
                case "get_Position": return LivePosition;
                default: throw new NotSupportedException(method?.Name);
            }
        }

        public void RaiseMove(IMouse mouse, Vector2 position)
        {
            LivePosition = position;
            _move?.Invoke(mouse, position);
        }

        public void RaiseDown(IMouse mouse, MouseButton button) => _down?.Invoke(mouse, button);

        public void RaiseUp(IMouse mouse, MouseButton button) => _up?.Invoke(mouse, button);
    }
}
