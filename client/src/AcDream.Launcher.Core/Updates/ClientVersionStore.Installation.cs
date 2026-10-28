using System.Text.Json;
using AcDream.Launcher.Core.Integrity;

namespace AcDream.Launcher.Core.Updates;

public sealed partial class ClientVersionStore
{
    public string ClientDirectory => Path.Combine(AppDirectory, "client");
    public string BackupDirectory => Path.Combine(AppDirectory, "client-previous");
    internal bool HasPendingReplacement => File.Exists(ReplacementPath);
    private string ReplacementPath => Path.Combine(AppDirectory, ".client-replacement.json");
    private string ReplacedDirectory => Path.Combine(AppDirectory, ".client-replaced");

    private sealed record Replacement(
        string IncomingName,
        string? OldName,
        ClientActivationPointer Pointer,
        bool KeepPrevious);

    private static string? InstalledVersion(string directory)
    {
        string path = GetMetadataPath(directory);
        if (!File.Exists(path))
            return null;
        try
        {
            return ParseStrict<ClientVersionRecord>(File.ReadAllBytes(path))?.Version;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or LauncherUpdateException)
        {
            return null;
        }
    }

    private async Task ReplaceInstallationAsync(
        string incoming,
        ClientActivationPointer? oldPointer,
        ClientActivationPointer pointer,
        string rid,
        CancellationToken cancellationToken)
    {
        if (File.Exists(ReplacementPath) || Directory.Exists(ReplacedDirectory))
            throw new LauncherUpdateException("An interrupted client replacement needs startup recovery first.");

        string? oldDirectory = oldPointer is null
            ? null
            : GetVersionDirectory(LauncherVersion.Parse(oldPointer.CurrentVersion));
        if (oldDirectory != ClientDirectory && Directory.Exists(ClientDirectory))
            throw new LauncherUpdateException("The client installation folder is already occupied by another installation.");
        var replacement = new Replacement(
            Path.GetFileName(incoming),
            oldDirectory is null ? null : Path.GetFileName(oldDirectory),
            pointer,
            oldPointer?.CurrentVersion == pointer.CurrentVersion);
        ValidateReplacement(replacement);
        cancellationToken.ThrowIfCancellationRequested();
        await AtomicJsonFile.WriteAsync(ReplacementPath, replacement, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        SetCached(Invalid("Client replacement is in progress."));
        // Once the durable journal exists, finish the swap even if the download
        // operation is cancelled. Startup resumes these same steps after a crash.
        await CompleteReplacementAsync(replacement, rid).ConfigureAwait(false);
    }

    private async Task RecoverReplacementAsync(string rid)
    {
        if (!File.Exists(ReplacementPath))
            return;
        Replacement replacement = ParseStrict<Replacement>(await File.ReadAllBytesAsync(ReplacementPath)
            .ConfigureAwait(false)) ?? throw new LauncherUpdateException("The client replacement journal is empty.");
        ValidateReplacement(replacement);
        await CompleteReplacementAsync(replacement, rid).ConfigureAwait(false);
    }

    private static void ValidateReplacement(Replacement replacement)
    {
        static bool IsSlot(string name) => name is "client" or "client-previous"
            || LauncherVersion.TryParse(name, out _);
        if (ValidatePointer(replacement.Pointer) is not null
            || !(replacement.IncomingName == "client-previous"
                || LauncherVersion.TryParse(replacement.IncomingName, out _)
                || HasCanonicalGuidName(replacement.IncomingName, ".client-staging-", string.Empty))
            || (replacement.OldName is not null && !IsSlot(replacement.OldName))
            || replacement.IncomingName == replacement.OldName)
            throw new LauncherUpdateException("The client replacement journal contains invalid paths or versions.");
    }

    private async Task CompleteReplacementAsync(Replacement replacement, string rid)
    {
        string incoming = Path.Combine(AppDirectory, replacement.IncomingName);
        string? old = replacement.OldName is null ? null : Path.Combine(AppDirectory, replacement.OldName);
        LauncherVersion version = LauncherVersion.Parse(replacement.Pointer.CurrentVersion);
        PointerRead published = await ReadPointerAsync(CurrentPointerPath, CancellationToken.None)
            .ConfigureAwait(false);
        bool alreadyPublished = published.Pointer == replacement.Pointer
            && InstalledVersion(ClientDirectory) == version.Value;
        if (!alreadyPublished && Directory.Exists(incoming))
        {
            ClientVersionResolution candidate = await VerifyVersionDirectoryAsync(
                incoming, version, rid, CancellationToken.None).ConfigureAwait(false);
            if (!candidate.IsVerified)
                throw new LauncherUpdateException(candidate.Status);
            if (old is not null && Directory.Exists(old))
            {
                RejectReparseTree(old);
                Directory.Move(old, ReplacedDirectory);
            }
            Directory.Move(incoming, ClientDirectory);
        }

        ClientVersionResolution installed = await VerifyVersionDirectoryAsync(
            ClientDirectory, version, rid, CancellationToken.None).ConfigureAwait(false);
        if (!installed.IsVerified)
            throw new LauncherUpdateException(installed.Status);

        PointerRead current = await ReadPointerAsync(CurrentPointerPath, CancellationToken.None)
            .ConfigureAwait(false);
        if (current.Pointer != replacement.Pointer)
            await SavePointerAsync(replacement.Pointer, CancellationToken.None).ConfigureAwait(false);

        if (Directory.Exists(ReplacedDirectory))
        {
            RejectReparseTree(ReplacedDirectory);
            if (replacement.KeepPrevious)
                Directory.Delete(ReplacedDirectory, recursive: true);
            else
            {
                if (Directory.Exists(BackupDirectory))
                {
                    RejectReparseTree(BackupDirectory);
                    Directory.Delete(BackupDirectory, recursive: true);
                }
                Directory.Move(ReplacedDirectory, BackupDirectory);
            }
        }
        File.Delete(ReplacementPath);
    }
}
