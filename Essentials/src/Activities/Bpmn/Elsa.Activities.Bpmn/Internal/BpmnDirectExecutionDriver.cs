using System.Text.Json;
using System.Text.Json.Serialization;
using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using RunBpmnCompletion = Elsa.Activities.Bpmn.Models.RunBpmnCompletion;
using RunBpmnState = Elsa.Activities.Bpmn.Models.RunBpmnState;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Direct and embedded model execution use the released BPMN semantics and retained host work bindings.</summary>
internal sealed class BpmnDirectExecutionDriver(RuntimeCheckpointCommitter committer,
    TimeProvider clock, BpmnModelHostingOptions options, IRuntimeModelExecutionReader? reader = null,
    BpmnNativeWorkBoundary? nativeWork = null, BpmnEmbeddingBoundary? embedding = null) : IRuntimeExecutionProtocolDriver
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private IRuntimeModelExecutionReader Reader => reader ?? throw new RuntimeSchedulerWorkUnavailableException(
        "bpmn.state-unavailable", "The selected host has no model state reader.");
    private BpmnNativeWorkBoundary NativeWork => nativeWork ?? throw new NotSupportedException("The native BPMN work binding is not installed.");
    private BpmnEmbeddingBoundary Embedding => embedding ?? throw new NotSupportedException("The native RunBpmn binding is not installed.");

    public ValueTask ValidateMaterialAsync(RuntimeModelMaterial material, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Graph(material);
        if (material.Profile == BpmnModelProtocol.NativeWorkProfile) NativeWork.EnsureQualified();
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
            case "start-embedded":
            case "cancel-embedded":
            case "native-parent-settled":
                await Embedding.ValidateInputAsync(context, cancellationToken);
                break;
            case "start":
                var start = context.Command.Payload.Deserialize<Start>(Strict) ?? throw new BpmnModelValidationException("BPMN start input is missing.");
                if (start.Inputs is { Count: > 0 }) throw new BpmnModelValidationException("This direct profile has no declared workflow inputs.");
                break;
            case "complete-human":
                if (context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile)
                    throw new BpmnModelValidationException("This native service profile declares no human completion binding.");
                var complete = Completion(context.Command.Payload);
                if (context.State is null) throw new BpmnModelValidationException("The BPMN execution has not started.");
                if (complete.Outcomes is { } outcomes && (outcomes.Length != 1 || outcomes[0] != "Done"))
                    throw new BpmnModelValidationException("This direct human binding supports the Done outcome.");
                break;
            case "cancel":
                var cancel = context.Command.Payload.Deserialize<CancelInput>(Strict)
                    ?? throw new BpmnModelValidationException("BPMN cancellation input is missing.");
                if (context.State is null || cancel.Reason?.Length > 2048)
                    throw new BpmnModelValidationException("Cancellation requires an existing execution and a bounded reason.");
                break;
            case "native-result":
                await NativeWork.ValidateResultAsync(context, NativeResult(context.Command.Payload), cancellationToken);
                break;
            case "native-start-retired":
                await NativeWork.ValidateRetirementAsync(context, context.Command.Payload.Deserialize<BpmnNativeWorkCancellation>(Strict)
                    ?? throw new BpmnModelValidationException("Native start retirement is missing."), cancellationToken);
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
        var isEmbeddedInput = command.InputKind is "start-embedded" or "cancel-embedded" or "native-parent-settled";
        var embedded = isEmbeddedInput ? await Embedding.ValidateInputAsync(context, cancellationToken) : ((BpmnEmbeddedInvocation Invocation, StimulusResumeRecipient? Wait)?)null;
        var document = current is null ? new Document(context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile ? 2 : 1,
            new BpmnExecutionState(), []) : Read(current);
        if (current is null && embedded is { } parent)
            document = document with { FormatVersion = context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile ? 5 : 3,
                NativeParent = new(BpmnEmbeddingJson.Write(parent.Invocation)) };
        if (current is null && command.InputKind == "start")
            document = document with { SourceReferenceId = command.Payload.Deserialize<Start>(Strict)!.SourceReferenceId };
        BpmnEvaluation? evaluation = null;
        string? rejection = null;
        CompletionInput? completion = null;
        BpmnNativeWorkResult? nativeResult = null;
        var cancelled = false;
        var settledCancellation = false;
        if (command.InputKind == "native-parent-settled")
        {
            if (current?.Lifecycle != RuntimeModelLifecycle.TerminalPendingSettlement) rejection = "parent-already-settled";
        }
        else if (command.InputKind is "cancel-embedded" or "cancel")
        {
            if (current?.TerminalResult is not null) rejection = "execution-terminal";
            else
            {
                cancelled = true;
                var cancellations = document.Work.Where(item => item.NativeWork is not null).Select(item =>
                {
                    var origin = item.NativeWork!;
                    var decision = "model-input:" + BpmnModelIdentity.Create(context.Scope.Value, executionId, command.InputId);
                    return new BpmnNativeWorkCancellation(origin, RuntimePostCommitOutboxItems.OutboxItemId(decision,
                        BpmnNativeWorkBoundary.CancellationIntentId(origin)));
                }).ToArray();
                document = document with { Work = [], FormatVersion = cancellations.Length > 0 ? document.NativeParent is null ? 4 : 6 : document.FormatVersion,
                    Cancellations = cancellations.Length > 0 ? cancellations : document.Cancellations };
            }
        }
        else if (command.InputKind == "native-start-retired")
        {
            var retirement = command.Payload.Deserialize<BpmnNativeWorkCancellation>(Strict)!;
            var owed = document.Cancellations!.Single(item => !item.Settled && retirement.Origin.IsEquivalentTo(item.Origin));
            document = document with { Cancellations = document.Cancellations.Select(item => item == owed
                ? item with { Settled = true, NotAdmitted = true } : item).ToArray() };
            completion = new(retirement.Origin.InvocationId, retirement.Origin.AttemptId);
            settledCancellation = true;
        }
        else if (command.InputKind is "start" or "start-embedded")
        {
            if (current is not null) rejection = "already-started";
            else if (embedded is { Wait: null }) cancelled = true;
            else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.Start(
                new(graph, null, BpmnHostSnapshot.Root(executionId), null)));
        }
        else
        {
            nativeResult = command.InputKind == "native-result" ? NativeResult(command.Payload) : null;
            completion = nativeResult is null ? Completion(command.Payload) :
                new(nativeResult.Origin.InvocationId, nativeResult.Origin.AttemptId);
            var work = document.Work.SingleOrDefault(item => item.InvocationId == completion.InvocationId && item.AttemptId == completion.AttemptId);
            if (nativeResult is not null && document.Cancellations?.SingleOrDefault(item => !item.Settled && nativeResult.Origin.IsEquivalentTo(item.Origin)) is { } cancellation)
            {
                // The semantic cancellation is final. Record the worker's actual terminal truth only for settlement.
                document = document with { Cancellations = document.Cancellations.Select(item => item == cancellation ? item with { Settled = true } : item).ToArray(),
                    NativeResults = [.. document.NativeResults ?? [], nativeResult] };
                settledCancellation = true;
            }
            else if (current!.Lifecycle is RuntimeModelLifecycle.TerminalSettled or RuntimeModelLifecycle.TerminalPendingSettlement)
                rejection = "execution-terminal";
            else if (work is null) rejection = "obsolete-human-work";
            else if (nativeResult is null && work.NativeWork is not null) rejection = "not-human-work";
            else if (nativeResult is not null && !nativeResult.Origin.IsEquivalentTo(work.NativeWork)) rejection = "obsolete-native-work";
            else
            {
                var live = document.Work.Where(item => item.InvocationId != work.InvocationId).Select(item =>
                    new BpmnLiveWork(item.BindingId, item.IterationId, item.InvocationId) { WorkTokenId = item.TokenId }).ToArray();
                var snapshot = BpmnHostSnapshot.Root(executionId) with { LiveWork = live };
                if (nativeResult is not null)
                {
                    document = document with { NativeResults = [.. document.NativeResults ?? [], nativeResult] };
                    if (nativeResult.Status == WorkflowExecutionStatus.Cancelled)
                    {
                        // Like native composite cancellation, the terminal execution header closes progression;
                        // keep the interpreter's last snapshot as history rather than inventing token operations.
                        cancelled = true;
                        document = document with { Work = [] };
                    }
                    else if (nativeResult.Status == WorkflowExecutionStatus.Faulted || nativeResult.Completion?.OutcomeKey == "BusinessError")
                    {
                        var binding = Definition(context.Material.Model!).ServiceBindings!.Single(item => item.BindingId == work.BindingId);
                        evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkFaulted(
                            new(graph, document.State, snapshot, work.BindingId, work.InvocationId, "Native service work failed.")
                            { WorkTokenId = work.TokenId, FaultedIterationId = work.IterationId,
                                ErrorCode = nativeResult.Status == WorkflowExecutionStatus.Completed ? binding.BusinessErrorCode : null }));
                    }
                    else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkCompleted(
                        new(graph, document.State, snapshot, work.BindingId, work.InvocationId,
                            [nativeResult.Completion!.OutcomeKey], work.IterationId) { WorkTokenId = work.TokenId }));
                }
                else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkCompleted(
                    new(graph, document.State, snapshot, work.BindingId, work.InvocationId, ["Done"], work.IterationId) { WorkTokenId = work.TokenId }));
            }
        }

        var lifecycle = current?.Lifecycle ?? RuntimeModelLifecycle.Running;
        JsonElement? terminal = current?.TerminalResult;
        var commitId = "model-input:" + BpmnModelIdentity.Create(context.Scope.Value, executionId, command.InputId);
        if (cancelled)
        {
            lifecycle = RuntimeModelLifecycle.TerminalSettled;
            terminal = command.InputKind == "cancel"
                ? JsonSerializer.SerializeToElement(new { status = "Cancelled", command.Payload.Deserialize<CancelInput>(Strict)!.Reason }, Web)
                : JsonSerializer.SerializeToElement(new { status = "Cancelled" }, Web);
        }
        if (evaluation is not null)
        {
            document = Apply(context, document, evaluation, commitId);
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
        if (terminal is not null && document.NativeParent is not null && rejection is null)
            lifecycle = command.InputKind == "native-parent-settled" ? RuntimeModelLifecycle.TerminalSettled : RuntimeModelLifecycle.TerminalPendingSettlement;
        if (document.Cancellations?.Any(item => !item.Settled) == true)
            lifecycle = RuntimeModelLifecycle.TerminalPendingSettlement;
        else if (settledCancellation && document.NativeParent is null)
            lifecycle = RuntimeModelLifecycle.TerminalSettled;
        var now = clock.GetUtcNow();
        if (current is not null && now < current.UpdatedAt) now = current.UpdatedAt;
        var revision = rejection is null ? checked((current?.Revision ?? 0) + 1) : current!.Revision;
        var state = rejection is not null ? current! : new RuntimeModelExecutionState(1, context.Scope, executionId, command.TenantId,
            context.Material.Model!.Protocol, context.Material.Identity, context.Material.IncarnationId, revision, lifecycle,
            command.Authority, current?.CreatedAt ?? now, now, JsonSerializer.SerializeToElement(document, Web), terminal);
        var receipt = new RuntimeModelInputReceipt(context.Scope, executionId, command.InputId, command.InputKind,
            fingerprint, revision, now, completion?.InvocationId ?? embedded?.Invocation.NativeInvocationId,
            completion?.AttemptId ?? embedded?.Invocation.NativeAttemptId,
            JsonSerializer.SerializeToElement(new { status = rejection is null ? "Accepted" : "Rejected", code = rejection,
                revision, lifecycle = lifecycle.ToString() }, Web));
        var starts = rejection is null ? document.Work.Where(work => work.NativeWork is not null &&
            !(current is null ? [] : Read(current).Work).Any(old => old.InvocationId == work.InvocationId))
            .Select(work => new RuntimePostCommitIntent(work.NativeWork!.SourceIntentId, executionId, BpmnNativeWorkBoundary.StartKind,
                now, null, work.NativeWork.SourceIntentId, JsonSerializer.SerializeToElement(work.NativeWork, Web))).ToArray() : [];
        var intents = starts.ToList();
        if (cancelled && rejection is null)
            intents.AddRange((document.Cancellations ?? []).Select(item => new RuntimePostCommitIntent(
                BpmnNativeWorkBoundary.CancellationIntentId(item.Origin), executionId, BpmnNativeWorkBoundary.CancelKind,
                now, null, BpmnNativeWorkBoundary.CancellationIntentId(item.Origin), JsonSerializer.SerializeToElement(item, Web))));
        if (rejection is null && current?.TerminalResult is null && terminal is not null && document.NativeParent is { } nativeParent)
        {
            var intent = "bpmn-native-parent:" + BpmnModelIdentity.Create(executionId);
            intents.Add(new(intent, executionId, BpmnEmbeddingBoundary.ResultKind, now, null, intent,
                JsonSerializer.SerializeToElement(new RunBpmnCompletion(1, nativeParent.Invocation, terminal.Value))));
        }
        var commit = new RuntimeCheckpointCommit(commitId, new(commitId, "ModelDecision", executionId, now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), intents, new Dictionary<string, string>())
        { ModelState = new(current?.Revision, state, receipt), ExpectedModelParentWait = current is null && !cancelled ? embedded?.Wait : null };
        var accepted = await committer.CommitAsync(commit, cancellationToken);
        if (!accepted.Succeeded) throw new BpmnModelValidationException($"BPMN execution '{executionId}' did not commit input '{command.InputId}': {accepted.FailureCode}.");
    }

    private static BpmnGraph Graph(RuntimeModelMaterial material)
    {
        if (material.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            material.Profile is not (BpmnModelProtocol.Profile or BpmnModelProtocol.NativeWorkProfile) || material.Outputs.Count != 0 ||
            material.InputContract.Version != WorkflowExecutableInputContract.CurrentVersion || material.InputContract.Inputs.Count != 0 ||
            material.RuntimeRequirements.Count != 0)
            throw new BpmnModelValidationException("The BPMN material profile or binding/input/output contract is unsupported.");
        var envelope = Definition(material);
        var native = material.Profile == BpmnModelProtocol.NativeWorkProfile;
        if (envelope.FormatVersion != (native ? 2 : 1) || material.Dependencies.Count != (envelope.ServiceBindings?.Count ?? 0) ||
            (envelope.ServiceBindings ?? []).Any(binding => !material.Dependencies.Any(dependency => dependency.BindingId == binding.BindingId &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dependency.Executable, binding.Executable))))
            throw new BpmnModelValidationException("The native service inventory differs from the model's complete retained dependency pins.");
        var process = envelope.Process.Deserialize<BpmnProcessDefinition>(Strict) ?? throw new BpmnModelValidationException("BPMN process definition is missing.");
        if (process.Variables.Count != 0 || process.IsTransaction || process.Elements.Any(item =>
                (native ? item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.ServiceTask or BpmnElementTypes.BoundaryEvent) :
                    item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.UserTask or BpmnElementTypes.ParallelGateway)) ||
                (item.ElementType == BpmnElementTypes.BoundaryEvent ? !native || !IsCodedServiceErrorBoundary(item, process) : item.EventDefinitions.Count != 0) ||
                item.LoopCharacteristics is not null || item.IsForCompensation || item.TriggeredByEvent ||
                item.ListenerBindingRef is not null || item.IsTransaction || item.Script is not null || item.Properties.Count != 0))
            throw new BpmnModelValidationException("The BPMN definition contains elements or events outside its declared hosting profile.");
        if (native && (envelope.HumanBindings.Count != 0 || envelope.ServiceBindings!.Count != 1 ||
            process.Elements.Count(item => item.ElementType == BpmnElementTypes.ServiceTask) != 1))
            throw new BpmnModelValidationException("The current native service profile requires one closed published native worker, plain start/end events and optional coded error boundaries.");
        // This restricted graph can interrupt only the single worker whose authoritative terminal result
        // is already being consumed. Apply refuses cancellation without that exact retained terminal proof.
        try { return BpmnGraph.Build(process, envelope.HumanBindings.Concat((envelope.ServiceBindings ?? []).Select(binding => binding.BindingId))
            .Select(id => new BpmnBoundWork(id)).ToArray(), native ? BpmnHostCapabilities.SubtreeCancellation : BpmnHostCapabilities.None); }
        catch (BpmnExecutionException exception) { throw new BpmnModelValidationException(exception.Message, exception); }
    }

    private static bool IsCodedServiceErrorBoundary(BpmnElement element, BpmnProcessDefinition process)
    {
        if (!element.CancelActivity || element.BindingRef is not null || element.EventDefinitions.Count != 1 ||
            !process.Elements.Any(item => item.ElementId == element.AttachedToRef && item.ElementType == BpmnElementTypes.ServiceTask)) return false;
        var definition = element.EventDefinitions.Single();
        return definition.Type == BpmnEventDefinitionTypes.Error && definition.Properties.Count == 1 &&
            definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Code, out var code) && !string.IsNullOrWhiteSpace(code);
    }

    internal static Document Read(RuntimeModelExecutionState state)
    {
        var result = state.Payload.Deserialize<Document>(Web) ?? throw new BpmnModelValidationException("BPMN model state is missing.");
        if (result.NativeParent is { } parent)
        {
            var invocation = BpmnEmbeddingBoundary.Read(new RunBpmnState(1, parent.Invocation));
            if (invocation.ExecutionId != state.ExecutionId || invocation.Scope != state.Scope || invocation.RegistrationId is null ||
                invocation.NativeAttemptId is null || invocation.Model.MaterialIncarnationId != state.MaterialIncarnationId ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(invocation.Model.Executable, state.Executable)) throw BpmnEmbeddingJson.Invalid();
        }
        var cancelled = state.TerminalResult is { } terminal && terminal.TryGetProperty("status", out var status) && status.GetString() == "Cancelled";
        // Formats 1-4 keep their original shapes. Embedded native service work and its cancellation
        // use 5/6 so an older driver refuses them instead of dropping parent or worker responsibility.
        if (result.FormatVersion is not (1 or 2 or 3 or 4 or 5 or 6) ||
            (result.FormatVersion is 3 or 5 or 6) != (result.NativeParent is not null) ||
            (result.FormatVersion is 4 or 6) != (result.Cancellations is not null) ||
            result.FormatVersion is 4 or 6 && (!cancelled || result.Cancellations!.Count == 0 ||
                (result.Cancellations.Any(item => !item.Settled) ? state.Lifecycle != RuntimeModelLifecycle.TerminalPendingSettlement :
                    result.NativeParent is null && state.Lifecycle != RuntimeModelLifecycle.TerminalSettled) ||
                result.Cancellations.Select(item => item.Origin.InvocationId).Distinct(StringComparer.Ordinal).Count() != result.Cancellations.Count ||
                result.Cancellations.Any(item => !item.Origin.MatchesModel(state) ||
                    item.Origin.ResultIntentKind != BpmnNativeWorkBoundary.ResultKind ||
                    item.Origin.WorkerExecutionId != BpmnNativeWorkBoundary.WorkerIdentity(state.Scope, state.ExecutionId,
                        item.Origin.InvocationId, item.Origin.AttemptId, item.Origin.WorkerExecutable).WorkflowExecutionId ||
                    !item.SourceOutboxItemId.EndsWith(":" + BpmnNativeWorkBoundary.CancellationIntentId(item.Origin), StringComparison.Ordinal) ||
                    item.NotAdmitted && (!item.Settled || result.NativeResults?.Any(value => item.Origin.IsEquivalentTo(value.Origin)) == true) ||
                    item.Settled && !item.NotAdmitted && result.NativeResults?.Any(value => item.Origin.IsEquivalentTo(value.Origin)) != true)) ||
            (cancelled ? result.Work.Count != 0 : result.Work.Count != result.State.ActiveWork.Count) ||
            result.Work.Select(item => item.InvocationId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Select(item => item.TokenId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Any(item => !result.State.ActiveWork.Any(active => active.TokenId == item.TokenId &&
                active.NodeId == item.BindingId && active.ElementId == item.ElementId && active.IterationId == item.IterationId) ||
                item.NativeWork is { } origin && (!origin.MatchesModel(state) || origin.InvocationId != item.InvocationId || origin.AttemptId != item.AttemptId ||
                    origin.BindingId != item.BindingId || origin.ResultIntentKind != BpmnNativeWorkBoundary.ResultKind ||
                    origin.WorkerExecutionId != BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, state.ExecutionId, item.InvocationId,
                        item.AttemptId, origin.WorkerExecutable).WorkflowExecutionId) || (result.FormatVersion is 2 or 5) != (item.NativeWork is not null)))
            throw new BpmnModelValidationException("The BPMN state and exact work inventory differ.");
        return result;
    }

    private static Document Apply(RuntimeModelExecutionContext context, Document prior, BpmnEvaluation evaluation, string commitId)
    {
        if (evaluation.Commands.Any(command => command is not BpmnHostCommand.StartWork &&
                (command is not BpmnHostCommand.CancelWorkSubtree cancel || !prior.Work.Any(work => work.InvocationId == cancel.Handle &&
                    work.ElementId == cancel.ElementId && work.NativeWork is not null && prior.NativeResults?.Any(result =>
                        result.Origin.IsEquivalentTo(work.NativeWork) && result.Status.IsTerminal()) == true))))
            throw new BpmnModelValidationException("The BPMN evaluation requires an unsupported direct-host binding.");
        var state = evaluation.State.Prune();
        var work = state.ActiveWork.Select(active =>
        {
            if (prior.Work.SingleOrDefault(item => item.TokenId == active.TokenId) is { } retained) return retained;
            if (!evaluation.Commands.OfType<BpmnHostCommand.StartWork>().Any(start => start.TokenId == active.TokenId && start.ElementId == active.ElementId && start.BindingRef == active.NodeId))
                throw new BpmnModelValidationException("An active BPMN work token has no matching start command.");
            var invocation = "bpmn-work:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, active.TokenId, active.ElementId, active.NodeId);
            var attempt = "attempt:" + BpmnModelIdentity.Create(invocation, "1");
            var binding = Definition(context.Material.Model!).ServiceBindings?.SingleOrDefault(item => item.BindingId == active.NodeId);
            RuntimeModelNativeWorkOrigin? origin = null;
            if (binding is not null)
            {
                var intentId = "bpmn-start-native:" + BpmnModelIdentity.Create(invocation, attempt);
                origin = new(1, context.Scope, context.WorkItem.WorkflowExecutionId, context.Material.Model!.Protocol,
                    context.Material.Identity, context.Material.IncarnationId, binding.BindingId, invocation, attempt, binding.Executable,
                    BpmnNativeWorkBoundary.WorkerIdentity(context.Scope, context.WorkItem.WorkflowExecutionId, invocation, attempt, binding.Executable).WorkflowExecutionId,
                    RuntimePostCommitOutboxItems.OutboxItemId(commitId, intentId), intentId, BpmnNativeWorkBoundary.ResultKind,
                    "native-result:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, invocation, attempt));
            }
            return new Work(active.ElementId, active.NodeId, active.TokenId, active.IterationId, invocation, attempt, origin);
        }).ToArray();
        return prior with { State = state, Work = work };
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

    private static BpmnModelDefinition Definition(RuntimeModelMaterial material) => material.Definition.Deserialize<BpmnModelDefinition>(Strict)
        ?? throw new BpmnModelValidationException("BPMN model definition is missing.");

    private static BpmnNativeWorkResult NativeResult(JsonElement payload) => payload.Deserialize<BpmnNativeWorkResult>(Strict)
        ?? throw new BpmnModelValidationException("Native work completion is missing.");

    internal sealed record Document(int FormatVersion, BpmnExecutionState State, IReadOnlyList<Work> Work, string? SourceReferenceId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BpmnNativeWorkResult>? NativeResults = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BpmnEmbeddedParent? NativeParent = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BpmnNativeWorkCancellation>? Cancellations = null);
    internal sealed record Work(string ElementId, string BindingId, string TokenId, string? IterationId, string InvocationId, string AttemptId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeModelNativeWorkOrigin? NativeWork = null);
    private sealed record Start(IReadOnlyDictionary<string, JsonElement>? Inputs = null, string? SourceReferenceId = null);
    private sealed record CompletionInput(string InvocationId, string AttemptId, string[]? Outcomes = null);
    private sealed record CancelInput(string? Reason = null);
}
