using System.Text;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Cli;

/// <summary>Reads one Workbench-style local bundle and detects edits before publication.</summary>
public sealed class CompositionFileSource
{
    private readonly string _hostDirectory;
    private readonly CompositionFileReader? _candidateReader;

    private CompositionFileSource(string hostDirectory, SourceSnapshot snapshot, CompositionFileReader? candidateReader)
    {
        _hostDirectory = hostDirectory;
        Snapshot = snapshot;
        _candidateReader = candidateReader;
    }

    public SourceSnapshot Snapshot { get; }

    public static CompositionFileSource Open(string hostDirectory, string shellId, string environment) =>
        Capture(hostDirectory, shellId, environment, candidateReader: null);

    /// <summary>Freezes all supported source files with candidate-only byte and count limits.</summary>
    public static CompositionFileSource OpenForCandidate(string hostDirectory, string shellId, string environment,
        CompositionFileReader? reader = null) => Capture(hostDirectory, shellId, environment, reader ?? new CompositionFileReader());

    private static CompositionFileSource Capture(string hostDirectory, string shellId, string environment,
        CompositionFileReader? candidateReader)
    {
        if (!SelectionValueRules.IsSafeReference(shellId) || !IsSafeEnvironment(environment) ||
            (candidateReader is not null && (shellId.Length > 128 || environment.Length > 128)))
            throw CliRefusal.Usage("bridge-source-invalid", "The selected shell or environment identity is invalid.");

        var directory = FullDirectory(hostDirectory);
        var files = ReadSupportedFiles(directory, rechecking: false, candidateReader);
        var names = files.Keys.ToHashSet(StringComparer.Ordinal);
        var shellOverlay = $"shells.{environment}.json";
        if (!names.Contains("shells.json") || !names.Contains("appsettings.json") || !names.Contains(shellOverlay))
            throw CliRefusal.Resolution("bridge-source-missing", "A required selected host source file is missing.");

        var appsettingsOverlay = $"appsettings.{environment}.json";
        var selection = new SourceSelection(shellId, environment, shellOverlay,
            names.Contains(appsettingsOverlay) ? appsettingsOverlay : null);
        var snapshot = SourceSnapshot.Freeze(selection, files);
        try
        {
            foreach (var name in snapshot.FileNames)
                _ = snapshot.ReadText(name);
        }
        catch (DecoderFallbackException)
        {
            throw CliRefusal.Usage("bridge-source-invalid", "A selected host source file is not valid UTF-8.");
        }

        return new CompositionFileSource(directory, snapshot, candidateReader);
    }

    public void VerifyUnchanged()
    {
        IReadOnlyDictionary<string, byte[]> current;
        try
        {
            current = ReadSupportedFiles(_hostDirectory, rechecking: true, _candidateReader);
        }
        catch (CliRefusal)
        {
            throw CliRefusal.Resolution("bridge-source-changed", "A copied host source file changed after preview.");
        }

        if (!Snapshot.FileNames.ToHashSet(StringComparer.Ordinal).SetEquals(current.Keys) ||
            current.Any(file => !Snapshot.ContentMatches(file.Key, file.Value)))
            throw CliRefusal.Resolution("bridge-source-changed", "A copied host source file changed after preview.");
    }

    private static string FullDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var info = new DirectoryInfo(fullPath);
            if (!info.Exists)
                throw CliRefusal.Resolution("bridge-source-missing", "The selected host directory is missing.");
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw CliRefusal.Resolution("bridge-source-unreadable", "The selected host directory is not a supported local source.");
            return fullPath;
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw CliRefusal.Resolution("bridge-source-unreadable", "The selected host directory could not be inspected.");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ReadSupportedFiles(string directory, bool rechecking,
        CompositionFileReader? candidateReader)
    {
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var totalBytes = 0;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(path);
                if (!IsSupportedName(name))
                    continue;
                if (files.ContainsKey(name))
                    throw CliRefusal.Usage("bridge-source-duplicate", "The host contains case-equivalent source file names.");

                if (candidateReader is not null)
                {
                    if (files.Count >= CompositionFileReader.MaximumFiles)
                        throw CompositionFileReader.LimitExceeded();
                    var captured = candidateReader.Read(path, Math.Min(CompositionFileReader.MaximumFileBytes,
                        CompositionFileReader.MaximumContextBytes - totalBytes));
                    totalBytes += captured.Length;
                    files.Add(name, captured);
                    continue;
                }

                var info = new FileInfo(path);
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    info.Attributes.HasFlag(FileAttributes.Directory))
                    throw CliRefusal.Resolution("bridge-source-unreadable", "A host source file is not a regular local file.");

                var bytes = File.ReadAllBytes(path);
                info.Refresh();
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw CliRefusal.Resolution("bridge-source-unreadable", "A host source file changed while being read.");
                files.Add(name, bytes);
            }
            return files;
        }
        catch (CliRefusal refusal) when (candidateReader is not null && refusal.Code == "composition-input-unreadable")
        {
            throw CliRefusal.Resolution(rechecking ? "bridge-source-changed" : "bridge-source-unreadable",
                rechecking ? "A copied host source file changed after preview." : "A host source file is not a regular local file.");
        }
        catch (CliRefusal) when (!rechecking)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw CliRefusal.Resolution(rechecking ? "bridge-source-changed" : "bridge-source-unreadable",
                rechecking ? "A copied host source file changed after preview." : "A host source file could not be read.");
        }
    }

    private static bool IsSafeEnvironment(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    internal static bool IsSupportedFileName(string name) => IsSupportedName(name);

    private static bool IsSupportedName(string name) =>
        name.Equals("shells.json", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) ||
        (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
         (name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase) ||
          name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)) &&
         IsSafeEnvironment(name[(name.IndexOf('.') + 1)..^5]));
}
