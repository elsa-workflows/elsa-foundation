using System.Text.Json;
using System.Text.Json.Serialization;
using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>The first direct profile uses library semantics and model-owned human work, without native activities.</summary>
internal sealed class BpmnDirectExecutionDriver(RuntimeCheckpointCommitter committer,
    TimeProvider clock, BpmnModelHostingOptions options, IRuntimeModelExecutionReader? reader = null) : IRuntimeExecutionProtocolDriver
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private IRuntimeModelExecutionReader Reader => reader ?? throw new RuntimeSchedulerWorkUnavailableException(
        "bpmn.state-unavailable", "The selected host has no model state reader.");

    public ValueTask ValidateMaterialAsync(RuntimeModelMaterial material, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Graph(material);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ValidateInputAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Command.InputKind == "start" && context.State is null && !options.AdmitNewExecutions)
            throw new RuntimeSchedulerWorkUnavailableException("bpmn.admission-disabled", "New BPMN execution admission is disabled.");
        await ValidateShapeAndReplayAsync(context, cancellationToken);
    }

    private async ValueTask ValidateShapeAndReplayAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (context.Command.InputKind)
        {
            case "start":
                var start = context.Command.Payload.Deserialize<Start>(Strict) ?? throw new BpmnModelValidationException("BPMN start input is missing.");
                if (start.Inputs is { Count: > 0 }) throw new BpmnModelValidationException("This direct profile has no declared workflow inputs.");
                break;
            case "complete-human":
                var complete = Completion(context.Command.Payload);
                if (context.State is null) throw new BpmnModelValidationException("The BPMN execution has not started.");
                if (complete.Outcomes is { } outcomes && (outcomes.Length != 1 || outcomes[0] != "Done"))
                    throw new BpmnModelValidationException("This direct human binding supports the Done outcome.");
                break;
            default: throw new BpmnModelValidationException("The BPMN input kind is unsupported.");
        }
        var prior = await Reader.FindInputAsync(context.WorkItem.WorkflowExecutionId, context.Command.InputId, cancellationToken);
        if (prior is not null && prior.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(context.Command))
            throw new RuntimeCheckpointReplayConflictException(context.Command.InputId);
    }

    public async ValueTask ExecuteAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default)
    {
        var command = context.Command;
        var executionId = context.WorkItem.WorkflowExecutionId;
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeModelInput(command);
        if (await Reader.FindInputAsync(executionId, command.InputId, cancellationToken) is { } prior)
        {
            if (prior.ContentFingerprint != fingerprint) throw new RuntimeCheckpointReplayConflictException(command.InputId);
            return;
        }
        await ValidateShapeAndReplayAsync(context, cancellationToken);
        var graph = Graph(context.Material.Model!);
        var current = context.State;
        var document = current is null ? new Document(1, new BpmnExecutionState(), []) : Read(current);
        if (current is null && command.InputKind == "start")
            document = document with { SourceReferenceId = command.Payload.Deserialize<Start>(Strict)!.SourceReferenceId };
        BpmnEvaluation? evaluation = null;
        string? rejection = null;
        CompletionInput? completion = null;
        if (command.InputKind == "start")
        {
            if (current is not null) rejection = "already-started";
            else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.Start(
                new(graph, null, BpmnHostSnapshot.Root(executionId), null)));
        }
        else
        {
            completion = Completion(command.Payload);
            var work = document.Work.SingleOrDefault(item => item.InvocationId == completion.InvocationId && item.AttemptId == completion.AttemptId);
            if (current!.Lifecycle is RuntimeModelLifecycle.TerminalSettled or RuntimeModelLifecycle.TerminalPendingSettlement)
                rejection = "execution-terminal";
            else if (work is null) rejection = "obsolete-human-work";
            else
            {
                var live = document.Work.Where(item => item.InvocationId != work.InvocationId).Select(item =>
                    new BpmnLiveWork(item.BindingId, item.IterationId, item.InvocationId) { WorkTokenId = item.TokenId }).ToArray();
                var snapshot = BpmnHostSnapshot.Root(executionId) with { LiveWork = live };
                evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkCompleted(
                    new(graph, document.State, snapshot, work.BindingId, work.InvocationId, ["Done"], work.IterationId) { WorkTokenId = work.TokenId }));
            }
        }

        var lifecycle = current?.Lifecycle ?? RuntimeModelLifecycle.Running;
        JsonElement? terminal = current?.TerminalResult;
        if (evaluation is not null)
        {
            document = Apply(context, document, evaluation);
            if (evaluation.Continuation is BpmnContinuation.Complete complete)
            {
                lifecycle = RuntimeModelLifecycle.TerminalSettled;
                terminal = JsonSerializer.SerializeToElement(new { status = "Completed", outcome = complete.Outcome }, Web);
            }
            else if (evaluation.Continuation is BpmnContinuation.Fault fault)
            {
                lifecycle = RuntimeModelLifecycle.TerminalSettled;
                terminal = JsonSerializer.SerializeToElement(new { status = "Faulted", fault.Code, fault.Message, fault.ErrorCode }, Web);
            }
        }
        var now = clock.GetUtcNow();
        if (current is not null && now < current.UpdatedAt) now = current.UpdatedAt;
        var revision = rejection is null ? checked((current?.Revision ?? 0) + 1) : current!.Revision;
        var state = rejection is not null ? current! : new RuntimeModelExecutionState(1, context.Scope, executionId, command.TenantId,
            context.Material.Model!.Protocol, context.Material.Identity, context.Material.IncarnationId, revision, lifecycle,
            command.Authority, current?.CreatedAt ?? now, now, JsonSerializer.SerializeToElement(document, Web), terminal);
        var receipt = new RuntimeModelInputReceipt(context.Scope, executionId, command.InputId, command.InputKind,
            fingerprint, revision, now, completion?.InvocationId, completion?.AttemptId,
            JsonSerializer.SerializeToElement(new { status = rejection is null ? "Accepted" : "Rejected", code = rejection,
                revision, lifecycle = lifecycle.ToString() }, Web));
        var commitId = "model-input:" + BpmnModelIdentity.Create(context.Scope.Value, executionId, command.InputId);
        var commit = new RuntimeCheckpointCommit(commitId, new(commitId, "ModelDecision", executionId, now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { ModelState = new(current?.Revision, state, receipt) };
        var accepted = await committer.CommitAsync(commit, cancellationToken);
        if (!accepted.Succeeded) throw new BpmnModelValidationException($"BPMN execution '{executionId}' did not commit input '{command.InputId}': {accepted.FailureCode}.");
    }

    private static BpmnGraph Graph(RuntimeModelMaterial material)
    {
        if (material.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            material.Profile != BpmnModelProtocol.Profile || material.Dependencies.Count != 0 || material.Outputs.Count != 0 ||
            material.InputContract.Version != WorkflowExecutableInputContract.CurrentVersion || material.InputContract.Inputs.Count != 0 ||
            material.RuntimeRequirements.Count != 0)
            throw new BpmnModelValidationException("The BPMN material profile or binding/input/output contract is unsupported.");
        var envelope = material.Definition.Deserialize<BpmnModelDefinition>(Strict) ?? throw new BpmnModelValidationException("BPMN model definition is missing.");
        var process = envelope.Process.Deserialize<BpmnProcessDefinition>(Strict) ?? throw new BpmnModelValidationException("BPMN process definition is missing.");
        if (process.Variables.Count != 0 || process.IsTransaction || process.Elements.Any(item =>
                item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.UserTask or BpmnElementTypes.ParallelGateway) ||
                item.EventDefinitions.Count != 0 || item.LoopCharacteristics is not null || item.IsForCompensation || item.TriggeredByEvent ||
                item.ListenerBindingRef is not null || item.IsTransaction || item.Script is not null || item.Properties.Count != 0))
            throw new BpmnModelValidationException("The direct profile currently supports plain start/end, parallel fork/join and explicitly bound human work.");
        try { return BpmnGraph.Build(process, envelope.HumanBindings.Select(id => new BpmnBoundWork(id)).ToArray(), BpmnHostCapabilities.None); }
        catch (BpmnExecutionException exception) { throw new BpmnModelValidationException(exception.Message, exception); }
    }

    private static Document Read(RuntimeModelExecutionState state)
    {
        var result = state.Payload.Deserialize<Document>(Web) ?? throw new BpmnModelValidationException("BPMN model state is missing.");
        if (result.FormatVersion != 1 || result.Work.Count != result.State.ActiveWork.Count ||
            result.Work.Select(item => item.InvocationId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Select(item => item.TokenId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Any(item => !result.State.ActiveWork.Any(active => active.TokenId == item.TokenId &&
                active.NodeId == item.BindingId && active.ElementId == item.ElementId && active.IterationId == item.IterationId)))
            throw new BpmnModelValidationException("The BPMN state and exact work inventory differ.");
        return result;
    }

    private static Document Apply(RuntimeModelExecutionContext context, Document prior, BpmnEvaluation evaluation)
    {
        if (evaluation.Commands.Any(command => command is not (BpmnHostCommand.StartWork or BpmnHostCommand.CancelWorkSubtree)))
            throw new BpmnModelValidationException("The BPMN evaluation requires an unsupported direct-host binding.");
        var state = evaluation.State.Prune();
        var work = state.ActiveWork.Select(active =>
        {
            if (prior.Work.SingleOrDefault(item => item.TokenId == active.TokenId) is { } retained) return retained;
            if (!evaluation.Commands.OfType<BpmnHostCommand.StartWork>().Any(start => start.TokenId == active.TokenId && start.ElementId == active.ElementId && start.BindingRef == active.NodeId))
                throw new BpmnModelValidationException("An active BPMN work token has no matching start command.");
            var invocation = "bpmn-work:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, active.TokenId, active.ElementId, active.NodeId);
            return new Work(active.ElementId, active.NodeId, active.TokenId, active.IterationId, invocation, "attempt:" + BpmnModelIdentity.Create(invocation, "1"));
        }).ToArray();
        return new(1, state, work, prior.SourceReferenceId);
    }

    private static BpmnEvaluation Evaluate(Func<BpmnEvaluation> action)
    {
        try { return BpmnEvaluationComponent.Evaluate(action); }
        catch (BpmnHostException exception) { throw new BpmnModelValidationException(exception.Message, exception); }
    }

    private static CompletionInput Completion(JsonElement payload)
    {
        var completion = payload.Deserialize<CompletionInput>(Strict) ?? throw new BpmnModelValidationException("Human completion is missing.");
        if (string.IsNullOrWhiteSpace(completion.InvocationId) || string.IsNullOrWhiteSpace(completion.AttemptId))
            throw new BpmnModelValidationException("Human completion requires its exact invocation and attempt.");
        return completion;
    }

    internal sealed record Document(int FormatVersion, BpmnExecutionState State, IReadOnlyList<Work> Work, string? SourceReferenceId = null);
    internal sealed record Work(string ElementId, string BindingId, string TokenId, string? IterationId, string InvocationId, string AttemptId);
    private sealed record Start(IReadOnlyDictionary<string, JsonElement>? Inputs = null, string? SourceReferenceId = null);
    private sealed record CompletionInput(string InvocationId, string AttemptId, string[]? Outcomes = null);
}
