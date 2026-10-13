using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps the retired-term analyzer from being switched off without anyone noticing. Its diagnostics are errors, and every
/// way to lower them, skip a file, or scope a term to nothing would otherwise read as "nothing to report".
/// </summary>
/// <remarks>
/// Three routes are closed. The analyzer configuration the compiler resolves for each production file, from every
/// .editorconfig and global config, may not lower the diagnostics below error, by their ids, by the Naming category or by a
/// blanket analyzer severity, and may not mark an ordinary source file as generated code, which analyzers skip. A section
/// that activates retired terms must match production source the analyzer compiles. And no source or build file may name
/// the diagnostics, which is how a pragma, a <c>SuppressMessage</c>, a <c>NoWarn</c> or a ruleset would silence them, and
/// no build file may turn analyzers off.
/// </remarks>
public sealed partial class RetiredTermEnforcementGuardTests
{
    private const string TermsKey = "elsa_naming.retired_terms";

    private static IReadOnlyList<string> ConfigFiles { get; } = FindConfigFiles(RepoRoot);

    private static IReadOnlyList<string> ProductionSources { get; } = ProductionSourceFiles(RepoRoot);

    /// <summary>The analyzer's rule ids, read from its release notes so a new rule is covered without editing this guard.</summary>
    private static IReadOnlyList<string> DiagnosticIds { get; } =
    [
        .. RuleId().Matches(File.ReadAllText(Path.Join(RepoRoot, "Essentials", "build", "Analyzers", "AnalyzerReleases.Unshipped.md")))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
    ];

