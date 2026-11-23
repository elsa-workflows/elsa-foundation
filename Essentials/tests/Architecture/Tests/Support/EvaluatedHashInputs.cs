using System.Diagnostics;
using System.Text.Json;

namespace Elsa.Architecture.Tests;

/// <summary>Uses the SDK's evaluated Compile items, including linked and conditional sources.</summary>
internal static class EvaluatedHashInputs
{
    internal static IReadOnlyList<string> Read(string project, string configuration, IReadOnlyList<string> buildSettings)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "msbuild", project, "-nologo", "-verbosity:quiet", "-property:Configuration=" + configuration,
                     "-getProperty:MSBuildAllProjects", "-getItem:Compile,AdditionalFiles,EditorConfigFiles,Analyzer" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the SDK input evaluation.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var text = output.GetAwaiter().GetResult();
        var diagnostic = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException($"SDK input evaluation failed for {project}: {diagnostic}\n{text}");
        // SDK warnings can precede the structured evaluation result. Parsing still fails closed if it is incomplete.
        var beginning = text.IndexOf('{');
        if (beginning < 0) throw new InvalidOperationException($"SDK input evaluation returned no JSON for {project}.");
        using var document = JsonDocument.Parse(text[beginning..]);
        return Parse(document.RootElement, project, buildSettings);
    }

    internal static IReadOnlyList<string> Parse(JsonElement root, string project, IReadOnlyList<string> buildSettings)
    {
        var files = new HashSet<string>(buildSettings, StringComparer.Ordinal) { Path.GetFullPath(project) };
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

    /// <summary>A conservative shared configuration closure also covers imports that reset MSBuildAllProjects.</summary>
    internal static IReadOnlyList<string> BuildSettings(string root)
    {
        var result = new List<string>();
        Visit(root);
        return result;
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFiles(directory))
                if (Path.GetExtension(path) is ".props" or ".targets" || Path.GetFileName(path) is "global.json" or ".editorconfig") result.Add(path);
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (Path.GetFileName(child) is not ("bin" or "obj" or "node_modules" or ".git" or ".agent-prefs")) Visit(child);
        }
    }
}
