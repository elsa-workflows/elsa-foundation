using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnEmbeddingDelivery(BpmnEmbeddingBoundary boundary, BpmnNativeWorkBoundary native,
    IWorkflowSchedulerWorkQueue queue, IBookmarkResumeDispatcher resumes,
    IWorkflowExecutionActorProvider actors, TimeProvider clock, IRuntimeModelExecutionReader? models = null) : IRuntimePostCommitDurableSourceIntentHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        try { await HandleCoreAsync(source, cancellationToken); }
        catch (JsonException exception) { throw InvalidOwnedJson(source, exception); }
        catch (BpmnModelValidationException exception) when (exception.InnerException is JsonException cause)
        { throw InvalidOwnedJson(source, cause); }
    }

    private static BpmnModelValidationException InvalidOwnedJson(PostCommitDeliveryContext source, JsonException exception) =>
        new($"BPMN delivery '{source.Intent.Kind}' for execution '{source.Intent.WorkflowExecutionId}', " +
            $"outbox source '{source.SourceOutboxItemId}' in scope '{source.Scope.Value}' has invalid owned JSON.", exception);

    private async ValueTask HandleCoreAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        boundary.EnsureQualified(); await native.RevalidateSourceAsync(source, cancellationToken);
        var reader = models ?? throw new NotSupportedException("RunBpmn requires qualified model state persistence.");
        if (actors is not InProcessWorkflowExecutionActorProvider) throw new NotSupportedException("RunBpmn requires the qualified built-in actor provider.");
        BpmnEmbeddedInvocation invocation; string inputKind;
        if (source.Intent.Kind == BpmnEmbeddingBoundary.ResultKind)
        {
            var completion = source.Intent.Payload?.Deserialize<RunBpmnCompletion>() ?? throw BpmnEmbeddingJson.Invalid();
            invocation = BpmnEmbeddingBoundary.Read(new RunBpmnState(1, completion.Invocation));
            var model = await boundary.RequireModelAsync(invocation, cancellationToken);
            BpmnEmbeddingBoundary.RequireWorkerCancellationSettled(model);
            if (completion.FormatVersion != 1 || source.Intent.WorkflowExecutionId != invocation.ExecutionId ||
                model.TerminalResult is not { } terminal || !JsonElement.DeepEquals(terminal, completion.Result)) throw BpmnEmbeddingJson.Invalid();
            var proof = await boundary.ParentProofAsync(invocation, cancellationToken);
            if (!proof)
            {
                await resumes.DispatchAsync(new(invocation.NativeExecutionId, BpmnEmbeddingBoundary.StimulusType, invocation.ExecutionId,
                    source.Intent.Payload, source.SourceOutboxItemId, BpmnEmbeddingBoundary.ProviderId, null,
                    BpmnEmbeddingBoundary.CompletionType, BpmnEmbeddingBoundary.ProviderId, invocation.RegistrationId, null), cancellationToken: cancellationToken);
                if (!await boundary.ParentProofAsync(invocation, cancellationToken)) throw Pending();
            }
            inputKind = "native-parent-settled";
        }
        else
        {
            if (source.Intent.Kind is not (BpmnEmbeddingBoundary.StartKind or BpmnEmbeddingBoundary.CancelKind)) throw BpmnEmbeddingJson.Invalid();
            invocation = BpmnEmbeddingJson.Read<BpmnEmbeddedInvocation>(source.Intent.Payload ?? throw BpmnEmbeddingJson.Invalid());
            if (source.Intent.WorkflowExecutionId != invocation.NativeExecutionId || source.Intent.ActivityExecutionId != invocation.NativeInvocationId ||
                !source.Intent.IsEquivalentTo(BpmnEmbeddingCheckpointEnricher.Intent(invocation, source.Intent.Kind == BpmnEmbeddingBoundary.CancelKind, source.Intent.RecordedAt)))
                throw BpmnEmbeddingJson.Invalid();
            if (await reader.FindAsync(invocation.ExecutionId, cancellationToken) is { } retained)
            {
                await boundary.RequireModelAsync(invocation, cancellationToken);
                if (source.Intent.Kind == BpmnEmbeddingBoundary.StartKind || retained.TerminalResult is not null) return;
            }
            inputKind = source.Intent.Kind == BpmnEmbeddingBoundary.StartKind ? "start-embedded" : "cancel-embedded";
        }
        await boundary.RequireMaterialAsync(invocation.Model, cancellationToken);
        var (workflow, _) = await boundary.ParentAsync(invocation, cancellationToken);
        var inputId = inputKind + ":" + BpmnModelIdentity.Create(source.SourceOutboxItemId);
        var command = new RuntimeModelExecutionCommand(invocation.Model.Executable, invocation.Model.MaterialIncarnationId, inputId, inputKind,
            JsonSerializer.SerializeToElement(new BpmnEmbeddedInput(BpmnEmbeddingJson.Write(invocation), source.SourceOutboxItemId), Web),
            workflow.Authority ?? throw BpmnEmbeddingJson.Invalid(), workflow.TenantId);
        if (await reader.FindInputAsync(invocation.ExecutionId, inputId, cancellationToken) is { } receipt)
        {
            if (receipt.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command)) throw new RuntimeCheckpointReplayConflictException(inputId);
            return;
        }
        if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation) throw new NotSupportedException("RunBpmn requires a physical claimed model input queue.");
        // Never synchronously re-enter the parent/model actor from its own post-commit delivery.
        var envelope = "bpmn-embedded:" + BpmnModelIdentity.Create(invocation.ExecutionId, inputId);
        await queue.EnqueueAsync(new(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(inputId), invocation.ExecutionId, inputId,
            WorkflowExecutionCommandKind.ExecuteModel, envelope, envelope, source.Intent.RecordedAt, clock.GetUtcNow(),
            payload: JsonSerializer.SerializeToElement(command, Web)), cancellationToken);
        throw Pending();
    }

    private static RuntimePostCommitDeliveryException Pending() => new(PostCommitFailureKind.Transient, "bpmn.embedded.pending", "The exact model/native recipient consumption remains owed.");
}