    [Fact]
    public void No_analyzer_config_lowers_the_retired_term_diagnostics_or_marks_source_as_generated()
    {
        var violations = SeverityViolations(RepoRoot, ConfigFiles, ProductionSources, DiagnosticIds);

        Assert.True(violations.Count == 0,
            "The retired-term diagnostics must stay errors on every production file:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_retired_term_section_matches_production_source()
    {
        var violations = UnmatchedSections(RepoRoot, ConfigFiles, ProductionSources);

        Assert.True(violations.Count == 0,
            "A section that activates retired terms must match production source the analyzer compiles:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void No_source_or_build_file_suppresses_the_retired_term_diagnostics_or_turns_analyzers_off()
    {
        var violations = SuppressionViolations(RepoRoot);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Pins that the sweeps read the real configuration, the real source and the analyzer's real rules.</summary>
    [Fact]
    public void The_sweeps_read_the_repository_config_its_production_source_and_the_analyzer_rules()
    {
        Assert.Contains(ConfigFiles, path => Relative(RepoRoot, path) == ".editorconfig");
        Assert.True(ProductionSources.Count > 1000, "Too few production source files were found; the sweep has stopped finding them.");
        Assert.Equal(["ELSANAME001", "ELSANAME002", "ELSANAME003"], DiagnosticIds);
    }

    /// <summary>The three detectors, against a sandbox repository holding one config of every kind they must tell apart.</summary>
    [Fact]
    public void Each_way_to_weaken_the_analyzer_is_flagged_and_an_honest_config_is_not()
    {
        using var sandbox = new Sandbox();
        sandbox.File("Essentials/src/Tasks/Elsa.Tasks/Worker.cs", "namespace Elsa.Tasks; internal sealed class Worker { }");
        sandbox.File("Essentials/src/Events/Elsa.Events/Thing.cs", "namespace Elsa.Events; internal sealed class Thing { }");
        sandbox.File("Essentials/src/Persistence/Migrations/Initial.cs", "namespace Elsa.Persistence.Migrations; internal sealed class Initial { }");
        sandbox.File("Essentials/tests/Tasks/Tests/WorkerTests.cs", "namespace Elsa.Tasks.Tests; public sealed class WorkerTests { }");
        sandbox.File(".editorconfig",
            """
            root = true

            [*.cs]
            dotnet_diagnostic.ELSANAME002.severity = error

            [**/Migrations/*.cs]
            generated_code = true
            dotnet_analyzer_diagnostic.severity = none

            [Essentials/src/Tasks/**.cs]
            dotnet_diagnostic.ELSANAME001.severity = warning
            elsa_naming.retired_terms = Pump=Worker

            [Essentials/src/Taskz/**.cs]
            elsa_naming.retired_terms = Pump=Worker

            [Essentials/tests/Tasks/**.cs]
            elsa_naming.retired_terms = Pump=Worker

            [Essentials/src/Events/**.cs]
            dotnet_analyzer_diagnostic.category-Naming.severity = suggestion
            generated_code = true
            """);
        sandbox.File("Essentials/build/naming.globalconfig",
            """
            is_global = true
            elsa_naming.retired_terms = Stimulus=Signal
            """);
        sandbox.File("Essentials/src/Tasks/Elsa.Tasks/Suppressed.cs", "#pragma warning disable ELSANAME001\nnamespace Elsa.Tasks;");
        sandbox.File("Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj",
            """<Project><PropertyGroup><NoWarn>$(NoWarn);ELSANAME002</NoWarn><RunAnalyzers>false</RunAnalyzers></PropertyGroup></Project>""");

        var configs = FindConfigFiles(sandbox.Root);
        var sources = ProductionSourceFiles(sandbox.Root);

        Assert.Equal(
        [
            "ELSANAME001 is lowered to Warn on 2 production file(s), for example Essentials/src/Tasks/Elsa.Tasks/Suppressed.cs",
            "dotnet_analyzer_diagnostic.category-naming.severity = suggestion lowers a retired-term diagnostic below error on 1 production file(s), for example Essentials/src/Events/Elsa.Events/Thing.cs",
            "elsa_naming.retired_terms is set in a global config, so it applies to every file; activate retired terms per domain in an .editorconfig section",
            "generated_code = true marks ordinary source as generated, which analyzers skip, on 1 production file(s), for example Essentials/src/Events/Elsa.Events/Thing.cs",
        ],
            SeverityViolations(sandbox.Root, configs, sources, ["ELSANAME001", "ELSANAME002", "ELSANAME003"]));
        Assert.Equal(
        [
            ".editorconfig [Essentials/src/Taskz/**.cs] matches no production source file",
            ".editorconfig [Essentials/tests/Tasks/**.cs] matches no production source file",
        ],
            UnmatchedSections(sandbox.Root, configs, sources));
        Assert.Equal(
        [
            "Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj names ELSANAME, which suppresses the retired-term diagnostics there",
            "Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj sets RunAnalyzers to false, which turns the retired-term analyzer off",
            "Essentials/src/Tasks/Elsa.Tasks/Suppressed.cs names ELSANAME, which suppresses the retired-term diagnostics there",
        ],
            SuppressionViolations(sandbox.Root));
    }

    /// <summary>
    /// What the compiler would resolve for each production file, judged: an id lowered below error, a Naming-category or
    /// blanket severity below error that some rule id has no explicit setting to override, or an ordinary file marked as
    /// generated code. Generated files themselves are skipped, since analyzers never read them.
    /// </summary>
    private static IReadOnlyList<string> SeverityViolations(
        string repoRoot, IReadOnlyList<string> configFiles, IReadOnlyList<string> sources, IReadOnlyList<string> ids)
    {
        var set = AnalyzerConfigSet.Create(configFiles.Select(path => AnalyzerConfig.Parse(File.ReadAllText(path), path)).ToImmutableArray());
        var findings = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var source in sources.Where(source => !IsGeneratedPath(source)))
        {
            foreach (var problem in Judge(set.GetOptionsForSourcePath(source), ids))
            {
                if (!findings.TryGetValue(problem, out var files))
                    findings[problem] = files = new SortedSet<string>(StringComparer.Ordinal);
                files.Add(Relative(repoRoot, source));
            }
        }

        var global = set.GlobalConfigOptions.AnalyzerOptions.ContainsKey(TermsKey)
            ? [$"{TermsKey} is set in a global config, so it applies to every file; activate retired terms per domain in an .editorconfig section"]
            : Array.Empty<string>();

        return
        [
            .. findings.Select(finding => $"{finding.Key} on {finding.Value.Count} production file(s), for example {finding.Value.First()}")
                .Concat(global)
                .Order(StringComparer.Ordinal)
        ];
    }

    private static IEnumerable<string> Judge(AnalyzerConfigOptionsResult options, IReadOnlyList<string> ids)
    {
        var explicitIds = ids.Where(id => options.TreeOptions.Keys.Any(key => string.Equals(key, id, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var id in explicitIds)
        {
            var severity = options.TreeOptions.First(entry => string.Equals(entry.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
            if (severity is not (ReportDiagnostic.Error or ReportDiagnostic.Default))
                yield return $"{id} is lowered to {severity}";
        }

        if (explicitIds.Length < ids.Count)
        {
            foreach (var key in new[] { "dotnet_analyzer_diagnostic.category-naming.severity", "dotnet_analyzer_diagnostic.severity" })
            {
                if (options.AnalyzerOptions.TryGetValue(key, out var value) && !IsError(value))
                    yield return $"{key} = {value} lowers a retired-term diagnostic below error";
            }
        }

        if (options.AnalyzerOptions.TryGetValue("generated_code", out var generated) && string.Equals(generated.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            yield return "generated_code = true marks ordinary source as generated, which analyzers skip,";
    }

    private static bool IsError(string severity) => severity.Trim() is "error" or "default";

    /// <summary>Generated code by the conventions the root .editorconfig marks as generated: EF migrations and generated file names.</summary>
    private static bool IsGeneratedPath(string path) =>
        path.Replace('\\', '/').Contains("/Migrations/", StringComparison.Ordinal) ||
        Path.GetFileName(path) is var name && (name.EndsWith(".Designer.cs", StringComparison.Ordinal) ||
                                                name.EndsWith(".g.cs", StringComparison.Ordinal) ||
                                                name.EndsWith(".generated.cs", StringComparison.Ordinal));

    /// <summary>
    /// Every .editorconfig section that activates retired terms but matches no production source file. Each section is
    /// resolved on its own, at its file's location, by the compiler's own matching.
    /// </summary>
    private static IReadOnlyList<string> UnmatchedSections(string repoRoot, IReadOnlyList<string> configFiles, IReadOnlyList<string> sources) =>
    [
        .. configFiles
            .Where(path => Path.GetFileName(path) == ".editorconfig")
            .SelectMany(path => SectionsSettingTerms(File.ReadAllText(path)).Select(section => (Path: path, Section: section)))
            .Where(entry =>
            {
                var alone = AnalyzerConfigSet.Create(ImmutableArray.Create(AnalyzerConfig.Parse(
                    $"root = true\n[{entry.Section}]\n{TermsKey} = Probe=Probe\n", entry.Path)));
                return !sources.Any(source => alone.GetOptionsForSourcePath(source).AnalyzerOptions.ContainsKey(TermsKey));
            })
            .Select(entry => $"{Relative(repoRoot, entry.Path)} [{entry.Section}] matches no production source file")
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>The headers of the sections in an .editorconfig that set <see cref="TermsKey"/>.</summary>
    private static IEnumerable<string> SectionsSettingTerms(string config)
    {
        string? section = null;
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                section = line[1..^1];
            else if (section is not null && line.Split('=', 2) is [var key, _] && string.Equals(key.Trim(), TermsKey, StringComparison.OrdinalIgnoreCase))
                yield return section;
        }
    }

    private static IReadOnlyList<string> SuppressionViolations(string repoRoot)
    {
        var files = SweptFiles(repoRoot).ToArray();
        var named = files
            .Where(path => File.ReadAllText(path).Contains("ELSANAME", StringComparison.OrdinalIgnoreCase))
            .Select(path => $"{Relative(repoRoot, path)} names ELSANAME, which suppresses the retired-term diagnostics there");
        var switchedOff = files
            .Where(path => Path.GetExtension(path) is ".csproj" or ".props" or ".targets")
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName is "RunAnalyzers" or "RunAnalyzersDuringBuild" &&
                                  string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                .Select(element => $"{Relative(repoRoot, path)} sets {element.Name.LocalName} to false, which turns the retired-term analyzer off"));

        return [.. named.Concat(switchedOff).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Source, build and ruleset files the analyzer's diagnostics could be suppressed from: the repository-root build files
    /// and everything under Essentials and Extensions, except the analyzer itself and the tests that exercise and guard it.
    /// </summary>
    private static IEnumerable<string> SweptFiles(string repoRoot)
    {
        string[] extensions = [".cs", ".csproj", ".props", ".targets", ".ruleset"];
        string[] exempt = ["Essentials/build/Analyzers/", "Essentials/tests/Architecture/"];

        return Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.TopDirectoryOnly)
            .Concat(new[] { "Essentials", "Extensions" }
                .Select(root => Path.Join(repoRoot, root))
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(repoRoot, path) && !IsUnder(path, "node_modules"))
            .Where(path => !exempt.Any(prefix => Relative(repoRoot, path).StartsWith(prefix, StringComparison.Ordinal)));
    }

    /// <summary>Every .editorconfig and global analyzer config in the repository.</summary>
    private static IReadOnlyList<string> FindConfigFiles(string repoRoot) =>
    [
        .. Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) == ".editorconfig" || path.EndsWith(".globalconfig", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(repoRoot, path) && !IsUnder(path, "node_modules") && !IsUnder(path, ".git"))
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>Every C# file in a production project, the files the analyzer compiles.</summary>
    private static IReadOnlyList<string> ProductionSourceFiles(string repoRoot) =>
    [
        .. ModuleRoots.Resolve(repoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(repoRoot, path))
            .Order(StringComparer.Ordinal)
    ];

    private static bool IsUnder(string path, string directoryName) =>
        path.Contains($"{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string repoRoot, string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    [GeneratedRegex(@"ELSANAME\d{3}")]
    private static partial Regex RuleId();

    /// <summary>A throwaway repository tree for the detector proof, cleaned up on dispose.</summary>
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("elsa-retired-terms-guard-").FullName;

        public void File(string relativePath, string content)
        {
            var path = Path.Join(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
