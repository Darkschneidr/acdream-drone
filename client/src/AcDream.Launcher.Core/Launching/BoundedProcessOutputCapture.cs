using System.Text;

namespace AcDream.Launcher.Core.Launching;

public sealed class BoundedProcessOutputCapture : IDisposable
{
    public const long DefaultMaxBytes = 2 * 1024 * 1024;
    public const int DefaultMaxFiles = 4;

    private static readonly byte[] Newline = "\n"u8.ToArray();

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly object _gate = new();
    private bool _directoryEnsured;
    private long _written;
    private bool _latchedOff;
    private bool _disposed;

    public BoundedProcessOutputCapture(
        string path,
        long maxBytes = DefaultMaxBytes,
        int maxFiles = DefaultMaxFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes),
                "The bounded capture size must be positive.");
        }
        if (maxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }

        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
    }

    public bool IsDone
    {
        get
        {
            lock (_gate)
            {
                return _latchedOff || _disposed;
            }
        }
    }

    public void AppendLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        byte[] textBytes = Encoding.UTF8.GetBytes(line);
        var buffer = new byte[textBytes.Length + Newline.Length];
        textBytes.CopyTo(buffer, 0);
        Newline.CopyTo(buffer, textBytes.Length);

        lock (_gate)
        {
            AppendLocked(buffer);
        }
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            AppendLocked(data);
        }
    }

    private void AppendLocked(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _disposed || _latchedOff)
        {
            return;
        }

        try
        {
            while (!data.IsEmpty)
            {
                long remaining = _maxBytes - _written;
                // Keep ordinary lines and stderr reads together. An oversized
                // write is split across generations without dropping bytes.
                if (remaining == 0
                    || (_written > 0 && data.Length <= _maxBytes && data.Length > remaining))
                {
                    RotateLocked();
                    remaining = _maxBytes;
                }

                int toWrite = (int)Math.Min(remaining, data.Length);
                WriteChunkLocked(data[..toWrite]);
                _written += toWrite;
                data = data[toWrite..];
            }
        }
        catch (Exception error) when (IsRecoverableIoFailure(error))
        {
            _latchedOff = true;
        }
    }

    private void WriteChunkLocked(ReadOnlySpan<byte> chunk)
    {
        EnsureDirectoryLocked();
        using FileStream stream = new(
            _path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        stream.Write(chunk);
        stream.Flush();
    }

    private void EnsureDirectoryLocked()
    {
        if (_directoryEnsured)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _directoryEnsured = true;
    }

    private void RotateLocked()
    {
        EnsureDirectoryLocked();
        if (_maxFiles == 1)
        {
            File.Delete(_path);
        }
        else
        {
            for (int generation = _maxFiles - 1; generation >= 1; generation--)
            {
                string source = generation == 1
                    ? _path
                    : BackupPath(generation - 1);
                if (File.Exists(source))
                    File.Move(source, BackupPath(generation), overwrite: true);
            }
        }
        _written = 0;
    }

    private string BackupPath(int generation) => $"{_path}.{generation}";

    private static bool IsRecoverableIoFailure(Exception error) =>
        error is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException
            or DirectoryNotFoundException;

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
