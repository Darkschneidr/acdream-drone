using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeFellowshipEventSinkTests
{
    private const uint Self = 1u;
    private const uint Leader = 2u;
    private const uint Other = 3u;

    [Theory]
    [InlineData(GameEventType.FellowshipQuit, Leader, Other, "Another has left your Fellowship.")]
    [InlineData(GameEventType.FellowshipQuit, Leader, Self, "You are no longer a member of the Test Fellows Fellowship.")]
    [InlineData(GameEventType.FellowshipDismiss, Leader, Self, "Jerry Seinfeld has dismissed you from the Fellowship.")]
    [InlineData(GameEventType.FellowshipDismiss, Leader, Other, "Another has been dismissed from the Fellowship.")]
    [InlineData(GameEventType.FellowshipDismiss, Self, Other, "You dismiss Another from your Fellowship.")]
    [InlineData(GameEventType.FellowshipDisband, Leader, Self, "Jerry Seinfeld has disbanded your Fellowship.")]
    [InlineData(GameEventType.FellowshipDisband, Self, Self, "You have disbanded your Fellowship.")]
    public void DeparturesPublishTheMessageBeforeChangingTheRoster(
        GameEventType kind, uint leader, uint actor, string expected)
    {
        using var state = State(leader);
        var messages = new List<string>();
        var sink = new RuntimeFellowshipEventSink(state, () => Self, text =>
        {
            Assert.True(state.IsInFellowship);
            Assert.True(state.View.TryGetMember(actor, out _));
            messages.Add(text);
        });

        Apply(sink, kind, actor);

        Assert.Equal(expected, Assert.Single(messages));
        if (kind == GameEventType.FellowshipDisband || actor == Self)
            Assert.False(state.IsInFellowship);
        else
            Assert.False(state.View.TryGetMember(actor, out _));
        Apply(sink, kind, actor);
        Assert.Single(messages);
    }

    [Fact]
    public void UnknownMembersAndInactiveFellowshipsProduceNoDepartureNotice()
    {
        using var state = State(Leader);
        var messages = new List<string>();
        var sink = new RuntimeFellowshipEventSink(state, () => Self, messages.Add);
        sink.ApplyQuit(99u);
        sink.ApplyDismiss(99u);
        Assert.Empty(messages);
        state.ResetSession();
        sink.ApplyQuit(Self);
        sink.ApplyDismiss(Self);
        sink.ApplyDisband();
        Assert.Empty(messages);
    }

    private static RuntimeFellowshipState State(uint leader)
    {
        var state = new RuntimeFellowshipState();
        state.ApplyFullUpdate(new GameEvents.FellowshipFullUpdate(
            [Member(Self, "Self"), Member(Leader, "Jerry Seinfeld"), Member(Other, "Another")],
            "Test Fellows", leader, true, true, true, false, []));
        return state;
    }

    private static GameEvents.FellowMember Member(uint guid, string name) =>
        new(guid, 0u, 0u, 10u, 100u, 100u, 100u, 100u, 100u, 100u, 0u, name);

    private static void Apply(RuntimeFellowshipEventSink sink, GameEventType kind, uint actor)
    {
        switch (kind)
        {
            case GameEventType.FellowshipQuit: sink.ApplyQuit(actor); break;
            case GameEventType.FellowshipDismiss: sink.ApplyDismiss(actor); break;
            case GameEventType.FellowshipDisband: sink.ApplyDisband(); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
