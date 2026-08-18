using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// US5 scenario 2 (T102) — the feature's sharpest invariant. On one engine that has <b>both</b> the design-side
/// publish path and executable artifact reconciliation armed, a definition arriving through both paths is
/// resolved by the slot's explicit <see cref="WorkflowActivationSource"/>: the same artifact is an idempotent
/// no-op, a different artifact from the non-owning source is refused loudly with a diagnostic naming the owner,
/// the definition is never double-activated, and a stimulus never starts two instances.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every assertion is against serving state or against an actually delivered stimulus.</b> "Never
/// double-activated" is read from the activation slot <em>and</em> from
/// <c>IWorkflowTriggerBindingStore.ListAllByStimulusAsync</c> — the active-rows-only projection the stimulus
/// router literally routes on — never from a log line or a return value. "One stimulus, one instance" delivers a
/// real named-event stimulus through the production <c>IStimulusRouter</c> and counts the workflow execution
/// states the engine ends up holding, with the engine minting a distinct execution id per start so a genuine
/// double-start cannot collapse onto one row.
/// </para>
/// <para>
/// <b>The colliding artifacts are compiled, not hand-built.</b> The competing v2 is produced by a separate
/// publish-capable engine and travels as exported closure bytes through the mount, which is how a foreign
/// artifact actually reaches a running host. Its content-addressed identity, trigger surface and declared
/// requirements are therefore whatever a real publish produces — and the importer's gates all pass, so the
/// rejection under test is the ownership rule firing rather than an earlier gate masking it.
/// </para>
/// </remarks>
public sealed class DualReconciliationOwnershipTests : IDisposable
{
    private const string OrdersDefinitionId = "definition-orders";
    private const string OrdersV1VersionId = "version-orders-1";
    private const string OrdersV2VersionId = "version-orders-2";
    private const string OrdersV1NodeId = "node-order-placed";
    private const string OrdersV2NodeId = "node-order-amended";
    private const string OrdersV1Event = "order-placed";
    private const string OrdersV2Event = "order-amended";

