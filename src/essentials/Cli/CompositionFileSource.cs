using System.Text;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Cli;

/// <summary>Reads one Workbench-style local bundle and detects edits before publication.</summary>
public sealed class CompositionFileSource
{
    private readonly string _hostDirectory;

    private CompositionFileSource(string hostDirectory, SourceSnapshot snapshot)
    {
        _hostDirectory = hostDirectory;
        Snapshot = snapshot;
    }

    public SourceSnapshot Snapshot { get; }

    public static CompositionFileSource Open(string hostDirectory, string shellId, string environment)
    {
        if (!SelectionValueRules.IsSafeReference(shellId) || !IsSafeEnvironment(environment))
            throw CliRefusal.Usage("bridge-source-invalid", "The selected shell or environment identity is invalid.");

        var directory = FullDirectory(hostDirectory);
        var files = ReadSupportedFiles(directory, rechecking: false);
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

        return new CompositionFileSource(directory, snapshot);
    }

    public void VerifyUnchanged()
    {
        IReadOnlyDictionary<string, byte[]> current;
        try
        {
            current = ReadSupportedFiles(_hostDirectory, rechecking: true);
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

    private static IReadOnlyDictionary<string, byte[]> ReadSupportedFiles(string directory, bool rechecking)
    {
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(path);
                if (!IsSupportedName(name))
                    continue;
                if (files.ContainsKey(name))
                    throw CliRefusal.Usage("bridge-source-duplicate", "The host contains case-equivalent source file names.");

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

    private static bool IsSupportedName(string name) =>
        name.Equals("shells.json", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) ||
        (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
         (name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase) ||
          name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)) &&
         IsSafeEnvironment(name[(name.IndexOf('.') + 1)..^5]));
}
