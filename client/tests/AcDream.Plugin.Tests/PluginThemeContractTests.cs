// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The theme is additive with an inert default: a host that never heard of
/// it answers Classic and never raises a change.
/// </summary>
public sealed class PluginThemeContractTests
{
    private sealed class BareRegistry : IUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }
    }

    [Fact]
    public void AHostThatPredatesThemesAnswersClassic()
    {
        IUiRegistry ui = new BareRegistry();

        Assert.Same(PluginUiThemeInfo.Classic, ui.Theme);
        Assert.Equal(PluginUiThemeKind.Classic, ui.Theme.Kind);
        Assert.Equal("Classic", ui.Theme.DisplayName);
        Assert.Null(ui.Theme.Palette);
    }

    [Fact]
    public void AHostThatPredatesThemesAcceptsAndIgnoresHandlers()
    {
        IUiRegistry ui = new BareRegistry();
        Action<PluginUiThemeInfo> handler = _ => { };

        ui.ThemeChanged += handler;
        ui.ThemeChanged -= handler;
    }

    [Fact]
    public void TheNoOpRegistryAnswersClassic() =>
        Assert.Same(PluginUiThemeInfo.Classic, ((IUiRegistry)NoOpUiRegistry.Instance).Theme);

    [Fact]
    public void ThemesWithTheSameColoursAreEqual()
    {
        PluginThemePalette Palette() => new(
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12),
            new(13, 14, 15), new(16, 17, 18), new(19, 20, 21));

        Assert.Equal(
            new PluginUiThemeInfo(PluginUiThemeKind.Moss, "Charcoal + moss", Palette()),
            new PluginUiThemeInfo(PluginUiThemeKind.Moss, "Charcoal + moss", Palette()));
    }
}
