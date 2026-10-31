using System.IO.Compression;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task An_already_queued_native_result_preserves_cancellation_owed_from_an_earlier_scope_checkpoint()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualPriorScopeCancellation.sqlite.gz")))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        const string outer = "bpmn:723068f9ada40fbde52746a0678a2f5c65e61ef6831c683f85b0098a3daaa044";
        const string middle = "wfexec:start:v1:d02cb25356a4825ec03647642c84fc5028d20300f53f65d3b8ea86a6a5044794";
        const string inner = "bpmn-embedded:57443f3f73b36e4b4cb155ba015275d92664c3a3637cfa4d77f7be40c2f1bcfc";
        const string source = "commit:round9:0ce3b59c12b34687b8e8c9af19979c01:activity-scope-cancelled:14oD2dLayY2:bpmn-embedded-cancel:e8559ceb6650ed55da14d57a1e5f6fae28b2a9393e60180403bb40be41374a16";
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)));
        await using var scope = await ScopeAsync(host);
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var lookup = scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var before = (await reader.FindAsync(outer))!;
        var inputId = Assert.Single((await queue.ListAsync(new(outer))).Items).CommandId;
        Assert.Equal(RuntimeModelLifecycle.Running, before.Lifecycle);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await lookup.FindDurableAsync(source))!.Status);

        // Consume the authentic queued result before its earlier descendant cancellation is allowed to settle.
        await DrainPriorCancellationAsync(outer);
        var deferred = (await reader.FindAsync(outer))!;
        Assert.Equal(RuntimeModelLifecycle.Running, deferred.Lifecycle);
        Assert.Equal(before.Revision, deferred.Revision);
        Assert.Null(await reader.FindInputAsync(outer, inputId));
        Assert.Equal(inputId, Assert.Single((await queue.ListAsync(new(outer))).Items).CommandId);
        Assert.Null((await lookup.FindDurableAsync(source))!.DeliveredAt);
        Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, (await reader.FindAsync(inner))!.Lifecycle);

        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        // Refusal retains the original positive queue claim for its real one-minute visibility period.
        // Let it expire naturally; do not replace the result, edit its claim or advance an artificial clock.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var execution in new[] { inner, middle, outer })
            {
                await processor.ProcessAsync(new(limit: 100, workflowExecutionId: execution));
                await DrainPriorCancellationAsync(execution);
            }
            if ((await reader.FindAsync(outer))!.Lifecycle == RuntimeModelLifecycle.TerminalSettled)
            {
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(source))!.Status);
                break;
            }
            await Task.Delay(1300);
        }
        var final = (await reader.FindAsync(outer))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, final.Lifecycle);
        Assert.Equal("Cancelled", final.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal(before.Authority, final.Authority);
        Assert.Equal(before.Executable, final.Executable);
        Assert.Equal(before.MaterialIncarnationId, final.MaterialIncarnationId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await lookup.FindDurableAsync(source))!.Status);
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await reader.FindAsync(inner))!.Lifecycle);
        Assert.Equal("Accepted", (await reader.FindInputAsync(outer, inputId))!.Result.GetProperty("status").GetString());

        async Task DrainPriorCancellationAsync(string execution)
        {
            var now = DateTimeOffset.UtcNow;
            var id = "prior-cancel-drain:" + Guid.NewGuid().ToString("N");
            var actor = await host.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(execution,
                WorkflowExecutionActorActivationReason.ControlPlaneCommand, now, "prior-cancel-test", WorkflowExecutionActorCapabilities.InProcessMailbox,
                partition: new(PersistenceScope.DefaultValue)));
            try
            {
                await actor.EnqueueAsync(new(id, execution, new(id, execution, WorkflowExecutionCommandKind.RunSchedulerWork, now, null,
                    new Dictionary<string, string>()), id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: new(PersistenceScope.DefaultValue)));
            }
            catch (RuntimeSchedulerWorkUnavailableException) { /* The original claimed input stays owed for normal retry. */ }
        }
    }
}
