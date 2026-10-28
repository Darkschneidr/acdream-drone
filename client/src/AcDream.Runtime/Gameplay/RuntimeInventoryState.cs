using AcDream.Core.Items;
using AcDream.Runtime.Entities;

namespace AcDream.Runtime.Gameplay;

public readonly record struct RuntimeInventoryOwnershipSnapshot(
    bool IsDisposed,
    bool TransactionsDisposed,
    bool ShortcutsDisposed,
    int BusyCount,
    bool HasPendingRequest,
    uint RequestedContainerId,
    uint CurrentContainerId,
    int ItemManaCount,
    int ShortcutCount,
    int ShortcutSubscriberCount,
    long ShortcutDispatchFailureCount,
    long TransactionDispatchFailureCount,
    int OpenedCorpseCount,
    uint VendorId,
    int MaterializedVendorItemCount)
{
    public bool IsConverged =>
        IsDisposed
        && TransactionsDisposed
        && ShortcutsDisposed
        && BusyCount == 0
        && !HasPendingRequest
        && RequestedContainerId == 0u
        && CurrentContainerId == 0u
        && ItemManaCount == 0
        && ShortcutCount == 0
        && ShortcutSubscriberCount == 0
        && OpenedCorpseCount == 0
        && VendorId == 0u
        && MaterializedVendorItemCount == 0;
}

public sealed class RuntimeInventoryState : IDisposable
{
    private readonly RuntimeEntityObjectLifetime _entityObjects;
    private Func<uint>? _playerGuid;
    private Action<ShortcutEntry>? _sendAddShortcut;
    private Action<int>? _sendRemoveShortcut;
    private bool _disposed;

    public RuntimeInventoryState(RuntimeEntityObjectLifetime entityObjects)
    {
        _entityObjects = entityObjects
            ?? throw new ArgumentNullException(nameof(entityObjects));
        ExternalContainers = new ExternalContainerState();
        _entityObjects.Objects.ObjectRemoved += OnObjectRemoved;
        _entityObjects.Objects.ObjectMoved += OnObjectMoved;
        _entityObjects.Objects.ObjectRemovalClassified += OnObjectRemovalClassified;
        ItemMana = new ItemManaState();
        Shortcuts = new ShortcutStore();
        Vendor = new VendorState();
        Transactions = new InventoryTransactionState(
            _entityObjects.Objects, Vendor);
        VendorItems = new VendorShopItemMaterializer(Vendor, _entityObjects.Objects);
        View = new InventoryStateView(this);
    }

    public ClientObjectTable Objects => _entityObjects.Objects;
    public ExternalContainerState ExternalContainers { get; }
    public ItemManaState ItemMana { get; }
    public ShortcutStore Shortcuts { get; }
    public InventoryTransactionState Transactions { get; }
    public VendorState Vendor { get; }
    public VendorShopItemMaterializer VendorItems { get; }
    public IRuntimeInventoryStateView View { get; }
    public bool IsDisposed => _disposed;

    public RuntimeInventoryOwnershipSnapshot CaptureOwnership() => new(
        _disposed,
        Transactions.IsDisposed,
        Shortcuts.IsDisposed,
        Transactions.BusyCount,
        Transactions.HasPendingRequest,
        ExternalContainers.RequestedContainerId,
        ExternalContainers.CurrentContainerId,
        ItemMana.Count,
        Shortcuts.Count,
        Shortcuts.SubscriberCount,
        Shortcuts.DispatchFailureCount,
        Transactions.DispatchFailureCount,
        ExternalContainers.OpenedCorpseCount,
        Vendor.VendorId,
        VendorItems.OwnedCount);

    public void ResetExternalContainer() => ExternalContainers.Reset();
    public void ResetTransactions() => Transactions.ResetSession();
    public void ResetItemMana() => ItemMana.Clear();
    public void ResetVendor() => Vendor.Reset();

