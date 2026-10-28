using AcDream.Core.Items;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeInventoryStateTests
{
    [Fact]
    public void ViewBorrowsTransactionShortcutAndManaOwners()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        inventory.Shortcuts.Load(
        [
            new AcDream.Core.Items.ShortcutEntry(3, 0x80000001u, 0u),
            new AcDream.Core.Items.ShortcutEntry(4, 0u, 42u),
        ]);
        inventory.ItemMana.OnQueryItemManaResponse(
            0x80000001u,
            0.75f,
            valid: true);
        inventory.Transactions.IncrementBusyCount();

        RuntimeInventoryStateSnapshot snapshot = inventory.View.Snapshot;

        Assert.Equal(2, snapshot.ShortcutCount);
        Assert.Equal(1, snapshot.ItemManaCount);
        Assert.Equal(1, snapshot.BusyCount);
        Assert.False(snapshot.CanBeginRequest);
        Assert.True(inventory.View.TryGetShortcut(
            3,
            out RuntimeShortcutSnapshot itemShortcut));
        Assert.Equal(
            new RuntimeShortcutSnapshot(3, 0x80000001u, 0u),
            itemShortcut);
        Assert.True(inventory.View.TryGetShortcut(
            4,
            out RuntimeShortcutSnapshot spellShortcut));
        Assert.Equal(new RuntimeShortcutSnapshot(4, 0u, 42u), spellShortcut);
        Assert.True(inventory.View.TryGetItemMana(
            itemShortcut.ObjectId,
            out float fraction));
        Assert.Equal(0.75f, fraction);
    }

    [Fact]
    public void OwnsOneGameplayGraphOverExactEntityObjectTable()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        IReadOnlyList<ShortcutEntry> shortcuts =
            [new ShortcutEntry(2, 0x50000001u, 0u)];

        inventory.ExternalContainers.RequestOpen(0x70000001u);
        inventory.ExternalContainers.ApplyViewContents(0x70000001u);
        inventory.ItemMana.OnQueryItemManaResponse(0x50000001u, 0.75f, true);
        inventory.Shortcuts.Load(shortcuts);

        Assert.Same(entities.Objects, inventory.Objects);
        Assert.Equal(0x70000001u, inventory.ExternalContainers.CurrentContainerId);
        Assert.Equal(0.75f, inventory.ItemMana.GetManaPercent(0x50000001u));
        Assert.Equal(shortcuts, inventory.Shortcuts.Items);
        Assert.Equal(1, inventory.Shortcuts.Revision);
    }

    [Fact]
    public void ResetOperationsClearOnlyTheirOwnedLifetimeGroup()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        inventory.ExternalContainers.RequestOpen(0x70000001u);
        inventory.ExternalContainers.ApplyViewContents(0x70000001u);
        inventory.ItemMana.OnQueryItemManaResponse(0x50000001u, 0.75f, true);
        inventory.Shortcuts.Load([new ShortcutEntry(1, 2u, 3u)]);
        inventory.Transactions.IncrementBusyCount();

        inventory.ResetExternalContainer();
        Assert.Equal(0u, inventory.ExternalContainers.CurrentContainerId);
        Assert.True(inventory.ItemMana.HasMana(0x50000001u));
        Assert.NotEmpty(inventory.Shortcuts.Items);
        Assert.Equal(1, inventory.Transactions.BusyCount);

        inventory.ResetTransactions();
        inventory.ResetItemMana();
        inventory.ResetPlayerSnapshots();

        Assert.Equal(0, inventory.Transactions.BusyCount);
        Assert.False(inventory.ItemMana.HasMana(0x50000001u));
        Assert.Empty(inventory.Shortcuts.Items);
    }

    [Fact]
    public void IndependentRuntimeInstancesNeverShareState()
    {
        using var firstEntities = new RuntimeEntityObjectLifetime();
        using var secondEntities = new RuntimeEntityObjectLifetime();
        using var first = new RuntimeInventoryState(firstEntities);
        using var second = new RuntimeInventoryState(secondEntities);

        first.Shortcuts.Load([new ShortcutEntry(1, 2u, 3u)]);
        first.Transactions.IncrementBusyCount();

        Assert.NotSame(first.Objects, second.Objects);
        Assert.Single(first.Shortcuts.Items);
        Assert.Empty(second.Shortcuts.Items);
        Assert.Equal(1, first.Transactions.BusyCount);
        Assert.Equal(0, second.Transactions.BusyCount);
    }

    [Fact]
    public void DisposeDetachesTransactionsAndClearsOwnedState()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        var inventory = new RuntimeInventoryState(entities);
        inventory.ExternalContainers.RequestOpen(0x70000001u);
        inventory.ExternalContainers.ApplyViewContents(0x70000001u);
        inventory.ItemMana.OnQueryItemManaResponse(0x50000001u, 0.5f, true);
        inventory.Shortcuts.Load([new ShortcutEntry(1, 2u, 3u)]);

        inventory.Dispose();
        inventory.Dispose();

        Assert.True(inventory.IsDisposed);
        Assert.True(inventory.Transactions.IsDisposed);
        Assert.Equal(0u, inventory.ExternalContainers.CurrentContainerId);
        Assert.False(inventory.ItemMana.HasMana(0x50000001u));
        Assert.Empty(inventory.Shortcuts.Items);
    }

    [Fact]
    public void RemovingAnObjectRetiresRetailOpenedCorpseHistory()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        const uint corpse = 0x70000020u;
        inventory.Objects.AddOrUpdate(new ClientObject
        {
            ObjectId = corpse,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse,
        });
        inventory.ExternalContainers.RequestOpen(corpse, isCorpse: true);
        Assert.True(inventory.ExternalContainers.HasCorpseBeenOpened(corpse));

        Assert.True(inventory.Objects.Remove(corpse));

        Assert.False(inventory.ExternalContainers.HasCorpseBeenOpened(corpse));
        Assert.Equal(0, inventory.CaptureOwnership().OpenedCorpseCount);
    }

    [Fact]
    public void DisposalFailureIsReportedAfterTerminalOwnerConvergence()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        var inventory = new RuntimeInventoryState(entities);
        inventory.ExternalContainers.RequestOpen(0x70000001u);
        inventory.ExternalContainers.ApplyViewContents(0x70000001u);
        bool fail = true;
        inventory.ExternalContainers.Changed += _ =>
        {
            if (fail)
            {
                fail = false;
                throw new InvalidOperationException("transient");
            }
        };

        Assert.Throws<AggregateException>(inventory.Dispose);
        Assert.True(inventory.IsDisposed);
        Assert.True(inventory.Transactions.IsDisposed);
        inventory.Dispose();

        Assert.True(inventory.CaptureOwnership().IsConverged);
        Assert.Equal(0u, inventory.ExternalContainers.CurrentContainerId);
    }

    [Fact]
    public void ShortcutCommandsUseRetailSendThenLocalOrderOnTheExactOwner()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        var order = new List<string>();
        inventory.Shortcuts.Changed += () => order.Add("local");
        var entry = new ShortcutEntry(3, 0x80000001u, 0u);

        Assert.True(inventory.TryAddShortcut(
            entry,
            () =>
            {
                Assert.Null(inventory.Shortcuts.GetEntry(3));
                order.Add("send");
            }));
        Assert.Equal(["send", "local"], order);
        Assert.Equal(entry, inventory.Shortcuts.GetEntry(3));

        order.Clear();
        Assert.True(inventory.TryRemoveShortcut(
            3,
            () =>
            {
                Assert.Equal(entry, inventory.Shortcuts.GetEntry(3));
                order.Add("send");
            }));
        Assert.Equal(["send", "local"], order);
        Assert.Null(inventory.Shortcuts.GetEntry(3));

        Assert.Throws<InvalidOperationException>(
            () => inventory.TryAddShortcut(
                entry,
                () => throw new InvalidOperationException("transport")));
        Assert.Equal(entry, inventory.Shortcuts.GetEntry(3));

        int sends = 0;
        Assert.False(inventory.TryAddShortcut(
            new ShortcutEntry(ShortcutStore.SlotCount, 1u, 0u),
            () => sends++));
        Assert.False(inventory.TryRemoveShortcut(-1, () => sends++));
        Assert.Equal(0, sends);
    }

    private const uint Player = 0x50000001u;
    private const uint SidePack = 0x80000010u;
    private const uint Kit = 0x80000020u;
    private const uint Chest = 0x70000030u;
    private const int KitSlot = 4;

    private static (RuntimeEntityObjectLifetime Entities,
        RuntimeInventoryState Inventory,
        List<int> Sent) NewHeldShortcut()
    {
        var entities = new RuntimeEntityObjectLifetime();
        var inventory = new RuntimeInventoryState(entities);
        var sent = new List<int>();
        inventory.BindShortcutOwnership(() => Player, static _ => { }, sent.Add);
        ClientObjectTable objects = inventory.Objects;
        objects.AddOrUpdate(new ClientObject { ObjectId = Player });
        objects.InitializeInventoryManifest(
            Player,
            [new ContainerContentEntry(SidePack, 1u),
             new ContainerContentEntry(Kit, 0u)]);
        inventory.Shortcuts.Load([new ShortcutEntry(KitSlot, Kit, 0u)]);
        return (entities, inventory, sent);
    }

    [Fact]
    public void ServerDropToTheGroundClearsTheShortcutAndTellsTheServer()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            inventory.Objects.ApplyConfirmedServerMove(
                Kit,
                newContainerId: 0u,
                newWielderId: 0u);

            Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void ItemPutInAContainerThePlayerDoesNotOwnLeavesTheToolbar()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            inventory.Objects.ApplyConfirmedServerMove(
                Kit,
                newContainerId: Chest,
                newWielderId: 0u);

            Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void ItemTheServerDestroysOrTakesAwayLeavesTheToolbar()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            Assert.True(inventory.Objects.Remove(Kit));

            Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void ItemStillHeldByThePlayerKeepsItsShortcut()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            inventory.Objects.ApplyConfirmedServerMove(
                Kit,
                newContainerId: SidePack,
                newWielderId: 0u);
            inventory.Objects.ApplyConfirmedServerWield(
                Kit,
                Player,
                EquipMask.MeleeWeapon);
            inventory.Objects.ApplyConfirmedServerMove(
                Kit,
                newContainerId: Player,
                newWielderId: 0u);

            Assert.Equal(
                new ShortcutEntry(KitSlot, Kit, 0u),
                inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Empty(sent);
        }
    }

    [Fact]
    public void ALocalProjectionAndAFreshIncarnationKeepTheShortcut()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            Assert.True(inventory.Objects.MoveItemOptimistic(Kit, 0u, 0));
            Assert.True(inventory.Objects.RollbackMove(Kit));
            Assert.NotNull(inventory.Objects.ReplaceGeneration(
                new WeenieData { Guid = Kit, ContainerId = Player },
                generation: 2));

            Assert.Equal(
                new ShortcutEntry(KitSlot, Kit, 0u),
                inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Empty(sent);
        }
    }

    [Fact]
    public void EveryClientsRuntimeClearsTheShortcutOfAnItemTheServerDrops()
    {
        var gameplay = new RuntimeCreatureDeathStateTests.NoOpOperations();
        using var runtime = new GameRuntime(new GameRuntimeDependencies(
            gameplay,
            gameplay,
            gameplay,
            gameplay));
        runtime.PlayerIdentity.ServerGuid = Player;
        RuntimeInventoryState inventory = runtime.InventoryOwner;
        inventory.Objects.AddOrUpdate(new ClientObject { ObjectId = Player });
        inventory.Objects.InitializeInventoryManifest(
            Player,
            [new ContainerContentEntry(Kit, 0u)]);
        inventory.Shortcuts.Load([new ShortcutEntry(KitSlot, Kit, 0u)]);

        inventory.Objects.ApplyConfirmedServerMove(Kit, 0u, 0u);

        Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
    }

    [Fact]
    public void NoShortcutIsDroppedWhileNoPlayerIsKnown()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        var sent = new List<int>();
        inventory.BindShortcutOwnership(() => 0u, static _ => { }, sent.Add);
        inventory.Objects.AddOrUpdate(new ClientObject { ObjectId = Kit });
        inventory.Shortcuts.Load([new ShortcutEntry(KitSlot, Kit, 0u)]);

        inventory.Objects.ApplyConfirmedServerMove(Kit, 0u, 0u);

        Assert.NotNull(inventory.Shortcuts.GetEntry(KitSlot));
        Assert.Empty(sent);
    }

    [Fact]
    public void AServerMoveOfAnItemTheClientNeverHeldClearsItsShortcut()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            const uint Unknown = 0x80000099u;
            const int UnknownSlot = 9;
            inventory.Shortcuts.Set(new ShortcutEntry(UnknownSlot, Unknown, 0u));

            Assert.False(inventory.Objects.ApplyConfirmedServerMove(
                Unknown,
                newContainerId: Player,
                newWielderId: 0u));

            Assert.Null(inventory.Shortcuts.GetEntry(UnknownSlot));
            Assert.Equal([UnknownSlot], sent);
            Assert.NotNull(inventory.Shortcuts.GetEntry(KitSlot));
        }
    }

    [Fact]
    public void AnItemOnTwoSlotsLosesOnlyTheFirstPerServerMove()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            const int LaterSlot = 11;
            inventory.Shortcuts.Set(new ShortcutEntry(LaterSlot, Kit, 0u));

            inventory.Objects.ApplyConfirmedServerMove(Kit, 0u, 0u);

            Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Equal(
                new ShortcutEntry(LaterSlot, Kit, 0u),
                inventory.Shortcuts.GetEntry(LaterSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void AnExactIncarnationDeleteClearsTheShortcut()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            Assert.True(inventory.Objects.RemoveLogicalGeneration(Kit, 1));

            Assert.Null(inventory.Shortcuts.GetEntry(KitSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void SpellShortcutsAreNeverTouchedByItemMovesOrRemovals()
    {
        var (entities, inventory, sent) = NewHeldShortcut();
        using (entities)
        using (inventory)
        {
            const int SpellSlot = 6;
            var spell = new ShortcutEntry(SpellSlot, 0u, 42u);
            inventory.Shortcuts.Set(spell);

            inventory.Objects.ApplyConfirmedServerMove(Kit, 0u, 0u);
            Assert.True(inventory.Objects.Remove(Kit));
            inventory.Objects.ApplyConfirmedServerMove(0u, 0u, 0u);

            Assert.Equal(spell, inventory.Shortcuts.GetEntry(SpellSlot));
            Assert.Equal([KitSlot], sent);
        }
    }

    [Fact]
    public void AMergeMovesThePouredStacksShortcutAndItsRemovalThenSendsNothing()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        var wire = new List<string>();
        inventory.BindShortcutOwnership(
            () => Player,
            entry => wire.Add(
                $"add:{entry.Index}:{entry.ObjectId:X8}:{entry.SpellId:X8}"),
            slot => wire.Add($"remove:{slot}"));
        const uint Poured = 0x80000031u;
        const uint Joined = 0x80000032u;
        inventory.Objects.AddOrUpdate(new ClientObject { ObjectId = Player });
        inventory.Objects.InitializeInventoryManifest(
            Player,
            [new ContainerContentEntry(Poured, 0u),
             new ContainerContentEntry(Joined, 0u)]);
        inventory.Shortcuts.Load([new ShortcutEntry(4, Poured, 0x11223344u)]);

        inventory.ReplaceFullyMergedShortcut(Poured, Joined);

        Assert.Equal(
            new ShortcutEntry(4, Joined, 0x11223344u),
            inventory.Shortcuts.GetEntry(4));
        Assert.Equal(["remove:4", "add:4:80000032:11223344"], wire);

        wire.Clear();
        Assert.True(inventory.Objects.Remove(Poured));

        Assert.Equal(
            new ShortcutEntry(4, Joined, 0x11223344u),
            inventory.Shortcuts.GetEntry(4));
        Assert.Empty(wire);
    }

    [Fact]
    public void AMergeLeavesTheBarAloneUntilTheOwnerIsBound()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        using var inventory = new RuntimeInventoryState(entities);
        inventory.Shortcuts.Load([new ShortcutEntry(4, 0x80000031u, 0u)]);

        inventory.ReplaceFullyMergedShortcut(0x80000031u, 0x80000032u);

        Assert.Equal(
            new ShortcutEntry(4, 0x80000031u, 0u),
            inventory.Shortcuts.GetEntry(4));
    }

    [Fact]
    public void DisposalRetiresShortcutObserversAndTransactionSubscriptions()
    {
        using var entities = new RuntimeEntityObjectLifetime();
        var inventory = new RuntimeInventoryState(entities);
        inventory.Shortcuts.Changed += static () => { };
        inventory.Shortcuts.Load([new ShortcutEntry(1, 2u, 3u)]);

        inventory.Dispose();

        Assert.True(inventory.IsDisposed);
        Assert.True(inventory.Shortcuts.IsDisposed);
        Assert.True(inventory.Transactions.IsDisposed);
        Assert.Equal(0, inventory.Shortcuts.SubscriberCount);
        Assert.Empty(inventory.Shortcuts.Items);
    }
}
