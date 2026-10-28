using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// Both hosts hand a plugin the theme through the scoped registry: it reads
/// the host's theme, hears each change, and stops hearing once the handler is
/// removed or the plugin goes.
/// </summary>
public sealed class ScopedUiRegistryThemeTests
{
    private static readonly PluginUiThemeInfo Moss = new(
        PluginUiThemeKind.Moss,
        "Charcoal + moss",
        new PluginThemePalette(
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12),
            new(13, 14, 15), new(16, 17, 18), new(19, 20, 21)));

    private sealed class FakeScopedUiRegistry : IScopedUiRegistry
    {
        public PluginUiThemeInfo Theme { get; set; } = PluginUiThemeInfo.Classic;

        public event Action<PluginUiThemeInfo>? ThemeChanged;

        public int HandlerCount => ThemeChanged?.GetInvocationList().Length ?? 0;

        public void Raise(PluginUiThemeInfo theme)
        {
            Theme = theme;
            ThemeChanged?.Invoke(theme);
        }

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;
    }

    private sealed class StubHost(IScopedUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }

    [Fact]
    public void ThePluginReadsTheHostsTheme()
    {
        var inner = new FakeScopedUiRegistry();
        using var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        Assert.Same(PluginUiThemeInfo.Classic, scoped.Ui.Theme);
        inner.Theme = Moss;
        Assert.Same(Moss, scoped.Ui.Theme);
    }

    [Fact]
    public void AHandlerHearsEachChangeUntilItIsRemoved()
    {
        var inner = new FakeScopedUiRegistry();
        using var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        var heard = new List<PluginUiThemeInfo>();
        Action<PluginUiThemeInfo> handler = heard.Add;

        scoped.Ui.ThemeChanged += handler;
        inner.Raise(Moss);
        scoped.Ui.ThemeChanged -= handler;
        inner.Raise(PluginUiThemeInfo.Classic);

        Assert.Equal([Moss], heard);
        Assert.Equal(0, inner.HandlerCount);
    }

    [Fact]
    public void DisposingThePluginTakesItsHandlersOffTheHost()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        int heard = 0;
        scoped.Ui.ThemeChanged += _ => heard++;
        scoped.Ui.ThemeChanged += _ => heard++;
        Assert.Equal(2, inner.HandlerCount);

        scoped.Dispose();
        inner.Raise(Moss);

        Assert.Equal(0, heard);
        Assert.Equal(0, inner.HandlerCount);
    }

    [Fact]
    public void RemovingAHandlerThatWasNeverAddedIsHarmless()
    {
        var inner = new FakeScopedUiRegistry();
        using var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        scoped.Ui.ThemeChanged -= _ => { };

        Assert.Equal(0, inner.HandlerCount);
    }

    [Fact]
    public void OnAHostWithoutThemesThePluginReadsClassic()
    {
        using var scoped = new ScopedPluginHost(
            new StubHost(NoOpUiRegistry.Instance), "example.plugin", "Example");

        scoped.Ui.ThemeChanged += _ => throw new InvalidOperationException("never raised");

        Assert.Same(PluginUiThemeInfo.Classic, scoped.Ui.Theme);
    }
}