    private const string AuditDefinitionId = "definition-audit";
    private const string AuditVersionId = "version-audit-1";
    private const string BillingDefinitionId = "definition-billing";
    private const string BillingVersionId = "version-billing-1";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-dual-reconciliation",
        Guid.NewGuid().ToString("N"));

    public DualReconciliationOwnershipTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_publish_owned_definition_no_ops_on_its_own_artifact_and_refuses_a_foreign_one_by_name()
    {
        await using var engine = CombinedEngine.Create([OrdersV1(), OrdersV2()], _mount);

        // The design-side path activates first, so publishing owns the slot.
        var published = await engine.PublishAsync(OrdersV1VersionId);
        var ownedSlot = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(published.PublicationId, ownedSlot!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, ownedSlot.Source!.Kind);

        // ---- Path two, same artifact -----------------------------------------------------------------------
        // The very artifact publishing activated, arriving as mounted closure bytes. Exported from this engine so
        // "same artifact" is a fact about the content-addressed id rather than a hope about two compilations.
        var exported = await engine.ExportToAsync(OrdersV1VersionId, _mount, "orders-v1.json");
        Assert.Equal(published.ArtifactId, exported.RootArtifactId);
        await ExportFromAnotherEngineAsync(Audit(), "audit.json");

        var firstPass = await engine.ReconcileAsync();

        var noOp = Single(firstPass, OrdersDefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.AlreadyCurrent, noOp.Outcome);
        Assert.Equal(published.ArtifactId, noOp.ArtifactId);
        // The no-op resolved onto publishing's activation — it did not mint an import activation beside it.
        Assert.Equal(published.PublicationId, noOp.ActivationId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, Single(firstPass, AuditDefinitionId).Outcome);

        var afterNoOp = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(ownedSlot.ActiveActivationId, afterNoOp!.ActiveActivationId);
        Assert.Equal(ownedSlot.Revision, afterNoOp.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterNoOp.Source!.Kind);

        await AssertServedByExactlyOneAsync(engine, published.ArtifactId, published.PublicationId);

        // One stimulus, one instance — delivered, not inferred.
        var firstDelivery = await engine.DeliverEventAsync(OrdersV1Event, "stimulus-after-no-op");
        Assert.Equal(1, firstDelivery.StartedCount);
        Assert.Single(firstDelivery.Starts);
        Assert.Equal(published.ArtifactId, Assert.Single(firstDelivery.Starts).ArtifactId);
        Assert.Single(await engine.ListExecutionsAsync());

        // ---- Path two, a different artifact ----------------------------------------------------------------
        // v2 is a genuinely different content-addressed artifact for the same definition, built elsewhere, and it
        // sorts ABOVE the active version — so latest-wins would activate it if ownership did not stop it first.
        File.Delete(Path.Combine(_mount, "orders-v1.json"));
        var foreignV2 = await ExportFromAnotherEngineAsync(OrdersV2(), "orders-v2.json");
        await ExportFromAnotherEngineAsync(Billing(), "billing.json");
        Assert.NotEqual(published.ArtifactId, foreignV2.RootArtifactId);

        var secondPass = await engine.ReconcileAsync();

        var refused = Single(secondPass, OrdersDefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, refused.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.ActivationConflict, refused.RejectionKind);
        Assert.Equal(foreignV2.RootArtifactId, refused.ArtifactId);

        // The owner is NAMED. Asserting on the owner's own rendering keeps this from passing on a generic
        // "conflict" message, which is the failure mode P3 exists to prevent: an operator must be told who holds
        // the definition, not merely that somebody does.
        Assert.NotNull(refused.Diagnostic);
        Assert.Contains(WorkflowActivationSource.Publishing.Describe(), refused.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(OrdersDefinitionId, refused.Diagnostic!, StringComparison.Ordinal);

        // Per closure unit: the rest of the batch still imported.
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, Single(secondPass, BillingDefinitionId).Outcome);
        Assert.Equal(1, secondPass.RejectedCount);

        // Never double-activated: the slot did not move, and there is still exactly ONE slot for the definition.
        var afterRefusal = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(ownedSlot.ActiveActivationId, afterRefusal!.ActiveActivationId);
        Assert.Equal(ownedSlot.Revision, afterRefusal.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterRefusal.Source!.Kind);
        Assert.Single(await engine.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .ListByDefinitionAsync(OrdersDefinitionId));

        // The refusal happened at activation, not at persistence: the rejected payload IS in the content-addressed
        // store, and is nonetheless serving nothing. That is the interesting shape — a store row that cannot run.
        Assert.NotNull(await engine.Services.GetRequiredService<IWorkflowExecutableStore>()
            .FindAsync(foreignV2.RootArtifactId));
        await AssertServedByExactlyOneAsync(engine, published.ArtifactId, published.PublicationId);
        Assert.Empty(await engine.ListServingBindingsAsync(OrdersV2Event));

        // And one stimulus still starts exactly one instance — one more than before the delivery, not two.
        var secondDelivery = await engine.DeliverEventAsync(OrdersV1Event, "stimulus-after-refusal");
        Assert.Equal(1, secondDelivery.StartedCount);
        Assert.Single(secondDelivery.Starts);
        Assert.Equal(published.ArtifactId, Assert.Single(secondDelivery.Starts).ArtifactId);
        Assert.Equal(2, (await engine.ListExecutionsAsync()).Count);

        // The refused artifact's own stimulus routes nowhere, which is the same claim from the other side.
        var amendedDelivery = await engine.DeliverEventAsync(OrdersV2Event, "stimulus-refused-artifact");
        Assert.Equal(0, amendedDelivery.StartedCount);
        Assert.Equal(2, (await engine.ListExecutionsAsync()).Count);
    }

    /// <summary>
    /// The other direction of ownership: an import-owned definition, and a publish that tries to take it over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The serving-state half of FR-B-006 holds in both directions: the definition is not double-activated by a
    /// publish, the mounted artifact keeps serving, and a stimulus still starts exactly one instance.
    /// </para>
    /// <para>
    /// <b>The surfacing half is what T116 added.</b> Publishing used to resolve every live activation of the
    /// definition through <c>IPublicationRecordStore</c> and throw a raw
    /// <see cref="InvalidOperationException"/> — <c>"Active publication '…' does not exist."</c> — when the lookup
    /// missed. An import-owned slot holds an activation publishing never journalled, so the lookup always missed,
    /// and the operator was told a record was absent rather than who owns the definition; FR-B-006's ownership
    /// rules never ran at all, on either leg. Both legs are now decided by those rules:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// a <b>different</b> artifact is refused by the activation authority and surfaces as publishing's own
    /// <c>PublicationActivationException</c> carrying <c>slot_owner_conflict</c> and a diagnostic that
    /// <b>names</b> the owning <see cref="WorkflowActivationSource"/> — the mirror of the importer's
    /// <c>ActivationConflict</c> entry in the test above;
    /// </item>
    /// <item>
    /// the <b>same</b> artifact is FR-B-006's idempotent no-op, which the coordinator resolves before the slot is
    /// ever touched, so the publish succeeds having written nothing to the ledger.
    /// </item>
    /// </list>
    /// <para>
    /// Note the refusal is an <em>activation</em> conflict, not the <c>PublicationPreflightConflictException</c>
    /// T102's note anticipated. Preflight answers a narrower question — cross-slot trigger cardinality — and here
    /// the contending activation is in the very slot being published to, so preflight correctly treats it as the
    /// replaced baseline and raises nothing. Ownership is the authority's rule to enforce, and it is enforced
    /// where the slot actually moves.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_import_owned_definition_is_not_taken_over_by_a_publish_and_the_refusal_names_the_owning_source()
    {
        await using var engine = CombinedEngine.Create([OrdersV1(), OrdersV2()], _mount);

        // This time the importer activates first, so artifact reconciliation owns the slot.
        var importedV1 = await ExportFromAnotherEngineAsync(OrdersV1(), "orders-v1.json");
        var pass = await engine.ReconcileAsync();

        var imported = Single(pass, OrdersDefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, imported.Outcome);
        var ownedSlot = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, ownedSlot!.Source!.Kind);
        Assert.Equal(CombinedEngine.MountSourceId, ownedSlot.Source.SourceId);
        await AssertServedByExactlyOneAsync(engine, importedV1.RootArtifactId, imported.ActivationId!);

        // The design-side path tries to take the definition over with a DIFFERENT artifact.
        var differentArtifact = await Record.ExceptionAsync(() => engine.PublishAsync(OrdersV2VersionId));

        // …and, separately, with the SAME artifact, which FR-B-006 defines as the idempotent no-op.
        var sameArtifact = await Record.ExceptionAsync(() => engine.PublishAsync(OrdersV1VersionId));

        // The invariant that matters holds in both cases: nothing was double-activated, and the mounted artifact
        // is still the one serving.
        var afterPublish = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(ownedSlot.ActiveActivationId, afterPublish!.ActiveActivationId);
        Assert.Equal(ownedSlot.Revision, afterPublish.Revision);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, afterPublish.Source!.Kind);
        Assert.Equal(CombinedEngine.MountSourceId, afterPublish.Source.SourceId);
        Assert.Single(await engine.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .ListByDefinitionAsync(OrdersDefinitionId));
        await AssertServedByExactlyOneAsync(engine, importedV1.RootArtifactId, imported.ActivationId!);
        Assert.Empty(await engine.ListServingBindingsAsync(OrdersV2Event));

        var delivery = await engine.DeliverEventAsync(OrdersV1Event, "stimulus-after-refused-publish");
        Assert.Equal(1, delivery.StartedCount);
        Assert.Equal(importedV1.RootArtifactId, Assert.Single(delivery.Starts).ArtifactId);
        Assert.Single(await engine.ListExecutionsAsync());

        // The diagnostic, which is the half T116 fixed. The different artifact is refused BY NAME; the same
        // artifact is the no-op and therefore raises nothing at all.
        AssertOwnershipRefusal(differentArtifact);
        Assert.Null(sameArtifact);
    }

    /// <summary>
    /// The intended shape of a publish into a foreign-owned activation slot: publishing's own activation-conflict
    /// exception, carrying the ownership code and a diagnostic naming the owner.
    /// </summary>
    /// <remarks>
    /// Asserting on the owner's own <c>Describe()</c> rendering — kind <em>and</em> source id — keeps this from
    /// passing on a generic "conflict" message, which is the failure mode P3 exists to prevent. The negative
    /// assertion is the defect this replaced: absence of a <c>PublicationRecord</c> reported as missing data.
    /// </remarks>
    private static void AssertOwnershipRefusal(Exception? failure)
    {
        var refusal = Assert.IsType<PublicationActivationException>(failure);
        Assert.Equal("slot_owner_conflict", refusal.Code);
        Assert.Contains(
            WorkflowActivationSource.ArtifactReconciliation(CombinedEngine.MountSourceId).Describe(),
            refusal.Message,
            StringComparison.Ordinal);
        Assert.Contains(OrdersDefinitionId, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts the definition is served by exactly one trigger binding, pointing at one artifact and one
    /// activation.
    /// </summary>
    /// <remarks>
    /// This is the projection the stimulus router resolves against, so it — not the slot — is the state a
    /// double activation would actually be visible in as a double start.
    /// </remarks>
    private static async Task AssertServedByExactlyOneAsync(
        CombinedEngine engine,
        string expectedArtifactId,
        string expectedActivationId)
    {
        var bindings = await engine.ListServingBindingsAsync(OrdersV1Event);
        var binding = Assert.Single(bindings);
        Assert.Equal(expectedArtifactId, binding.ArtifactId);
        Assert.Equal(expectedActivationId, binding.ActivationId);
    }

    /// <summary>
    /// Compiles, publishes and exports one authored version on a throwaway publish-capable engine, writing the
    /// closure bytes into the mount.
    /// </summary>
    /// <remarks>
    /// A separate container on purpose: the artifact has to arrive the way a foreign one does — through the wire
    /// format and the production JSON source — rather than by sharing a store with the engine under test.
    /// </remarks>
    private async Task<WorkflowArtifactClosure> ExportFromAnotherEngineAsync(
        WorkflowDefinitionVersion version,
        string fileName)
    {
        await using var builder = CombinedEngine.Create([version]);
        await builder.PublishAsync(version.Id);
        return await builder.ExportToAsync(version.Id, _mount, fileName);
    }

    private static WorkflowArtifactImportEntry Single(WorkflowArtifactReconciliationResult result, string definitionId) =>
        Assert.Single(result.Entries, entry => entry.DefinitionId == definitionId);

    private static WorkflowDefinitionVersion OrdersV1() =>
        CombinedEngine.EventWorkflow(OrdersDefinitionId, OrdersV1VersionId, "1.0.0", OrdersV1NodeId, OrdersV1Event);

    private static WorkflowDefinitionVersion OrdersV2() =>
        CombinedEngine.EventWorkflow(OrdersDefinitionId, OrdersV2VersionId, "2.0.0", OrdersV2NodeId, OrdersV2Event);

    private static WorkflowDefinitionVersion Audit() =>
        CombinedEngine.EventWorkflow(AuditDefinitionId, AuditVersionId, "1.0.0", "node-audit", "audit-requested");

    private static WorkflowDefinitionVersion Billing() =>
        CombinedEngine.EventWorkflow(BillingDefinitionId, BillingVersionId, "1.0.0", "node-billing", "billing-requested");
}
