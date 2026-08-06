using System.Reflection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CheckpointAuthorityScopeTests
{
    private static readonly NullabilityInfoContext NullabilityContext = new();

    [Fact]
    public void Ambient_accessor_is_nullable_stack_restoring_and_execution_outside_dispatch_observes_null()
    {
        var accessor = NewAccessor();
        var outer = RecoveryAuthorityTestContract.Encode(AuthorityWorkItem.Sample().ToRuntimeWorkItem());
        var inner = RecoveryAuthorityTestContract.Encode(AuthorityWorkItem.Sample().Change("work-item").ToRuntimeWorkItem());

        Assert.Null(Current(accessor));
        using (Push(accessor, outer))
        {
            Assert.Same(outer, Current(accessor));
            using (Push(accessor, inner))
                Assert.Same(inner, Current(accessor));
            Assert.Same(outer, Current(accessor));
        }
        Assert.Null(Current(accessor));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("exception")]
    [InlineData("cancellation")]
    public async Task Drainer_scopes_the_actually_acquired_durable_item_and_restores_after_every_exit(string exit)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = NewAccessor();
        var observations = new List<object?>();
        var handler = new AuthorityObservingHandler(() => Current(accessor), observations, exit);
        var drainer = NewDrainer(queue, [handler], accessor);
        var workItem = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        await queue.EnqueueAsync(workItem);
        using var cancellation = new CancellationTokenSource();

        if (exit == "cancellation")
        {
            handler.Cancellation = cancellation;
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                drainer.DrainAsync(new RuntimeSchedulerDrainRequest(workItem.WorkflowExecutionId), cancellation.Token).AsTask());
        }
        else
        {
            var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(workItem.WorkflowExecutionId));
            Assert.Equal(
                exit == "success" ? RuntimeSchedulerWorkItemResultStatus.Completed : RuntimeSchedulerWorkItemResultStatus.Faulted,
                Assert.Single(result.Items).Status);
        }

        var observed = Assert.Single(observations);
        Assert.NotNull(observed);
        Assert.Equal(workItem.WorkflowExecutionId, RecoveryAuthorityTestContract.Property<string>(observed!, "WorkflowExecutionId"));
        Assert.Equal(workItem.WorkItemId, RecoveryAuthorityTestContract.Property<string>(observed!, "WorkItemId"));
        Assert.Null(Current(accessor));
    }

    [Fact]
    public async Task Durable_ScheduleActivity_dispatch_scopes_the_exact_outer_D1_authority_through_nested_async_calls()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = NewAccessor();
        var observations = new List<AuthorityObservation>();
        var handler = new OuterD1AuthorityObservingHandler(() => Current(accessor), observations);
        var drainer = NewDrainer(queue, [handler], accessor);
        var workItem = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        var expectedAuthority = RecoveryAuthorityTestContract.Encode(workItem);
        await queue.EnqueueAsync(workItem);

        Assert.Null(Current(accessor));
        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest(workItem.WorkflowExecutionId));

        Assert.Equal(3, observations.Count);
        var firstAuthority = observations[0].Authority;
        Assert.NotNull(firstAuthority);
        Assert.All(observations, observation =>
        {
            Assert.Same(workItem, observation.WorkItem);
            Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, observation.WorkItem.CommandKind);
            Assert.Same(firstAuthority, observation.Authority);
            Assert.Equal(workItem.WorkflowExecutionId, RecoveryAuthorityTestContract.Property<string>(observation.Authority!, "WorkflowExecutionId"));
            Assert.Equal(workItem.WorkItemId, RecoveryAuthorityTestContract.Property<string>(observation.Authority!, "WorkItemId"));
            Assert.Equal(Fingerprint(expectedAuthority), Fingerprint(observation.Authority!));
        });
        Assert.Null(Current(accessor));
    }

    [Fact]
    public void Only_the_committer_can_copy_ambient_authority_into_preparation_and_providers_cannot_infer_it()
    {
        var prepareRequest = typeof(RuntimeCheckpointPrepareRequest);
        Assert.NotNull(prepareRequest.GetProperty("RecoveryAuthority", BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            typeof(RuntimeCheckpointCommit).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Authority", StringComparison.Ordinal));
        Assert.Contains(
            typeof(RuntimeCheckpointCommitter).GetConstructors(),
            constructor => constructor.GetParameters().Any(parameter =>
                parameter.ParameterType == RecoveryAuthorityTestContract.AccessorContractType));

        var root = FindRepositoryRoot();
        var productionReferences = Directory.EnumerateFiles(Path.Combine(root, "src", "Elsa"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("IRuntimeCheckpointRecoveryAuthorityAccessor", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointRecoveryAuthorityAccessor.cs",
                "src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs",
                "src/Elsa/Workflows/Runtime/Services/AmbientCheckpointRecoveryAuthorityAccessor.cs",
                "src/Elsa/Workflows/Runtime/Services/RuntimeCheckpointCommitter.cs",
                "src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs"
            ],
            productionReferences);
        Assert.DoesNotContain(productionReferences, path => path.Contains("/Persistence/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Real_committer_copies_the_exact_scoped_authority_into_prepare_and_invalid_source_never_prepares()
    {
        var validItem = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        var validStore = new RecordingPreparedStore();
        var validAccessor = NewAccessor();
        var validHandler = new CommittingHandler(NewCommitter(validStore, validAccessor));
        var validQueue = new InMemoryWorkflowSchedulerWorkQueue();
        await validQueue.EnqueueAsync(validItem);

        var result = await NewDrainer(validQueue, [validHandler], validAccessor)
            .DrainAsync(new RuntimeSchedulerDrainRequest(validItem.WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
        var request = Assert.Single(validStore.PreparedRequests);
        var captured = request.GetType().GetProperty("RecoveryAuthority")?.GetValue(request);
        Assert.NotNull(captured);
        var expected = RecoveryAuthorityTestContract.Encode(validItem);
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<int>(expected, "Version"),
            RecoveryAuthorityTestContract.Property<int>(captured!, "Version"));
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "Kind"),
            RecoveryAuthorityTestContract.Property<string>(captured!, "Kind"));
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "WorkflowExecutionId"),
            RecoveryAuthorityTestContract.Property<string>(captured!, "WorkflowExecutionId"));
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "WorkItemId"),
            RecoveryAuthorityTestContract.Property<string>(captured!, "WorkItemId"));
        Assert.Equal(Fingerprint(expected), Fingerprint(captured!));

        var invalid = AuthorityWorkItem.Sample().WithBoundedField("work-item", new string('x', 451)).ToRuntimeWorkItem();
        var invalidStore = new RecordingPreparedStore();
        var invalidAccessor = NewAccessor();
        var invalidHandler = new CommittingHandler(NewCommitter(invalidStore, invalidAccessor));
        var invalidQueue = new InMemoryWorkflowSchedulerWorkQueue();
        await invalidQueue.EnqueueAsync(invalid);

        var invalidResult = await NewDrainer(invalidQueue, [invalidHandler], invalidAccessor)
            .DrainAsync(new RuntimeSchedulerDrainRequest(invalid.WorkflowExecutionId));

        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, Assert.Single(invalidResult.Items).Status);
        Assert.Equal(0, invalidHandler.CallCount);
        Assert.Empty(invalidStore.PreparedRequests);
        Assert.Null(Current(invalidAccessor));
    }

    private static object NewAccessor()
    {
        Assert.True(RecoveryAuthorityTestContract.AccessorContractType.IsInterface);
        var accessor = Activator.CreateInstance(RecoveryAuthorityTestContract.AccessorType);
        Assert.NotNull(accessor);
        Assert.True(RecoveryAuthorityTestContract.AccessorContractType.IsInstanceOfType(accessor));
        return accessor!;
    }

    private static object? Current(object accessor) =>
        RecoveryAuthorityTestContract.AccessorContractType.GetProperty("Current")?.GetValue(accessor);

    private static IDisposable Push(object accessor, object? authority)
    {
        var push = RecoveryAuthorityTestContract.AccessorContractType.GetMethod("Push", [RecoveryAuthorityTestContract.AuthorityType]);
        Assert.NotNull(push);
        return Assert.IsAssignableFrom<IDisposable>(push!.Invoke(accessor, [authority]));
    }

    private static WorkflowSchedulerDrainer NewDrainer(
        IWorkflowSchedulerWorkQueue queue,
        IReadOnlyCollection<IWorkflowSchedulerWorkHandler> handlers,
        object accessor)
    {
        var constructor = Assert.Single(typeof(WorkflowSchedulerDrainer).GetConstructors());
        var arguments = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(IWorkflowSchedulerWorkQueue))
                return (object)queue;
            if (parameter.ParameterType == typeof(IEnumerable<IWorkflowSchedulerWorkHandler>))
                return handlers;
            if (parameter.ParameterType == typeof(IWorkflowExecutionStateStore))
                return new InMemoryWorkflowExecutionStateStore();
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AccessorContractType)
                return accessor;
            if (parameter.ParameterType == RecoveryAuthorityTestContract.CodecType)
                return Activator.CreateInstance(RecoveryAuthorityTestContract.CodecType);
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            Assert.Fail($"Unexpected required drainer dependency '{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();

        Assert.Contains(constructor.GetParameters(), parameter =>
            parameter.ParameterType == RecoveryAuthorityTestContract.AccessorContractType);
        return Assert.IsType<WorkflowSchedulerDrainer>(constructor.Invoke(arguments));
    }

    private static RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore store, object accessor)
    {
        var constructor = typeof(RuntimeCheckpointCommitter).GetConstructors()
            .Where(candidate => candidate.GetParameters().Any(parameter =>
                parameter.ParameterType == RecoveryAuthorityTestContract.AccessorContractType))
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        Assert.NotNull(constructor);
        var arguments = constructor!.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(IRuntimeCheckpointPersistencePolicy))
                return (object)new ImmediateRuntimeCheckpointPersistencePolicy();
            if (parameter.ParameterType == typeof(IRuntimeCheckpointCommitStore))
                return store;
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AccessorContractType)
                return accessor;
            if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return Array.CreateInstance(parameter.ParameterType.GetGenericArguments()[0], 0);
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            if (NullabilityContext.Create(parameter).ReadState == NullabilityState.Nullable)
                return null;
            Assert.Fail($"Unexpected required committer dependency '{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        return Assert.IsType<RuntimeCheckpointCommitter>(constructor.Invoke(arguments));
    }

    private static RuntimeCheckpointCommit NewCommit(string workflowExecutionId) => new(
        $"commit-{Guid.NewGuid():N}",
        new RuntimeCheckpoint(
            $"checkpoint-{Guid.NewGuid():N}",
            "AuthorityCapture",
            workflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    private static string Fingerprint(object authority) =>
        RecoveryAuthorityTestContract.Property<string>(authority, "Fingerprint");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class AuthorityObservingHandler(
        Func<object?> current,
        ICollection<object?> observations,
        string exit) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AuthorityObservingHandler);
        public CancellationTokenSource? Cancellation { get; set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            observations.Add(current());
            if (exit == "exception")
                throw new InvalidOperationException("requested handler failure");
            if (exit == "cancellation")
            {
                Cancellation!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CommittingHandler(RuntimeCheckpointCommitter committer) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(CommittingHandler);
        public int CallCount { get; private set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            CallCount++;
            await committer.CommitAsync(NewCommit(workItem.WorkflowExecutionId), cancellationToken);
        }
    }

    private sealed class RecordingPreparedStore : IRuntimeCheckpointCommitStore
    {
        private readonly InMemoryRuntimeCheckpointCommitStore _inner = new();
        public List<RuntimeCheckpointPrepareRequest> PreparedRequests { get; } = [];

        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(
            RuntimeCheckpointPrepareRequest request,
            CancellationToken cancellationToken = default)
        {
            PreparedRequests.Add(request);
            return _inner.PrepareAsync(request, cancellationToken);
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            _inner.CommitPreparedAsync(token, commit, decision, cancellationToken);

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            _inner.CommitAsync(commit, decision, cancellationToken);
    }

    private sealed record AuthorityObservation(RuntimeSchedulerWorkItem WorkItem, object? Authority);

    private sealed class OuterD1AuthorityObservingHandler(
        Func<object?> current,
        ICollection<AuthorityObservation> observations) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(OuterD1AuthorityObservingHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            observations.Add(new(workItem, current()));
            await ObserveNestedAsync(workItem);
        }

        private async ValueTask ObserveNestedAsync(RuntimeSchedulerWorkItem workItem)
        {
            await Task.Yield();
            observations.Add(new(workItem, current()));
            await ObserveNestedAgainAsync(workItem);
        }

        private async ValueTask ObserveNestedAgainAsync(RuntimeSchedulerWorkItem workItem)
        {
            await Task.Yield();
            observations.Add(new(workItem, current()));
        }
    }
}
