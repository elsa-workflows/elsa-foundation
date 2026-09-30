using System.Text;
using System.Diagnostics;

namespace Elsa.Cli;

/// <summary>Captures supplied composition inputs once and detects edits before reviewed publication.</summary>
internal sealed class CompositionInputSnapshot
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;

    private CompositionInputSnapshot(IReadOnlyDictionary<string, byte[]> files) => _files = files;

    public static CompositionInputSnapshot Open(IEnumerable<string> paths)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var fullPath = FullPath(path);
            if (files.ContainsKey(fullPath))
                continue;

            try
            {
                EnsureRegularFile(fullPath);
                var bytes = File.ReadAllBytes(fullPath);
                EnsureRegularFile(fullPath);
                files.Add(fullPath, bytes);
            }
            catch (CliRefusal)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw Unreadable();
            }
        }

        return new CompositionInputSnapshot(files);
    }

    public string ReadText(string path)
    {
        var fullPath = FullPath(path);
        if (!_files.TryGetValue(fullPath, out var bytes))
            throw Unreadable();

        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is DecoderFallbackException or ArgumentException)
        {
            throw CliRefusal.Usage("composition-input-invalid", "A supplied composition input is not valid encoded JSON text.");
        }
    }

    public void VerifyUnchanged()
    {
        foreach (var (path, snapshot) in _files)
        {
            try
            {
                EnsureRegularFile(path);
                if (!snapshot.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
                    throw Changed();
                EnsureRegularFile(path);
            }
            catch (CliRefusal)
            {
                throw Changed();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw Changed();
            }
        }
    }

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Unreadable();
        }
    }

    private static void EnsureRegularFile(string path)
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
            return false;
        }
    }

    private static string FindLinuxStat() => File.Exists("/usr/bin/stat") ? "/usr/bin/stat" :
        File.Exists("/bin/stat") ? "/bin/stat" : string.Empty;

    private static CliRefusal Unreadable() =>
        CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read as a regular local file.");

    private static CliRefusal Changed() =>
        CliRefusal.Resolution("composition-input-changed", "A supplied composition input changed after review.");
}
