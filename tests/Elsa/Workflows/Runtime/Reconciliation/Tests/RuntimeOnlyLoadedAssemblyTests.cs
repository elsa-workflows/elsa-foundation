using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// SC-B-005 measured the way the success criterion words it — <em>loaded</em>, not merely <em>unreferenced</em>.
/// </summary>
/// <remarks>
/// <para>
/// This assertion is only meaningful because of where it lives. This test assembly references the runtime,
/// reconciliation and activity-execution projects and nothing else, so the process it runs in has no reason to
/// hold a Design or Publishing assembly unless the composition under test pulled one in. The companion guard in
/// <c>tests/Elsa/Architecture</c> proves the same claim structurally over the shipped binaries; that suite
/// references Publishing and Design on purpose for its other guards, so it cannot make this particular
/// measurement.
/// </para>
/// <para>
/// The snapshot is taken <b>after</b> a full import-and-execute cycle rather than at composition time: an assembly
/// that only loads when a workflow actually runs would be invisible to a check taken any earlier.
/// </para>
/// </remarks>
public sealed class RuntimeOnlyLoadedAssemblyTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-runtime-only-loaded",
        Guid.NewGuid().ToString("N"));

    public RuntimeOnlyLoadedAssemblyTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Importing_and_executing_a_mounted_artifact_loads_no_design_or_publishing_assembly()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options = { SourceId = "mounted-artifacts", FolderPath = _mount },
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
                services.AddSingleton<IActivityTriggerStimulusProvider, ProbeTriggerStimulusProvider>();
            })
            .Build(Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray());

        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger")),
            "definition-loaded");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(executable));

        harness.InitializeActivityTypes();
        await using (var scope = harness.Services.CreateAsyncScope())
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync()).ImportedCount);

        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var routing = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(
                new StimulusDispatchRequest(
                    ArtifactClosureFixture.TriggerStimulusType,
                    ArtifactClosureFixture.TriggerStimulusHash("node-trigger"),
                    mode: StimulusRoutingMode.StartOnly,
                    idempotencyKey: "loaded-assembly-stimulus"));

            Assert.Equal(1, routing.StartedCount);
            (await harness.ReadRunAsync(Assert.Single(routing.Starts).WorkflowExecutionId!)).AssertWorkflowCompleted();
        }

        // The matcher is not vacuous. Every assertion in this file is of the form "nothing forbidden is present",
        // which a classifier that matches nothing satisfies without checking anything — this whole suite stays
        // green with it stubbed out. The exhaustive pin is DesignTimeAssemblyClassifierTests over in
        // tests/Elsa/Architecture; these two lines are what keep THIS assembly from passing on a dead matcher.
        Assert.True(DesignTimeAssemblies.IsDesignOrPublishing("Elsa.Activities.Sequence.Design"));
        Assert.False(DesignTimeAssemblies.IsDesignOrPublishing("Elsa.Persistence.Groundwork.DesignConformance"));

        // The classifier is DesignTimeAssemblies, shared with the three sibling guards in tests/Elsa/Architecture.
        // This copy used to carry its own prefix list, and it kept the pre-T128 one: the six per-package design
        // halves (Elsa.Activities.{Bpmn,ControlFlow,DispatchWorkflow,Flowchart,Graph,Sequence}.Design) matched no
        // prefix in it and were caught only transitively, through a *.Design.Core each happens to reference.
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .OfType<string>()
            .Where(DesignTimeAssemblies.IsDesignOrPublishing)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(loaded.Length == 0, "A design or publishing assembly is loaded in the runtime-only process: " + string.Join(", ", loaded));
    }
}
