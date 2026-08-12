using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerWorkHandlerBaseTests
{
    [Fact]
    public void Constructor_WhenScopeFactoryIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TestHandler(null!));
    }

    [Fact]
    public async Task HandleAsync_WithoutPipeline_DeserializesOnceAndRunsInFreshScope()
    {
        await using var provider = new ServiceCollection().AddSingleton(new Marker()).BuildServiceProvider();
        var handler = new TestHandler(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.HandleAsync(WorkItem());

        Assert.Equal(1, handler.Deserialized);
        Assert.NotNull(handler.SeenProvider);
        Assert.NotSame(provider, handler.SeenProvider);
    }

    [Fact]
    public async Task HandleAsync_WhenDeserializationThrows_NoScopeIsCreated()
    {
        var scopeFactory = new CountingScopeFactory();
        var handler = new TestHandler(scopeFactory) { FailDeserialize = true };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(WorkItem()).AsTask());
        Assert.Equal(0, scopeFactory.ScopesCreated);
    }

    [Fact]
    public async Task HandleAsync_WithPipeline_UsesAmbientServicesAndCreatesNoScope()
    {
        await using var ambient = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = new CountingScopeFactory();
        var handler = new TestHandler(scopeFactory);
        var pipelineContext = new WorkflowRuntimePipelineContext(WorkItem());
        pipelineContext.Workspace.AmbientServices = ambient;

        await handler.HandleAsync(WorkItem(), pipelineContext);

        Assert.Same(ambient, handler.SeenProvider);
        Assert.Equal(0, scopeFactory.ScopesCreated);
    }

    [Fact]
    public async Task HandleAsync_WithPipelineButNoAmbientServices_FallsBackToFreshScope()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var handler = new TestHandler(provider.GetRequiredService<IServiceScopeFactory>());

        await handler.HandleAsync(WorkItem(), new WorkflowRuntimePipelineContext(WorkItem()));

        Assert.NotNull(handler.SeenProvider);
        Assert.NotSame(provider, handler.SeenProvider);
    }

    [Fact]
    public async Task HandleAsync_NullArgumentsAndCancelledTokens_ThrowOnBothOverloads()
    {
        var handler = new TestHandler(new CountingScopeFactory());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!, new WorkflowRuntimePipelineContext(WorkItem())).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(WorkItem(), pipelineContext: null!).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(WorkItem(), cancelled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(WorkItem(), new WorkflowRuntimePipelineContext(WorkItem()), cancelled.Token).AsTask());
        Assert.Equal(0, handler.Deserialized);
    }

    [Fact]
    public void TimeProvider_DefaultsToSystemAndKeepsExplicitInstance()
    {
        var explicitProvider = new FixedTimeProvider();

        Assert.Same(TimeProvider.System, new TestHandler(new CountingScopeFactory()).Clock);
        Assert.Same(explicitProvider, new TestHandler(new CountingScopeFactory(), explicitProvider).Clock);
    }

    private static RuntimeSchedulerWorkItem WorkItem()
    {
        var now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        return new RuntimeSchedulerWorkItem(
            workItemId: "work-1",
            workflowExecutionId: "execution-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "idempotency-1",
            enqueuedAt: now,
            recordedAt: now,
            sequence: null,
            payload: null);
    }

    private sealed class Marker;

    private sealed class FixedTimeProvider : TimeProvider;

    private sealed class TestHandler(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
        : RuntimeSchedulerWorkHandlerBase<string>(scopeFactory, timeProvider)
    {
        public bool FailDeserialize { get; init; }
        public int Deserialized { get; private set; }
        public IServiceProvider? SeenProvider { get; private set; }
        public TimeProvider Clock => TimeProvider;

        public override string Name => nameof(TestHandler);

        public override bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        protected override string DeserializePayload(RuntimeSchedulerWorkItem workItem)
        {
            if (FailDeserialize)
                throw new ArgumentException("invalid payload");
            Deserialized++;
            return "payload";
        }

        protected override ValueTask HandleWithServicesAsync(
            RuntimeSchedulerWorkItem workItem,
            string payload,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            SeenProvider = serviceProvider;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int ScopesCreated { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopesCreated++;
            throw new InvalidOperationException("The test never expects a scope to be used.");
        }
    }
}
