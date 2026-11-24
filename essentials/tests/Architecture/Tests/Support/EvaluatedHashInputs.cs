using System.Text.Json;
using System.Xml.Linq;

namespace Elsa.Architecture.Tests;

/// <summary>Uses evaluated SDK compile/import inputs, including linked sources and referenced-project constants.</summary>
internal sealed class EvaluatedHashInputs(string configuration)
{
    private readonly Dictionary<string, IReadOnlyList<string>> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlySet<string>> compileSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> intermediateRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> packageSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, SdkCompileReferences.Asset>> compileReferences = new(StringComparer.Ordinal);
    private readonly SdkCompileReferences sdkReferences = new();
    private readonly HashSet<string> evaluating = new(StringComparer.Ordinal);

    internal IReadOnlyList<string> Read(string project)
    {
        project = Path.GetFullPath(project);
        if (cache.TryGetValue(project, out var known)) return known;
        if (!evaluating.Add(project)) throw new InvalidOperationException($"A project-input cycle cannot be admitted: {project}");
        var imports = Path.Join(Path.GetTempPath(), "elsa-s9-msbuild-" + Guid.NewGuid().ToString("N") + ".xml");
        var result = Path.Join(Path.GetTempPath(), "elsa-s9-msbuild-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // No node reuse: a lingering worker node inherits the redirected stdout/stderr pipes, so the
            // reads never end even though dotnet exited (observed on Windows as an unbounded hang).
            var common = new[] { "msbuild", project, "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-property:Configuration=" + configuration };
            // Resolve through PATH so an installed build-slot wrapper remains in force. The shared
            // helper clears inherited testhost MSBuild variables and bounds/cleans up the process.
            var startDirectory = Path.GetDirectoryName(project)!;
            // Resolve final compiler inputs using existing project outputs. This target does not
            // invoke Csc; BuildProjectReferences=false makes referenced projects return GetTargetPath.
            var (code, text) = ChildProcess.Run("dotnet", [.. common, "-target:FindReferenceAssembliesForReferences", "-property:BuildProjectReferences=false",
                "-getProperty:MSBuildAllProjects,IntermediateOutputPath,ProjectAssetsFile", "-getItem:Compile,AdditionalFiles,EditorConfigFiles,Analyzer,ProjectReference,ReferencePathWithRefAssemblies",
                "-getResultOutputFile:" + result], workingDirectory: startDirectory, timeout: TimeSpan.FromMinutes(5));
            if (code != 0) throw new InvalidOperationException($"SDK input evaluation failed for {project}: {text}");
            // Warnings may follow valid result JSON on stderr. Console output is diagnostic
            // text, never structured compiler evidence; the SDK owns this dedicated result file.
            using var document = ReadResultFile(result, project);
            compileSources[project] = document.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
                .Select(item => Path.GetFullPath(item.GetProperty("FullPath").GetString()!))
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var assetsFile = document.RootElement.GetProperty("Properties").GetProperty("ProjectAssetsFile").GetString();
            if (string.IsNullOrWhiteSpace(assetsFile)) throw new InvalidOperationException("SDK omitted restored assets for " + project);
            packageSources[project] = JsonHashExternalSources.Read(Path.GetFullPath(assetsFile, startDirectory), compileSources[project]);
            compileReferences[project] = sdkReferences.Read(document.RootElement);
            var intermediate = document.RootElement.GetProperty("Properties").GetProperty("IntermediateOutputPath").GetString();
            if (string.IsNullOrWhiteSpace(intermediate)) throw new InvalidOperationException($"SDK input evaluation omitted IntermediateOutputPath for {project}.");
            intermediateRoots[project] = Path.GetFullPath(intermediate, startDirectory);
            var (importCode, importOutput) = ChildProcess.Run("dotnet", [.. common, "-preprocess:" + imports], workingDirectory: startDirectory, timeout: TimeSpan.FromMinutes(5));
            if (importCode != 0) throw new InvalidOperationException($"SDK import evaluation failed for {project}: {importOutput}");
            var settings = ParseImports(File.ReadAllText(imports));
            var selection = SelectedGlobalJson(startDirectory);
            var own = Parse(document.RootElement, project, selection is null ? settings : [.. settings, selection])
                .Concat(compileReferences[project].Values.Select(reference => reference.Path)).Distinct(StringComparer.Ordinal).ToArray();
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
            if (File.Exists(result)) File.Delete(result);
        }
    }

    internal static JsonDocument ReadResultFile(string result, string project)
    {
        if (!File.Exists(result)) throw new InvalidOperationException($"SDK input evaluation returned no structured result for {project}: {result}");
        return JsonDocument.Parse(File.ReadAllText(result));
    }

    internal IReadOnlySet<string> CompileSources(string project) { _ = Read(project); return compileSources[Path.GetFullPath(project)]; }
    internal string IntermediateRoot(string project) { _ = Read(project); return intermediateRoots[Path.GetFullPath(project)]; }
    internal IReadOnlyDictionary<string, string> PackageSources(string project) { _ = Read(project); return packageSources[Path.GetFullPath(project)]; }
    internal IReadOnlyDictionary<string, SdkCompileReferences.Asset> CompileReferences(string project) { _ = Read(project); return compileReferences[Path.GetFullPath(project)]; }

    internal static IReadOnlyList<string> Close(IReadOnlyList<string> own, IEnumerable<IReadOnlyList<string>> dependencies) =>
        own.Concat(dependencies.SelectMany(files => files)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> Parse(JsonElement root, string project, IReadOnlyList<string> imports)
    {
        var files = new HashSet<string>(imports, StringComparer.Ordinal) { Path.GetFullPath(project) };
        if (root.GetProperty("Properties").TryGetProperty("ProjectAssetsFile", out var assets) && !string.IsNullOrWhiteSpace(assets.GetString()))
            files.Add(Path.GetFullPath(assets.GetString()!, Path.GetDirectoryName(Path.GetFullPath(project))!));
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
