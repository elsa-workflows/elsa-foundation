using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Maps.Generator;

/// <summary>
/// Writes the v1 map layer: project-reference, package, feature, test and spec-status maps, the
/// maps-v1-findings report, and <c>docs/maps/manifest.json</c>.
/// </summary>
public static partial class CoreMapsGenerator
{
    [GeneratedRegex("<TargetFramework>([^<]*)</TargetFramework>", RegexOptions.Compiled)]
    private static partial Regex TargetFrameworkPattern { get; }

    [GeneratedRegex("<IsPackable>([^<]*)</IsPackable>", RegexOptions.Compiled)]
    private static partial Regex IsPackablePattern { get; }

    [GeneratedRegex(@"^\s*public\s.*class\s+(?<name>[A-Za-z0-9_]+)\s*:(?<bases>[^{]*)", RegexOptions.Compiled)]
    private static partial Regex FeatureClassPattern { get; }

    private sealed record ProjectRow(string Name, string RelativePath, string Kind, string Domain, IReadOnlyList<string> References);
    private sealed record PackageRow(string Id, string Version, string Project, string RelativePath, string Kind);
    private sealed record FeatureRow(string ClassName, string Kind, string Project, string BaseText, string RelativePath);

    /// <summary>Generates the v1 layer and returns the repo-relative paths written.</summary>
    public static IReadOnlyList<string> Generate(RepoContext repo)
    {
        var projectPaths = repo.ListFiles("src/*.csproj", "tests/*.csproj");
        var packages = PackageVersions.Load(repo);

        var projects = projectPaths.Select(path => ReadProject(repo, path)).ToArray();
        var packageRows = projects.SelectMany(project => ReadPackages(repo, packages, project)).ToArray();
        var features = ReadFeatures(repo, projects);
        var specs = SpecScanner.Read(repo);

        var testReferences = projects
            .Where(project => project.Kind == "test")
            .SelectMany(project => project.References)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var untestedSources = projects
            .Where(project => project.Kind == "source" && !testReferences.Contains(project.Name))
            .ToArray();

        WriteProjectReferenceMap(repo, projects);
        WritePackageMap(repo, packageRows);
        WriteFeatureMap(repo, features);
        WriteTestMap(repo, projects, testReferences, untestedSources);
        WriteSpecStatusMap(repo, specs);
        WriteFindings(repo, projects, packageRows, features, specs, untestedSources.Length);
        WriteManifest(repo, projects, packageRows, features, specs);

        return
        [
            "docs/maps/project-reference-map.md",
            "docs/maps/package-map.md",
            "docs/maps/feature-map.md",
            "docs/maps/test-map.md",
            "docs/maps/spec-status-map.md",
            "docs/reports/maps-v1-findings.md",
            "docs/maps/manifest.json"
        ];
    }

    private static ProjectRow ReadProject(RepoContext repo, string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);

        // Not deduplicated: this layer reports the raw reference count, unlike the domain map.
        var references = ProjectGraph.ReadReferencedProjectNames(repo.Absolute(relativePath));

