using System;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Core.Net;

namespace AcDream.Runtime.Gameplay;

public readonly record struct RuntimeSpellCastCompletion(
    long Revision,
    uint SpellId,
    uint TargetObjectId,
    uint WeenieError)
{
    public bool IsSuccess => Revision != 0 && WeenieError == 0u;
}

public interface IRuntimeSpellCastOperations
{
    uint LocalPlayerId { get; }
    bool CanSend { get; }
    bool HasRequiredComponents(uint spellId);
    bool IsTargetCompatible(
        uint targetId,
        SpellMetadata spell,
        bool showMessage);
    void StopCompletely();
    void SendUntargeted(uint spellId);
    void SendTargeted(uint targetId, uint spellId);
    void DisplayMessage(string message);
    void IncrementBusy();
}

public sealed class RuntimeSpellCastState
{
    private readonly Spellbook _spellbook;
    private readonly SelectionState _selection;
    private readonly IRuntimeSpellCastOperations _operations;
    private long _outstandingCastReceipts;

    public RuntimeSpellCastState(
        Spellbook spellbook,
        SelectionState selection,
        IRuntimeSpellCastOperations operations)
    {
        _spellbook = spellbook ?? throw new ArgumentNullException(nameof(spellbook));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public uint? LastRequestedSpellId { get; private set; }
    public uint? LastRequestedTargetId { get; private set; }
    public uint? PendingSpellId { get; private set; }
    public uint? PendingTargetId { get; private set; }
    public RuntimeSpellCastCompletion LastCompletion { get; private set; }
    public event Action? StateChanged;

    public bool HasRequiredComponents(uint spellId) =>
        _operations.HasRequiredComponents(spellId);

    public bool IsTargetReady(uint spellId) =>
        EvaluateCastGate(spellId)
            is SpellCastGate.NoTargetNeeded or SpellCastGate.TargetCompatible;

    public SpellCastGate EvaluateCastGate(uint spellId)
    {
        if (!_spellbook.Knows(spellId)
            || !_spellbook.TryGetMetadata(spellId, out SpellMetadata spell))
            return SpellCastGate.Unknown;
        if (spell.IsSelfTargeted || spell.IsUntargeted || spell.TargetMask == 0u)
            return SpellCastGate.NoTargetNeeded;
        if (_selection.SelectedObjectId is not (uint target and not 0u))
            return SpellCastGate.NoTargetSelected;
        return _operations.IsTargetCompatible(target, spell, showMessage: false)
            ? SpellCastGate.TargetCompatible
            : SpellCastGate.TargetIncompatible;
    }

    public CastRequestResult Cast(uint spellId)
    {
        if (!_spellbook.Knows(spellId) || !_spellbook.TryGetMetadata(spellId, out SpellMetadata spell))
        {
            _operations.DisplayMessage("You do not know that spell.");
            return CastRequestResult.UnknownSpell;
        }

        if (!_operations.HasRequiredComponents(spellId))
        {
            _operations.DisplayMessage(
                "You do not have all of this spell's components.");
            return CastRequestResult.MissingComponents;
        }

        uint? target;
        bool untargeted;
        if (spell.IsSelfTargeted)
        {
            uint playerId = _operations.LocalPlayerId;
            target = playerId == 0 ? null : playerId;
            untargeted = target is null;
        }
        else if (spell.IsUntargeted || spell.TargetMask == 0)
        {
            target = null;
            untargeted = true;
        }
        else
        {
            target = _selection.SelectedObjectId;
            untargeted = false;
            if (target is null or 0)
            {
                _operations.DisplayMessage(
                    "You must select a suitable target.");
                return CastRequestResult.NoTarget;
            }
            if (!_operations.IsTargetCompatible(
                    target.Value,
                    spell,
                    showMessage: true))
                return CastRequestResult.IncompatibleTarget;
        }

        if (!_operations.CanSend)
        {
            _operations.DisplayMessage("You cannot cast a spell right now.");
            return CastRequestResult.Unavailable;
        }
        uint? previousRequestedSpellId = LastRequestedSpellId;
        uint? previousRequestedTargetId = LastRequestedTargetId;
        uint? previousPendingSpellId = PendingSpellId;
        uint? previousPendingTargetId = PendingTargetId;
        long previousOutstandingCastReceipts = _outstandingCastReceipts;
        try
        {
            _operations.StopCompletely();
            LastRequestedSpellId = spellId;
            LastRequestedTargetId = target;
            if (_outstandingCastReceipts == 0)
            {
                PendingSpellId = spellId;
                PendingTargetId = target;
            }
            else if (PendingSpellId != spellId || PendingTargetId != target)
            {
                PendingSpellId = null;
                PendingTargetId = null;
            }
            if (untargeted)
                _operations.SendUntargeted(spellId);
            else
                _operations.SendTargeted(target!.Value, spellId);
            _operations.IncrementBusy();
            if (_outstandingCastReceipts < long.MaxValue)
                _outstandingCastReceipts++;
            CastTraceDiagnostics.Log.Trace("runtime-pending", spellId, target ?? 0u);
        }
        catch
        {
            LastRequestedSpellId = previousRequestedSpellId;
            LastRequestedTargetId = previousRequestedTargetId;
            PendingSpellId = previousPendingSpellId;
            PendingTargetId = previousPendingTargetId;
            _outstandingCastReceipts = previousOutstandingCastReceipts;
            throw;
        }
        StateChanged?.Invoke();
        return CastRequestResult.Sent;
    }

    public bool CompleteUse(uint weenieError)
    {
        if (_outstandingCastReceipts == 0)
        {
            CastTraceDiagnostics.Log.Trace("runtime-orphan-receipt", error: weenieError);
            return false;
        }

        _outstandingCastReceipts--;
        if (PendingSpellId is not uint spellId)
        {
            CastTraceDiagnostics.Log.Trace("runtime-ambiguous-receipt", error: weenieError);
            StateChanged?.Invoke();
            return false;
        }

        CastTraceDiagnostics.Log.Trace(
            "runtime-completed", spellId, PendingTargetId ?? 0u,
            error: weenieError);

        long revision = LastCompletion.Revision + 1;
        LastCompletion = new RuntimeSpellCastCompletion(
            revision,
            spellId,
            PendingTargetId ?? 0u,
            weenieError);
        if (_outstandingCastReceipts == 0)
        {
            PendingSpellId = null;
            PendingTargetId = null;
        }
        StateChanged?.Invoke();
        return true;
    }

    public void Reset()
    {
        bool changed = LastRequestedSpellId is not null
            || LastRequestedTargetId is not null
            || PendingSpellId is not null
            || PendingTargetId is not null
            || _outstandingCastReceipts != 0
            || LastCompletion.Revision != 0;
        LastRequestedSpellId = null;
        LastRequestedTargetId = null;
        PendingSpellId = null;
        PendingTargetId = null;
        _outstandingCastReceipts = 0;
        LastCompletion = default;
        if (changed)
            StateChanged?.Invoke();
    }
}

public enum CastRequestResult
{
    Sent,
    UnknownSpell,
    NoTarget,
    IncompatibleTarget,
    MissingComponents,
    Unavailable,
}

public enum SpellCastGate
{
    /// <summary>Spell metadata is missing / not known.</summary>
    Unknown,
    NoTargetNeeded,
    TargetCompatible,
    TargetIncompatible,
    NoTargetSelected,
}
