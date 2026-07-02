using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Shared fixtures for the runtime execution-pipeline tests: a fixed clock, a scheduler work-item factory, a recording
/// activity middleware, and a recording work handler. Kept in one place so the two pipeline test classes don't drift.
/// </summary>
public abstract class RuntimePipelineTestSupport
{
    protected readonly DateTimeOffset Now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    protected RuntimeSchedulerWorkItem NewWorkItem(
        WorkflowExecutionCommandKind commandKind,
        int index = 1,
        JsonElement? payload = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: "wfexec-1",
            commandId: $"command-{index}",
            commandKind: commandKind,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"wfexec-1:command-{index}",
            enqueuedAt: Now,
            recordedAt: Now,
            sequence: index,
            payload: payload ?? document.RootElement.Clone());
    }

    protected sealed class RecordingActivityMiddleware : ActivityRuntimeMiddlewareBase
    {
        public int Invocations { get; private set; }

        public override ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
        {
            Invocations++;
            return next(context);
        }
    }

    protected sealed class RecordingHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }

    protected sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
