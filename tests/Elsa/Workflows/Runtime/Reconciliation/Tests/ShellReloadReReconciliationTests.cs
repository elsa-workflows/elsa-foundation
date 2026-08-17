using Elsa.Activities.Testing;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// FR-B-008: re-reconciliation rides the <b>existing</b> shell-reload path. Reloading a shell re-runs its startup
/// tasks, so the importer needs no trigger, watcher or coordinator of its own — and #1303's is deferred.
/// </summary>
/// <remarks>
/// <para>
/// The reload is modelled at the seam that actually carries it: the registered <see cref="IStartupTask"/> is
/// resolved out of the composed engine and executed again in a fresh scope, which is precisely what a shell
/// activation does to it. Standing up a real CShells host would exercise the host's reload plumbing — already its
/// own tested concern — rather than the claim under test, which is that <em>this task</em> is safely replayable and
/// that replaying it is enough to pick up a changed mount.
/// </para>
/// <para>
/// The task is resolved rather than constructed so the test fails if the feature ever stops registering it, or
/// registers it as something other than a startup task.
/// </para>
/// </remarks>
public sealed class ShellReloadReReconciliationTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-reload",
        Guid.NewGuid().ToString("N"));

    public ShellReloadReReconciliationTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Replaying_the_startup_task_picks_up_a_changed_mount_and_is_a_no_op_when_nothing_changed()
    {
        await using var harness = ArtifactImportHarness.Build(_mount, ComposeLockProvider);
        harness.InitializeActivityTypes();

        var v1 = TriggerExecutable("node-v1", "1.0.0");
        var v2 = TriggerExecutable("node-v2", "1.2.0");

        // First activation of the shell.
        Mount(harness, v1);
        await RunStartupTaskAsync(harness);

        var afterFirstBoot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.NotNull(afterFirstBoot);
        var v1ActivationId = afterFirstBoot!.ActiveActivationId;
        Assert.NotNull(v1ActivationId);
        Assert.Single(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));

        // Reload with nothing changed: the pass runs again and settles on the same activation, so a reload is safe
        // to trigger for unrelated reasons.
        await RunStartupTaskAsync(harness);

        var afterIdleReload = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(v1ActivationId, afterIdleReload!.ActiveActivationId);
        Assert.Equal(afterFirstBoot.Revision, afterIdleReload.Revision);
        Assert.Single(await ArtifactImportHarness.ListAllReferencesAsync(harness));

        // Reload after the operator swapped the mounted artifact: this is the whole promote/rollout loop, with no
        // API call and no new trigger surface.
        Mount(harness, v2);
        await RunStartupTaskAsync(harness);

        var afterUpgrade = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.NotEqual(v1ActivationId, afterUpgrade!.ActiveActivationId);
        Assert.Single(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v2")));
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));

        var retired = await ArtifactImportHarness.FindReferenceAsync(harness, v1ActivationId!);
        Assert.Equal("activation-replaced", retired!.DeletedReason);

        // And the upgraded shell is itself idempotent on the next reload.
        await RunStartupTaskAsync(harness);
        var afterSecondIdleReload = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(afterUpgrade.ActiveActivationId, afterSecondIdleReload!.ActiveActivationId);
        Assert.Equal(afterUpgrade.Revision, afterSecondIdleReload.Revision);
    }

    /// <summary>
    /// Runs the artifact-reconciliation startup task in its own scope, as a shell activation does.
    /// </summary>
    /// <remarks>
    /// Built from the scope's own services rather than pulled out of <c>GetServices&lt;IStartupTask&gt;()</c>, because
    /// resolving that enumerable materializes <em>every</em> registered startup task and the bare execution harness
    /// does not compose the dependencies some of the runtime's other ones need. The registration itself is asserted
    /// in <see cref="AssertStartupTaskIsRegistered"/> against the service collection, so nothing here is taken on
    /// trust; everything the task consumes still comes from the real container.
    /// </remarks>
    private static async Task RunStartupTaskAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var task = ActivatorUtilities.CreateInstance<WorkflowArtifactReconcilerStartupTask>(scope.ServiceProvider);
        await task.ExecuteAsync(CancellationToken.None);
    }

    private static void ComposeLockProvider(IServiceCollection services)
    {
        AssertStartupTaskIsRegistered(services);

        // No default IDistributedLockProvider exists anywhere in src/ — deliberately, so a multi-node host cannot
        // silently reconcile one mount twice. A host composes a locking feature; a test composes this.
        services.AddSingleton<IDistributedLockProvider, GrantingLockProvider>();
    }

    /// <summary>The reload path only exists because the feature registers the pass as a startup task.</summary>
    private static void AssertStartupTaskIsRegistered(IServiceCollection services) =>
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(IStartupTask)
            && descriptor.ImplementationType == typeof(WorkflowArtifactReconcilerStartupTask)));

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

    private sealed class GrantingLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new Handle();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
