using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Processor forwarding/refusal mechanics. Test probes implement no Message acceptance or physical capability.</summary>
public sealed class RuntimePostCommitFinalizationProcessorTests
{
    private const string Kind = "Test.SourceFinalization";
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("scope");

    [Theory]
    [InlineData("missing-source")]
    [InlineData("source-refuses")]
    [InlineData("completion-refuses")]
    public async Task Unsupported_actual_boundary_keeps_prepared_source_owed(string defect)
    {
        var registrations = Services();
        GetRecorder(registrations).SourceCapable = defect != "source-refuses";
        GetRecorder(registrations).CompletionCapable = defect != "completion-refuses";
        if (defect != "missing-source") registrations.AddScoped<IRuntimeDeliverySourceStore, SourceProbe>();
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = await SeedAsync(services);

        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1)).AsTask());

        var recorder = services.GetRequiredService<Recorder>();
        Assert.Equal(1, recorder.Prepared); Assert.Null(recorder.Completion);
        await AssertOwedAsync(inner);
    }

    [Fact]
    public async Task Exact_prepared_plan_and_actual_scoped_tuple_reach_only_atomic_completion()
    {
        var registrations = Services(); var recorder = GetRecorder(registrations);
        recorder.SourceCapable = recorder.CompletionCapable = true;
        registrations.AddScoped<IRuntimeDeliverySourceStore, SourceProbe>();
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = await SeedAsync(services);

        // The probe deliberately stops before persistence. Observing forwarding is not a successful source commit.
        await Assert.ThrowsAsync<ObservedCompletionException>(() =>
            services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1)).AsTask());

        var completion = Assert.IsType<RuntimePostCommitOutboxClaimCompletion>(recorder.Completion);
        Assert.Same(recorder.Finalization, completion.SuccessfulFinalization);
        Assert.Equal(recorder.Source!.Claim.FencingToken, completion.Claim.FencingToken);
        Assert.Equal(recorder.Source.Claim.OwnerId, completion.Claim.OwnerId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, completion.DeliveryResult.Status);
        Assert.Null(completion.WorkflowDispatch); Assert.Null(completion.FollowUpOutboxItem);
        var boundary = Assert.IsType<RuntimeDeliveryBoundary>(recorder.Boundary);
        Assert.Equal(Scope, boundary.Scope);
        Assert.Same(services.GetRequiredService<IRuntimeDeliverySourceStore>(), boundary.Sources);
        Assert.Same(services.GetRequiredService<IRuntimeCheckpointCommitStore>(), boundary.Checkpoints);
        Assert.Same(services.GetRequiredService<IRuntimePostCommitOutboxStore>(), boundary.Outbox);
        Assert.Same(services.GetRequiredService<IWorkflowExecutableStore>(), boundary.Materials);
        Assert.Same(services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), boundary.References);
        Assert.Same(services.GetRequiredService<IDurableValueStateStore>(), boundary.Values);
        Assert.Same(services.GetRequiredService<IWorkflowSchedulerWorkQueue>(), boundary.Scheduler);
        Assert.Same(services.GetRequiredService<IWorkflowExecutionStateStore>(), boundary.Executions);
        Assert.Same(services.GetRequiredService<IActivityExecutionStateStore>(), boundary.Activities);
        Assert.Same(services.GetRequiredService<IBookmarkStateStore>(), boundary.Bookmarks);
        await AssertOwedAsync(inner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_replacement_dispatcher_cannot_silently_downgrade_required_finalization(bool completionAware)
    {
        var registrations = Services();
        registrations.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitIntentDispatcher>(services =>
            completionAware ? new EmptyPreparationDispatcher(services.GetRequiredService<Recorder>()) :
                new BareDispatcher(services.GetRequiredService<Recorder>())));
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = await SeedAsync(services);

        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1)).AsTask());

        Assert.Equal(0, services.GetRequiredService<Recorder>().BareDispatches);
        Assert.Equal(completionAware ? 1 : 0, services.GetRequiredService<Recorder>().DispatcherPreparations);
        Assert.Null(services.GetRequiredService<Recorder>().Completion);
        await AssertOwedAsync(inner);
    }

    [Fact]
    public async Task A_preserved_public_constructor_does_not_infer_a_supported_physical_boundary()
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = await SeedAsync(services);
        var processor = new RuntimePostCommitOutboxProcessor(services.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            services.GetRequiredService<IRuntimePostCommitIntentDispatcher>(), services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<IRuntimeFaultCapturePolicy>(), null, [], null, null, null,
            services.GetRequiredService<IPersistenceAccessContextAccessor>(), services.GetServices<RuntimePostCommitIntentHandlerContribution>());

        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => processor.ProcessAsync(new(1)).AsTask());

        Assert.Equal(1, services.GetRequiredService<Recorder>().Prepared);
        Assert.Null(services.GetRequiredService<Recorder>().Completion);
        await AssertOwedAsync(inner);
    }

    [Fact]
    public async Task Preparation_is_awaited_without_recording_delivery()
    {
        var registrations = Services(); var recorder = GetRecorder(registrations);
        var gate = new TaskCompletionSource<RuntimePostCommitSourceFinalization>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.Preparation = gate.Task;
        using var provider = registrations.BuildServiceProvider(); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider; var inner = await SeedAsync(services);

        var pending = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1)).AsTask();
        await recorder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted); Assert.Null(recorder.Completion);
        await AssertOwedAsync(inner);
        gate.SetResult(recorder.Finalization);
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => pending);
        await AssertOwedAsync(inner);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection(); services.AddWorkflowRuntime();
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new Clock()));
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        services.AddSingleton(new Recorder()); services.AddScoped<Handler>();
        services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(Kind, typeof(Handler),
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), true, null, true));
        services.AddScoped<CompletionProbe>();
        services.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitOutboxStore>(services => services.GetRequiredService<CompletionProbe>()));
        return services;
    }

    private static Recorder GetRecorder(ServiceCollection services) =>
        (Recorder)services.Single(item => item.ServiceType == typeof(Recorder)).ImplementationInstance!;

    private static async Task<InMemoryRuntimeCheckpointCommitStore> SeedAsync(IServiceProvider services)
    {
        var inner = services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        await inner.AddPendingForTestingAsync(new("commit:send", new("send", "sender", Kind, Now, null, "key", null),
            RuntimePostCommitOutboxStatus.Pending, Now, Now,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), 0, null, null, null, null,
            null, 0, null, null, Scope, null));
        return inner;
    }

    private static async Task AssertOwedAsync(InMemoryRuntimeCheckpointCommitStore store)
    {
        var retained = (await store.FindAsync("commit:send"))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained.Status);
        Assert.Equal(0, retained.DeliveryAttemptCount); Assert.Null(retained.LastFailureMessage);
    }

    private sealed class Recorder
    {
        public int Prepared; public int BareDispatches; public int DispatcherPreparations;
        public bool SourceCapable; public bool CompletionCapable;
        public PostCommitDeliveryContext? Source; public RuntimeDeliveryBoundary? Boundary;
        public RuntimePostCommitOutboxClaimCompletion? Completion;
        public Task<RuntimePostCommitSourceFinalization>? Preparation;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RuntimePostCommitSourceFinalization Finalization { get; } = new(1, "operation", "fingerprint", new Plan());
    }

    private sealed class Plan : IRuntimeSourcePlan { public string Family => "test"; public int Version => 1; }
    private sealed class Handler(Recorder recorder) : IRuntimePostCommitCompletionAwareIntentHandler
    {
        public async ValueTask<RuntimePostCommitSourceFinalization> PrepareCompletionAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            recorder.Prepared++; recorder.Source = source; recorder.Entered.TrySetResult();
            return await (recorder.Preparation ?? Task.FromResult(recorder.Finalization));
        }
    }
    private sealed class CompletionProbe(InMemoryRuntimeCheckpointCommitStore inner, Recorder recorder) :
        ForwardingOutboxStore(inner), IRuntimePostCommitOutboxClaimCompletionStore, IPostCommitOutboxLookupStore
    {
        public bool CanFinalizeSource(RuntimeDeliveryBoundary boundary, RuntimePostCommitSourceFinalization finalization)
        { recorder.Boundary = boundary; Assert.Same(recorder.Finalization, finalization); return recorder.CompletionCapable; }
        ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> IRuntimePostCommitOutboxClaimCompletionStore.CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion, CancellationToken cancellationToken)
        { recorder.Completion = completion; throw new ObservedCompletionException(); }
        public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            inner.FindDurableAsync(outboxItemId, cancellationToken);
    }
    private sealed class SourceProbe(Recorder recorder) : IRuntimeDeliverySourceStore
    {
        public bool CanParticipate(RuntimeDeliveryBoundary boundary) { recorder.Boundary = boundary; return recorder.SourceCapable; }
        public ValueTask<RuntimeDeliverySourceSnapshot?> ReadAsync(RuntimeDeliverySourceAddress source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<RuntimeDeliveryWorkClaim>> ClaimAsync(RuntimeDeliveryClaimRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeDeliveryWorkClaim?> RenewAsync(RuntimeDeliveryWorkClaim claim, TimeSpan visibility, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeSourceCommitResult> CommitAsync(RuntimeSourceCommit commit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class BareDispatcher(Recorder recorder) : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        { recorder.BareDispatches++; return ValueTask.CompletedTask; }
        public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { recorder.BareDispatches++; return ValueTask.CompletedTask; }
    }
    private sealed class EmptyPreparationDispatcher(Recorder recorder) : IRuntimePostCommitCompletionAwareDispatcher
    {
        public ValueTask<RuntimePostCommitDispatchPreparation> PrepareDispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default) =>
            Prepared(recorder);
        private static ValueTask<RuntimePostCommitDispatchPreparation> Prepared(Recorder recorder)
        { recorder.DispatcherPreparations++; return ValueTask.FromResult(RuntimePostCommitDispatchPreparation.LegacyHandled); }
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        { recorder.BareDispatches++; return ValueTask.CompletedTask; }
        public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        { recorder.BareDispatches++; return ValueTask.CompletedTask; }
    }
    private sealed class ObservedCompletionException : Exception { }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
}
