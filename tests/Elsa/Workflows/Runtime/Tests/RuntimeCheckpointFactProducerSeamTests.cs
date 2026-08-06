using System.Reflection;
using Elsa.Testing.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointFactProducerSeamTests
{
    private static readonly Type[] FactProducers =
    [
        typeof(WorkflowCheckpointSchedulerWorkHandler),
        typeof(WorkflowScheduleActivitySchedulerWorkHandler),
        typeof(WorkflowStartActivitySchedulerWorkHandler),
        typeof(WorkflowStartSchedulerWorkHandler)
    ];

    private static readonly Type[] ProviderOwnedTypes =
    [
        typeof(RuntimeCheckpointProvenance),
        typeof(RuntimeExecutionContextSnapshot),
        typeof(RuntimeCheckpointPreparationToken),
        typeof(RuntimeLogicalCheckpointLedgerEntry)
    ];

    [Fact]
    public void Four_reviewed_fact_producers_never_allocate_provider_owned_provenance_order_or_context()
    {
        var offenders = FactProducers
            .SelectMany(DeclaredMethods)
            .SelectMany(method => MethodCallInspector.GetCalledMethods(method)
                .Where(target => ProviderOwnedTypes.Contains(target.DeclaringType) || IsDirectCommitStoreCall(target))
                .Select(target => $"{method.DeclaringType!.Name}.{method.Name} -> {target.DeclaringType?.Name}.{target.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Transition_fact_producers_build_only_raw_checkpoint_or_checkpoint_command_shapes()
    {
        AssertBuildsRawCheckpoint(typeof(WorkflowCheckpointSchedulerWorkHandler));
        AssertBuildsRawCheckpoint(typeof(WorkflowScheduleActivitySchedulerWorkHandler));
        AssertBuildsRawCheckpoint(typeof(WorkflowStartActivitySchedulerWorkHandler));

        var workflowStartCalls = CalledMethods(typeof(WorkflowStartSchedulerWorkHandler));
        Assert.Contains(workflowStartCalls, target => target.DeclaringType == typeof(RuntimeCheckpointCommandPayload) && target.IsConstructor);
        Assert.Contains(workflowStartCalls, target =>
            target.DeclaringType == typeof(IWorkflowSchedulerWorkQueue) &&
            target.Name == nameof(IWorkflowSchedulerWorkQueue.EnqueueAsync));
    }

    [Fact]
    public void Every_direct_fact_commit_routes_through_the_central_committer()
    {
        foreach (var producer in FactProducers)
        {
            var calls = CalledMethods(producer);
            Assert.DoesNotContain(calls, IsDirectCommitStoreCall);
            if (producer != typeof(WorkflowStartSchedulerWorkHandler))
            {
                Assert.Contains(calls, target =>
                    target.DeclaringType == typeof(RuntimeCheckpointCommitter) &&
                    target.Name == nameof(RuntimeCheckpointCommitter.CommitAsync));
            }
        }
    }

    [Fact]
    public void Concrete_commit_store_bypass_is_detected()
    {
        var calls = CalledMethods(typeof(SyntheticConcreteBypass));

        Assert.Contains(calls, IsDirectCommitStoreCall);
    }

    private static void AssertBuildsRawCheckpoint(Type producer)
    {
        var calls = CalledMethods(producer);
        Assert.Contains(calls, target => target.DeclaringType == typeof(RuntimeCheckpoint) && target.IsConstructor);
        Assert.Contains(calls, target => target.DeclaringType == typeof(RuntimeCheckpointCommit) && target.IsConstructor);
    }

    private static MethodBase[] CalledMethods(Type producer) => DeclaredMethods(producer)
        .SelectMany(MethodCallInspector.GetCalledMethods)
        .Distinct()
        .ToArray();

    private static IEnumerable<MethodInfo> DeclaredMethods(Type producer) =>
        new[] { producer }
            .Concat(producer.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetMethodBody() is not null);

    private static bool IsDirectCommitStoreCall(MethodBase target) =>
        target.DeclaringType is { } declaringType &&
        typeof(IRuntimeCheckpointCommitStore).IsAssignableFrom(declaringType) &&
        target.Name is nameof(IRuntimeCheckpointCommitStore.PrepareAsync)
            or nameof(IRuntimeCheckpointCommitStore.CommitPreparedAsync)
            or nameof(IRuntimeCheckpointCommitStore.CommitAsync);

    private sealed class SyntheticConcreteBypass
    {
        public ValueTask<RuntimeCheckpointPreparationResult> Prepare(
            SyntheticCommitStore store,
            RuntimeCheckpointPrepareRequest request) =>
            store.PrepareAsync(request);
    }

    private sealed class SyntheticCommitStore : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
            RuntimeCheckpointPrepareRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RuntimeCheckpointPreparationResult.Conflict());

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
    }
}
