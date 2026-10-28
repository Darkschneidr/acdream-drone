namespace AcDream.Plugin.Abstractions;

/// <summary>Peer features a plugin explicitly participates in for this client.</summary>
[Flags]
public enum PluginPeerCapabilities
{
    /// <summary>No peer work.</summary>
    None = 0,
    /// <summary>Publish this character and receive other participating characters' state.</summary>
    ClientState = 1,
    /// <summary>Publish casts and receive other participating characters' casts.</summary>
    Casts = 2,
    /// <summary>Receive trusted local broadcast commands.</summary>
    Commands = 4,
    /// <summary>Connect to send a broadcast without enabling command reception.</summary>
    SendCommands = 8,
}
