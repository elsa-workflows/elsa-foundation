using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// US5 scenario 1 (T101): an all-in-one engine with the new artifact features enabled authors, publishes and
/// executes in-process exactly as it does without them.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Unchanged from today" is measured, not asserted by adjective.</b> The same authored version is published
/// and run on two engines that differ by exactly one thing — whether
/// <c>JsonWorkflowArtifactReconciliationFeature</c> is composed — and the two outcomes are compared as a single
/// structural equality. A field added to <see cref="PublishAndRunObservation"/> automatically joins the
/// comparison, whereas a forgotten <c>Assert.Equal</c> would be silent.
/// </para>
/// <para>
/// The comparison covers all three phases the scenario names. <i>Authoring/compilation</i>: the content-addressed
/// artifact id and hash, the node count and the root node — arming the importer must not perturb the compiler.
/// <i>Publication</i>: the slot's owning source and revision, and the serving trigger binding the runtime would
/// route on. <i>Execution</i>: a real stimulus is delivered and the run's status, node ids and pinned artifact are
/// compared.
/// </para>
/// <para>
/// The mount is deliberately present but empty, which is the realistic shape of the regression risk: a host that
/// enables the feature before it has anything to import. A reconcile pass over it is asserted to be a no-op that
/// leaves the publish-owned slot untouched — the feature being armed must not be able to disturb what publishing
/// activated.
/// </para>
/// </remarks>
public sealed class CombinedEnginePublishExecuteRegressionTests : IDisposable
{
    private const string DefinitionId = "definition-orders";
    private const string VersionId = "version-orders-1";
    private const string NodeId = "node-order-placed";
    private const string EventName = "order-placed";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-combined-regression",
        Guid.NewGuid().ToString("N"));

    public CombinedEnginePublishExecuteRegressionTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Enabling_the_artifact_features_leaves_author_publish_execute_behaviour_identical()
    {
        await using var withoutArtifactFeatures = CombinedEngine.Create([Version()]);
        var baseline = await PublishAndRunAsync(withoutArtifactFeatures);

        await using var withArtifactFeatures = CombinedEngine.Create([Version()], _mount);
        var withFeatures = await PublishAndRunAsync(withArtifactFeatures);

        // The whole scenario in one line: same engine, same workflow, one extra feature, same outcome.
        Assert.Equal(baseline, withFeatures);

        // Pinned so the equality above cannot be satisfied by two equally broken runs.
        Assert.Equal(PublicationStatusView.Active, withFeatures.PublicationStatus);
        Assert.Equal(WorkflowActivationSource.PublishingKind, withFeatures.SlotSourceKind);
        Assert.Equal(WorkflowExecutionStatus.Completed, withFeatures.ExecutionStatus);
        Assert.Equal([NodeId], withFeatures.RanNodeIds);
        Assert.Equal(1, withFeatures.StartedCount);

        // Armed over an empty mount, a reconcile pass must be a no-op — and must not touch the publish-owned slot.
        var slotBefore = await withArtifactFeatures.FindSlotAsync(DefinitionId);
        var result = await withArtifactFeatures.ReconcileAsync();
        Assert.Empty(result.Entries);

        var slotAfter = await withArtifactFeatures.FindSlotAsync(DefinitionId);
        Assert.Equal(slotBefore!.ActiveActivationId, slotAfter!.ActiveActivationId);
        Assert.Equal(slotBefore.Revision, slotAfter.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, slotAfter.Source!.Kind);
    }

    private static WorkflowDefinitionVersion Version() =>
        CombinedEngine.EventWorkflow(DefinitionId, VersionId, "1.0.0", NodeId, EventName);

    private static async Task<PublishAndRunObservation> PublishAndRunAsync(CombinedEngine engine)
    {
        var published = await engine.PublishAsync(VersionId);
        var slot = await engine.FindSlotAsync(DefinitionId);
        var bindings = await engine.ListServingBindingsAsync(EventName);
        var binding = Assert.Single(bindings);

        var routing = await engine.DeliverEventAsync(EventName, idempotencyKey: "regression-stimulus");
        var start = Assert.Single(routing.Starts);
        var run = await engine.Harness.ReadRunAsync(start.WorkflowExecutionId!);
        var executions = await engine.ListExecutionsAsync();

        Assert.NotNull(slot);
        Assert.NotNull(run.WorkflowState);

        return new PublishAndRunObservation(
            ArtifactId: published.ArtifactId,
            ArtifactHash: published.ArtifactHash,
            NodeCount: published.NodeCount,
            RootActivityId: published.RootActivityId,
            SlotName: published.SlotName,
            PublicationStatus: published.Status,
            SlotSourceKind: slot!.Source!.Kind,
            SlotRevision: slot.Revision,
            SlotServesPublication: StringComparer.Ordinal.Equals(slot.ActiveActivationId, published.PublicationId),
            BindingStimulusType: binding.StimulusType,
            BindingStimulusHash: binding.StimulusHash,
            BindingArtifactId: binding.ArtifactId,
            BindingServesPublication: StringComparer.Ordinal.Equals(binding.ActivationId, published.PublicationId),
            StartedCount: routing.StartedCount,
            ExecutionCount: executions.Count,
            ExecutionStatus: run.WorkflowState!.Status,
            PinnedArtifactId: run.WorkflowState.PinnedExecutable.ArtifactId,
            RanNodeIds: run.RanNodeIds.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// One engine's complete author → publish → execute outcome, comparable as a value.
    /// </summary>
    /// <remarks>
    /// The activation and publication <em>ids</em> are deliberately not members — they are freshly minted per
    /// engine and would differ for reasons that have nothing to do with behaviour. What is compared instead is
    /// whether the slot and the serving binding point <em>at</em> the publication each engine produced, which is
    /// the same claim without the incidental identity.
    /// </remarks>
    private sealed record PublishAndRunObservation(
        string ArtifactId,
        string ArtifactHash,
        int NodeCount,
        string RootActivityId,
        string SlotName,
        PublicationStatusView PublicationStatus,
        string SlotSourceKind,
        long SlotRevision,
        bool SlotServesPublication,
        string BindingStimulusType,
        string BindingStimulusHash,
        string BindingArtifactId,
        bool BindingServesPublication,
        int StartedCount,
        int ExecutionCount,
        WorkflowExecutionStatus ExecutionStatus,
        string PinnedArtifactId,
        string[] RanNodeIds)
    {
        public bool Equals(PublishAndRunObservation? other) =>
            other is not null &&
            StringComparer.Ordinal.Equals(ArtifactId, other.ArtifactId) &&
            StringComparer.Ordinal.Equals(ArtifactHash, other.ArtifactHash) &&
            NodeCount == other.NodeCount &&
            StringComparer.Ordinal.Equals(RootActivityId, other.RootActivityId) &&
            StringComparer.Ordinal.Equals(SlotName, other.SlotName) &&
            PublicationStatus == other.PublicationStatus &&
            StringComparer.Ordinal.Equals(SlotSourceKind, other.SlotSourceKind) &&
            SlotRevision == other.SlotRevision &&
            SlotServesPublication == other.SlotServesPublication &&
            StringComparer.Ordinal.Equals(BindingStimulusType, other.BindingStimulusType) &&
            StringComparer.Ordinal.Equals(BindingStimulusHash, other.BindingStimulusHash) &&
            StringComparer.Ordinal.Equals(BindingArtifactId, other.BindingArtifactId) &&
            BindingServesPublication == other.BindingServesPublication &&
            StartedCount == other.StartedCount &&
            ExecutionCount == other.ExecutionCount &&
            ExecutionStatus == other.ExecutionStatus &&
            StringComparer.Ordinal.Equals(PinnedArtifactId, other.PinnedArtifactId) &&
            RanNodeIds.SequenceEqual(other.RanNodeIds, StringComparer.Ordinal);

        public override int GetHashCode() => HashCode.Combine(ArtifactId, ArtifactHash, ExecutionStatus, StartedCount);
    }
}
