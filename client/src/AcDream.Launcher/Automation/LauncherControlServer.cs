using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace AcDream.Launcher.Automation;

/// <summary>Opt-in, same-user pipe transport for repeatable launcher-driven tests.</summary>
internal sealed class LauncherControlServer : IDisposable
{
    internal const int MaximumRequestBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _stop = new();
    private readonly NamedPipeServerStream _pipe;
    private readonly Func<ControlRequest, CancellationToken, Task<ControlReply>> _handle;
    private readonly Action<string> _reportError;
    private readonly TimeSpan _requestTimeout;
    private int _disposed;
    internal Task Completion { get; }

    public LauncherControlServer(string name,
        Func<ControlRequest, CancellationToken, Task<ControlReply>> handle,
        Action<string> reportError, TimeSpan? requestTimeout = null)
    {
        _handle = handle;
        _reportError = reportError;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        // Construction reserves the endpoint synchronously; another launcher cannot
        // silently share its command name. The pipe ACL restricts callers to this user.
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Completion = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _pipe.WaitForConnectionAsync(_stop.Token);
                using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                requestDeadline.CancelAfter(_requestTimeout);
                try
                {
                    byte[] prefix = new byte[4];
                    await _pipe.ReadExactlyAsync(prefix, requestDeadline.Token);
                    int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
                    ControlReply reply;
                    if (length is <= 0 or > MaximumRequestBytes)
                        reply = new(false, Error: "Invalid request length.");
                    else
                    {
                        byte[] payload = new byte[length];
                        await _pipe.ReadExactlyAsync(payload, requestDeadline.Token);
                        try
                        {
                            ControlRequest? request = JsonSerializer.Deserialize<ControlRequest>(payload, Json);
                            reply = request is null ? new(false, Error: "Empty request.")
                                : await _handle(request, requestDeadline.Token).WaitAsync(requestDeadline.Token);
                        }
                        catch (JsonException)
                        {
                            reply = new(false, Error: "Invalid JSON request.");
                        }
                        catch (Exception ex) when (ex is AcDream.Launcher.Core.Orchestration.LauncherOperationException
                            or AcDream.Launcher.Core.Updates.LauncherUpdateException)
                        {
                            reply = new(false, Error: "Launcher refused the operation; inspect its session status.");
                        }
                    }
                    byte[] response = JsonSerializer.SerializeToUtf8Bytes(reply, Json);
                    BinaryPrimitives.WriteInt32LittleEndian(prefix, response.Length);
                    await _pipe.WriteAsync(prefix, requestDeadline.Token);
                    await _pipe.WriteAsync(response, requestDeadline.Token);
                    await _pipe.FlushAsync(requestDeadline.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    if (!_stop.IsCancellationRequested)
                        _reportError($"Launcher control connection ended: {ex.GetType().Name}. Command outcome may be unknown; inspect status before retrying.");
                }
                finally
                {
                    if (!_stop.IsCancellationRequested)
                        _pipe.Disconnect();
                }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Application shutdown cancels the pending connection/read.
        }
        catch (Exception ex)
        {
            _reportError($"Launcher control server stopped: {ex.GetType().Name}.");
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _pipe.Dispose();
        _ = Completion.ContinueWith(task =>
        {
            _ = task.Exception; // RunAsync has already reported a terminal transport failure.
            _stop.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
