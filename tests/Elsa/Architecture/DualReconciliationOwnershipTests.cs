using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// US5 scenario 2 (T102, amended by T118) — the feature's sharpest invariant. On one engine that has <b>both</b>
/// the design-side publish path and executable artifact reconciliation armed, a definition arriving through both
/// paths is resolved by the slot's explicit <see cref="WorkflowActivationSource"/> plus the requesting party's
/// declared ownership intent: the same artifact is an idempotent no-op; an explicit publish <b>takes</b> an
/// import-owned slot; reconciliation <b>never takes it back</b>, skipping loudly with a diagnostic naming the
/// owner. In every direction the definition is never double-activated and a stimulus never starts two instances.
/// </summary>
/// <remarks>
/// <para>
/// <b>The asymmetry is the design (Joey, 2026-08-17, amending FR-B-006).</b> Publishing is an explicit operator
/// command; reconciliation is a boot-time declarative import. Explicit wins — and because reconciliation runs at
/// boot and reload rather than continuously, it must not re-assert afterwards, or the next shell reload would
/// silently revert the operator. Both halves are asserted below: the second is what makes the first safe.
/// </para>
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
/// <b>The colliding artifacts are compiled, not hand-built.</b> Every competing version is produced by a separate
/// publish-capable engine and travels as exported closure bytes through the mount, which is how a foreign
/// artifact actually reaches a running host. Its content-addressed identity, trigger surface and declared
/// requirements are therefore whatever a real publish produces — and the importer's gates all pass, so the
/// outcome under test is the ownership rule firing rather than an earlier gate masking it. The mounted versions
/// deliberately sort <em>above</em> what is serving wherever the ownership rule is the subject, so latest-wins
/// would activate them and only ownership does not.
/// </para>
/// </remarks>
public sealed class DualReconciliationOwnershipTests : IDisposable
{
    private const string OrdersDefinitionId = "definition-orders";
    private const string OrdersV1VersionId = "version-orders-1";
    private const string OrdersV2VersionId = "version-orders-2";
    private const string OrdersV3VersionId = "version-orders-3";
    private const string OrdersV1NodeId = "node-order-placed";
    private const string OrdersV2NodeId = "node-order-amended";
    private const string OrdersV3NodeId = "node-order-settled";
    private const string OrdersV1Event = "order-placed";
    private const string OrdersV2Event = "order-amended";
    private const string OrdersV3Event = "order-settled";

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

    /// <summary>
    /// A publish-owned definition: the same artifact arriving through the mount is the idempotent no-op, and a
    /// different one is skipped by name rather than taking the slot.
    /// </summary>
    /// <remarks>
    /// Before T118 the skip was a <c>Rejected</c> / <c>ActivationConflict</c> entry. It is now a named
    /// <see cref="WorkflowArtifactSkipReason.ForeignSlotOwner"/> skip, which is a change of <em>report</em>, not of
    /// behaviour: nothing about the slot, the serving projection or the stimulus outcome moves either way. The
    /// artifact is not broken and its closure unit imported fine — what did not happen is activation — and only a
    /// skip can say that without also telling the operator to go fix an export that is perfectly good.
    /// </remarks>
    [Fact]
    public async Task A_publish_owned_definition_no_ops_on_its_own_artifact_and_skips_a_foreign_one_by_name()
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

