using System.Reflection;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.ArtifactPortability.Tests;

/// <summary>
/// The one execution composition both sides of the round trip are built from.
/// </summary>
/// <remarks>
/// Shared on purpose. If the exporting and importing engines were composed separately, a parity assertion would be
/// comparing two engines as much as two delivery paths, and a difference in either could explain a difference in
/// outcome. Here the two compositions differ by exactly the features under test: the publish-capable side adds
/// publishing and the design stores, the runtime-only side adds artifact reconciliation.
/// </remarks>
internal static class RuntimeEngines
{
    /// <summary>The source id the runtime-only engine's mount identifies itself by.</summary>
    internal const string SourceId = "mounted-artifacts";

    /// <summary>Enough deterministic activity-execution ids for a parent, a child and their scheduling hops.</summary>
    internal static string[] ActivityExecutionIds { get; } =
        Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray();

    /// <summary>
    /// The execution spine every engine here shares: the runtime, the resumption sweep the dispatch outbox needs,
    /// and the DispatchWorkflow runtime activity that makes a parent able to start a child at all.
    /// </summary>
    internal static WorkflowExecutionHarness.Builder NewBuilder() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .ConfigureServices(services => services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)));

    /// <summary>
    /// A runtime-only engine: the shared spine plus the artifact importer mounted on <paramref name="mount"/>, and
    /// <b>nothing</b> from Design or Publishing.
    /// </summary>
    /// <remarks>
    /// This composition is the subject of the "never saw the source definitions" claim. It shares no store, no
    /// container and no service instance with the exporting engine — the only channel between them is the file the
    /// exporter wrote into <paramref name="mount"/>.
    /// </remarks>
    internal static WorkflowExecutionHarness NewRuntimeOnlyEngine(string mount) =>
        NewBuilder()
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = SourceId,
                    FolderPath = mount,
                },
            }.ConfigureServices(services))
            .Build(ActivityExecutionIds);

    /// <summary>
    /// Every Elsa assembly the runtime-only composition's features can reach through the reference graph.
    /// </summary>
    /// <remarks>
    /// A property of the shipped binaries, which is the only form the claim can take in this assembly: this test
    /// project references Publishing on purpose, so <c>AppDomain.CurrentDomain.GetAssemblies()</c> would report
    /// Publishing as loaded no matter what the runtime-only composition does. Assemblies that fail to load are
    /// recorded rather than skipped — an unresolvable reference is still an edge, and dropping it is exactly how a
    /// forbidden one would slip past.
    /// </remarks>
    internal static IReadOnlyCollection<string> RuntimeOnlyAssemblyClosure()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(
        [
            typeof(SerializationFeature).Assembly,
            typeof(WorkflowsRuntimeApiFeature).Assembly,
            typeof(WorkflowsRuntimeTriggersFeature).Assembly,
            typeof(WorkflowsRuntimeResumptionFeature).Assembly,
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(DispatchWorkflowRuntimeFeature).Assembly,
            typeof(JsonWorkflowArtifactReconciliationFeature).Assembly,
        ]);

        while (pending.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name;
            if (name is null || !name.StartsWith("Elsa", StringComparison.Ordinal) || !visited.Add(name))
                continue;

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is not { } referenceName || !referenceName.StartsWith("Elsa", StringComparison.Ordinal))
                    continue;

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    visited.Add(referenceName);
                }
            }
        }

        // A traversal that silently found nothing would satisfy every containment check built on it.
        Assert.True(visited.Count > 15, $"The traversal only reached {visited.Count} Elsa assemblies; it is not walking the graph.");
        Assert.Contains("Elsa.Workflows.Runtime.Reconciliation", visited);
        return visited;
    }
}
