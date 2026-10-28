using System.Diagnostics.Tracing;
using AcDream.Core.Net;
using Xunit;

namespace AcDream.Core.Net.Tests;

public sealed class CastTraceDiagnosticsTests
{
    [Fact]
    public void AttachedListenerReceivesCastStagesAndIdentifiers()
    {
        using var listener = new CastListener();

        CastTraceDiagnostics.Log.Trace(
            "sent-targeted", spellId: 4407, targetId: 0x5003F84E,
            sequence: 42);

        EventWrittenEventArgs received = Assert.Single(listener.Events);
        Assert.Equal("Cast", received.EventName);
        Assert.Equal("sent-targeted", received.Payload![0]);
        Assert.Equal(4407u, received.Payload[1]);
        Assert.Equal(0x5003F84Eu, received.Payload[2]);
        Assert.Equal(42u, received.Payload[3]);
    }

    private sealed class CastListener : EventListener
    {
        public List<EventWrittenEventArgs> Events { get; } = [];

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "OpenAC-CastTrace")
                EnableEvents(source, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "OpenAC-CastTrace")
                Events.Add(eventData);
        }
    }
}
