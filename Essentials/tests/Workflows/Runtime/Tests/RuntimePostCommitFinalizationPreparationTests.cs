using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitFinalizationPreparationTests
{
    private const string Kind = "Test.Finalization";
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Legacy_constructors_preserve_ordinary_source_and_Event_policy_without_finalization()
    {
        RuntimePostCommitIntentHandlerContribution[] legacy = [new("ordinary", typeof(OrdinaryHandler)),
            new("ordinary", typeof(OrdinaryHandler), RuntimePostCommitRetryPolicy.None),
            new("source", typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true),
            new("event", typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true, 1)];
        Assert.All(legacy, contribution => Assert.False(contribution.RequiresSuccessfulFinalization));
        Assert.False(legacy[0].RequiresDurableSource); Assert.True(legacy[2].RequiresDurableSource);
        Assert.Equal(1, legacy[3].StimulusDeliveryProtocolVersion);
        Assert.True(Required().RequiresSuccessfulFinalization);
    }

    [Theory]
    [InlineData(false, typeof(CompletionHandler), null)]
    [InlineData(true, typeof(SourceHandler), null)]
    [InlineData(true, typeof(OrdinaryHandler), null)]
    [InlineData(true, typeof(CompletionHandler), 1)]
    public void Finalization_requires_explicit_source_and_preparation_without_replacing_Event_protocol(bool durable, Type handler, int? protocol) =>
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitIntentHandlerContribution(Kind, handler, RuntimePostCommitRetryPolicy.None, durable, protocol, true));

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Every_ordinary_constructor_refuses_a_completion_aware_handler(int arguments) => Assert.Throws<ArgumentException>(() => arguments switch
    {
        2 => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(CompletionHandler)),
        3 => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(CompletionHandler), RuntimePostCommitRetryPolicy.None),
        4 => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(CompletionHandler), RuntimePostCommitRetryPolicy.None, true),
        5 => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(CompletionHandler), RuntimePostCommitRetryPolicy.None, true, null),
        _ => new RuntimePostCommitIntentHandlerContribution(Kind, typeof(CompletionHandler), RuntimePostCommitRetryPolicy.None, true, null, false)
    });

    [Fact]
    public async Task Required_kind_bare_paths_refuse_before_resolving_or_invoking_any_handler()
    {
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope(); var state = scope.ServiceProvider.GetRequiredService<Calls>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(); var source = Source();
        Assert.True(dispatcher.RequiresDurableSource(Kind));
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(source.Intent).AsTask());
        var failure = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.DispatchAsync(source).AsTask());
        Assert.Equal(source.SourceOutboxItemId, failure.SourceOutboxItemId);
        Assert.Equal(0, state.Resolutions); Assert.Empty(state.Prepared); Assert.Empty(state.Intents); Assert.Empty(state.Contexts);
    }

    [Fact]
    public async Task Actual_scoped_dispatcher_returns_the_exact_prepared_plan_without_any_bare_handler_call()
    {
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope(); var state = scope.ServiceProvider.GetRequiredService<Calls>();
        var dispatcher = Assert.IsAssignableFrom<IRuntimePostCommitCompletionAwareDispatcher>(scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>());
        var source = Source(); var preparation = await dispatcher.PrepareDispatchAsync(source);
        Assert.Same(state.Finalization, preparation.Finalization); Assert.Same(source, Assert.Single(state.Prepared));
        Assert.Equal(1, state.Resolutions); Assert.Empty(state.Intents); Assert.Empty(state.Contexts);
        using var another = provider.CreateScope();
        Assert.NotSame(state, another.ServiceProvider.GetRequiredService<Calls>());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Preparation_awaits_existing_ordinary_or_source_delivery_before_LegacyHandled(bool durable)
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<Calls>(); state.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = (IRuntimePostCommitCompletionAwareDispatcher)scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        var source = Source(durable ? "Test.Source" : "Test.Ordinary");
        var pending = dispatcher.PrepareDispatchAsync(source).AsTask();
        Assert.False(pending.IsCompleted);
        if (durable) Assert.Same(source, Assert.Single(state.Contexts)); else Assert.Same(source.Intent, Assert.Single(state.Intents));
        state.Gate.SetResult();
        Assert.Same(RuntimePostCommitDispatchPreparation.LegacyHandled, await pending); Assert.Empty(state.Prepared);
    }

    [Fact]
    public async Task Preparation_preserves_requested_cancellation_and_handler_cause_and_refuses_missing_plan()
    {
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<Calls>();
        var dispatcher = (IRuntimePostCommitCompletionAwareDispatcher)scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => dispatcher.PrepareDispatchAsync(Source(), cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, canceled.CancellationToken); Assert.Equal(0, state.Resolutions);
        var cause = new IOException("private-provider-cause"); state.Failure = cause;
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => dispatcher.PrepareDispatchAsync(Source()).AsTask()));
        state.Failure = null; state.Finalization = null;
        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => dispatcher.PrepareDispatchAsync(Source()).AsTask());
    }

    [Fact]
    public void Conflicting_finalization_policy_refuses_dispatch_and_pending_factory_before_registration_or_delivery()
    {
        var ordinary = new RuntimePostCommitIntentHandlerContribution(Kind, typeof(SourceHandler), RuntimePostCommitRetryPolicy.None, true);
        RuntimePostCommitIntentHandlerContribution[] conflicting = [Required(), ordinary];
        using var provider = new ServiceCollection().BuildServiceProvider();
        var conflict = Assert.Throws<InvalidOperationException>(() => new RuntimePostCommitIntentDispatcher(conflicting, provider));
        Assert.Contains("source or finalization requirements", conflict.Message);
        var intent = Source().Intent; var metadata = new Dictionary<string, string>();
        var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", "test", "publisher", Now, [], metadata),
            new(null, null, [], [], [], [], []), [intent], metadata);
        Assert.Throws<InvalidOperationException>(() => RuntimePostCommitOutboxItems.CreatePendingChanges(commit, conflicting, new("scope")));
        var services = Services(); var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitIntentHandler<SourceHandler>(Kind, RuntimePostCommitRetryPolicy.None, true));
        Assert.Equal(before, services);
        Assert.Throws<ArgumentException>(() => services.AddRuntimePostCommitIntentHandler<CompletionHandler>("Other"));
        Assert.Equal(before, services);
    }

    private static RuntimePostCommitIntentHandlerContribution Required() => new(Kind, typeof(CompletionHandler), RuntimePostCommitRetryPolicy.None, true, null, true);
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection(); services.AddScoped<Calls>(); services.AddScoped<CompletionHandler>();
        services.AddSingleton(Required()); services.AddSingleton(Required());
        services.AddRuntimePostCommitIntentHandler<OrdinaryHandler>("Test.Ordinary");
        services.AddRuntimePostCommitIntentHandler<SourceHandler>("Test.Source", RuntimePostCommitRetryPolicy.None, true);
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        return services;
    }
    private static PostCommitDeliveryContext Source(string kind = Kind)
    {
        var intent = new RuntimePostCommitIntent("send", "publisher", kind, Now, null, null, null);
        var item = new RuntimePostCommitOutboxItem("commit:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", Now, TimeSpan.FromMinutes(1), 1)), new("scope"));
    }
    private sealed class Plan : IRuntimeSourcePlan { public string Family => "BusinessMessage"; public int Version => 1; }
    private sealed class Calls
    {
        public int Resolutions { get; set; }
        public List<PostCommitDeliveryContext> Prepared { get; } = [];
        public List<PostCommitDeliveryContext> Contexts { get; } = [];
        public List<RuntimePostCommitIntent> Intents { get; } = [];
        public TaskCompletionSource? Gate { get; set; }
        public Exception? Failure { get; set; }
        public RuntimePostCommitSourceFinalization? Finalization { get; set; } = new(1, "operation", "fingerprint", new Plan());
    }
    private sealed class OrdinaryHandler(Calls calls) : IRuntimePostCommitIntentHandler
    {
        public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken)
        { calls.Intents.Add(intent); if (calls.Gate is not null) await calls.Gate.Task.WaitAsync(cancellationToken); }
    }
    private sealed class SourceHandler(Calls calls) : IRuntimePostCommitDurableSourceIntentHandler
    {
        public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
        { calls.Contexts.Add(source); if (calls.Gate is not null) await calls.Gate.Task.WaitAsync(cancellationToken); }
    }
    private sealed class CompletionHandler : IRuntimePostCommitCompletionAwareIntentHandler
    {
        private readonly Calls calls;
        public CompletionHandler(Calls calls) { this.calls = calls; calls.Resolutions++; }
        public ValueTask<RuntimePostCommitSourceFinalization> PrepareCompletionAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
        {
            calls.Prepared.Add(source); if (calls.Failure is not null) throw calls.Failure;
            return ValueTask.FromResult(calls.Finalization!);
        }
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken) => throw new InvalidOperationException("Bare intent path was reached.");
        public ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken) => throw new InvalidOperationException("Bare source path was reached.");
    }
}
