using System.Reflection;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using BpmnProcess = Elsa.Activities.Bpmn.Activities.BpmnProcess;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;
using Delay = Elsa.Activities.Scheduling.Activities.Delay;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using GraphActivity = Elsa.Activities.Graph.Runtime.Activities.GraphActivity;
using HttpEndpoint = Elsa.Activities.Http.Activities.HttpEndpoint;
using IfActivity = Elsa.Activities.If.Activities.If;
using RunJavaScript = Elsa.Activities.Scripting.Activities.RunJavaScript;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WriteLine = Elsa.Activities.Primitives.Activities.WriteLine;

namespace Elsa.Activities.Design.Tests.Contracts;

/// <summary>
/// Contract-surface snapshot guard (#1119). Runs the production reflection-only scanner
/// (<see cref="ClrAssemblyScanner"/> — the same code path the design catalog reconciles from) over every
/// shipped activity assembly, normalizes the result to a total ordinal ordering, and compares it against a
/// committed baseline. Any added, removed or altered input, output or outcome port lands as a reviewable
/// JSON diff instead of passing silently.
/// </summary>
/// <remarks>
/// <para>
/// The scanner is used deliberately in preference to <c>ClrActivityContractTestBuilder</c>: the builder reads
/// only <c>[ActivityOutcome]</c> and would silently drop <c>[ActivityValueOutcomes]</c>, so
/// <c>SendHttpRequest</c>'s dynamic per-status-code ports — the exact gap class that motivated this guard —
/// would never appear in the baseline.
/// </para>
/// <para>
/// To accept an intended contract change, re-run this test with
/// <c>ELSA_UPDATE_ACTIVITY_CONTRACT_BASELINE=1</c> and commit the rewritten baseline alongside the source
/// change, mirroring the <c>ELSA_UPDATE_EF_CORE_BASELINE</c> ratchet in <c>tests/Elsa/Architecture</c>.
/// </para>
/// </remarks>
public sealed class ActivityContractSurfaceSnapshotTests
{
    private const string UpdateEnvironmentVariable = "ELSA_UPDATE_ACTIVITY_CONTRACT_BASELINE";

    private const string RegenerationCommand =
        $"{UpdateEnvironmentVariable}=1 dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj " +
        "--filter FullyQualifiedName~ActivityContractSurfaceSnapshotTests";

    [Fact]
    public void Activity_contract_surface_matches_the_committed_baseline()
    {
        var actual = ActivityContractSurface.Serialize(ActivityContractSurface.Build(ScanShippedActivities()));

        if (Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
            File.WriteAllText(BaselinePath, actual);
            File.Delete(ActualPath);
            return;
        }

        if (!File.Exists(BaselinePath))
        {
            File.WriteAllText(ActualPath, actual);
            Assert.Fail(
                $"No activity contract baseline exists at '{BaselinePath}'. " +
                $"The scanned surface was written to '{ActualPath}'. Generate the baseline with:{Environment.NewLine}{RegenerationCommand}");
        }

        var expected = File.ReadAllText(BaselinePath);
        if (Normalize(expected) == Normalize(actual))
        {
            // Clear a stale artifact from an earlier failing run so the working tree does not carry noise.
            if (File.Exists(ActualPath))
                File.Delete(ActualPath);
            return;
        }

        File.WriteAllText(ActualPath, actual);
        Assert.Fail(
            $"The activity contract surface no longer matches the committed baseline.{Environment.NewLine}" +
            $"Baseline: {BaselinePath}{Environment.NewLine}" +
            $"Actual:   {ActualPath}{Environment.NewLine}{Environment.NewLine}" +
            $"{Diff(expected, actual)}{Environment.NewLine}" +
            $"If the change is intended, refresh the baseline in the same commit as the source change:{Environment.NewLine}{RegenerationCommand}");
    }

    /// <summary>
    /// Every activity module is represented in the scan. A module whose assembly stops being copied into the
    /// scan folder would otherwise silently drop its whole contract surface and still look like a green
    /// "no diff" run, so each anchor type is asserted present rather than trusted implicitly.
    /// </summary>
    [Fact]
    public void Every_shipped_activity_assembly_contributes_its_anchor_activity()
    {
        var models = ScanShippedActivities();

        foreach (var anchor in ActivityAnchors)
            Assert.True(
                models.Any(model => model.ActivityTypeKey == anchor.FullName),
                $"Assembly '{anchor.Assembly.GetName().Name}' contributed no activities to the contract scan " +
                $"(anchor '{anchor.FullName}' is missing).");
    }