        return new ProjectRow(
            name,
            relativePath,
            relativePath.StartsWith("tests/", StringComparison.Ordinal) ? "test" : "source",
            DomainGroup(name),
            references);
    }

    /// <summary>
    /// This layer's grouping, which differs from the domain map's: a bare <c>Server</c> project stays
    /// <c>Server</c> rather than being folded into <c>Elsa.Workbench</c>.
    /// </summary>
    private static string DomainGroup(string project)
    {
        if (project == "Server") return "Server";
        if (project.StartsWith("Test.", StringComparison.Ordinal)) return "Test";
        if (project.StartsWith("Elsa3.", StringComparison.Ordinal)) return "Elsa3";
        if (!project.StartsWith("Elsa.", StringComparison.Ordinal)) return "Other";

        var segments = project.Split('.');
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
    }

    private static IEnumerable<PackageRow> ReadPackages(RepoContext repo, PackageVersions packages, ProjectRow project) =>
        packages.ReferencesFor(repo.Absolute(project.RelativePath))
            .Select(entry => entry.Split(' ', 2))
            .Select(parts => new PackageRow(parts[0], parts[1], project.Name, project.RelativePath, project.Kind));

    private static IReadOnlyList<FeatureRow> ReadFeatures(RepoContext repo, IReadOnlyList<ProjectRow> projects)
    {
        var rows = new List<FeatureRow>();

        foreach (var relativePath in repo.ListFiles("src/*.cs"))
        {
            var text = File.ReadAllText(repo.Absolute(relativePath));
            if (!text.Contains("FeatureBase", StringComparison.Ordinal) && !text.Contains("IShellFeature", StringComparison.Ordinal))
                continue;

            var owner = ProjectGraph.OwningProject(projects, relativePath, project => project.RelativePath);
            if (owner is null) continue;

            foreach (var line in text.Split('\n'))
            {
                var match = FeatureClassPattern.Match(line.TrimEnd('\r'));
                if (!match.Success) continue;

                var bases = match.Groups["bases"].Value;
                var baseText = Regex.Replace(bases.Split('<')[0], @"\s+", " ").Trim();
                if (baseText.Length == 0) continue;

                var kind = Classify(bases);
                if (kind is null) continue;

                rows.Add(new FeatureRow(match.Groups["name"].Value, kind, owner.Name, baseText, relativePath));
            }
        }

        return rows;
    }

    /// <summary>The later probes win, matching the shell's sequence of overriding assignments.</summary>
    private static string? Classify(string bases)
    {
        string? kind = null;
        if (Regex.IsMatch(bases, @"IShellFeature|FastEndpointsFeatureBase|EFCore.*FeatureBase|FeatureBase")) kind = "feature-base-derived";
        if (Regex.IsMatch(bases, @"\bIShellFeature\b")) kind = "direct IShellFeature";
        if (Regex.IsMatch(bases, @"\bFastEndpointsFeatureBase\b")) kind = "FastEndpoints feature";
        if (Regex.IsMatch(bases, "EFCore.*FeatureBase")) kind = "EF Core feature base";
        return kind;
    }

    private static string TargetFramework(RepoContext repo, ProjectRow project)
    {
        var match = TargetFrameworkPattern.Match(File.ReadAllText(repo.Absolute(project.RelativePath)));
        return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : "-";
    }

    private static string IsPackable(RepoContext repo, ProjectRow project)
    {
        var match = IsPackablePattern.Match(File.ReadAllText(repo.Absolute(project.RelativePath)));
        return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : "default";
    }

    private static void WriteProjectReferenceMap(RepoContext repo, IReadOnlyList<ProjectRow> projects)
    {
        var map = new MapBuilder()
            .Line("# Project Reference Map")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("Records direct project references only.")
            .Line()
            .Line("## Summary")
            .Line()
            .Line($"- Source projects: {projects.Count(project => project.Kind == "source")}")
            .Line($"- Test projects: {projects.Count(project => project.Kind == "test")}")
            .Line($"- Direct project references: {projects.Sum(project => project.References.Count)}")
            .Line()
            .Line("## Projects")
            .Line()
            .Row("Project", "Kind", "Domain", "Target", "Packable", "Direct project references")
            .Line("|---|---|---|---|---|---|");

        foreach (var project in projects)
            map.Row(
                $"[{project.Name}](../../{project.RelativePath})",
                project.Kind,
                project.Domain,
                TargetFramework(repo, project),
                IsPackable(repo, project),
                MarkdownTable.Cell(MarkdownTable.Lines(project.References)));

        map.Line()
            .Line("## Domain Groups")
            .Line()
            .Row("Domain", "Source projects", "Test projects")
            .Line("|---|---:|---:|");

        foreach (var domain in projects.Select(project => project.Domain).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            map.Row(
                domain,
                projects.Count(project => project.Domain == domain && project.Kind == "source").ToString(),
                projects.Count(project => project.Domain == domain && project.Kind == "test").ToString());

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "project-reference-map.md"), map.Content);
    }

    private static void WritePackageMap(RepoContext repo, IReadOnlyList<PackageRow> packages)
    {
        var map = new MapBuilder()
            .Line("# Package Map")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("Records direct `PackageReference` entries only; it does not compute transitive closure.")
            .Line()
            .Line("## Package Version Clusters")
            .Line()
            .Row("Package", "Versions", "Projects")
            .Line("|---|---|---|");

        foreach (var id in packages.Select(package => package.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var forId = packages.Where(package => package.Id == id).ToArray();
            map.Row(
                id,
                MarkdownTable.Cell(MarkdownTable.Lines(forId.Select(package => package.Version).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))),
                MarkdownTable.Cell(MarkdownTable.Lines(forId.Select(package => $"{package.Project} ({package.Version})").Order(StringComparer.Ordinal))));
        }

        map.Line()
            .Line("## Direct Package References")
            .Line()
            .Row("Project", "Kind", "Package", "Version")
            .Line("|---|---|---|---|");

        foreach (var package in packages.OrderBy(package => package.Project, StringComparer.Ordinal).ThenBy(package => package.Id, StringComparer.Ordinal))
            map.Row($"[{package.Project}](../../{package.RelativePath})", package.Kind, package.Id, package.Version);

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "package-map.md"), map.Content);
    }

    private static void WriteFeatureMap(RepoContext repo, IReadOnlyList<FeatureRow> features)
    {
        var map = new MapBuilder()
            .Line("# Feature Map")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("Discovers public CShells feature classes and feature-base-derived classes by scanning source text.")
            .Line()
            .Line("## Summary")
            .Line()
            .Line($"- Discovered feature classes: {features.Count}")
            .Line()
            .Line("## Features")
            .Line()
            .Row("Feature class", "Kind", "Project", "Base/interface", "File")
            .Line("|---|---|---|---|---|");

        foreach (var feature in features.OrderBy(feature => feature.Project, StringComparer.Ordinal).ThenBy(feature => feature.ClassName, StringComparer.Ordinal))
            map.Row(
                feature.ClassName,
                feature.Kind,
                feature.Project,
                MarkdownTable.Cell(feature.BaseText),
                $"[{Path.GetFileName(feature.RelativePath)}](../../{feature.RelativePath})");

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "feature-map.md"), map.Content);
    }

    private static void WriteTestMap(
        RepoContext repo,
        IReadOnlyList<ProjectRow> projects,
        IReadOnlySet<string> testReferences,
        IReadOnlyList<ProjectRow> untestedSources)
    {
        var testProjects = projects.Where(project => project.Kind == "test").ToArray();

        var map = new MapBuilder()
            .Line("# Test Map")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("Records direct test-project references and test source-file inventory; it does not claim behavioral coverage.")
            .Line()
            .Line("## Summary")
            .Line()
            .Line($"- Test projects: {testProjects.Length}")
            .Line($"- Source projects directly referenced by at least one test project: {testReferences.Count}")
            .Line($"- Source projects not directly referenced by test projects: {untestedSources.Count}")
            .Line()
            .Line("## Test Projects")
            .Line()
            .Row("Test project", "Direct production references")
            .Line("|---|---|");

        foreach (var project in testProjects)
            map.Row($"[{project.Name}](../../{project.RelativePath})", MarkdownTable.Cell(MarkdownTable.Lines(project.References)));

        map.Line()
            .Line("## Test Source Files")
            .Line()
            .Row("Test project", "Source files")
            .Line("|---|---|");

        foreach (var project in testProjects)
        {
            var directory = Path.GetDirectoryName(project.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            var files = repo.ListFiles($"{directory}/*.cs")
                .Select(file => $"[{Path.GetFileName(file)}](../../{file})")
                .ToArray();
            map.Row(project.Name, MarkdownTable.Cell(MarkdownTable.Lines(files)));
        }

        map.Line()
            .Line("## Source Projects Without Direct Test Reference")
            .Line()
            .Row("Project", "Domain")
            .Line("|---|---|");

        foreach (var project in untestedSources)
            map.Row($"[{project.Name}](../../{project.RelativePath})", project.Domain);

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "test-map.md"), map.Content);
    }

    private static void WriteSpecStatusMap(RepoContext repo, IReadOnlyList<SpecFacts> specs)
    {
        var map = new MapBuilder()
            .Line("# Spec Status Map")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("Reads current `specs/*` folders and task checkboxes. Status clues are textual, not authoritative workflow state.")
            .Line()
            .Line("## Specs")
            .Line()
            .Row("Spec", "Title", "Status", "Plan verdict", "Tasks done", "Tasks open", "Notes")
            .Line("|---|---|---|---|---:|---:|---|");

        foreach (var spec in specs)
            map.Row(
                $"[{spec.Id}](../../specs/{spec.Id}/spec.md)",
                MarkdownTable.Cell(spec.Title),
                MarkdownTable.Cell(spec.Status),
                MarkdownTable.Cell(spec.PlanVerdict),
                spec.TasksDone.ToString(),
                spec.TasksOpen.ToString(),
                MarkdownTable.Cell(spec.Notes));

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "spec-status-map.md"), map.Content);
    }

    private static void WriteFindings(
        RepoContext repo,
        IReadOnlyList<ProjectRow> projects,
        IReadOnlyList<PackageRow> packages,
        IReadOnlyList<FeatureRow> features,
        IReadOnlyList<SpecFacts> specs,
        int untestedCount)
    {
        var multiVersion = packages
            .GroupBy(package => package.Id, StringComparer.Ordinal)
            .Where(group => group.Select(package => package.Version).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var findings = new MapBuilder()
            .Line("# Maps v1 Findings")
            .Line()
            .Line("Generated by `tools/maps/generate-maps`.")
            .Line()
            .Line("This is a point-in-time report from direct repo facts. It is not a constitution compliance verdict.")
            .Line()
            .Line("## Summary")
            .Line()
            .Line($"- Source projects: {projects.Count(project => project.Kind == "source")}")
            .Line($"- Test projects: {projects.Count(project => project.Kind == "test")}")
            .Line($"- Discovered CShells feature classes: {features.Count}")
            .Line($"- Specs: {specs.Count}")
            .Line($"- Direct package IDs with multiple versions: {multiVersion.Length}")
            .Line()
            .Line("## Package Version Clusters")
            .Line();

        if (multiVersion.Length == 0)
        {
            findings.Line("No direct package ID has multiple direct versions.");
        }
        else
        {
            findings.Row("Package", "Versions", "Projects").Line("|---|---|---|");
            foreach (var id in multiVersion)
            {
                var forId = packages.Where(package => package.Id == id).ToArray();
                findings.Row(
                    id,
                    MarkdownTable.Cell(MarkdownTable.Lines(forId.Select(package => package.Version).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))),
                    MarkdownTable.Cell(MarkdownTable.Lines(forId.Select(package => $"{package.Project} ({package.Version})").Order(StringComparer.Ordinal))));
            }
        }

        findings.Line()
            .Line("## Test Visibility")
            .Line()
            .Line($"- Source projects without a direct test-project reference: {untestedCount}")
            .Line("- This is only a visibility signal; tests may cover behavior indirectly through higher-level projects.")
            .Line()
            .Line("## Spec 006 State")
            .Line();

        var spec006 = specs.FirstOrDefault(spec => spec.Id == "006-activity-construction-seam");
        findings.Line(spec006 is not null
            ? $"- `006-activity-construction-seam`: {spec006.TasksDone} tasks done, {spec006.TasksOpen} tasks open; notes: {spec006.Notes}."
            : "- Spec 006 was not found.");

        findings.Line()
            .Line("## Runtime/Design Reference Signal")
            .Line();

        var runtimeSignals = projects
            .Where(project => project.Kind == "source")
            .Where(project => project.Name.Contains("Runtime", StringComparison.Ordinal)
                || project.Name is "Elsa.Activities.Primitives" or "Elsa.Activities.Composition.Runtime")
            .SelectMany(project => project.References
                .Where(reference => reference.Contains("Design", StringComparison.Ordinal))
                .Select(reference => $"- {project.Name} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (runtimeSignals.Length == 0)
            findings.Line("No direct runtime-path project reference to an `Elsa.*Design*` project was found by the Maps v1 heuristic.");
        else
            foreach (var signal in runtimeSignals)
                findings.Line(signal);

        findings.Line()
            .Line("## Next Map Work")
            .Line()
            .Line("- Add feature dependency/activation IDs from CShells configuration.")
            .Line("- Add constitution-specific verification reports that consume these maps.")
            .Line("- Add richer test maturity classification beyond direct project references.");

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "reports", "maps-v1-findings.md"), findings.Content);
    }

    /// <summary>
    /// Writes the freshness manifest. <c>input_fingerprint</c> is a SHA-256 over a
    /// <c>path=sha256</c> listing of every map input, so a stale map is detectable without re-running
    /// the generators.
    /// </summary>
    private static void WriteManifest(
        RepoContext repo,
        IReadOnlyList<ProjectRow> projects,
        IReadOnlyList<PackageRow> packages,
        IReadOnlyList<FeatureRow> features,
        IReadOnlyList<SpecFacts> specs)
    {
        var (fingerprint, inputs) = ComputeFingerprint(repo);
        var head = repo.TryGetHead();
        var dirty = repo.TryGetInputsDirty();

        var manifest = new MapBuilder()
            .Line("{")
            .Line("  \"schema_version\": \"1.0\",")
            .Line("  \"generator\": \"tools/maps/generate-maps\",")
            .Line("  \"freshness_rule\": \"Maps are fresh when input_fingerprint matches the current inputs. git_head is advisory because generated maps may be committed with their inputs.\",")
            .Line($"  \"git_head\": {(head is null ? "null" : $"\"{head}\"")},")
            .Line($"  \"relevant_inputs_dirty\": {(dirty is null ? "null" : dirty.Value ? "true" : "false")},")
            .Line($"  \"input_fingerprint\": \"{fingerprint}\",")
            .Line($"  \"input_file_count\": {inputs.Count},")
            .Line("  \"counts\": {")
            .Line($"    \"source_projects\": {projects.Count(project => project.Kind == "source")},")
            .Line($"    \"test_projects\": {projects.Count(project => project.Kind == "test")},")
            .Line($"    \"direct_project_references\": {projects.Sum(project => project.References.Count)},")
            .Line($"    \"direct_package_references\": {packages.Count},")
            .Line($"    \"feature_classes\": {features.Count},")
            .Line($"    \"specs\": {specs.Count}")
            .Line("  },")
            .Line("  \"generated_files\": [");

        // Only the files this run actually produced, so a v1-only refresh cannot over-report freshness
        // for the v2 layer -- which the shell version did, because the list was a hardcoded literal.
        var generated = new[]
        {
            "docs/maps/project-reference-map.md",
            "docs/maps/package-map.md",
            "docs/maps/feature-map.md",
            "docs/maps/test-map.md",
            "docs/maps/spec-status-map.md",
            "docs/reports/maps-v1-findings.md"
        };

        for (var index = 0; index < generated.Length; index++)
            manifest.Line($"    \"{generated[index]}\"{(index == generated.Length - 1 ? string.Empty : ",")}");

        manifest.Line("  ]").Line("}");

        MarkdownTable.Write(Path.Combine(repo.Root, "docs", "maps", "manifest.json"), manifest.Content);
    }

    /// <summary>The generator scripts are themselves map inputs: changing one can change every output.</summary>
    private static IEnumerable<string> GeneratorScripts =>
    [
        "tools/maps/generate-maps.ps1",
        "tools/maps/generate-maps.sh",
        "tools/maps/generate-domain-map.ps1",
        "tools/maps/generate-domain-map.sh",
        "tools/maps/generate-extension-point-map.ps1",
        "tools/maps/generate-extension-point-map.sh",
        "tools/maps/generate-architecture-reference-map.ps1",
        "tools/maps/generate-architecture-reference-map.sh",
        "tools/maps/generate-feature-dependency-map.ps1",
        "tools/maps/generate-feature-dependency-map.sh"
    ];

    /// <summary>
    /// The fingerprint over every map input, plus the input list it was computed from. Exposed so the
    /// freshness check can recompute it without writing anything.
    /// </summary>
    public static (string Fingerprint, IReadOnlyList<string> Inputs) ComputeFingerprint(RepoContext repo)
    {
        // EXTENSION_POINTS.md files are inputs to the extension-point map, so they belong in the
        // fingerprint. The shell generator omitted them, which meant adding an extension-point
        // heading changed the map while the freshness signal still read clean.
        var inputs = repo.ListFiles(
                "src/*.csproj", "src/*.cs", "tests/*.csproj", "specs/*.md",
                "EXTENSION_POINTS.md", "src/EXTENSION_POINTS.md", "src/*/EXTENSION_POINTS.md")
            .Concat(["Directory.Packages.props"])
            .Concat(GeneratorScripts)
            .Where(path => File.Exists(repo.Absolute(path)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var listing = new StringBuilder();
        foreach (var path in inputs)
            listing.Append(path).Append('=').Append(Sha256File(repo.Absolute(path))).Append('\n');

        return (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(listing.ToString()))), inputs);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
