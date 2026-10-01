using CShells;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Services;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Reconciliation;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Startup;
using Elsa.Workflows.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// The design-side version reconcilers run on every node at shell start (#2192). Their inputs are node-local, so a node that
/// skipped its pass because another node held a lock would never reconcile its own; two passes at once converge instead
/// (#2189). The git export runs on every Writer node the same way (#2197): its fence is the push, not a lock. Each task
/// here runs through the shell's task executor while another node holds every lock there is.
/// </summary>
public sealed class NodeLocalReconcilerStartupTaskTests
{
    private readonly HeldEverywhereLockProvider _locks = new();
    private readonly TaskExecutor _executor;

    public NodeLocalReconcilerStartupTaskTests() =>
        _executor = new TaskExecutor(_locks, NullLogger<TaskExecutor>.Instance, new ShellSettings("default"));

    [Fact]
    public async Task The_activity_version_reconciler_runs_while_another_node_holds_every_lock()
    {
        var reconciler = new SpyReconciler();

        await _executor.ExecuteTaskAsync(new ActivityVersionReconcilerStartupTask(reconciler), CancellationToken.None);

        Assert.Equal(1, reconciler.Passes);
        Assert.Equal(0, _locks.Attempts);
    }

    [Fact]
    public async Task The_workflow_version_reconciler_runs_while_another_node_holds_every_lock()
    {
        var reconciler = new SpyReconciler();

        await _executor.ExecuteTaskAsync(new WorkflowsVersionReconcilerStartupTask(reconciler), CancellationToken.None);

        Assert.Equal(1, reconciler.Passes);
        Assert.Equal(0, _locks.Attempts);
    }

    [Fact]
    public async Task The_git_export_runs_while_another_node_holds_every_lock()
    {
        var exporter = new SpyReconciler();

        await _executor.ExecuteTaskAsync(new GitWorkflowExportStartupTask(exporter), CancellationToken.None);

        Assert.Equal(1, exporter.Passes);
        Assert.Equal(0, _locks.Attempts);
    }

    [Fact]
    public void The_activity_reconciliation_feature_refuses_the_retired_lock_timeout()
    {
        var feature = new ActivitiesDesignReconciliationFeature();
#pragma warning disable CS0618 // Setting the retired value is the case under test.
        feature.StartupTaskOptions.LockTimeoutMs = "10000";
#pragma warning restore CS0618

        var failure = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains("'StartupTaskOptions:LockTimeoutMs'", failure.Message);
        Assert.Contains("retired", failure.Message);
    }

    [Fact]
    public void The_workflow_reconciliation_feature_refuses_the_retired_lock_timeout()
    {
        var feature = new MinimalWorkflowsDesignReconciliationFeature();
#pragma warning disable CS0618 // Setting the retired value is the case under test.
        feature.StartupTaskOptions.LockTimeoutMs = "10000";
#pragma warning restore CS0618

        var failure = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains("'StartupTaskOptions:LockTimeoutMs'", failure.Message);
        Assert.Contains("retired", failure.Message);
    }

    private sealed class MinimalWorkflowsDesignReconciliationFeature : WorkflowsDesignReconciliationFeature;

    private sealed class SpyReconciler : IActivityVersionReconciler, IWorkflowVersionReconciler, IGitWorkflowExporter
    {
        public int Passes { get; private set; }

        public Task Reconcile(CancellationToken cancellationToken)
        {
            Passes++;
            return Task.CompletedTask;
        }

        public Task ExportAsync(CancellationToken cancellationToken) => Reconcile(cancellationToken);
    }

    /// <summary>Another node holds every lock, for longer than anyone waits.</summary>
    private sealed class HeldEverywhereLockProvider : IDistributedLockProvider
    {
        public int Attempts { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return null;
        }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return ValueTask.FromResult<IDistributedSynchronizationHandle?>(null);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new TimeoutException($"Lock '{name}' is held by another node.");
        }
    }
}
