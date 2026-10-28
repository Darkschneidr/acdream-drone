using System.Text;
using AcDream.Launcher.Core.Launching;

namespace AcDream.Launcher.Core.Tests.Launching;

public sealed class BoundedProcessOutputCaptureTests
{
    [Fact]
    public void AppendLineWritesEachLineWithATrailingNewline()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path);

            capture.AppendLine("first");
            capture.AppendLine("second");
            capture.Dispose();

            Assert.Equal("first\nsecond\n", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ANullLineFromTheEndOfStreamSentinelIsANoOp()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path);

            capture.AppendLine("kept");
            capture.AppendLine(null);
            capture.Dispose();

            Assert.Equal("kept\n", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void RotatesAtTheBoundaryAndKeepsWritingRecentLines()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path, maxBytes: 12);

            capture.AppendLine("first");
            capture.AppendLine("next!");
            capture.AppendLine("third");

            Assert.False(capture.IsDone);
            Assert.Equal("first\nnext!\n", File.ReadAllText(path + ".1"));
            Assert.Equal("third\n", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void OversizedRawWritesKeepTheirTailAcrossGenerations()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path, maxBytes: 5);

            capture.Append(Encoding.UTF8.GetBytes("abcdefghijkl"));
            capture.Dispose();

            Assert.Equal("abcde", File.ReadAllText(path + ".2"));
            Assert.Equal("fghij", File.ReadAllText(path + ".1"));
            Assert.Equal("kl", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void OldestGenerationIsDiscardedButTheLatestOutputSurvivesExit()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(
                path,
                maxBytes: 4,
                maxFiles: 3);

            for (int i = 0; i < 8; i++)
                capture.AppendLine($"{i:000}");
            capture.Dispose();

            Assert.True(capture.IsDone);
            Assert.Equal("007\n", File.ReadAllText(path));
            Assert.Equal("006\n", File.ReadAllText(path + ".1"));
            Assert.Equal("005\n", File.ReadAllText(path + ".2"));
            Assert.False(File.Exists(path + ".3"));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void RotatingOneSessionDoesNotTouchAnotherSessionsLog()
    {
        string first = TempPath();
        string second = TempPath();
        try
        {
            using var firstCapture = new BoundedProcessOutputCapture(first, maxBytes: 4);
            using var secondCapture = new BoundedProcessOutputCapture(second, maxBytes: 4);
            secondCapture.AppendLine("ok");
            firstCapture.AppendLine("one");
            firstCapture.AppendLine("two");

            Assert.Equal("one\n", File.ReadAllText(first + ".1"));
            Assert.Equal("two\n", File.ReadAllText(first));
            Assert.False(File.Exists(second + ".1"));
            Assert.Equal("ok\n", File.ReadAllText(second));
        }
        finally
        {
            TryDelete(first);
            TryDelete(second);
        }
    }

    [Fact]
    public void AppendCreatesTheSessionDirectoryOnFirstWrite()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "acdream-406-capture-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "client.err.log");
        Assert.False(Directory.Exists(directory));

        try
        {
            using var capture = new BoundedProcessOutputCapture(path);
            capture.AppendLine("hello");
            capture.Dispose();

            Assert.True(File.Exists(path));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void RawByteAppendsAreConcatenatedWithoutAnImpliedLineBoundary()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path);

            capture.Append(Encoding.UTF8.GetBytes("abc"));
            capture.Append(Encoding.UTF8.GetBytes("def"));
            capture.Dispose();

            Assert.Equal("abcdef", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void EmptyAppendsAreNoOps()
    {
        string path = TempPath();
        try
        {
            using var capture = new BoundedProcessOutputCapture(path);

            capture.Append(ReadOnlySpan<byte>.Empty);
            capture.AppendLine(string.Empty);
            capture.Dispose();

            // An empty string line still gets its trailing newline —
            // only a genuinely zero-length byte span (or a null line) is
            // a true no-op.
            Assert.Equal("\n", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AppendAfterDisposeIsASilentNoOp()
    {
        string path = TempPath();
        try
        {
            var capture = new BoundedProcessOutputCapture(path);
            capture.AppendLine("before");
            capture.Dispose();

            capture.AppendLine("after — must not throw or reopen the file");

            Assert.Equal("before\n", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ConstructorRejectsANonPositiveMaxBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundedProcessOutputCapture(TempPath(), maxBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundedProcessOutputCapture(TempPath(), maxBytes: -1));
    }

    [Fact]
    public void ConstructorRejectsANonPositiveMaxFiles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundedProcessOutputCapture(TempPath(), maxFiles: 0));
    }

    [Fact]
    public void ConstructorRejectsANullOrBlankPath()
    {
        Assert.Throws<ArgumentException>(() => new BoundedProcessOutputCapture(""));
        Assert.Throws<ArgumentException>(() => new BoundedProcessOutputCapture("   "));
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        "acdream-406-capture-" + Guid.NewGuid().ToString("N") + ".log");

    private static void TryDelete(string path)
    {
        for (int generation = 0; generation < BoundedProcessOutputCapture.DefaultMaxFiles;
             generation++)
        {
            try
            {
                File.Delete(generation == 0 ? path : $"{path}.{generation}");
            }
            catch (IOException)
            {
            }
        }
    }
}
