using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Elsa.Testing.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeCheckpointCommitterCallerCoverageTests
{
    private static readonly string[] ProductionRoots =
    [
        "src/Elsa/Workflows/Runtime",
        "src/Elsa/Activities/Runtime"
    ];

    [Fact]
    public void Current_production_call_sites_exactly_match_the_reviewed_inventory()
    {
        var expected = CallerRows().Select(ToIdentity).Order(StringComparer.Ordinal).ToArray();
        var actual = ScanDirectCommitterCalls().Select(ToIdentity).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(27, actual.Length);
        Assert.Equal(21, actual.Select(identity => identity[..identity.LastIndexOf(':')]).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(CallerRows))]
    public async Task Every_inventory_row_reaches_the_generic_preparation_funnel(
        int number,
        string classification,
        string sourcePath,
        int sourceLine)
    {
        Assert.Contains(ScanDirectCommitterCalls(), site => site.Path == sourcePath && site.Line == sourceLine);
        var events = new List<string>();
        var store = new PreparationProbeStore(number, events);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), store);

        await committer.CommitAsync(NewCommit($"caller-{number}"));

        Assert.Equal(["prepare", "finalize"], events);
        Assert.False(string.IsNullOrWhiteSpace(classification));
        Assert.Equal(number, store.CommittedCheckpoint!.Provenance!.WorkflowCheckpointOrder);
    }

    [Fact]
    public async Task Central_funnel_prepares_before_enrichment_and_finalizes_after_enrichment()
    {
        var events = new List<string>();
        var store = new PreparationProbeStore(1, events);
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [new OrderProbeEnricher(events)]);

        await committer.CommitAsync(NewCommit("ordered"));

        Assert.Equal(["prepare", "enrich", "finalize"], events);
    }

    [Fact]
    public void Production_callers_do_not_bypass_the_committer_through_the_commit_store_contract_or_concrete_providers()
    {
        var assemblyNames = MapInventoryPathsToAssemblies()
            .Select(mapping => mapping.AssemblyName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("Elsa.Activities.Runtime", assemblyNames);
        Assert.Contains("Elsa.Workflows.Runtime", assemblyNames);
        var assemblies = assemblyNames.Select(Assembly.Load).ToArray();
        var offenders = ScanCommitStoreBypasses(assemblies.SelectMany(GetInspectibleMethods));

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_inventory_path_maps_to_its_nearest_production_project_and_assembly()
    {
        var mappings = MapInventoryPathsToAssemblies();
        var inventoryPaths = ReadInventory().Select(row => row.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(21, mappings.Length);
        Assert.Equal(inventoryPaths, mappings.Select(mapping => mapping.SourcePath).Order(StringComparer.Ordinal));
        Assert.All(mappings, mapping =>
        {
            Assert.True(File.Exists(mapping.ProjectPath), $"Nearest project was not found for '{mapping.SourcePath}'.");
            var expectedAssembly = mapping.SourcePath.StartsWith("src/Elsa/Activities/Runtime/", StringComparison.Ordinal)
                ? "Elsa.Activities.Runtime"
                : "Elsa.Workflows.Runtime";
            Assert.Equal(expectedAssembly, mapping.AssemblyName);
        });
        Assert.Equal(
            ["Elsa.Activities.Runtime", "Elsa.Workflows.Runtime"],
            mappings.Select(mapping => mapping.AssemblyName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Commit_store_bypass_is_detected_from_compiled_il_regardless_of_receiver_syntax()
    {
        var methods = new[]
        {
            typeof(SyntheticBypassingConsumer).GetMethod(nameof(SyntheticBypassingConsumer.Prepare))!,
            typeof(SyntheticBypassingConsumer).GetMethod(nameof(SyntheticBypassingConsumer.Persist))!
        };
        var offenders = ScanCommitStoreBypasses(methods);

        Assert.Equal(2, offenders.Length);
        Assert.All(offenders, offender => Assert.Equal(typeof(SyntheticBypassingConsumer), offender.Caller.DeclaringType));
        Assert.Equal(
            [nameof(IRuntimeCheckpointCommitStore.CommitPreparedAsync), nameof(IRuntimeCheckpointCommitStore.PrepareAsync)],
            offenders.Select(offender => offender.Target.Name).Order(StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> CallerRows() => ReadInventory()
        .Select(row => new object[] { row.Number, row.Classification, row.Path, row.Line });

    private static CallerSite[] ReadInventory()
    {
        var reportPath = Path.Combine(FindRepositoryRoot(), "docs/reports/runtime-checkpoint-committer-callers.md");
        var rows = File.ReadLines(reportPath)
            .Select(line => CallerRowPattern().Match(line))
            .Where(match => match.Success)
            .Select(match => new CallerSite(
                int.Parse(match.Groups["number"].Value),
                match.Groups["classification"].Value,
                match.Groups["path"].Value,
                int.Parse(match.Groups["line"].Value)))
            .ToArray();
        Assert.Equal(27, rows.Length);
        Assert.Equal(21, rows.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count());
        return rows;
    }

    private static CallerSite[] ScanDirectCommitterCalls()
    {
        var root = FindRepositoryRoot();
        var sites = new List<CallerSite>();
        foreach (var productionRoot in ProductionRoots)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, productionRoot), "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                foreach (Match match in DirectCommitterCallPattern().Matches(source))
                {
                    var line = 1 + source.AsSpan(0, match.Index).Count('\n');
                    sites.Add(new CallerSite(
                        0,
                        string.Empty,
                        Path.GetRelativePath(root, file).Replace('\\', '/'),
                        line));
                }
            }
        }
        return sites.ToArray();
    }

    /// <summary>
    /// The compiled-IL gate follows the exact reviewed inventory to each source file's nearest project. Sibling
    /// projects nested under a declared production root are not loaded unless an inventory path maps to them; the
    /// root-wide source scan above still fails if such a project adds a direct committer call without an inventory row.
    /// </summary>
    private static CallerAssemblyMapping[] MapInventoryPathsToAssemblies()
    {
        var root = FindRepositoryRoot();
        return ReadInventory()
            .Select(row => row.Path)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(sourcePath =>
            {
                var projectPath = FindNearestProject(root, sourcePath);
                return new CallerAssemblyMapping(sourcePath, projectPath, ReadAssemblyName(projectPath));
            })
            .ToArray();
    }

    private static string FindNearestProject(string repositoryRoot, string sourcePath)
    {
        var sourceFile = Path.Combine(repositoryRoot, sourcePath);
        Assert.True(File.Exists(sourceFile), $"Inventory source path '{sourcePath}' does not exist.");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
             directory is not null && directory.FullName.StartsWith(repositoryRoot, StringComparison.Ordinal);
             directory = directory.Parent)
        {
            var projects = Directory.EnumerateFiles(directory.FullName, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
            if (projects.Length == 0)
                continue;
            Assert.Single(projects);
            return projects[0];
        }
        throw new FileNotFoundException($"No project owns inventory source path '{sourcePath}'.", sourceFile);
    }

    private static string ReadAssemblyName(string projectPath)
    {
        var declaredName = XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "AssemblyName")
            .Select(element => element.Value.Trim())
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return declaredName ?? Path.GetFileNameWithoutExtension(projectPath);
    }

    private static IEnumerable<MethodInfo> GetInspectibleMethods(Assembly assembly) => assembly.GetTypes()
        .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Where(method => method.GetMethodBody() is not null);

    private static CommitStoreBypass[] ScanCommitStoreBypasses(IEnumerable<MethodInfo> methods) => methods
        .Where(method => !IsReviewedCommitStoreOwner(method.DeclaringType))
        .SelectMany(method => MethodCallInspector.GetCalledMethods(method)
            .Where(IsDirectCommitStoreCall)
            .Select(target => new CommitStoreBypass(method, target)))
        .OrderBy(bypass => bypass.Caller.DeclaringType?.FullName, StringComparer.Ordinal)
        .ThenBy(bypass => bypass.Caller.Name, StringComparer.Ordinal)
        .ToArray();

    private static bool IsDirectCommitStoreCall(MethodBase method) =>
        method.Name is nameof(IRuntimeCheckpointCommitStore.PrepareAsync) or
            nameof(IRuntimeCheckpointCommitStore.CommitAsync) or
            nameof(IRuntimeCheckpointCommitStore.CommitPreparedAsync) &&
        method.DeclaringType is { } declaringType &&
        typeof(IRuntimeCheckpointCommitStore).IsAssignableFrom(declaringType);

    private static bool IsReviewedCommitStoreOwner(Type? type)
    {
        while (type?.DeclaringType is { } declaringType)
            type = declaringType;
        return type == typeof(RuntimeCheckpointCommitter) ||
               type is not null && typeof(IRuntimeCheckpointCommitStore).IsAssignableFrom(type);
    }

    private static string ToIdentity(object[] row) => $"{row[2]}:{row[3]}";
    private static string ToIdentity(CallerSite row) => $"{row.Path}:{row.Line}";

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs/reports/runtime-checkpoint-committer-callers.md")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private static RuntimeCheckpointCommit NewCommit(string commitId) =>
        new(
            commitId,
            new RuntimeCheckpoint($"checkpoint-{commitId}", "CallerCoverage", $"workflow-{commitId}", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

    [GeneratedRegex(@"^\| (?<number>\d+) \| (?<classification>[^|]+?) \| `(?<path>src/[^:]+):(?<line>\d+)` \|$")]
    private static partial Regex CallerRowPattern();

    [GeneratedRegex(@"(?:_checkpointCommitter!?|checkpointCommitter|request\.CheckpointCommitter)\s*\.\s*CommitAsync\s*\(")]
    private static partial Regex DirectCommitterCallPattern();

    private sealed record CallerSite(int Number, string Classification, string Path, int Line);
    private sealed record CallerAssemblyMapping(string SourcePath, string ProjectPath, string AssemblyName);
    private sealed record CommitStoreBypass(MethodInfo Caller, MethodBase Target);

    private sealed class SyntheticBypassingConsumer
    {
        public ValueTask<RuntimeCheckpointPreparationResult> Prepare(
            IRuntimeCheckpointCommitStore store,
            RuntimeCheckpointPrepareRequest request) =>
            store.PrepareAsync(request);

        public ValueTask<RuntimeCheckpointCommitStoreResult> Persist(
            IRuntimeCheckpointCommitStore store,
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision) =>
            store.CommitPreparedAsync(token, commit, decision);
    }

    private sealed class OrderProbeEnricher(ICollection<string> events) : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            events.Add("enrich");
            Assert.NotNull(commit.Checkpoint.Provenance);
            return ValueTask.FromResult(commit);
        }
    }

    private sealed class PreparationProbeStore(long order, ICollection<string> events) : IRuntimeCheckpointCommitStore
    {
        public RuntimeCheckpoint? CommittedCheckpoint { get; private set; }

        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default)
        {
            events.Add("prepare");
            var token = new RuntimeCheckpointPreparationToken(
                request.Commit.CommitId,
                $"ledger-{order}",
                new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, order),
                order,
                0,
                request.Commit.ExpectedFence,
                new RuntimeCheckpointPreparationPayloadCodec().Encode(request).PayloadSha256,
                request.Commit.CommitId,
                RuntimeCheckpointPersistenceMode.Immediate);
            return ValueTask.FromResult(RuntimeCheckpointPreparationResult.Prepared(token));
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            events.Add("finalize");
            CommittedCheckpoint = commit.Checkpoint;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The central committer must finalize through CommitPreparedAsync.");
    }
}
