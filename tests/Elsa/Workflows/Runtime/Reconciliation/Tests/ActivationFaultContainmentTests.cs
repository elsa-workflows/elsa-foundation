using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// T138: a fault raised while activating one imported artifact stays that artifact's problem. It is recorded as a
/// rejection in the pass result and never escapes to fail shell activation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a family and not one exception type.</b> <see cref="IWorkflowActivationCoordinator.ActivateAsync"/>
/// documents that it wraps faults in <see cref="WorkflowActivationException"/> per §2.23.5, and the reconciler used
/// to catch exactly that type. The contract is not airtight: the Groundwork authority raises
/// <c>GroundworkWorkflowActivationAuthorityException</c> and the publishing side raises
/// <c>PublicationActivationException</c> — both <em>siblings</em> rather than subtypes — and the root-write
/// lease faults are rethrown unwrapped. Either escapes a narrow catch, and because the reconcile
/// pass runs as a startup task before readiness, an escape does not degrade one workflow — it stops the shell.
/// </para>
/// <para>
/// The theory therefore asserts containment over the shapes that can actually reach the catch, rather than over the
/// one shape the contract promises. <see cref="Cancellation_still_escapes_so_a_shutdown_is_not_swallowed"/> is the
/// other half: the fix must be broad, not unbounded.
/// </para>
/// </remarks>
public sealed class ActivationFaultContainmentTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-activation-faults",
        Guid.NewGuid().ToString("N"));

    public ActivationFaultContainmentTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    public static TheoryData<string, Exception> ActivationFaults() => new()
    {
        {
            // The shape the coordinator's contract promises. Kept so the original behavior stays pinned.
            "wrapped activation exception",
            new WorkflowActivationException(DefinitionId, "default", "activation-1", "the activation ledger refused the transition.")
        },
        {
            // Stands for the authority-layer siblings -- GroundworkWorkflowActivationAuthorityException and
            // PublicationActivationException. Both are siblings of WorkflowActivationException rather than
            // subtypes, which is the whole defect; neither is referenced here on purpose, because dragging the
            // persistence and publishing assemblies into a reconciliation test project to reach two exception
            // types would couple this suite to layers it exists to stay clear of. What is under test is the
            // catch's breadth, and a foreign exception type exercises that faithfully.
            "authority-layer sibling",
            new AuthorityLayerFault("the ledger write failed.")
        },
        {
            // Stands in for the root-write lease faults, which the coordinator rethrows unwrapped.
            "unwrapped infrastructure fault",
            new InvalidOperationException("the root-write lease could not be acquired.")
        }
    };

    [Theory]
    [MemberData(nameof(ActivationFaults))]
    public async Task An_activation_fault_rejects_only_its_own_artifact_and_never_fails_the_pass(
        string because,
        Exception fault)
    {
        await using var harness = ArtifactImportHarness.Build(
            _mount,
            services =>
            {
                services.RemoveAll<IWorkflowActivationCoordinator>();
                services.AddSingleton<IWorkflowActivationCoordinator>(new ThrowingActivationCoordinator(fault));
            });

        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "invoice.json",
            ArtifactClosureFixture.Closure(
                ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-a"), DefinitionId, "1.0.0")));

        // The pass completing at all is the assertion: a throw here is a failed shell activation in production.
        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.ActivationFailure, entry.RejectionKind);
        Assert.Contains(fault.Message, entry.Diagnostic ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, result.ImportedCount);
    }

    /// <summary>
    /// The containment must not extend to cancellation: a shutdown has to stop the pass, not be filed as a
    /// per-artifact rejection and reported as a completed reconcile.
    /// </summary>
    [Fact]
    public async Task Cancellation_still_escapes_so_a_shutdown_is_not_swallowed()
    {
        await using var harness = ArtifactImportHarness.Build(
            _mount,
            services =>
            {
                services.RemoveAll<IWorkflowActivationCoordinator>();
                services.AddSingleton<IWorkflowActivationCoordinator>(
                    new ThrowingActivationCoordinator(new OperationCanceledException()));
            });

        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "invoice.json",
            ArtifactClosureFixture.Closure(
                ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-a"), DefinitionId, "1.0.0")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArtifactImportHarness.ReconcileAsync(harness));
    }

    /// <summary>Stand-in for the authority-layer sibling exceptions; see the theory data for why.</summary>
    private sealed class AuthorityLayerFault(string message) : Exception(message);

    private sealed class ThrowingActivationCoordinator(Exception fault) : IWorkflowActivationCoordinator
    {
        public ValueTask<WorkflowActivationResult> ActivateAsync(
            WorkflowActivationCommand command,
            CancellationToken cancellationToken = default) => throw fault;

        public ValueTask<WorkflowActivationResult> DeactivateAsync(
            WorkflowDeactivationCommand command,
            CancellationToken cancellationToken = default) => throw fault;
    }
}
