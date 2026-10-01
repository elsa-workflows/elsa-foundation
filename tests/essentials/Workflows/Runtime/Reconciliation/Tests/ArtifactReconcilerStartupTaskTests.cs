using CShells;
using Elsa.Locking.Core;
using Elsa.Tasks.Services;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// The artifact reconcile pass is a <c>[SingleNodeTask]</c> (#2192): each node reconciles its own mounted set, one node at a
/// time. A node that finds another node's pass running waits for it and then runs its own, rather than skipping it, and it
/// takes no lock of its own beside the executor's.
/// </summary>
public sealed class ArtifactReconcilerStartupTaskTests
{
    private const string LockKey = "elsa:single-node-task:default:Elsa.Workflows.Runtime.Reconciliation.Startup.WorkflowArtifactReconcilerStartupTask";

    private readonly SequencedLockProvider _locks = new();
    private readonly SpyArtifactReconciler _reconciler = new();
    private readonly TaskExecutor _executor;
    private readonly WorkflowArtifactReconcilerStartupTask _task;

    public ArtifactReconcilerStartupTaskTests()
    {
        _executor = new TaskExecutor(_locks, NullLogger<TaskExecutor>.Instance, new ShellSettings("default"));
        _task = new WorkflowArtifactReconcilerStartupTask(_reconciler, NullLogger<WorkflowArtifactReconcilerStartupTask>.Instance);
    }

    [Fact]
    public async Task Waits_for_another_nodes_pass_and_then_reconciles_its_own()
    {
        _locks.FreeAfter = 1;

        await _executor.ExecuteTaskAsync(_task, CancellationToken.None);

        Assert.Equal(1, _reconciler.Passes);
        Assert.Equal([LockKey, LockKey], _locks.Names);
    }

    [Fact]
    public async Task Fails_rather_than_skipping_when_the_other_nodes_pass_outlasts_the_wait()
    {
        _locks.FreeAfter = int.MaxValue;

        await Assert.ThrowsAsync<TimeoutException>(() => _executor.ExecuteTaskAsync(_task, CancellationToken.None));

        Assert.Equal(0, _reconciler.Passes);
    }

    [Fact]
    public void The_feature_refuses_the_retired_lock_timeout()
    {
        var feature = new JsonWorkflowArtifactReconciliationFeature { Options = { SourceId = "catalog", FilePath = "/mounts/artifacts.json" } };
#pragma warning disable CS0618 // Setting the retired value is the case under test.
        feature.StartupTaskOptions.LockTimeoutMs = "5000";
#pragma warning restore CS0618

        var failure = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains("'StartupTaskOptions:LockTimeoutMs'", failure.Message);
        Assert.Contains("LockAcquisitionTimeoutMinutes", failure.Message);
    }

    private sealed class SpyArtifactReconciler : IWorkflowArtifactReconciler
    {
        public int Passes { get; private set; }

        public ValueTask<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            Passes++;
            return ValueTask.FromResult(WorkflowArtifactReconciliationResult.Empty);
        }
    }

    /// <summary>Held by another node for the first <see cref="FreeAfter"/> attempts, then free.</summary>
    private sealed class SequencedLockProvider : IDistributedLockProvider
    {
        public int FreeAfter { get; set; }
        public List<string> Names { get; } = [];

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Names.Add(name);
            return ValueTask.FromResult<IDistributedSynchronizationHandle?>(Names.Count > FreeAfter ? new Handle() : null);
        }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