    /// <summary>
    /// The scan folder holds only the shipped activity assemblies, never the test doubles
    /// (<c>ProbeActivity</c>, <c>FaultingActivity</c>, the ClrFixture activities). Those live in the same
    /// output directory, so a scan pointed at <c>AppContext.BaseDirectory</c> would smuggle them into the
    /// baseline; this asserts the isolation holds.
    /// </summary>
    [Fact]
    public void Scan_excludes_test_double_activities()
    {
        var models = ScanShippedActivities();

        Assert.DoesNotContain(models, model => model.ActivityTypeKey.Contains(".Testing.", StringComparison.Ordinal));
        Assert.DoesNotContain(models, model => model.ActivityTypeKey.Contains(".Tests.", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ActivityVersionReconciliationModel> ScanShippedActivities()
    {
        using var folder = ActivityAssemblyFolder.Containing(ActivityAssemblies);
        var scanner = new ClrAssemblyScanner(
            new ActivityTypeVersionResolver(),
            new ActivityTypeCategoryResolver(),
            NullLogger<ClrAssemblyScanner>.Instance);

        return scanner.Scan(folder.Path);
    }

    /// <summary>
    /// One anchor type per shipped activity assembly. Referencing a type (rather than probing by assembly
    /// name) makes the compiler enforce that the project reference exists, so deleting a module breaks the
    /// build here instead of quietly shrinking the baseline.
    /// </summary>
    private static IReadOnlyList<Type> ActivityAnchors { get; } =
    [
        typeof(BpmnProcess),
        typeof(IfActivity),
        typeof(DispatchWorkflowActivity),
        typeof(FlowchartActivity),
        typeof(GraphActivity),
        typeof(HttpEndpoint),
        typeof(WriteLine),
        typeof(Delay),
        typeof(RunJavaScript),
        typeof(SequenceActivity)
    ];

    private static IReadOnlyList<Assembly> ActivityAssemblies { get; } =
        ActivityAnchors.Select(anchor => anchor.Assembly).Distinct().ToArray();

    private static string BaselinePath => Path.Combine(ContractsDirectory, "activity-contract-surface.baseline.json");

    private static string ActualPath => Path.Combine(ContractsDirectory, "activity-contract-surface.actual.json");

    private static string ContractsDirectory => Path.Combine(
        RepoRoot, "tests", "Elsa", "Activities", "Design", "Tests", "Contracts");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    // Compare on parsed structure, not raw text, so a line-ending or trailing-newline difference between a
    // Windows checkout and a macOS/Linux run never reads as a contract change.
    private static string Normalize(string json) =>
        JsonNode.Parse(json)?.ToJsonString() ?? throw new InvalidOperationException("Contract surface document is not valid JSON.");

    /// <summary>
    /// A minimal line diff. The repo has no snapshot library, and the point is to put the changed contract
    /// lines in the failure message so a reviewer sees *what* moved without opening two files.
    /// </summary>
    private static string Diff(string expected, string actual)
    {
        var expectedLines = expected.ReplaceLineEndings("\n").Split('\n');
        var actualLines = actual.ReplaceLineEndings("\n").Split('\n');
        var expectedSet = expectedLines.ToHashSet(StringComparer.Ordinal);
        var actualSet = actualLines.ToHashSet(StringComparer.Ordinal);

        var removed = expectedLines.Where(line => !actualSet.Contains(line) && line.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var added = actualLines.Where(line => !expectedSet.Contains(line) && line.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToArray();

        return string.Join(Environment.NewLine,
            [
                .. removed.Take(40).Select(line => "- " + line.Trim()),
                .. added.Take(40).Select(line => "+ " + line.Trim())
            ]);
    }

    /// <summary>
    /// A temp folder holding exactly the shipped activity assemblies. The scanner takes a folder and reads
    /// every DLL in it, so pointing it at the test output directory would sweep in the test-double activities
    /// as well; copying the intended set keeps the baseline scoped to what actually ships.
    /// </summary>
    private sealed class ActivityAssemblyFolder : IDisposable
    {
        private ActivityAssemblyFolder(string path) => Path = path;

        public string Path { get; }

        public static ActivityAssemblyFolder Containing(IEnumerable<Assembly> assemblies)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "elsa-activity-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            foreach (var assembly in assemblies)
            {
                var source = assembly.Location;
                File.Copy(source, System.IO.Path.Combine(directory, System.IO.Path.GetFileName(source)), overwrite: true);
            }

            return new ActivityAssemblyFolder(directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a locked file must not fail the test.
            }
        }
    }
}