        await AssertServedByExactlyOneAsync(engine, OrdersV1Event, published.ArtifactId, published.PublicationId);

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
        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, refused.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, refused.SkipReason);
        Assert.Equal(foreignV2.RootArtifactId, refused.ArtifactId);

        // The owner is NAMED. Asserting on the owner's own rendering keeps this from passing on a generic
        // "conflict" message, which is the failure mode P3 exists to prevent: an operator must be told who holds
        // the definition, not merely that somebody does.
        Assert.NotNull(refused.Diagnostic);
        Assert.Contains(WorkflowActivationSource.Publishing.Describe(), refused.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(OrdersDefinitionId, refused.Diagnostic!, StringComparison.Ordinal);
        // …and it reaches the boot log, which is the only place an operator would ever see it.
        Assert.Same(refused, Assert.Single(secondPass.OwnershipSkips));

        // Per closure unit: the rest of the batch still imported. A skipped definition is not a rejection — the
        // artifact is sound and its unit wrote everything it should have.
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, Single(secondPass, BillingDefinitionId).Outcome);
        Assert.Equal(0, secondPass.RejectedCount);

        // Never double-activated: the slot did not move, and there is still exactly ONE slot for the definition.
        var afterRefusal = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(ownedSlot.ActiveActivationId, afterRefusal!.ActiveActivationId);
        Assert.Equal(ownedSlot.Revision, afterRefusal.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterRefusal.Source!.Kind);
        Assert.Single(await engine.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .ListByDefinitionAsync(OrdersDefinitionId));

        // The refusal happened at activation, not at persistence: the skipped payload IS in the content-addressed
        // store, and is nonetheless serving nothing. That is the interesting shape — a store row that cannot run.
        Assert.NotNull(await engine.Services.GetRequiredService<IWorkflowExecutableStore>()
            .FindAsync(foreignV2.RootArtifactId));
        await AssertServedByExactlyOneAsync(engine, OrdersV1Event, published.ArtifactId, published.PublicationId);
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
    /// The other direction of ownership: an import-owned definition, an explicit publish that <b>takes</b> it,
    /// and the reconcile passes afterwards that must never take it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is T118's whole subject (Joey, 2026-08-17, approved — amends FR-B-006 and US5 scenario 2).</b>
    /// Before it, the authority refused the publish and named the owner, which meant that on a combined engine an
    /// imported definition could never be published over: the operator's only escape was deleting the mount. That
    /// is wrong. Publishing is an explicit operator command; reconciliation is a boot-time declarative import.
    /// Explicit wins.
    /// </para>
    /// <para>
    /// <b>The second half is what makes the first half safe, and it is asserted twice.</b> Reconciliation must
    /// never reclaim the slot — first with the mount <em>unchanged</em>, which is what a shell reload replays and
    /// where a silent revert of the operator's publish would live; then with the mount updated to an artifact that
    /// sorts <em>above</em> the published one, so latest-wins would activate it if ownership did not stop it. The
    /// second pass is the case an operator actually hits, and it is invisible without a diagnostic: a file is
    /// replaced, everything looks healthy, and that definition quietly keeps serving something else.
    /// </para>
    /// <para>
    /// <b>How the intent gets there matters as much as the outcome.</b> Publishing passes an explicit takeover
    /// because it is the only party that knows a human ran a publish command; the runtime honours it generically
    /// and never names publishing. The tripwire for that living where it belongs is
    /// <c>WorkflowActivationAuthorityTests.Two_reconciliation_sources_are_different_owners</c> — if takeover ever
    /// let any source claim any slot, that test fails.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_publish_takes_over_an_import_owned_definition_and_reconciliation_never_takes_it_back()
    {
        await using var engine = CombinedEngine.Create([OrdersV1(), OrdersV2(), OrdersV3()], _mount);

        // The importer activates first, so artifact reconciliation owns the slot.
        var importedV1 = await ExportFromAnotherEngineAsync(OrdersV1(), "orders-v1.json");
        var firstPass = await engine.ReconcileAsync();

        var imported = Single(firstPass, OrdersDefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, imported.Outcome);
        var importOwnedSlot = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, importOwnedSlot!.Source!.Kind);
        Assert.Equal(CombinedEngine.MountSourceId, importOwnedSlot.Source.SourceId);
        await AssertServedByExactlyOneAsync(engine, OrdersV1Event, importedV1.RootArtifactId, imported.ActivationId!);

        // ---- The explicit publish takes the slot ------------------------------------------------------------
        var published = await engine.PublishAsync(OrdersV2VersionId);
        Assert.NotEqual(importedV1.RootArtifactId, published.ArtifactId);

        var takenOver = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(published.PublicationId, takenOver!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, takenOver.Source!.Kind);
        Assert.Null(takenOver.Source.SourceId);
        // The takeover REPLACED the default slot. Two live slots for one definition from two sources is the
        // double-activation US5 forbids, so "one slot" is asserted, not assumed.
        Assert.Single(await engine.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .ListByDefinitionAsync(OrdersDefinitionId));
        await AssertServedByExactlyOneAsync(engine, OrdersV2Event, published.ArtifactId, published.PublicationId);
        // The loser's stimulus resolves to nothing — the same claim from the other side.
        Assert.Empty(await engine.ListServingBindingsAsync(OrdersV1Event));

        var publishedDelivery = await engine.DeliverEventAsync(OrdersV2Event, "stimulus-after-takeover");
        Assert.Equal(1, publishedDelivery.StartedCount);
        Assert.Equal(published.ArtifactId, Assert.Single(publishedDelivery.Starts).ArtifactId);
        Assert.Single(await engine.ListExecutionsAsync());

        var displacedDelivery = await engine.DeliverEventAsync(OrdersV1Event, "stimulus-displaced-import");
        Assert.Equal(0, displacedDelivery.StartedCount);
        Assert.Single(await engine.ListExecutionsAsync());

        // ---- Reload with the mount unchanged: no silent revert ----------------------------------------------
        var reloadPass = await engine.ReconcileAsync();

        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, Single(reloadPass, OrdersDefinitionId).Outcome);
        var afterReload = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(published.PublicationId, afterReload!.ActiveActivationId);
        Assert.Equal(takenOver.Revision, afterReload.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterReload.Source!.Kind);

        // ---- The operator replaces the mounted artifact with a NEWER one ------------------------------------
        // v3 sorts above the published v2, so latest-wins would activate it. Ownership stops it first, and the
        // only thing standing between the operator and an unexplained no-op is the diagnostic.
        File.Delete(Path.Combine(_mount, "orders-v1.json"));
        var mountedV3 = await ExportFromAnotherEngineAsync(OrdersV3(), "orders-v3.json");

        var updatedMountPass = await engine.ReconcileAsync();

        var skipped = Single(updatedMountPass, OrdersDefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, skipped.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, skipped.SkipReason);
        Assert.Equal(mountedV3.RootArtifactId, skipped.ArtifactId);
        // Not a rejection: the artifact is sound and its unit imported. What did not happen is activation.
        Assert.Equal(0, updatedMountPass.RejectedCount);
        Assert.NotNull(await engine.Services.GetRequiredService<IWorkflowExecutableStore>()
            .FindAsync(mountedV3.RootArtifactId));

        // The OWNER is named, and the entry reaches the boot log the startup task writes — a skip an operator
        // cannot see is the failure this rule was approved with a diagnostic attached to avoid.
        Assert.NotNull(skipped.Diagnostic);
        Assert.Contains(WorkflowActivationSource.Publishing.Describe(), skipped.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(OrdersDefinitionId, skipped.Diagnostic!, StringComparison.Ordinal);
        Assert.Same(skipped, Assert.Single(updatedMountPass.OwnershipSkips));

        // Nothing moved: one slot, same activation, same revision, same owner.
        var afterUpdatedMount = await engine.FindSlotAsync(OrdersDefinitionId);
        Assert.Equal(published.PublicationId, afterUpdatedMount!.ActiveActivationId);
        Assert.Equal(takenOver.Revision, afterUpdatedMount.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterUpdatedMount.Source!.Kind);
        Assert.Single(await engine.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .ListByDefinitionAsync(OrdersDefinitionId));
        await AssertServedByExactlyOneAsync(engine, OrdersV2Event, published.ArtifactId, published.PublicationId);
        Assert.Empty(await engine.ListServingBindingsAsync(OrdersV3Event));

        // And the delivered stimuli agree: the mounted v3 routes nowhere, the published v2 still starts one.
        var mountedDelivery = await engine.DeliverEventAsync(OrdersV3Event, "stimulus-skipped-artifact");
        Assert.Equal(0, mountedDelivery.StartedCount);
        Assert.Single(await engine.ListExecutionsAsync());

        var stillPublished = await engine.DeliverEventAsync(OrdersV2Event, "stimulus-after-skip");
        Assert.Equal(1, stillPublished.StartedCount);
        Assert.Equal(published.ArtifactId, Assert.Single(stillPublished.Starts).ArtifactId);
        Assert.Equal(2, (await engine.ListExecutionsAsync()).Count);
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
        string eventName,
        string expectedArtifactId,
        string expectedActivationId)
    {
        var bindings = await engine.ListServingBindingsAsync(eventName);
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

    private static WorkflowDefinitionVersion OrdersV3() =>
        CombinedEngine.EventWorkflow(OrdersDefinitionId, OrdersV3VersionId, "3.0.0", OrdersV3NodeId, OrdersV3Event);

    private static WorkflowDefinitionVersion Audit() =>
        CombinedEngine.EventWorkflow(AuditDefinitionId, AuditVersionId, "1.0.0", "node-audit", "audit-requested");

    private static WorkflowDefinitionVersion Billing() =>
        CombinedEngine.EventWorkflow(BillingDefinitionId, BillingVersionId, "1.0.0", "node-billing", "billing-requested");
}
