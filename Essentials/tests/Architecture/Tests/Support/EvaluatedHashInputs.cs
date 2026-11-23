using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.Architecture.Tests;

/// <summary>Uses evaluated SDK compile/import inputs, including linked sources and referenced-project constants.</summary>
internal sealed class EvaluatedHashInputs(string configuration)
{
    private readonly Dictionary<string, IReadOnlyList<string>> cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> evaluating = new(StringComparer.Ordinal);

    internal IReadOnlyList<string> Read(string project)
    {
        project = Path.GetFullPath(project);
        if (cache.TryGetValue(project, out var known)) return known;
        if (!evaluating.Add(project)) throw new InvalidOperationException($"A project-input cycle cannot be admitted: {project}");
        var imports = Path.Join(Path.GetTempPath(), "elsa-s9-msbuild-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var common = new[] { "msbuild", project, "-nologo", "-verbosity:quiet", "-property:Configuration=" + configuration };
            // Resolve through PATH so an installed build-slot wrapper remains in force. The shared
            // helper clears inherited testhost MSBuild variables and bounds/cleans up the process.
            var startDirectory = Path.GetDirectoryName(project)!;
            var (code, text) = ChildProcess.Run("dotnet", [.. common, "-getProperty:MSBuildAllProjects", "-getItem:Compile,AdditionalFiles,EditorConfigFiles,Analyzer,ProjectReference"], workingDirectory: startDirectory, timeout: TimeSpan.FromMinutes(5));
            if (code != 0) throw new InvalidOperationException($"SDK input evaluation failed for {project}: {text}");
            var beginning = text.IndexOf('{');
            if (beginning < 0) throw new InvalidOperationException($"SDK input evaluation returned no JSON for {project}.");
            using var document = JsonDocument.Parse(text[beginning..]);
            var (importCode, importOutput) = ChildProcess.Run("dotnet", [.. common, "-preprocess:" + imports], workingDirectory: startDirectory, timeout: TimeSpan.FromMinutes(5));
            if (importCode != 0) throw new InvalidOperationException($"SDK import evaluation failed for {project}: {importOutput}");
            var settings = ParseImports(File.ReadAllText(imports));
            var selection = SelectedGlobalJson(startDirectory);
            var own = Parse(document.RootElement, project, selection is null ? settings : [.. settings, selection]);
            var dependencies = document.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray()
                .Select(item => item.GetProperty("FullPath").GetString()!).ToArray();
            var closed = Close(own, dependencies.Select(Read));
            cache[project] = closed;
            return closed;
        }
        finally
        {
            evaluating.Remove(project);
            if (File.Exists(imports)) File.Delete(imports);
        }
    }

    internal static IReadOnlyList<string> Close(IReadOnlyList<string> own, IEnumerable<IReadOnlyList<string>> dependencies) =>
        own.Concat(dependencies.SelectMany(files => files)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> Parse(JsonElement root, string project, IReadOnlyList<string> imports)
    {
        var files = new HashSet<string>(imports, StringComparer.Ordinal) { Path.GetFullPath(project) };
        foreach (var import in root.GetProperty("Properties").GetProperty("MSBuildAllProjects").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            files.Add(Path.GetFullPath(import));
        var items = root.GetProperty("Items");
        foreach (var group in new[] { "Compile", "AdditionalFiles", "EditorConfigFiles", "Analyzer" })
        {
            if (!items.TryGetProperty(group, out var entries)) throw new InvalidOperationException($"SDK input evaluation omitted {group} for {project}.");
            foreach (var entry in entries.EnumerateArray())
            {
                files.Add(Path.GetFullPath(entry.GetProperty("FullPath").GetString()!));
                if (entry.TryGetProperty("DefiningProjectFullPath", out var defining) && !string.IsNullOrWhiteSpace(defining.GetString()))
                    files.Add(Path.GetFullPath(defining.GetString()!));
            }
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    // dotnet starts SDK selection at its working directory. The resolver's similarly named
    // property is populated only for selection failure and cannot identify normal inputs.
    internal static string? SelectedGlobalJson(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory)); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Join(directory.FullName, "global.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>SDK preprocessing names the resolved import at each generated XML boundary; unrelated files are not inputs.</summary>
    internal static IReadOnlyList<string> ParseImports(string preprocessed)
    {
        var document = XDocument.Parse(preprocessed, LoadOptions.PreserveWhitespace);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var comment in document.DescendantNodes().OfType<XComment>())
        {
            var lines = comment.Value.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            if (lines.Length == 0 || !lines[0].StartsWith("===", StringComparison.Ordinal)) continue;
            foreach (var line in lines)
                if (Path.IsPathRooted(line) && File.Exists(line)) files.Add(Path.GetFullPath(line));
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }
}
