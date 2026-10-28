using System.Text.Json;
using AcDream.Core.CharGen;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Properties;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Tests.Support;

namespace AcDream.Runtime.Tests.Plugins;

public sealed class RuntimeInventorySnapshotCacheTests
{
    [Fact]
    public void UnchangedInventoryReusesReadOnlySnapshotAndNestedValues()
    {
        using var fixture = new Fixture();
        ClientObject item = fixture.Add(0x70000100, "item");
        item.AppraisedSpellIds = new uint[] { 1, 2 };
        var first = fixture.Surface.CaptureOwnedItems();
        Assert.Same(first, fixture.Surface.CaptureOwnedItems());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PluginInventoryItem>)first)[0] = default);
        PluginInventoryItem captured = first.Single(i => i.ObjectId == item.ObjectId);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<uint>)captured.AppraisedSpellIds)[0] = 99);
    }

    [Fact]
    public void DirectChangesRemainVisibleWithoutObjectTableNotifications()
    {
        using var fixture = new Fixture();
        ClientObject item = fixture.Add(0x70000100, "item");
        Action<ClientObject>[] changes =
        [
            i => i.Name = "renamed",
            i => i.StackSize = 3,
            i => i.StackSizeMax = 99,
            i => i.Structure = 5,
            i => i.MaxStructure = 10,
            i => i.CurrentlyEquippedLocation = EquipMask.Held,
            i => i.WielderId = fixture.Host.Runtime.PlayerIdentity.ServerGuid,
            i => i.ContainerSlot = 7,
            i => i.IconOverlayId = 13,
            i => i.IconUnderlayId = 14,
            i => i.Effects = 15,
            i => i.Properties.Ints[(uint)PropertyInt.ItemCurMana] = 44,
            i => i.Properties.Ints[(uint)PropertyInt.ItemMaxMana] = 100,
            i => i.Properties.Ints[(uint)PropertyInt.ItemSpellcraft] = 200,
            i => i.Properties.Floats[(uint)PropertyFloat.DamageVariance] = .7,
            i => i.Properties.Bools[(uint)PropertyBool.Retained] = true,
            i => i.Properties.DataIds[(uint)PropertyDataId.Spell] = 66,
            i => i.Properties.Ints.Remove((uint)PropertyInt.ItemCurMana),
            i => i.AppraisedSpellIds = new uint[] { 7, 8 },
            i => ((uint[])i.AppraisedSpellIds)[0] = 9,
            i => i.WeaponProfile = new ClientWeaponProfile { Damage = 45, WeaponSkill = 44 },
            i => i.Header = new ClientObjectHeader(null, null, null, null, null,
                null, null, null, null, null, 3f),
            i => i.Header = i.Header! with { UseRadius = 4f },
        ];
        foreach (Action<ClientObject> change in changes)
        {
            var before = fixture.Surface.CaptureOwnedItems();
            string saved = JsonSerializer.Serialize(before);
            change(item);
            var after = fixture.Surface.CaptureOwnedItems();
            Assert.NotSame(before, after);
            Assert.Equal(saved, JsonSerializer.Serialize(before));
            Assert.Equal(
                JsonSerializer.Serialize(fixture.Surface.ProjectInventoryItem(fixture.Host.Runtime, item)),
                JsonSerializer.Serialize(after.Single(i => i.ObjectId == item.ObjectId)));
            Assert.Same(after, fixture.Surface.CaptureOwnedItems());
        }
    }

    [Fact]
    public void ParentOwnershipChangesRemoveAndRestoreBagContents()
    {
        using var fixture = new Fixture();
        ClientObject bag = fixture.Add(0x70000100, "bag");
        ClientObject item = fixture.Add(0x70000101, "inside");
        item.ContainerId = bag.ObjectId;
        var before = fixture.Surface.CaptureOwnedItems();
        Assert.Contains(before, i => i.ObjectId == item.ObjectId);
        bag.ContainerId = 0;
        var dropped = fixture.Surface.CaptureOwnedItems();
        Assert.DoesNotContain(dropped, i => i.ObjectId == bag.ObjectId || i.ObjectId == item.ObjectId);
        bag.ContainerId = fixture.Host.Runtime.PlayerIdentity.ServerGuid;
        Assert.Contains(fixture.Surface.CaptureOwnedItems(), i => i.ObjectId == item.ObjectId);
        fixture.Host.Runtime.InventoryOwner.Objects.Remove(item.ObjectId);
        Assert.DoesNotContain(fixture.Surface.CaptureOwnedItems(), i => i.ObjectId == item.ObjectId);
        fixture.Add(item.ObjectId, "replacement");
        Assert.Contains(fixture.Surface.CaptureOwnedItems(), i => i.ObjectId == item.ObjectId && i.Name == "replacement");
    }

    [Fact]
    public void RenameResortsByNameThenIdWithoutChangingEarlierSnapshot()
    {
        using var fixture = new Fixture();
        ClientObject a = fixture.Add(0x70000102, "Z");
        fixture.Add(0x70000101, "A");
        var before = fixture.Surface.CaptureOwnedItems();
        a.Name = "A";
        var after = fixture.Surface.CaptureOwnedItems();
        var selected = after.Where(i => i.ObjectId is 0x70000101 or 0x70000102).ToArray();
        Assert.Equal(0x70000101u, selected[0].ObjectId);
        Assert.Equal(0x70000102u, selected[1].ObjectId);
        Assert.Equal("Z", before.Single(i => i.ObjectId == a.ObjectId).Name);
    }

    [Fact]
    public void ChangedPaletteResolverAndLiveRadiusInvalidateAppearance()
    {
        using var fixture = new Fixture();
        ClientObject item = fixture.Add(0x70000100, "painted");
        var colors = new MutableColors();
        fixture.Surface.BindPaletteColorResolver(colors);
        var record = fixture.Host.Runtime.EntityObjects.RegisterEntity(new WorldSession.EntitySpawn(
            item.ObjectId, null, null, [], [],
            [new CreateObject.SubPaletteSwap(0x04000123, 3, 4)],
            null, null, "painted", null, null, null, UseRadius: 2f)).Canonical!;
        var first = fixture.Surface.CaptureOwnedItems();
        Assert.Same(first, fixture.Surface.CaptureOwnedItems());
        colors.Red = 77;
        var recolored = fixture.Surface.CaptureOwnedItems();
        Assert.Equal((byte)77, recolored.Single(i => i.ObjectId == item.ObjectId).Palettes[0].Red);
        Assert.Equal((byte)0, first.Single(i => i.ObjectId == item.ObjectId).Palettes[0].Red);
        fixture.Host.Runtime.EntityObjects.Entities.RefreshSnapshot(record, record.Snapshot with { UseRadius = 4f });
        Assert.Equal(4f, fixture.Surface.CaptureOwnedItems().Single(i => i.ObjectId == item.ObjectId).UseRadius);
        fixture.Surface.BindPaletteColorResolver(new MutableColors { Red = 99 });
        Assert.Equal((byte)99, fixture.Surface.CaptureOwnedItems().Single(i => i.ObjectId == item.ObjectId).Palettes[0].Red);
    }

    [Fact]
    public void RebindingDoesNotRetainThePreviousPlayersInventory()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.Add(0x70000100, "first session");
        second.Add(0x70000100, "second session");
        var old = first.Surface.CaptureOwnedItems();
        RuntimeAutomationBindings.Apply(first.Surface, second.Host.Runtime, Fixture.Capabilities());
        var current = first.Surface.CaptureOwnedItems();
        Assert.Equal("second session", current.Single(i => i.ObjectId == 0x70000100).Name);
        Assert.Equal("first session", old.Single(i => i.ObjectId == 0x70000100).Name);
    }

    [Fact]
    public void RepeatedCapturesDoNotAllocateInventorySizedBuffers()
    {
        using var fixture = new Fixture();
        for (uint i = 0; i < 200; i++)
            fixture.Add(0x70000100 + i, "item " + i).AppraisedSpellIds = new uint[] { 1, 2, 3 };
        var first = fixture.Surface.CaptureOwnedItems();
        for (int i = 0; i < 20; i++) fixture.Surface.CaptureOwnedItems();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) fixture.Surface.CaptureOwnedItems();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 100_000, $"Repeated unchanged captures allocated {allocated} bytes.");
        Assert.Same(first, fixture.Surface.CaptureOwnedItems());
    }

    private sealed class MutableColors : IChargenPaletteColorSource
    {
        internal byte Red;
        public bool TryGetColor(uint paletteId, int index, out ChargenSwatchRgb color)
        {
            color = new ChargenSwatchRgb(Red, 0, 0);
            return true;
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal NoWindowGameRuntimeHost Host { get; } = new();
        internal RuntimeAutomationSurface Surface { get; } = new();
        internal Fixture()
        {
            Host.Start();
            for (int i = 0; i < 4; i++) Host.Session.Tick();
            RuntimeAutomationBindings.Apply(Surface, Host.Runtime, Capabilities());
        }
        internal static RuntimeAutomationHostCapabilities Capabilities() => new()
        {
            HostName = "inventory test",
            Declared = RuntimeAutomationHostCapabilities.AllCapabilityNames,
        };
        internal ClientObject Add(uint id, string name)
        {
            var item = new ClientObject { ObjectId = id, Name = name,
                ContainerId = Host.Runtime.PlayerIdentity.ServerGuid };
            Host.Runtime.InventoryOwner.Objects.AddOrUpdate(item);
            return item;
        }
        public void Dispose() { Surface.Dispose(); Host.Dispose(); }
    }
}
