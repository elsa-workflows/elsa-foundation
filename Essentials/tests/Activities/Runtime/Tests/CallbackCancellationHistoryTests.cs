using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class CallbackCancellationHistoryTests
{
    [Fact]
    public async Task An_unrequested_callback_cancellation_is_an_actual_callback_fault_without_a_return_observation()
    {
        await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
            services.AddRuntimeHistory();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "callback-cancellation-history-at-least32bytes");
        }).Build("unrequested-cancellation");
        var root = new ExecutableNode("cancel", "authored-cancel", typeof(UnrequestedCancellation).FullName!, "1.0.0",
            typeof(ClrActivityDescriptor).FullName!, JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeof(UnrequestedCancellation).FullName!)),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var published = await host.PublishAsync(WorkflowExecutionHarness.NewExecutable(root), "callback-cancellation-source");
        await host.StartPublishedAsync(published, WorkflowExecutionHarness.WorkflowExecutionId);
        var page = await host.Services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(WorkflowExecutionHarness.WorkflowExecutionId, limit: 500));
        Assert.Null(page.NextContinuationToken);
        var started = Assert.Single(page.Items, e => e.Name == "ActivityContinuationStarted");
        var faulted = Assert.Single(page.Items, e => e.Name == "ActivityContinuationFaulted");
        Assert.Equal(started.CheckpointId, faulted.CheckpointId);
        Assert.True(started.Sequence < faulted.Sequence);
        Assert.DoesNotContain(page.Items, e => e.Name == "ActivityContinuationReturned");
        Assert.DoesNotContain(page.Items, e => e.Name == "ActivityContinuationCancelled");
        Assert.Single(page.Items, e => e.Name == "ActivityFaulted");
    }

    public sealed class UnrequestedCancellation : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromException<ActivityTransition<ActivityUnit>>(new OperationCanceledException("private-exception-sentinel"));
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(WorkflowExecutionHarness.Partition.Value)); }
}
