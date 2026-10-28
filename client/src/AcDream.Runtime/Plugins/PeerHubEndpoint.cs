using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AcDream.Runtime.Plugins;

internal sealed record PeerHubEndpoint(string Directory, string Name)
{
    // The private directory already identifies the hub; keep the socket basename
    // short enough for Unix platforms with a small endpoint path limit.
    internal string SocketPath => Path.Combine(Directory, "hub.sock");
    internal static PeerHubEndpoint ForDirectory(string directory)
    {
        string full = Path.GetFullPath(directory);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full + Environment.UserName)))[..24];
        return new(full, "openac-peer-" + hash);
    }
    internal void Prepare()
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    internal async Task<Stream> ConnectAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(1000, token).ConfigureAwait(false); return pipe; }
            catch { pipe.Dispose(); throw; }
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
    internal void StartHub()
    {
        string name = OperatingSystem.IsWindows() ? "acdream-headless.exe" : "acdream-headless";
        string executable = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(executable)) throw new FileNotFoundException("The local peer hub requires the installed headless host.", executable);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("peer-hub"); info.ArgumentList.Add(Directory);
        using Process? process = Process.Start(info);
        if (process is null) throw new IOException("Could not start the peer hub.");
    }
}
