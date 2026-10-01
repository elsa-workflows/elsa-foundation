using System.Diagnostics;

namespace Elsa.Cli;

/// <summary>Reads finite candidate files through injectable regular-file and stream boundaries.</summary>
/// <remarks>Byte bounds are enforced during reads, including streams with no trustworthy length.</remarks>
public sealed class CompositionFileReader
{
    public const int MaximumFiles = 32;
    public const int MaximumFileBytes = 1024 * 1024;
    public const int MaximumContextBytes = 8 * MaximumFileBytes;

    private readonly Action<string> _ensureRegularFile;
    private readonly Func<string, Stream> _openRead;

    public CompositionFileReader() : this(EnsureRegularFile, RegularFileOpener.OpenRead) { }

    public CompositionFileReader(Action<string> ensureRegularFile, Func<string, Stream> openRead)
    {
        ArgumentNullException.ThrowIfNull(ensureRegularFile);
        ArgumentNullException.ThrowIfNull(openRead);
        _ensureRegularFile = ensureRegularFile;
        _openRead = openRead;
    }

    /// <summary>Returns captured bytes or a fixed, value-free refusal for unreadable or excessive input.</summary>
    /// <exception cref="CliRefusal">The input is unreadable, nonregular or exceeds the candidate bound.</exception>
    public byte[] Read(string path, int maximumBytes)
    {
        if (maximumBytes < 0 || maximumBytes > MaximumFileBytes)
            throw LimitExceeded();
        try
        {
            _ensureRegularFile(path);
            using var input = _openRead(path) ?? throw Unreadable();
            using var captured = new MemoryStream();
            var buffer = new byte[Math.Min(81920, maximumBytes + 1)];
            while (true)
            {
                // Probe at most one byte past the remaining bound, without seeking or using Length.
                var count = Math.Min(buffer.Length, maximumBytes - (int)captured.Length + 1);
                var read = input.Read(buffer, 0, count);
                if (read == 0)
                    break;
                if (read > maximumBytes - captured.Length)
                    throw LimitExceeded();
                captured.Write(buffer, 0, read);
            }
            _ensureRegularFile(path);
            return captured.ToArray();
        }
        catch (CliRefusal refusal)
        {
            throw refusal.Code == "candidate-capture-invalid" ? LimitExceeded() : Unreadable();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                                         NotSupportedException or InvalidOperationException)
        {
            throw Unreadable();
        }
    }

    internal static CliRefusal LimitExceeded() =>
        CliRefusal.Usage("candidate-capture-invalid", "A candidate input exceeds the supported capture limits.");

    /// <summary>Refuses an input whose path does not currently identify a regular local file.</summary>
    public static void EnsureRegularFile(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists || info.LinkTarget is not null ||
            (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0 ||
            !IsUnixRegularFile(path))
            throw Unreadable();
    }

    private static bool IsUnixRegularFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsMacOS() ? "/usr/bin/stat" : OperatingSystem.IsLinux() ? FindLinuxStat() : string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (string.IsNullOrEmpty(startInfo.FileName))
            return false;

        if (OperatingSystem.IsMacOS())
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("%HT");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("%F");
        }
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(path);
        startInfo.Environment["LC_ALL"] = "C";

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            if (!process.WaitForExit(2_000))
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Timeout always refuses; still attempt the bounded cleanup wait if termination fails.
                }
                if (!process.WaitForExit(1_000))
                    return false;
            }

            var standardOutput = output.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            if (standardOutput.EndsWith("\r\n", StringComparison.Ordinal))
                standardOutput = standardOutput[..^2];
            else if (standardOutput.EndsWith('\n') || standardOutput.EndsWith('\r'))
                standardOutput = standardOutput[..^1];
            return !timedOut && process.ExitCode == 0 && standardOutput == (OperatingSystem.IsMacOS() ? "Regular File" : "regular file");
        }
        catch (Exception)
        {
            // Any stat/process anomaly fails closed. Do not expose a path-bearing platform exception
            // or admit an input whose regular-file identity could not be established.
            return false;
        }
    }

    private static string FindLinuxStat() => File.Exists("/usr/bin/stat") ? "/usr/bin/stat" :
        File.Exists("/bin/stat") ? "/bin/stat" : string.Empty;

    private static CliRefusal Unreadable() =>
        CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read as a regular local file.");
}
