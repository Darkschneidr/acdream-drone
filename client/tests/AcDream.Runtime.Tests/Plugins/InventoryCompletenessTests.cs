using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Tests.Support;

namespace AcDream.Runtime.Tests.Plugins;

public sealed class InventoryCompletenessTests
{
    [Fact]
    public void PluginHostSeesNestedInventoryReadinessAndDisconnect()
    {
        using var runtimeHost = new NoWindowGameRuntimeHost();
        runtimeHost.Start();
        for (int tick = 0; tick < 4; tick++) runtimeHost.Session.Tick();
        using var surface = new RuntimeAutomationSurface();
        RuntimeAutomationBindings.Apply(surface, runtimeHost.Runtime,
            new RuntimeAutomationHostCapabilities
            {
                HostName = "inventory test",
                Declared = RuntimeAutomationHostCapabilities.AllCapabilityNames,
            });
        IPluginHost pluginHost = new Host(surface);
        var objects = runtimeHost.Runtime.InventoryOwner.Objects;
        uint player = runtimeHost.Runtime.PlayerIdentity.ServerGuid;
        objects.InitializeInventoryManifest(player, [new ContainerContentEntry(0x70001234, 1)]);
        Assert.False(pluginHost.Automation.Items.IsOwnedInventoryComplete);
        objects.ReplaceContents(0x70001234, Array.Empty<uint>());
        Assert.True(pluginHost.Automation.Items.IsOwnedInventoryComplete);
        surface.Unbind();
        Assert.False(pluginHost.Automation.Items.IsOwnedInventoryComplete);
    }

    [Fact]
    public void DefaultHostDoesNotClaimCompleteInventory() =>
        Assert.False(NoOpAutomationSurface.Instance.Items.IsOwnedInventoryComplete);

    private sealed class Host(IAutomationSurface automation) : IPluginHost
    {
        public IAutomationSurface Automation => automation;
        public bool HasUi => false;
        public IPluginLogger Log => throw new NotSupportedException();
        public IGameState State => throw new NotSupportedException();
        public IEvents Events => throw new NotSupportedException();
        public ISelectionService Selection => throw new NotSupportedException();
        public IUiRegistry Ui => throw new NotSupportedException();
    }
}
