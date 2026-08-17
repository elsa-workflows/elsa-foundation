using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// SC-B-002: an artifact whose requirements the engine cannot meet is rejected <b>at reconcile</b> with a clear
/// diagnostic — <b>never</b> surfacing as a first-activation fault in production.
/// </summary>
/// <remarks>
/// <para>
/// The success criterion has two halves and the second is the one that is easy to leave unproven. "Rejected at
/// reconcile" is straightforward. "Never as a first-activation fault" means asserting the <em>absence</em> of a
/// downstream failure, which can only be done by showing there is nothing left for the fault to happen to: no
/// stored executable, no live slot, no source reference, no trigger binding. A gate that rejected the artifact but
/// still activated it would pass a naive first-half-only test.
/// </para>
/// <para>
/// The T012 classification is paired in here deliberately rather than left in the publishing suite. It is the
/// defense-in-depth path behind this criterion — what happens to an artifact that somehow activates past the gate
/// — and the two only make sense read together: the gate is the primary detection, the classification is what
/// keeps the residual case from being retried forever as if it were a transient activity failure.
/// </para>
/// </remarks>
public sealed class UnexecutableArtifactFailsAtReconcileTests : IDisposable
{
    private const string MissingTypeAlias = "Acme.Warehouse.PickAndPack";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-scb002",
        Guid.NewGuid().ToString("N"));

    public UnexecutableArtifactFailsAtReconcileTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task The_failure_surfaces_at_reconcile_and_leaves_nothing_that_could_fault_at_first_activation()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var trigger = ArtifactClosureFixture.AsStartTrigger(
            ArtifactClosureFixture.UnregisteredClrNode("node-trigger", MissingTypeAlias));
        var executable = ArtifactClosureFixture.Executable(trigger, "definition-unrunnable");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "unrunnable.json", ArtifactClosureFixture.Closure(executable));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        // Half one: refused at reconcile, by name.
        var entry = Assert.Single(result.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains(MissingTypeAlias, entry.Diagnostic);
        Assert.Contains("not registered on this runtime", entry.Diagnostic);

        // Half two: nothing survives the pass that a first activation could fault on. A trigger node is used on
        // purpose — it is the one shape that could be started by an arriving stimulus rather than by an explicit
        // call, so it is the shape most likely to fault "much later, in production".
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-unrunnable"));

        var bindings = await harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(
                ArtifactClosureFixture.TriggerStimulusType,
                ArtifactClosureFixture.TriggerStimulusHash("node-trigger")));
        Assert.Empty(bindings.Items);
    }

    [Fact]
    public void A_missing_activity_type_that_somehow_reaches_activation_is_a_non_retryable_deployment_incident()
    {
        // Defense in depth behind the gate above (T012). Before the re-parenting, Classify returned null for this
        // one failure — UnknownActivityTypeException extended Exception directly — so a missing activity type was
        // the single activation failure the incident machinery could not describe, and it retried as though the
        // activity had merely misbehaved. Redeploying is the only cure, so it must classify as a deployment
        // correction like every sibling kind.
        var failure = new ActivityActivationFailureHandler()
            .Classify(new UnknownActivityTypeException(MissingTypeAlias), "artifact-1", "node-trigger");

        Assert.NotNull(failure);
        Assert.Equal(ActivityActivationFailureKind.MissingActivityType, failure!.Kind);
        Assert.Equal(WellKnownRuntimeActivityConsumers.ClrActivity, failure.ConsumerKey);
        Assert.Equal("node-trigger", failure.ExecutableNodeId);
        Assert.Equal("false", failure.Metadata[ActivityActivationFailureHandler.RetryEligibleMetadataKey]);
        Assert.Equal(
            ActivityActivationFailureHandler.DeploymentCorrectionRecoveryAction,
            failure.Metadata[ActivityActivationFailureHandler.RecoveryActionMetadataKey]);
    }
}
