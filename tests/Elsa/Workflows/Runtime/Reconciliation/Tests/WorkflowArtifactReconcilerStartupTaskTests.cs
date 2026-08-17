using Elsa.Activities.Runtime.Tasks;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Options;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Framework §2.23.1/§2.23.2 coverage for <see cref="WorkflowArtifactReconcilerStartupTask"/>: the single-node and
/// ordering attributes that make the pass safe and meaningful, and the lock behaviour that decides whether a node
/// reconciles at all.
/// </summary>
public sealed class WorkflowArtifactReconcilerStartupTaskTests
{
    [Fact]
    public void Task_is_marked_single_node()
    {
        // Several nodes booting against one mounted set would contend for the same activation slots and every
        // loser would take a CAS conflict for work that was already done.
        Assert.Single(typeof(WorkflowArtifactReconcilerStartupTask)
            .GetCustomAttributes(typeof(SingleNodeTaskAttribute), inherit: false));
    }

    [Fact]
    public void Task_runs_after_the_activity_type_scan()
    {
        // The import gate's type-presence axis is meaningless before the assembly scan has populated the
        // well-known type registry: running first would reject every artifact on a cold start.
        var dependency = Assert.Single(typeof(WorkflowArtifactReconcilerStartupTask)
            .GetCustomAttributes(typeof(TaskDependencyAttribute), inherit: false)
            .Cast<TaskDependencyAttribute>());

        Assert.Equal(typeof(RegisterActivityTypesStartupTask), dependency.DependencyTaskType);
    }

    [Fact]
    public void Task_is_a_startup_task_so_a_shell_reload_replays_it()
    {
        Assert.True(typeof(IStartupTask).IsAssignableFrom(typeof(WorkflowArtifactReconcilerStartupTask)));
    }

    [Fact]
    public async Task Execute_reconciles_when_it_holds_the_lock()
    {
        var reconciler = new RecordingReconciler(WorkflowArtifactReconciliationResult.Empty);
        var lockProvider = new TrackingLockProvider(grant: true);
        var task = CreateTask(reconciler, lockProvider);

        await task.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, reconciler.Calls);
        Assert.Equal(nameof(WorkflowArtifactReconcilerStartupTask), lockProvider.RequestedName);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), lockProvider.RequestedTimeout);
        Assert.True(lockProvider.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Execute_uses_the_configured_lock_timeout()
    {
        var lockProvider = new TrackingLockProvider(grant: true);
        var task = CreateTask(new RecordingReconciler(WorkflowArtifactReconciliationResult.Empty), lockProvider, lockTimeoutMs: 250);

        await task.ExecuteAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(250), lockProvider.RequestedTimeout);
    }

    [Fact]
    public async Task Execute_returns_without_reconciling_when_another_node_holds_the_lock()
    {
        // Losing the lock is the expected outcome on all but one node — not a failure, and emphatically not a
        // reason to fail readiness.
        var reconciler = new RecordingReconciler(WorkflowArtifactReconciliationResult.Empty);
        var task = CreateTask(reconciler, new TrackingLockProvider(grant: false));

        await task.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, reconciler.Calls);
    }

    [Fact]
    public async Task Execute_reports_rejections_without_failing_the_shell()
    {
        // A broken closure unit must not stop the shell from starting with the units that did import.
        var result = new WorkflowArtifactReconciliationResult(
        [
            WorkflowArtifactImportEntry.Imported("good.json", "mounted", "artifact-1", "definition-1", "1.0.0", "import:mounted:definition-1:artifact-1"),
            WorkflowArtifactImportEntry.Rejected("bad.json", "mounted", "artifact-2", "definition-2", "1.0.0", WorkflowArtifactRejectionKind.ContentHashMismatch, "payload does not hash to its declared id."),
        ]);
        var reconciler = new RecordingReconciler(result);
        var task = CreateTask(reconciler, new TrackingLockProvider(grant: true));

        await task.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, reconciler.Calls);
    }

    [Fact]
    public async Task Execute_propagates_a_pass_aborting_failure()
    {
        // A configured mount that does not exist is not "no artifacts": it must fail the startup task rather than
        // let the shell come up serving whatever it happened to have.
        var reconciler = new ThrowingReconciler(
            new Core.Exceptions.WorkflowArtifactReconciliationException("mounted", "the configured folder '/mnt/artifacts' does not exist."));
        var task = CreateTask(reconciler, new TrackingLockProvider(grant: true));

        await Assert.ThrowsAsync<Core.Exceptions.WorkflowArtifactReconciliationException>(
            () => task.ExecuteAsync(CancellationToken.None));
    }

    private static WorkflowArtifactReconcilerStartupTask CreateTask(
        IWorkflowArtifactReconciler reconciler,
        IDistributedLockProvider lockProvider,
        int lockTimeoutMs = 5000) =>
        new(
            reconciler,
            lockProvider,
            MicrosoftOptions.Create(new WorkflowArtifactReconcilerStartupTaskOptions { LockTimeoutMs = lockTimeoutMs }),
            NullLogger<WorkflowArtifactReconcilerStartupTask>.Instance);

    private sealed class RecordingReconciler(WorkflowArtifactReconciliationResult result) : IWorkflowArtifactReconciler
    {
        public int Calls { get; private set; }

        public ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingReconciler(Exception exception) : IWorkflowArtifactReconciler
    {
        public ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class TrackingLockProvider(bool grant) : IDistributedLockProvider
    {
        public string? RequestedName { get; private set; }
        public TimeSpan? RequestedTimeout { get; private set; }
        public TrackingHandle? LastHandle { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The startup task must take the lock asynchronously.");

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            RequestedName = name;
            RequestedTimeout = timeout;
            if (!grant)
                return ValueTask.FromResult<IDistributedSynchronizationHandle?>(null);

            LastHandle = new TrackingHandle();
            return ValueTask.FromResult<IDistributedSynchronizationHandle?>(LastHandle);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The startup task must never block on the lock.");

        internal sealed class TrackingHandle : IDistributedSynchronizationHandle
        {
            public bool Disposed { get; private set; }
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
