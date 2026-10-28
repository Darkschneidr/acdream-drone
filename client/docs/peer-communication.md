# Local peer communication

Peer communication is off until a plugin acquires a subscription through
`IPluginHost.Automation.Network.Subscribe`. Each client combines its own plugins'
requests. Enabling a feature on one client does not enable another client's plugins.

The capabilities are `ClientState`, `Casts`, and `Commands`. `SendCommands` is a
temporary outgoing role; `BroadcastCommand` acquires it automatically and releases
it after acknowledgement or timeout. Sending does not enable command reception.
Dispose a subscription when its feature stops. The plugin scope also releases all
remaining subscriptions on unload. Older hosts return `SupportsSubscriptions = false`
and a null lease.

One hub per user and data directory starts on demand. It runs independently of the
launcher using the installed `acdream-headless peer-hub <peer-directory>` entry point.
Both client executables must be present in the installation. Windows uses a
current-user named pipe; Linux and macOS use a Unix-domain socket in a private
directory. No TCP listener, peer JSON files, or directory polling are used. The hub
exits after its last connection leaves and its 15-second idle period expires.

Clients register their character, world, tags and requested capabilities. Messages
are routed only within the same world to subscribed recipients. State is sent only
when another participant requests it, and replaces older queued state. Casts and
commands retain their order in bounded queues. Incoming commands are submitted on
the client's tick thread through the ordinary command entry, with tag filtering and
per-recipient stagger. Acknowledgement confirms hub queue acceptance, not execution.
Commands are deduplicated, expire after 15 seconds before delivery, and are never
replayed after a connection drops. Queue overflow closes the connection and is
reported as a gap rather than silently dropping an event.

`CaptureClients`, `CaptureCasts`, and `CaptureCommands` read the local memory cache.
`IsConnected` becomes true after registration and initial peer state are received.
Until then, an empty capture must not be interpreted as proof that nobody else is
present. Disconnect removes local peers and their cached casts. Connection errors
are logged and reconnection uses bounded backoff. Remote imports remain independent
of this local transport; a plugin can continue using its existing remote relay.

## Validation and performance

The IPC integration tests cover routing, world/tag filtering, stagger, casts,
disconnect, duplicate identities, bounds and inactivity without subscribers. Host
parity tests drive both hosts through `IPluginHost` and test local and imported
commands and casts. Plugin lifecycle tests check lease release and independent
client settings.

For performance, compare identical Release client/plugin versions and launcher
profiles, with the same number of characters and feature settings. Measure process
CPU seconds over the recorded interval and normalize by elapsed seconds and logical
processor count. Record session identity, world state, personal kills and covered
seconds. Reject restarts and gaps. Compare subscriptions off, casts on, state on,
commands on, and the full combination separately. Feature-off measurements cannot
isolate transport savings or establish that combat behaviour is equivalent.

## Coordinated contract release

This implementation targets the 0.1.21 contract. Build the client first and pack
`AcDream.Plugin.Abstractions` in Release. Copy that package into each plugin's
`packages-local` folder before restore/build. The package is compile-only; the host
supplies the assembly at runtime. Plugin manifests require host 0.1.21. CI downloads
the corresponding OpenAC release, so the contract release must exist before the
dependent plugin changes can pass remote CI or be published.
