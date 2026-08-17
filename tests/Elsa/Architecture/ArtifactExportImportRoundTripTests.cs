using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// SC-B-003 / US3 scenario 5: a parent-with-child closure exported from a publish-capable engine is imported by a
/// runtime that never saw the source definitions, and the parent still dispatches the child.
/// </summary>
/// <remarks>
/// <para>
/// This is the feature's headline claim, so the test is written to be hard to satisfy accidentally.
/// </para>
/// <list type="number">
/// <item>
/// <b>The artifacts are compiled, not hand-built.</b> Both come out of the production
/// <c>IWorkflowExecutableCompiler</c>, so the child carries a real engine intrinsic — the reserved
/// <c>"intrinsic"</c> consumer key — and the parent carries a pinned CLR activity contract and a dependency edge
/// contributed by <c>DispatchPinSource</c>. Every earlier import fixture in this repo is intrinsic-free and
/// dependency-edge-by-hand, which is exactly why the intrinsic gate axis sat unproven for three commits.
/// </item>
/// <item>
/// <b>The bytes make the trip.</b> The exported closure is encoded by the runtime-owned
/// <c>IWorkflowArtifactClosureSerializer</c>, written to a folder, and read back by the production JSON source.
/// The importing engine is never handed the in-memory <c>WorkflowArtifactClosure</c>, so a codec that dropped or
/// reshaped a field would fail here rather than pass invisibly.
/// </item>
/// <item>
/// <b>The child must actually execute.</b> The assertion subject is the child's own durable execution state and
/// the correlation id its intrinsic wrote there — not a persisted artifact, not a binding row. A round trip that
/// imported two artifacts and never ran them would prove serialization, not portability.
/// </item>
/// <item>
/// <b>Parity is measured, not asserted by adjective.</b> The same compiled artifacts are also published and run
/// in place on the exporting engine, and the two runs are compared field by field through
/// <see cref="DispatchObservation"/>.
/// </item>
/// </list>
/// </remarks>
public sealed class ArtifactExportImportRoundTripTests : IDisposable
{
    private const string ParentExecutionId = "wfexec-parent";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-round-trip",
        Guid.NewGuid().ToString("N"));

    public ArtifactExportImportRoundTripTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task An_exported_closure_runs_on_a_runtime_that_never_saw_the_definitions_with_parity_to_compile_in_place()
    {
        await using var publisher = PublishCapableEngine.Create();
        var (parent, child) = await publisher.CompileAndPublishAsync();

        // The compiler, not the fixture, produced the closure's shape. Assert that before relying on it: a parent
        // with no dependency edge would still round-trip, and would prove nothing about a closure.
        var edge = Assert.Single(parent.Dependencies);
        Assert.Equal(child.Identity.ArtifactId, edge.ArtifactId);
        Assert.Equal(child.Identity.ArtifactHash, edge.ArtifactHash);
        Assert.Equal([PublishCapableEngine.ParentNodeId], edge.DispatchNodeIds.ToArray());
        Assert.Contains(child.RuntimeRequirements, requirement => requirement.ConsumerKey == "intrinsic");

        // Export → wire bytes → file. Nothing but this file crosses between the two engines.
        var closure = await publisher.ExportAsync(PublishCapableEngine.ParentVersionId);
        Assert.Equal(parent.Identity.ArtifactId, closure.RootArtifactId);
        Assert.Equal(
            new[] { child.Identity.ArtifactId, parent.Identity.ArtifactId }.Order(StringComparer.Ordinal).ToArray(),
            closure.Artifacts.Select(artifact => artifact.Identity.ArtifactId).Order(StringComparer.Ordinal).ToArray());
        var wire = publisher.Encode(closure);
        File.WriteAllText(Path.Combine(_mount, "closure.json"), wire);

        // The wire form really is the transport, and it really carries the compiled shape: the reserved intrinsic
        // consumer key and both content-addressed ids are in the bytes, and the recomputed node projections the
        // codec drops are not.
        Assert.Contains("\"intrinsic\"", wire, StringComparison.Ordinal);
        Assert.Contains(child.Identity.ArtifactHash, wire, StringComparison.Ordinal);
        Assert.Contains(parent.Identity.ArtifactHash, wire, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nodesById\"", wire, StringComparison.OrdinalIgnoreCase);

        // The parent's pinned-target metadata rides in the bytes, and with it the EXPORTING engine's
        // source-reference id — an identifier the importing engine has never heard of, sitting inside a
        // content-hashed node. It survives the trip because it is provenance, not admission: the child start is
        // authorized by the parent's dependency edge (RetainedDependency), not by that reference. Asserted rather
        // than left implicit, because the day something starts resolving it the round trip breaks here first.
        Assert.Contains("source-child", wire, StringComparison.Ordinal);

        // Compile-in-place: the same compiled artifacts, run where they were compiled.
        var inPlace = await RunParentAsync(publisher.Harness, "source-parent");

        // The round trip.
        await using var runtimeOnly = RuntimeEngines.NewRuntimeOnlyEngine(_mount);
        AssertNeverSawTheSourceDefinitions(runtimeOnly, publisher);

        runtimeOnly.InitializeActivityTypes();
        WorkflowArtifactReconciliationResult result;
        await using (var scope = runtimeOnly.Services.CreateAsyncScope())
            result = await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(
            [child.Identity.ArtifactId, parent.Identity.ArtifactId],
            result.Entries.Select(entry => entry.ArtifactId).ToArray());

        // The importer minted its own activation and its own source reference; the exporter's are provenance only.
        var parentEntry = result.Entries.Single(entry => entry.DefinitionId == PublishCapableEngine.ParentDefinitionId);
        var importedReference = await runtimeOnly.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(parentEntry.ActivationId!));
        Assert.NotNull(importedReference);
        Assert.NotEqual("source-parent", importedReference!.SourceReferenceId);

        var imported = await RunParentAsync(runtimeOnly, importedReference.SourceReferenceId);

        // The claim, stated directly: the parent dispatched, the child ran on the importing engine, and the child's
        // own intrinsic left its effect on the child's own execution state.
        Assert.Equal(WorkflowExecutionStatus.Completed, imported.ParentStatus);
        Assert.Equal([PublishCapableEngine.ParentNodeId], imported.ParentNodeIds);
        Assert.Equal(WorkflowDispatchStatus.Completed, imported.DispatchStatus);
        Assert.Equal(WorkflowExecutionStatus.Completed, imported.ChildStatus);
        Assert.Equal([PublishCapableEngine.ChildNodeId], imported.ChildNodeIds);
        Assert.Equal(PublishCapableEngine.ChildCorrelationEffect, imported.ChildCorrelationId);
        Assert.Equal(child.Identity.ArtifactId, imported.ChildArtifactId);
        Assert.Equal(1, imported.ChildDispatchNestingDepth);

        // Behavior parity, compared rather than asserted: same outcome on both engines from the same bytes. Pinning
        // the imported side to literals above is what keeps this line from being satisfied by two empty runs.
        Assert.Equal(inPlace, imported);
    }

    [Fact]
    public async Task The_importing_engine_holds_nothing_of_the_source_definitions_before_the_bytes_arrive()
    {
        await using var runtimeOnly = RuntimeEngines.NewRuntimeOnlyEngine(_mount);

        // A structurally runtime-only container: the compile side is not merely unused, it is unresolvable.
        Assert.Null(runtimeOnly.Services.GetService<IWorkflowExecutableCompiler>());
        Assert.Null(runtimeOnly.Services.GetService<IWorkflowArtifactClosureFactory>());
        Assert.Null(runtimeOnly.Services.GetService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>());
        Assert.Null(runtimeOnly.Services.GetService<Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore>());

        // And it holds no artifact of any kind until a mount is reconciled.
        var stored = await runtimeOnly.Services.GetRequiredService<IWorkflowExecutableStore>()
            .ListPageAsync(new RuntimeStorePageRequest(limit: 10));
        Assert.Empty(stored.Items);
    }

    /// <summary>
    /// Establishes "never saw the source definitions" as a property of the composition rather than an intention.
    /// </summary>
    /// <remarks>
    /// Three independent mechanisms, because each one alone has a hole. The container check proves the importing
    /// engine cannot reach a compiler or a design store even if something asked. The instance check proves the two
    /// engines share no store object, so nothing arrived by aliasing rather than by import. The reference-closure
    /// check proves the claim about the shipped binaries — the one an <c>AppDomain</c> assembly snapshot cannot
    /// make in this assembly, which references Publishing on purpose.
    /// </remarks>
    private static void AssertNeverSawTheSourceDefinitions(WorkflowExecutionHarness runtimeOnly, PublishCapableEngine publisher)
    {
        Assert.Null(runtimeOnly.Services.GetService<IWorkflowExecutableCompiler>());
        Assert.Null(runtimeOnly.Services.GetService<IWorkflowArtifactClosureFactory>());
        Assert.Null(runtimeOnly.Services.GetService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>());

        Assert.NotSame(
            publisher.Services.GetRequiredService<IWorkflowExecutableStore>(),
            runtimeOnly.Services.GetRequiredService<IWorkflowExecutableStore>());
        Assert.NotSame(
            publisher.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            runtimeOnly.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());

        var forbidden = RuntimeEngines.RuntimeOnlyAssemblyClosure()
            .Where(name =>
                name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal) ||
                name.StartsWith("Elsa.Workflows.Publishing", StringComparison.Ordinal) ||
                name.StartsWith("Elsa.Activities.Design", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            forbidden.Length == 0,
            "The runtime-only composition's reference closure reaches: " + string.Join(", ", forbidden));
    }

    /// <summary>
    /// Starts the parent through the production start dispatcher and sweeps the post-commit outbox to quiescence,
    /// then reads back everything the run left behind.
    /// </summary>
    /// <remarks>
    /// The sweep is not optional scaffolding: <c>DispatchWorkflow</c> stages a durable intent rather than starting
    /// the child inline, so a drive that stopped at the parent's checkpoint would observe a parent that "completed"
    /// while the child had never been asked to run.
    /// </remarks>
    private static async Task<DispatchObservation> RunParentAsync(WorkflowExecutionHarness harness, string parentSourceReferenceId)
    {
        var reference = await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(parentSourceReferenceId);
        Assert.NotNull(reference);

        await harness.StartPublishedAsync(reference!, ParentExecutionId);
        await harness.SweepUntilQuietAsync();

        var dispatch = Assert.Single(
            await harness.Services.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentExecutionId));
        var parentState = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(ParentExecutionId);
        var childState = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(dispatch.ChildWorkflowExecutionId);
        var parentActivities = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(ParentExecutionId);
        var childActivities = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(dispatch.ChildWorkflowExecutionId);

        Assert.NotNull(parentState);
        Assert.NotNull(childState);

        return new DispatchObservation(
            ParentStatus: parentState!.Status,
            ParentNodeIds: parentActivities.Select(activity => activity.Execution.ExecutableNodeId).Order(StringComparer.Ordinal).ToArray(),
            DispatchStatus: dispatch.Status,
            ChildStatus: childState!.Status,
            ChildCorrelationId: childState.CorrelationId,
            ChildArtifactId: childState.PinnedExecutable.ArtifactId,
            ChildNodeIds: childActivities.Select(activity => activity.Execution.ExecutableNodeId).Order(StringComparer.Ordinal).ToArray(),
            ChildDispatchNestingDepth: childState.DispatchNestingDepth);
    }

    /// <summary>
    /// The comparable outcome of one parent-dispatches-child run.
    /// </summary>
    /// <remarks>
    /// A record rather than a bag of assertions so parity is a single structural equality: a field added here is
    /// automatically part of the comparison, whereas a forgotten <c>Assert.Equal</c> is silent. The child's
    /// artifact id and correlation id are the load-bearing members — together they say the imported artifact, not
    /// some local stand-in, ran and had its effect.
    /// </remarks>
    private sealed record DispatchObservation(
        WorkflowExecutionStatus ParentStatus,
        string[] ParentNodeIds,
        WorkflowDispatchStatus DispatchStatus,
        WorkflowExecutionStatus ChildStatus,
        string? ChildCorrelationId,
        string ChildArtifactId,
        string[] ChildNodeIds,
        int ChildDispatchNestingDepth)
    {
        public bool Equals(DispatchObservation? other) =>
            other is not null &&
            ParentStatus == other.ParentStatus &&
            ParentNodeIds.SequenceEqual(other.ParentNodeIds, StringComparer.Ordinal) &&
            DispatchStatus == other.DispatchStatus &&
            ChildStatus == other.ChildStatus &&
            StringComparer.Ordinal.Equals(ChildCorrelationId, other.ChildCorrelationId) &&
            StringComparer.Ordinal.Equals(ChildArtifactId, other.ChildArtifactId) &&
            ChildNodeIds.SequenceEqual(other.ChildNodeIds, StringComparer.Ordinal) &&
            ChildDispatchNestingDepth == other.ChildDispatchNestingDepth;

        public override int GetHashCode() => HashCode.Combine(ParentStatus, DispatchStatus, ChildStatus, ChildArtifactId);
    }
}
