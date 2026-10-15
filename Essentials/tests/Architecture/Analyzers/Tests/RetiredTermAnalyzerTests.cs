using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Elsa.Architecture.Analyzers.Tests;

/// <summary>
/// The retired-term analyzer, driven through Roslyn's own .editorconfig parsing and section matching, so a test that
/// passes here passes for the same reason a build does.
/// </summary>
public sealed class RetiredTermAnalyzerTests
{
    /// <summary>A Tasks-only activation, the shape a domain's realignment adds to the root .editorconfig.</summary>
    private const string TasksSection =
        """
        root = true

        [Essentials/src/Tasks/**.cs]
        elsa_naming.retired_terms = Pump|Pumps=Worker, Stimulus|Stimuli=Signal, Stamp|Stamps=SchemaVersion
        """;

    private const string TasksFile = "Essentials/src/Tasks/Elsa.Tasks/Sample.cs";

    private static readonly string FakeRepoRoot = Path.Join(Path.GetTempPath(), "elsa-retired-terms-repo");

    [Fact]
    public async Task A_public_type_using_a_retired_term_is_an_error_that_names_the_replacement()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(TasksSection,
            (TasksFile, "namespace Elsa.Tasks.Schedules; public abstract class BackoffSweepPumpTask { }")));

        Assert.Equal(RetiredTermAnalyzer.TypeNameId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "Type 'BackoffSweepPumpTask' uses the retired term 'Pump'; use 'Worker' instead",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("internal sealed class PumpScheduler { }", "PumpScheduler")]
    [InlineData("internal static class Outer { private sealed class NestedPumps { } }", "NestedPumps")]
    [InlineData("internal sealed record PumpDecision(int Value);", "PumpDecision")]
    [InlineData("internal enum PumpState { Idle }", "PumpState")]
    [InlineData("internal interface IStimulusRouter { }", "IStimulusRouter")]
    [InlineData("internal readonly struct StimuliKey { }", "StimuliKey")]
    [InlineData("internal delegate void PumpCallback();", "PumpCallback")]
    public async Task An_internal_or_nested_declaration_of_any_kind_is_flagged(string declaration, string name)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(TasksSection, (TasksFile, $"namespace Elsa.Tasks; {declaration}")));

        Assert.Equal(RetiredTermAnalyzer.TypeNameId, diagnostic.Id);
        Assert.Equal(name, diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData("namespace Elsa.Tasks.Pumps;", "Elsa.Tasks.Pumps", "Pumps", "Worker")]
    [InlineData("namespace Elsa.Stimuli.Routing { }", "Elsa.Stimuli.Routing", "Stimuli", "Signal")]
    public async Task A_namespace_using_a_retired_term_is_flagged(string source, string ns, string spelling, string replacement)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(TasksSection, (TasksFile, source)));

        Assert.Equal(RetiredTermAnalyzer.NamespaceId, diagnostic.Id);
        Assert.Equal(
            $"Namespace '{ns}' uses the retired term '{spelling}'; use '{replacement}' instead",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_term_is_enforced_only_in_the_domain_whose_section_activates_it()
    {
        var diagnostics = await AnalyzeAsync(TasksSection,
            ("Essentials/src/Tasks/Schedules/Worker.cs", "namespace Elsa.Tasks.Schedules; internal sealed class PumpScheduler { }"),
            ("Essentials/src/Events/Elsa.Events/Worker.cs", "namespace Elsa.Events; internal sealed class PumpScheduler { }"),
            ("Essentials/tests/Tasks/Tests/WorkerTests.cs", "namespace Elsa.Tasks.Tests; public sealed class PumpSchedulerTests { }"));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(Path.Join(FakeRepoRoot, "Essentials/src/Tasks/Schedules/Worker.cs"), diagnostic.Location.SourceTree!.FilePath);
    }

    [Fact]
    public async Task A_file_that_no_section_configures_is_held_to_nothing()
    {
        Assert.Empty(await AnalyzeAsync("root = true",
            (TasksFile, "namespace Elsa.Stimuli; internal sealed class BackoffSweepPumpTask { }")));
    }

    [Theory]
    [InlineData("internal sealed class TimestampBits { }")]
    [InlineData("internal sealed class Pumpkin { }")]
    [InlineData("internal sealed class StimulateOnce { }")]
    public async Task A_name_that_only_contains_the_letters_of_a_term_is_not_flagged(string declaration)
    {
        Assert.Empty(await AnalyzeAsync(TasksSection, (TasksFile, $"namespace Elsa.Tasks; {declaration}")));
    }

    [Fact]
    public async Task Each_retired_term_in_one_name_is_reported()
    {
        var diagnostics = await AnalyzeAsync(TasksSection, (TasksFile, "namespace Elsa.Tasks; internal sealed class StimulusPump { }"));

        Assert.Equal(
            ["Type 'StimulusPump' uses the retired term 'Pump'; use 'Worker' instead",
             "Type 'StimulusPump' uses the retired term 'Stimulus'; use 'Signal' instead"],
            diagnostics.Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Pump")]
    [InlineData("pump=Worker")]
    [InlineData("Pump=Worker,")]
    [InlineData("Pump=Worker=Again")]
    [InlineData("Pump|=Worker")]
    public async Task An_unparseable_setting_is_itself_an_error_rather_than_a_silent_no_op(string value)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(
            $"root = true\n\n[Essentials/src/Tasks/**.cs]\nelsa_naming.retired_terms = {value}",
            (TasksFile, "namespace Elsa.Tasks; internal sealed class PumpScheduler { }")));

        Assert.Equal(RetiredTermAnalyzer.InvalidConfigurationId, diagnostic.Id);
    }

    /// <summary>
    /// Holds the committed configuration to the grammar: every .editorconfig in the repository, as the compiler would
    /// combine them, is resolved for a file in every production folder that holds C# source, nested ones included, so a
    /// section scoped to a sub-folder such as <c>Essentials/src/Workflows/Runtime/**</c> is resolved too. None of them may
    /// produce a setting the analyzer refuses to parse.
    /// </summary>
    [Fact]
    public async Task Every_committed_retired_term_setting_parses()
    {
        var repoRoot = FindRepoRoot();
        var configs = AnalyzerConfigSet.Create(Directory
            .EnumerateFiles(repoRoot, ".editorconfig", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => AnalyzerConfig.Parse(File.ReadAllText(path), path))
            .ToImmutableArray());
        var probes = new[] { "Essentials/src", "Essentials/apps" }
            .Select(root => Path.Join(repoRoot, root))
            .Concat(Directory.EnumerateDirectories(Path.Join(repoRoot, "Extensions"))
                .Select(extension => Path.Join(extension, "src"))
                .Where(Directory.Exists))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(repoRoot, path))
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(directory => (Path: Path.Join(directory, "Probe.cs"), Source: "namespace Probe; internal sealed class Probe { }"))
            .ToArray();

        var diagnostics = await AnalyzeAsync(configs, probes);

        Assert.True(probes.Length > 500, $"Only {probes.Length} production folders were probed; the sweep has stopped finding them.");
        Assert.Contains(probes, probe => probe.Path.Contains(
            $"{Path.DirectorySeparatorChar}Workflows{Path.DirectorySeparatorChar}Runtime{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == RetiredTermAnalyzer.InvalidConfigurationId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string editorConfig, params (string Path, string Source)[] files) =>
        AnalyzeAsync(
            AnalyzerConfigSet.Create(ImmutableArray.Create(AnalyzerConfig.Parse(editorConfig, Path.Join(FakeRepoRoot, ".editorconfig")))),
            [.. files.Select(file => (Path.Join(FakeRepoRoot, file.Path), file.Source))]);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(AnalyzerConfigSet configs, (string Path, string Source)[] files)
    {
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(file.Source, path: file.Path)).ToArray();
        var compilation = CSharpCompilation.Create(
            "RetiredTermSample",
            trees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new EditorConfigOptionsProvider(configs));

        return await compilation.WithAnalyzers([new RetiredTermAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
    }

    private static bool IsBuildOutput(string repoRoot, string path) =>
        $"/{Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/')}" is var relative &&
        (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>Resolves each file's options the way the compiler does: from the .editorconfig sections matching its path.</summary>
    private sealed class EditorConfigOptionsProvider(AnalyzerConfigSet configs) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(configs.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(configs.GetOptionsForSourcePath(textFile.Path).AnalyzerOptions);
    }

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
