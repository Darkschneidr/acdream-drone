using AcDream.Runtime.Entities;

namespace AcDream.Runtime.Gameplay;

/// <summary>Releases death facts when the canonical creature leaves the world.</summary>
public sealed class RuntimeCreatureDeathEntityFollower : IRuntimeEntityObjectObserver, IDisposable
{
    private readonly RuntimeCreatureDeathState _death;
    private readonly RuntimeEntityObjectLifetime _entities;
    private readonly IDisposable _subscription;

    public RuntimeCreatureDeathEntityFollower(
        RuntimeEntityObjectLifetime entities,
        RuntimeCreatureDeathState death)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _death = death ?? throw new ArgumentNullException(nameof(death));
        _subscription = entities.Events.Subscribe(this);
    }

    public void OnEntity(in RuntimeEntityDelta delta)
    {
        if (delta.Change is not (RuntimeEntityChange.Deleted or RuntimeEntityChange.Withdrawn))
            return;
        uint guid = delta.Entity.Identity.ServerGuid;
        ushort incarnation = delta.Entity.Identity.Incarnation;
        if (delta.Change == RuntimeEntityChange.Withdrawn
            && _entities.Entities.TryGetActive(guid, out RuntimeEntityRecord active)
            && active.Incarnation == incarnation)
        {
            return;
        }
        _death.Forget(guid, incarnation);
    }

    public void OnInventory(in RuntimeInventoryDelta delta) { }

    public void Dispose() => _subscription.Dispose();
}
