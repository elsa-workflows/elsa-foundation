using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US4's crashed half-import: an activation that dies <b>after the slot CAS but before the projections serve</b> is
/// compensated back to the predecessor, and the <em>next reconcile pass</em> completes the import.
/// </summary>
/// <remarks>
/// <para>
/// The injection point is deliberate. Failing at the top of the call would only prove that a request that never
/// started changes nothing — the interesting window is the one where the authority has already flipped and the
/// serving projections have not, because that is the only state in which the engine can be internally inconsistent.
/// <c>IWorkflowTriggerBindingStore.ActivateAsync</c> is exactly step 4 of the coordinator's sequence, immediately
/// after the CAS, so a throw there lands in <c>CompensateAsync</c> with <c>flipped == true</c> — the branch that
/// has to restore the predecessor rather than merely tidy up the candidate.
/// </para>
/// <para>
/// <b>No importer-side journal is involved and none may be introduced.</b> The recovery unit is the next pass, and
/// the reason it works is that the pass is a pure function of the mount: the same mount re-derives the same
/// activation id, so the retry is an overwrite rather than a second activation. Symmetric bookkeeping on the
/// importer is an explicit non-goal.
/// </para>
/// </remarks>
public sealed class HalfImportRecoveryTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-half-import",
        Guid.NewGuid().ToString("N"));

    private readonly ActivationFailureGate _gate = new();

    public HalfImportRecoveryTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_failure_after_the_slot_cas_is_compensated_and_the_next_pass_completes_the_import()
    {
        await using var harness = ArtifactImportHarness.Build(_mount, InjectMidActivationFailure);
        var v1 = TriggerExecutable("node-v1", "1.0.0");
        var v2 = TriggerExecutable("node-v2", "2.0.0");

        // Pass 1 — a healthy import establishes the predecessor there is something to restore.
        Mount(harness, v1);
        var first = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, first.Outcome);

        // Pass 2 — v2's projection activation dies between the CAS and serving. Only the candidate's activation is
        // failed, so the compensation path's own re-activation of the predecessor is allowed to succeed.
        _gate.OnlyAllow = first.ActivationId;
        Mount(harness, v2);
        var crashed = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, crashed.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.ActivationFailure, crashed.RejectionKind);
        Assert.True(_gate.Rejections > 0, "the failure was never injected, so nothing about compensation was proven.");

        // Compensation restored the predecessor: v1 owns the slot again and still serves.
        var restoredSlot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(first.ActivationId, restoredSlot!.ActiveActivationId);
        var restoredBinding = Assert.Single(
            await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));
        Assert.True(restoredBinding.IsActive);
        Assert.Equal(first.ActivationId, restoredBinding.ActivationId);
        Assert.Null((await ArtifactImportHarness.FindReferenceAsync(harness, first.ActivationId!))!.DeletedAt);

        // The candidate left nothing behind: its projections were removed and its reference retired as failed.
        var bindingStore = harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>();
        Assert.Empty(await bindingStore.ListAllByArtifactAsync(v2.Identity.ArtifactId));
        var failedReference = await FindReferenceForArtifactAsync(harness, v2.Identity.ArtifactId);
        Assert.NotNull(failedReference!.DeletedAt);
        Assert.Equal("activation-failed", failedReference.DeletedReason);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, failedReference.DeletedReason);

        // The artifact itself is in the store — an unreferenced content-addressed blob, which is why the retry is
        // an overwrite and not a re-import.
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, v2.Identity.ArtifactId));

        // Pass 3 — the mount is unchanged and the fault is gone. The importer has no journal telling it to retry:
        // the next pass simply reaches the same conclusion again.
        _gate.OnlyAllow = null;
        var healed = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, healed.Outcome);
        Assert.Equal(v2.Identity.ArtifactId, healed.ArtifactId);

        var slot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(healed.ActivationId, slot!.ActiveActivationId);

        var serving = Assert.Single(
            await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v2")));
        Assert.True(serving.IsActive);
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));

        // The retry re-used the same deterministic activation id the crashed attempt minted, so its reference is
        // live again rather than a second row beside a retired one.
        Assert.Equal(2, (await ArtifactImportHarness.ListAllReferencesAsync(harness)).Count);
        var healedReference = await FindReferenceForArtifactAsync(harness, v2.Identity.ArtifactId);
        Assert.Null(healedReference!.DeletedAt);
        Assert.Equal(healed.ActivationId, healedReference.ActivationId);

        // And the predecessor is now retired for the ordinary reason, not the failure one.
        var predecessor = await ArtifactImportHarness.FindReferenceAsync(harness, first.ActivationId!);
        Assert.NotNull(predecessor!.DeletedAt);
        Assert.Equal("activation-replaced", predecessor.DeletedReason);
    }

    private static async Task<WorkflowExecutableSourceReference?> FindReferenceForArtifactAsync(
        WorkflowExecutionHarness harness,
        string artifactId)
    {
        var reference = (await ArtifactImportHarness.ListAllReferencesAsync(harness))
            .SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ArtifactId, artifactId));
        Assert.NotNull(reference);
        return reference;
    }

    private static WorkflowExecutable TriggerExecutable(string nodeId, string artifactVersion) =>
        ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode(nodeId)),
            DefinitionId,
            artifactVersion);

    private void Mount(WorkflowExecutionHarness harness, WorkflowExecutable executable)
    {
        foreach (var stale in Directory.GetFiles(_mount, "*.json"))
            File.Delete(stale);

        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(executable));
    }

    /// <summary>
    /// Wraps whatever trigger-binding store the runtime composed, rather than substituting a hand-written one, so
    /// every step of the sequence except the injected one runs against the real implementation.
    /// </summary>
    private void InjectMidActivationFailure(IServiceCollection services)
    {
        var descriptor = services.Single(candidate => candidate.ServiceType == typeof(IWorkflowTriggerBindingStore));
        Assert.NotNull(descriptor.ImplementationType);
        services.Remove(descriptor);
        services.AddSingleton<IWorkflowTriggerBindingStore>(provider => new GatedTriggerBindingStore(
            (IWorkflowTriggerBindingStore)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!),
            _gate));
    }

    /// <summary>Which activation ids are permitted to reach serving, and how often the gate actually fired.</summary>
    private sealed class ActivationFailureGate
    {
        /// <summary>When set, every other activation id fails at the activate step. Null disables the gate.</summary>
        public string? OnlyAllow { get; set; }

        public int Rejections { get; private set; }

        public void Check(string activationId)
        {
            if (OnlyAllow is null || StringComparer.Ordinal.Equals(OnlyAllow, activationId))
                return;

            Rejections++;
            throw new InvalidOperationException(
                $"Injected fault: activation '{activationId}' died between the slot CAS and its projections serving.");
        }
    }

    private sealed class GatedTriggerBindingStore(IWorkflowTriggerBindingStore inner, ActivationFailureGate gate)
        : IWorkflowTriggerBindingStore
    {
        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            gate.Check(activationId);
            return inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        }

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(binding, cancellationToken);

        public ValueTask PrepareActivationAsync(
            string activationId,
            IReadOnlyCollection<WorkflowTriggerBinding> bindings,
            CancellationToken cancellationToken = default) =>
            inner.PrepareActivationAsync(activationId, bindings, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(
            WorkflowTriggerBindingActivationPageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByActivationAsync(query, cancellationToken);

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
            inner.DeleteByActivationAsync(activationId, cancellationToken);

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
            WorkflowTriggerBindingPageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByStimulusAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
            WorkflowTriggerBindingArtifactPageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByArtifactAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
            WorkflowTriggerBindingTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByStimulusTypeAsync(query, cancellationToken);
    }
}
