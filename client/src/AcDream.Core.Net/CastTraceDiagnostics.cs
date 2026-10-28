using System.Diagnostics.Tracing;

namespace AcDream.Core.Net;

/// <summary>On-demand cast and completion events for one attached session.</summary>
[EventSource(Name = "OpenAC-CastTrace")]
public sealed class CastTraceDiagnostics : EventSource
{
    public static CastTraceDiagnostics Log { get; } = new();

    private CastTraceDiagnostics() { }

    public void Trace(
        string stage,
        uint spellId = 0,
        uint targetId = 0,
        uint sequence = 0,
        uint error = 0,
        int payloadLength = -1)
    {
        if (!IsEnabled())
            return;

        Write("Cast", new EventSourceOptions
        {
            Level = EventLevel.Informational,
        }, new
        {
            Stage = stage,
            SpellId = spellId,
            TargetId = targetId,
            Sequence = sequence,
            Error = error,
            PayloadLength = payloadLength,
        });
    }
}