    /// <summary>
    /// Binds what the toolbar needs to follow items on its own: who the
    /// player is, and how to tell the server a slot was filled or cleared.
    /// Until it is bound, and while no player is known, no shortcut is
    /// dropped or moved by this owner.
    /// </summary>
    public void BindShortcutOwnership(
        Func<uint> playerGuid,
        Action<ShortcutEntry> sendAddShortcut,
        Action<int> sendRemoveShortcut)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerGuid = playerGuid
            ?? throw new ArgumentNullException(nameof(playerGuid));
        _sendAddShortcut = sendAddShortcut
            ?? throw new ArgumentNullException(nameof(sendAddShortcut));
        _sendRemoveShortcut = sendRemoveShortcut
            ?? throw new ArgumentNullException(nameof(sendRemoveShortcut));
    }

    /// <summary>
    /// A stack merge has just been sent, from any client and any caller (a
    /// drag, a plugin, an automatic join): the shortcut of the stack being
    /// poured moves to the stack it is poured into, keeping its slot, and
    /// the server is told of both changes. The poured stack's later removal
    /// then finds nothing of it on the bar.
    /// </summary>
    public void ReplaceFullyMergedShortcut(uint oldObjectId, uint newObjectId)
    {
        if (_disposed
            || _sendAddShortcut is not { } sendAdd
            || _sendRemoveShortcut is not { } sendRemove)
        {
            return;
        }

        ShortcutMutation[] plan = ShortcutDropPlanner.PlanFullStackMerge(
            Shortcuts.Snapshot(),
            oldObjectId,
            newObjectId);
        foreach (ShortcutMutation mutation in plan)
        {
            if (mutation.Kind == ShortcutMutationKind.Remove)
            {
                int slot = mutation.Slot;
                TryRemoveShortcut(slot, () => sendRemove(slot));
            }
            else if (mutation.Entry is { } entry)
            {
                TryAddShortcut(entry, () => sendAdd(entry));
            }
        }
    }

    public void ResetPlayerSnapshots()
    {
        Shortcuts.Clear();
    }

    public bool TryAddShortcut(
        ShortcutEntry entry,
        Action publishOutbound)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(publishOutbound);
        if ((uint)entry.Index >= ShortcutStore.SlotCount
            || (entry.ObjectId == 0u && entry.SpellId == 0u))
        {
            return false;
        }

        try
        {
            publishOutbound();
        }
        finally
        {
            Shortcuts.Set(entry);
        }
        return true;
    }

    public bool TryRemoveShortcut(
        int index,
        Action publishOutbound)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(publishOutbound);
        if ((uint)index >= ShortcutStore.SlotCount)
            return false;

        try
        {
            publishOutbound();
        }
        finally
        {
            Shortcuts.Remove(index);
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        List<Exception>? failures = null;
        try
        {
            _entityObjects.Objects.ObjectRemoved -= OnObjectRemoved;
            _entityObjects.Objects.ObjectMoved -= OnObjectMoved;
            _entityObjects.Objects.ObjectRemovalClassified -=
                OnObjectRemovalClassified;
            Try(() => ExternalContainers.Reset(), ref failures);
            Try(() => Vendor.Reset(), ref failures);
            Try(VendorItems.Dispose, ref failures);
            Try(ItemMana.Clear, ref failures);
            Try(Shortcuts.Dispose, ref failures);
            Try(Transactions.Dispose, ref failures);
        }
        finally
        {
            _disposed = true;
        }
        if (failures is not null)
            throw new AggregateException(
                "Runtime inventory state did not converge during disposal.",
                failures);
    }

    private static void Try(Action action, ref List<Exception>? failures)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }
    }

    private void OnObjectRemoved(ClientObject item)
        => ExternalContainers.SetCorpseDeleted(item.ObjectId);

    // Only the server's word takes a shortcut off the toolbar: a local
    // projection of a drag may still be refused and rolled back.
    private void OnObjectMoved(ClientObjectMove move)
    {
        if (move.Origin == ClientObjectMoveOrigin.AuthoritativeResponse)
            RemoveShortcutIfNoLongerOwned(move.ItemId);
    }

    // A fresh incarnation of the same object takes its place in the same
    // breath; that is a re-description, not the item leaving the player.
    private void OnObjectRemovalClassified(ClientObjectRemoval removal)
    {
        if (removal.Reason != ClientObjectRemovalReason.GenerationReplacement)
            RemoveShortcutIfNoLongerOwned(removal.Object.ObjectId);
    }

    /// <summary>
    /// When the server moves or removes an item that sits on the toolbar and
    /// the player no longer holds it (dropped, given, sold, put in a
    /// container that is not the player's, destroyed, used up), its shortcut
    /// is cleared and the server is told, the same way a drag off the bar
    /// clears it. An item still held (moved between the player's own packs,
    /// wielded) keeps its slot.
    /// </summary>
    private void RemoveShortcutIfNoLongerOwned(uint itemId)
    {
        if (_disposed || itemId == 0u || _sendRemoveShortcut is null)
            return;
        uint player = _playerGuid?.Invoke() ?? 0u;
        if (player == 0u)
            return;

        for (int slot = 0; slot < ShortcutStore.SlotCount; slot++)
        {
            if (Shortcuts.Get(slot) != itemId
                || Objects.IsOwnedByObject(itemId, player))
            {
                continue;
            }

            Action<int> send = _sendRemoveShortcut;
            int cleared = slot;
            TryRemoveShortcut(cleared, () => send(cleared));
            return;
        }
    }

    private sealed class InventoryStateView(RuntimeInventoryState owner)
        : IRuntimeInventoryStateView
    {
        public RuntimeInventoryStateSnapshot Snapshot
        {
            get
            {
                RuntimePendingInventoryRequestSnapshot? pending = null;
                if (owner.Transactions.TryGetPending(
                        out PendingInventoryRequest request))
                {
                    pending = new RuntimePendingInventoryRequestSnapshot(
                        request.Token,
                        (int)request.Kind,
                        request.ItemId,
                        request.Dispatched);
                }

                return new RuntimeInventoryStateSnapshot(
                    owner.ExternalContainers.RequestedContainerId,
                    owner.ExternalContainers.CurrentContainerId,
                    owner.Transactions.BusyCount,
                    owner.Transactions.CanBeginRequest,
                    pending,
                    owner.Shortcuts.Count,
                    owner.Shortcuts.Revision,
                    owner.ItemMana.Count,
                    owner.ItemMana.Revision);
            }
        }

        public bool TryGetShortcut(
            int index,
            out RuntimeShortcutSnapshot shortcut)
        {
            IReadOnlyList<ShortcutEntry> shortcuts = owner.Shortcuts.Items;
            for (int i = 0; i < shortcuts.Count; i++)
            {
                if (shortcuts[i].Index != index)
                    continue;
                ShortcutEntry current = shortcuts[i];
                shortcut = new RuntimeShortcutSnapshot(
                    current.Index,
                    current.ObjectId,
                    current.SpellId);
                return true;
            }
            shortcut = default;
            return false;
        }

        public bool TryGetItemMana(uint objectId, out float fraction) =>
            owner.ItemMana.TryGetManaPercent(objectId, out fraction);
    }
}
