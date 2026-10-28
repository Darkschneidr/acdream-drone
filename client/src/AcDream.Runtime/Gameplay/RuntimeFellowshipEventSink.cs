namespace AcDream.Runtime.Gameplay;

/// <summary>Publishes fellowship departure notices before applying the roster change.</summary>
internal sealed class RuntimeFellowshipEventSink(
    RuntimeFellowshipState state,
    Func<uint> playerGuid,
    Action<string> addText)
{
    public void ApplyQuit(uint quitterGuid)
    {
        uint self = playerGuid();
        RuntimeFellowshipSnapshot snapshot = state.View.Snapshot;
        if (snapshot.IsInFellowship)
        {
            if (quitterGuid == self)
                addText($"You are no longer a member of the {snapshot.Name} Fellowship.");
            else if (state.View.TryGetMember(quitterGuid, out RuntimeFellowMemberSnapshot member))
                addText($"{member.Name} has left your Fellowship.");
        }
        state.ApplyQuit(quitterGuid, self);
    }

    public void ApplyDismiss(uint dismissedGuid)
    {
        uint self = playerGuid();
        RuntimeFellowshipSnapshot snapshot = state.View.Snapshot;
        if (snapshot.IsInFellowship)
        {
            if (dismissedGuid == self)
            {
                if (state.View.TryGetMember(snapshot.LeaderGuid, out RuntimeFellowMemberSnapshot leader))
                    addText($"{leader.Name} has dismissed you from the Fellowship.");
            }
            else if (state.View.TryGetMember(dismissedGuid, out RuntimeFellowMemberSnapshot member))
            {
                addText(snapshot.LeaderGuid == self
                    ? $"You dismiss {member.Name} from your Fellowship."
                    : $"{member.Name} has been dismissed from the Fellowship.");
            }
        }
        state.ApplyDismiss(dismissedGuid, self);
    }

    public void ApplyDisband()
    {
        RuntimeFellowshipSnapshot snapshot = state.View.Snapshot;
        if (snapshot.IsInFellowship)
        {
            if (snapshot.LeaderGuid == playerGuid())
                addText("You have disbanded your Fellowship.");
            else if (state.View.TryGetMember(snapshot.LeaderGuid, out RuntimeFellowMemberSnapshot leader))
                addText($"{leader.Name} has disbanded your Fellowship.");
        }
        state.ApplyDisband();
    }
}
