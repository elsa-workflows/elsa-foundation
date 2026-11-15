using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Reader/settlement controls only. These synthetic work variants are not host recovery evidence.</summary>
public sealed class BpmnEmbeddedServiceStateTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_saved_human_embedding_keeps_its_original_version_and_work_shape()
    {
        var saved = Saved();
        var document = BpmnDirectExecutionDriver.Read(saved);
        Assert.Equal(3, document.FormatVersion);
        Assert.NotNull(document.NativeParent);
        Assert.Null(Assert.Single(document.Work).NativeWork);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Native_service_embedding_refuses_an_old_or_future_frame(int format)
    {
        var (_, document) = ServiceFrame();
        Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.Read(State(document with { FormatVersion = format })));
    }

    [Fact]
    public void Embedded_native_work_requires_its_retained_parent()
    {
        var (_, document) = ServiceFrame();
        Assert.Equal(5, BpmnDirectExecutionDriver.Read(State(document)).FormatVersion);
        Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.Read(State(document with { NativeParent = null })));
    }

    [Fact]
    public void Parent_settlement_cannot_advance_while_native_cancellation_is_owed()
    {
        var (_, document) = ServiceFrame();
        var origin = Assert.Single(document.Work).NativeWork!;
        document = document with { FormatVersion = 6, Work = [], Cancellations = [new(origin, "synthetic-cancel:" + BpmnNativeWorkBoundary.CancellationIntentId(origin))] };
        var state = State(document, RuntimeModelLifecycle.TerminalPendingSettlement, Cancelled());
        Assert.Single(BpmnDirectExecutionDriver.Read(state).Cancellations!);
        Assert.Throws<RuntimeSchedulerWorkUnavailableException>(() => BpmnEmbeddingBoundary.RequireWorkerCancellationSettled(state));
        Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.Read(State(document, RuntimeModelLifecycle.TerminalSettled, Cancelled())));
    }

    [Fact]
    public void Settled_worker_cancellation_can_still_owe_its_parent_return()
    {
        var (_, document) = ServiceFrame();
        var origin = Assert.Single(document.Work).NativeWork!;
        document = document with { FormatVersion = 6, Work = [],
            Cancellations = [new(origin, "synthetic-cancel:" + BpmnNativeWorkBoundary.CancellationIntentId(origin), Settled: true)],
            NativeResults = [new(origin, "synthetic-terminal:result", WorkflowExecutionStatus.Cancelled, null, null, [])] };
        var pendingParent = State(document, RuntimeModelLifecycle.TerminalPendingSettlement, Cancelled());
        BpmnEmbeddingBoundary.RequireWorkerCancellationSettled(pendingParent);
        Assert.Equal(6, BpmnDirectExecutionDriver.Read(pendingParent).FormatVersion);
        Assert.Equal(6, BpmnDirectExecutionDriver.Read(State(document, RuntimeModelLifecycle.TerminalSettled, Cancelled())).FormatVersion);
        Assert.Throws<BpmnModelValidationException>(() => BpmnDirectExecutionDriver.Read(State(document with { NativeResults = null }, RuntimeModelLifecycle.TerminalPendingSettlement, Cancelled())));
    }

    private static (RuntimeModelExecutionState, BpmnDirectExecutionDriver.Document) ServiceFrame()
    {
        var saved = Saved();var document = BpmnDirectExecutionDriver.Read(saved);var work = Assert.Single(document.Work);
        var parent = BpmnEmbeddingBoundary.Read(new RunBpmnState(1, document.NativeParent!.Invocation));
        var worker = BpmnNativeWorkBoundary.WorkerIdentity(saved.Scope, saved.ExecutionId, work.InvocationId, work.AttemptId, parent.NativeExecutable);
        var origin = new RuntimeModelNativeWorkOrigin(1, saved.Scope, saved.ExecutionId, saved.Protocol, saved.Executable, saved.MaterialIncarnationId,
            work.BindingId, work.InvocationId, work.AttemptId, parent.NativeExecutable, worker.WorkflowExecutionId,
            "synthetic-start:synthetic-intent", "synthetic-intent", BpmnNativeWorkBoundary.ResultKind, "synthetic-result-input");
        return (saved, document with { FormatVersion = 5, Work = [work with { NativeWork = origin }] });
    }

    private static RuntimeModelExecutionState Saved()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualHumanEmbedding.json")));
        return json.RootElement.GetProperty("model").Deserialize<RuntimeModelExecutionState>(Web)!;
    }

    private static RuntimeModelExecutionState State(BpmnDirectExecutionDriver.Document document, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running, JsonElement? terminal = null)
    {
        var saved = Saved();
        return new(saved.FormatVersion, saved.Scope, saved.ExecutionId, saved.TenantId, saved.Protocol, saved.Executable, saved.MaterialIncarnationId,
            saved.Revision, lifecycle, saved.Authority, saved.CreatedAt, saved.UpdatedAt, JsonSerializer.SerializeToElement(document, Web), terminal);
    }

    private static JsonElement Cancelled() => JsonSerializer.SerializeToElement(new { status = "Cancelled" });
}
