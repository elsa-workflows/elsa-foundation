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
    BpmnNativeWorkBoundary? nativeWork = null, BpmnEmbeddingBoundary? embedding = null,
    BpmnDurationTimerDelivery? durationTimers = null) : IRuntimeExecutionProtocolDriver
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
        try { _ = Graph(material); }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"BPMN material '{material.Identity.ArtifactId}' has invalid definition JSON.", exception);
        }
        if (material.Profile == BpmnModelProtocol.NativeWorkProfile) NativeWork.EnsureQualified();
        return ValueTask.CompletedTask;
    }

    public async ValueTask ValidateInputAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Command.InputKind == "start" && context.State is null && !options.AdmitNewExecutions)
            throw new RuntimeSchedulerWorkUnavailableException("bpmn.admission-disabled", "New BPMN execution admission is disabled.");
        try { await ValidateShapeAndReplayAsync(context, cancellationToken); }
        catch (JsonException exception) { throw InvalidCommandJson(context, exception); }
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
                if (context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile && Definition(context.Material.Model).HumanBindings.Count == 0)
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
            case "timer-elapsed":
                await (durationTimers ?? throw new NotSupportedException("Duration timer delivery is not installed."))
                    .ValidateAsync(context.State ?? throw new BpmnModelValidationException("A timer cannot start an execution."), context.Command, cancellationToken);
                break;
            case "native-start-retired":
                await NativeWork.ValidateRetirementAsync(context, context.Command.Payload.Deserialize<BpmnNativeWorkCancellation>(Strict)
                    ?? throw new BpmnModelValidationException("Native start retirement is missing."), cancellationToken);
                break;
            case "retry-service":
                if (context.State is null) throw new BpmnModelValidationException("The BPMN execution has not started.");
                _ = Retry(context.Command.Payload);
                break;
            default: throw new BpmnModelValidationException("The BPMN input kind is unsupported.");
        }
        var prior = await Reader.FindInputAsync(context.WorkItem.WorkflowExecutionId, context.Command.InputId, cancellationToken);
        if (prior is not null && prior.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(context.Command))
            throw new RuntimeCheckpointReplayConflictException(context.Command.InputId);
    }

    public async ValueTask ExecuteAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default)
    {
        try { await ExecuteCoreAsync(context, cancellationToken); }
        catch (JsonException exception) { throw InvalidCommandJson(context, exception); }
    }

    private static BpmnModelValidationException InvalidCommandJson(RuntimeModelExecutionContext context, JsonException exception) =>
        new($"BPMN execution '{context.WorkItem.WorkflowExecutionId}', input '{context.Command.InputId}' ({context.Command.InputKind}) " +
            $"has invalid owned JSON for material '{context.Material.Identity.ArtifactId}'.", exception);

    private async ValueTask ExecuteCoreAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken)
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
        var definition = Definition(context.Material.Model!);
        var timedFrame = definition.FormatVersion == 4;
        var policyFrame = definition.FormatVersion is 3 or 4;
        var current = context.State;
        var isEmbeddedInput = command.InputKind is "start-embedded" or "cancel-embedded" or "native-parent-settled";
        var embedded = isEmbeddedInput ? await Embedding.ValidateInputAsync(context, cancellationToken) : ((BpmnEmbeddedInvocation Invocation, StimulusResumeRecipient? Wait)?)null;
        var document = current is null ? new Document(timedFrame ? 11 : policyFrame ? 7 : context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile ? 2 : 1,
            new BpmnExecutionState(), [], Failures: policyFrame ? [] : null) : Read(current);
        if (current is null && embedded is { } parent)
            document = document with { FormatVersion = timedFrame ? 12 : policyFrame ? 8 : context.Material.Model!.Profile == BpmnModelProtocol.NativeWorkProfile ? 5 : 3,
                NativeParent = new(BpmnEmbeddingJson.Write(parent.Invocation)) };
        if (current is null && command.InputKind == "start")
            document = document with { SourceReferenceId = command.Payload.Deserialize<Start>(Strict)!.SourceReferenceId };
        if (policyFrame != (document.Failures is not null))
            throw new BpmnModelValidationException("The model definition and retained service policy frame differ.");
        BpmnEvaluation? evaluation = null;
        string? rejection = null;
        CompletionInput? completion = null;
        BpmnNativeWorkResult? nativeResult = null;
        var cancelled = false;
        var settledCancellation = false;
        var held = false;
        var retried = false;
        var commitId = "model-input:" + BpmnModelIdentity.Create(context.Scope.Value, executionId, command.InputId);
        DateTimeOffset? timerDecisionAt = timedFrame ? clock.GetUtcNow() : null;
        if (timerDecisionAt is { } decisionAt && current is not null && decisionAt < current.UpdatedAt) timerDecisionAt = current.UpdatedAt;
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
                document = CancelRemainingWork(document, commitId, command.InputId, timedFrame, policyFrame);
            }
        }
        else if (command.InputKind == "retry-service")
        {
            var retry = Retry(command.Payload);
            completion = new(retry.InvocationId, retry.FailedAttemptId);
            var failure = document.Failures?.SingleOrDefault(item => item.IncidentId == retry.IncidentId);
            var work = document.Work.SingleOrDefault(item => item.InvocationId == retry.InvocationId && item.AttemptId == retry.FailedAttemptId);
            if (current!.TerminalResult is not null) rejection = "execution-terminal";
            else if (!policyFrame) rejection = "retry-not-enabled";
            else if (current.Lifecycle != RuntimeModelLifecycle.WaitingIncident || failure is not { Status: "Open" } || work is null ||
                failure.InvocationId != retry.InvocationId || failure.FailedAttemptId != retry.FailedAttemptId) rejection = "incident-not-current";
            else if (definition.ServiceBindings?.SingleOrDefault(item => item.BindingId == work.BindingId)?.FaultPolicy is not
                { Disposition: BpmnServiceFaultDisposition.HoldForRetry } policy || failure.MaxAttempts != policy.MaxAttempts) rejection = "retry-not-enabled";
            else if (work.AttemptNumber >= policy.MaxAttempts) rejection = "attempt-budget-exhausted";
            else
            {
                var result = document.NativeResults!.Single(item => item.Origin.ResultInputId == failure.ResultInputId);
                await NativeWork.ValidateRetainedResultAsync(current, result, cancellationToken);
                var number = work.AttemptNumber!.Value + 1;
                var attempt = "attempt:" + BpmnModelIdentity.Create(work.InvocationId, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var binding = definition.ServiceBindings!.Single(item => item.BindingId == work.BindingId);
                var replacement = work with { AttemptId = attempt, AttemptNumber = number,
                    NativeWork = NativeOrigin(context, binding, work.InvocationId, attempt, commitId) };
                document = document with { Work = document.Work.Select(item => item == work ? replacement : item).ToArray(),
                    Failures = document.Failures!.Select(item => item == failure ? item with
                        { Status = "Retried", RetryAttemptId = attempt, ResolutionInputId = command.InputId } : item).ToArray() };
                retried = true;
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
            var timer = command.InputKind == "timer-elapsed" ? command.Payload.Deserialize<BpmnDurationTimer>(Strict) : null;
            completion = timer is not null ? new(timer.InvocationId, timer.AttemptId) : nativeResult is null ? Completion(command.Payload) :
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
            else if (work is null) rejection = timer is null ? "obsolete-human-work" : "obsolete-timer-work";
            else if (timer is null && nativeResult is null && (work.NativeWork is not null || work.Timer is not null)) rejection = "not-human-work";
            else if (timer is not null && (work.Timer is null || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(work.Timer, Web), command.Payload))) rejection = "obsolete-timer-work";
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
                        document = CancelRemainingWork(document, commitId, command.InputId, timedFrame, policyFrame);
                    }
                    else if (nativeResult.Status == WorkflowExecutionStatus.Faulted || nativeResult.Completion?.OutcomeKey == "BusinessError")
                    {
                        var binding = definition.ServiceBindings?.Single(item => item.BindingId == work.BindingId);
                        if (nativeResult.Status == WorkflowExecutionStatus.Faulted && binding?.FaultPolicy is { } policy)
                        {
                            held = policy.Disposition == BpmnServiceFaultDisposition.HoldForRetry && work.AttemptNumber < policy.MaxAttempts;
                            var failedAt = clock.GetUtcNow();
                            if (failedAt < current.UpdatedAt) failedAt = current.UpdatedAt;
                            var failure = new ServiceFailure("bpmn-incident:" + BpmnModelIdentity.Create(executionId, work.InvocationId, work.AttemptId),
                                work.ElementId, work.BindingId, work.TokenId, work.IterationId, work.InvocationId, work.AttemptId,
                                work.AttemptNumber!.Value, policy.MaxAttempts, failedAt, JsonSerializer.SerializeToElement(document.State, Web),
                                nativeResult.Origin.ResultInputId, held ? "Open" : "Final", ResolutionInputId: held ? null : command.InputId);
                            document = document with { Failures = [.. document.Failures!, failure] };
                        }
                        if (!held) evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkFaulted(
                            new(graph, document.State, snapshot, work.BindingId, work.InvocationId,
                                binding is null ? "Native event work failed." : "Native service work failed.")
                            { WorkTokenId = work.TokenId, FaultedIterationId = work.IterationId,
                                ErrorCode = nativeResult.Status == WorkflowExecutionStatus.Completed ? binding?.BusinessErrorCode : null }));
                    }
                    else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkCompleted(
                        new(graph, document.State, snapshot, work.BindingId, work.InvocationId,
                            [nativeResult.Completion!.OutcomeKey], work.IterationId) { WorkTokenId = work.TokenId }));
                }
                else evaluation = Evaluate(() => BpmnEvaluationComponent.Interpreter.OnWorkCompleted(
                    new(graph, document.State, snapshot, work.BindingId, work.InvocationId, ["Done"], work.IterationId) { WorkTokenId = work.TokenId }));
            }
        }

        var lifecycle = held ? RuntimeModelLifecycle.WaitingIncident : retried ? RuntimeModelLifecycle.Running : current?.Lifecycle ?? RuntimeModelLifecycle.Running;
        JsonElement? terminal = current?.TerminalResult;
        if (cancelled)
        {
            lifecycle = RuntimeModelLifecycle.TerminalSettled;
            terminal = command.InputKind == "cancel"
                ? JsonSerializer.SerializeToElement(new { status = "Cancelled", command.Payload.Deserialize<CancelInput>(Strict)!.Reason }, Web)
                : JsonSerializer.SerializeToElement(new { status = "Cancelled" }, Web);
        }
        if (evaluation is not null)
        {
            document = Apply(context, document, evaluation, commitId, timerDecisionAt ?? clock.GetUtcNow());
            if (timedFrame && document.Failures is not null)
                document = document with { Failures = document.Failures.Select(failure => failure.Status == "Open" &&
                    !document.Work.Any(work => work.InvocationId == failure.InvocationId && work.AttemptId == failure.FailedAttemptId)
                        ? failure with { Status = "Retired", ResolutionInputId = command.InputId } : failure).ToArray() };
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
        else if (timedFrame && terminal is null)
            lifecycle = document.Failures?.Any(failure => failure.Status == "Open") == true ? RuntimeModelLifecycle.WaitingIncident : RuntimeModelLifecycle.Running;
        var now = timerDecisionAt ?? clock.GetUtcNow();
        if (current is not null && now < current.UpdatedAt) now = current.UpdatedAt;
        var revision = rejection is null ? checked((current?.Revision ?? 0) + 1) : current!.Revision;
        var state = rejection is not null ? current! : new RuntimeModelExecutionState(1, context.Scope, executionId, command.TenantId,
            context.Material.Model!.Protocol, context.Material.Identity, context.Material.IncarnationId, revision, lifecycle,
            command.Authority, current?.CreatedAt ?? now, now, JsonSerializer.SerializeToElement(document, Web), terminal);
        if (policyFrame && rejection is null) _ = Read(state);
        var receipt = new RuntimeModelInputReceipt(context.Scope, executionId, command.InputId, command.InputKind,
            fingerprint, revision, now, completion?.InvocationId ?? embedded?.Invocation.NativeInvocationId,
            completion?.AttemptId ?? embedded?.Invocation.NativeAttemptId,
            JsonSerializer.SerializeToElement(new { status = rejection is null ? "Accepted" : "Rejected", code = rejection,
                revision, lifecycle = lifecycle.ToString() }, Web));
        var starts = rejection is null ? document.Work.Where(work => work.NativeWork is not null &&
            !(current is null ? [] : Read(current).Work).Any(old => old.InvocationId == work.InvocationId && old.AttemptId == work.AttemptId))
            .Select(work => new RuntimePostCommitIntent(work.NativeWork!.SourceIntentId, executionId, BpmnNativeWorkBoundary.StartKind,
                now, null, work.NativeWork.SourceIntentId, JsonSerializer.SerializeToElement(work.NativeWork, Web))).ToArray() : [];
        var intents = starts.ToList();
        if (rejection is null)
            intents.AddRange(document.Work.Where(work => work.Timer is not null &&
                !(current is null ? [] : Read(current).Work).Any(old => old.InvocationId == work.InvocationId && old.AttemptId == work.AttemptId))
                .Select(work => new RuntimePostCommitIntent(work.Timer!.IntentId, executionId, BpmnDurationTimer.Kind,
                    now, null, work.Timer.IntentId, JsonSerializer.SerializeToElement(work.Timer, Web), notBefore: work.Timer.DueAt)));
        if (rejection is null)
            intents.AddRange((document.Cancellations ?? []).Where(item =>
                current is null || Read(current).Cancellations?.Any(old => old.Origin.IsEquivalentTo(item.Origin)) != true).Select(item => new RuntimePostCommitIntent(
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

    private static Document CancelRemainingWork(Document document, string commitId, string inputId, bool timedFrame, bool policyFrame)
    {
        var cancellations = document.Work.Where(item => item.NativeWork is not null &&
            document.NativeResults?.Any(result => result.Origin.IsEquivalentTo(item.NativeWork)) != true).Select(item =>
        {
            var origin = item.NativeWork!;
            return new BpmnNativeWorkCancellation(origin, RuntimePostCommitOutboxItems.OutboxItemId(commitId,
                BpmnNativeWorkBoundary.CancellationIntentId(origin)));
        }).ToArray();
        return document with { Work = [], FormatVersion = cancellations.Length > 0
                ? timedFrame ? document.FormatVersion : policyFrame ? document.NativeParent is null ? 9 : 10 : document.NativeParent is null ? 4 : 6 : document.FormatVersion,
            Cancellations = cancellations.Length > 0 ? cancellations : document.Cancellations,
            Failures = document.Failures?.Select(failure => failure.Status == "Open"
                ? failure with { Status = "Retired", ResolutionInputId = inputId } : failure).ToArray() };
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
        var timed = envelope.FormatVersion == 4;
        var events = envelope.FormatVersion == 5;
        var dependencies = (envelope.ServiceBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))
            .Concat((envelope.NativeEventBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))).ToArray();
        if ((native ? envelope.FormatVersion is not (2 or 3 or 4 or 5) : envelope.FormatVersion != 1) || material.Dependencies.Count != dependencies.Length ||
            dependencies.Any(binding => !material.Dependencies.Any(dependency => dependency.BindingId == binding.BindingId &&
                WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dependency.Executable, binding.Executable))))
            throw new BpmnModelValidationException("The native work inventory differs from the model's complete retained dependency pins.");
        var process = envelope.Process.Deserialize<BpmnProcessDefinition>(Strict) ?? throw new BpmnModelValidationException("BPMN process definition is missing.");
        if (process.Variables.Count != 0 || process.IsTransaction || process.Elements.Any(item =>
                (events ? item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.ParallelGateway or BpmnElementTypes.IntermediateCatchEvent) :
                    timed ? item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.ServiceTask or BpmnElementTypes.UserTask or BpmnElementTypes.ParallelGateway or BpmnElementTypes.BoundaryEvent) :
                    native ? item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.ServiceTask or BpmnElementTypes.BoundaryEvent) :
                    item.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.UserTask or BpmnElementTypes.ParallelGateway)) ||
                (events && item.ElementType == BpmnElementTypes.IntermediateCatchEvent ? !IsSignalCatch(item, envelope) :
                    item.ElementType == BpmnElementTypes.BoundaryEvent ? !native ||
                    !(IsCodedServiceErrorBoundary(item, process) || timed && IsDurationBoundary(item, process, envelope)) : item.EventDefinitions.Count != 0) ||
                item.LoopCharacteristics is not null || item.IsForCompensation || item.TriggeredByEvent ||
                item.ListenerBindingRef is not null || item.IsTransaction || item.Script is not null || item.Properties.Count != 0))
            throw new BpmnModelValidationException("The BPMN definition contains elements or events outside its declared hosting profile.");
        if (native && !timed && !events && (envelope.HumanBindings.Count != 0 || envelope.ServiceBindings!.Count != 1 ||
            process.Elements.Count(item => item.ElementType == BpmnElementTypes.ServiceTask) != 1))
            throw new BpmnModelValidationException("The current native service profile requires one closed published native worker, plain start/end events and optional coded error boundaries.");
        if (timed && (process.Elements.Any(item => item.ElementType == BpmnElementTypes.UserTask && !envelope.HumanBindings.Contains(item.BindingRef!, StringComparer.Ordinal) ||
                item.ElementType == BpmnElementTypes.ServiceTask && envelope.ServiceBindings?.Any(binding => binding.BindingId == item.BindingRef) != true) ||
                (envelope.ServiceBindings?.Count ?? 0) > 1 || envelope.ServiceBindings is { Count: > 0 } && envelope.HumanBindings.Count != 0))
            throw new BpmnModelValidationException("The duration profile requires explicit human work or one closed native service, with exact timer bindings.");
        if ((envelope.FormatVersion == 3 || timed && envelope.ServiceBindings is { Count: > 0 }) && (process.Elements.Count(item => item.ElementType == BpmnElementTypes.StartEvent) != 1 ||
            process.SequenceFlows.GroupBy(flow => flow.SourceRef, StringComparer.Ordinal).Any(group => group.Count() > 1)))
            throw new BpmnModelValidationException("The service fault policy profile requires one start and no concurrent outgoing branches.");
        if (timed && envelope.ServiceBindings is { Count: > 0 } && process.SequenceFlows.Any(flow =>
                process.Elements.Any(item => item.ElementId == flow.SourceRef && item.ElementType is BpmnElementTypes.ServiceTask or BpmnElementTypes.BoundaryEvent) &&
                !process.Elements.Any(item => item.ElementId == flow.TargetRef && item.ElementType == BpmnElementTypes.EndEvent)))
            throw new BpmnModelValidationException("The initial timed native-service profile routes its worker and boundary outcomes directly to end events.");
        try { return BpmnGraph.Build(process, envelope.HumanBindings.Concat((envelope.ServiceBindings ?? []).Select(binding => binding.BindingId))
            .Concat(envelope.TimerBindings ?? [])
            .Concat((envelope.NativeEventBindings ?? []).Select(binding => binding.BindingId))
            .Select(id => new BpmnBoundWork(id)).ToArray(), native ? BpmnHostCapabilities.SubtreeCancellation : BpmnHostCapabilities.None); }
        catch (BpmnExecutionException exception) { throw new BpmnModelValidationException(exception.Message, exception); }
    }

    private static bool IsSignalCatch(BpmnElement element, BpmnModelDefinition definition) =>
        element.EventDefinitions.Count == 1 && element.EventDefinitions.Single() is { Type: BpmnEventDefinitionTypes.Signal } signal &&
        signal.Properties.Count == 1 && signal.Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name) && !string.IsNullOrWhiteSpace(name) && name == name.Trim() &&
        definition.NativeEventBindings?.Any(binding => binding.BindingId == element.BindingRef) == true;

    private static bool IsDurationBoundary(BpmnElement element, BpmnProcessDefinition process, BpmnModelDefinition definition)
    {
        if (element.EventDefinitions.Count != 1 || element.EventDefinitions.Single().Type != BpmnEventDefinitionTypes.Timer ||
            element.BindingRef is null || definition.TimerBindings?.Contains(element.BindingRef, StringComparer.Ordinal) != true ||
            !process.Elements.Any(item => item.ElementId == element.AttachedToRef && item.ElementType is BpmnElementTypes.UserTask or BpmnElementTypes.ServiceTask)) return false;
        _ = BpmnDurationTimer.Duration(element);
        return true;
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
        try { return ReadCore(state); }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"BPMN execution '{state.ExecutionId}' has invalid persisted model JSON.", exception);
        }
    }

    private static Document ReadCore(RuntimeModelExecutionState state)
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
        // Formats 1-6 keep their original wire. Policy-bearing work uses 7/8 (cancel obligations 9/10),
        // so an older driver refuses rather than discarding a retained failure or physical attempt budget.
        var timedFrame = result.FormatVersion is 11 or 12;
        var policyFrame = result.FormatVersion is 7 or 8 or 9 or 10 or 11 or 12;
        if (result.FormatVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12) ||
            (result.FormatVersion is 3 or 5 or 6 or 8 or 10 or 12) != (result.NativeParent is not null) ||
            !timedFrame && (result.FormatVersion is 4 or 6 or 9 or 10) != (result.Cancellations is not null) ||
            policyFrame != (result.Failures is not null) ||
            result.Cancellations is not null && (!timedFrame && !cancelled || result.Cancellations.Count == 0 ||
                (result.Cancellations.Any(item => !item.Settled) ? state.Lifecycle != RuntimeModelLifecycle.TerminalPendingSettlement :
                    result.NativeParent is null && state.Lifecycle != RuntimeModelLifecycle.TerminalSettled) ||
                result.Cancellations.Select(item => item.Origin.InvocationId).Distinct(StringComparer.Ordinal).Count() != result.Cancellations.Count ||
                result.Cancellations.Any(item => !item.Origin.MatchesModel(state) ||
                    item.Origin.ResultIntentKind != BpmnNativeWorkBoundary.ResultKind ||
                    item.Origin.WorkerExecutionId != BpmnNativeWorkBoundary.WorkerIdentity(state.Scope, state.ExecutionId,
                        item.Origin.InvocationId, item.Origin.AttemptId, item.Origin.WorkerExecutable).WorkflowExecutionId ||
                    !item.SourceOutboxItemId.EndsWith(":" + BpmnNativeWorkBoundary.CancellationIntentId(item.Origin), StringComparison.Ordinal) ||
                    item.NotAdmitted && (!item.Settled || result.NativeResults?.Any(value => item.Origin.IsEquivalentTo(value.Origin)) == true) ||
                    item.Settled && !item.NotAdmitted && result.NativeResults?.Any(value => item.Origin.IsEquivalentTo(value.Origin)) != true ||
                    result.Work.Any(work => item.Origin.IsEquivalentTo(work.NativeWork)))) ||
            (cancelled ? result.Work.Count != 0 : result.Work.Count != result.State.ActiveWork.Count) ||
            result.Work.Select(item => item.InvocationId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Select(item => item.TokenId).Distinct(StringComparer.Ordinal).Count() != result.Work.Count ||
            result.Work.Any(item => !result.State.ActiveWork.Any(active => active.TokenId == item.TokenId &&
                active.NodeId == item.BindingId && active.ElementId == item.ElementId && active.IterationId == item.IterationId) ||
                item.NativeWork is { } origin && (!origin.MatchesModel(state) || origin.InvocationId != item.InvocationId || origin.AttemptId != item.AttemptId ||
                    origin.BindingId != item.BindingId || origin.ResultIntentKind != BpmnNativeWorkBoundary.ResultKind ||
                    origin.WorkerExecutionId != BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, state.ExecutionId, item.InvocationId,
                        item.AttemptId, origin.WorkerExecutable).WorkflowExecutionId) || !timedFrame && (result.FormatVersion is 2 or 5 or 7 or 8) != (item.NativeWork is not null) ||
                item.Timer is { } timer && (!timedFrame || item.NativeWork is not null || !timer.Matches(state) ||
                    timer.InvocationId != item.InvocationId || timer.AttemptId != item.AttemptId || timer.BindingId != item.BindingId) ||
                policyFrame != (item.AttemptNumber is not null) || item.AttemptNumber is < 1 or > BpmnServiceFaultPolicy.MaximumAttempts ||
                item.AttemptNumber is { } number && item.AttemptId != "attempt:" + BpmnModelIdentity.Create(item.InvocationId,
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            throw new BpmnModelValidationException("The BPMN state and exact work inventory differ.");
        if (result.Failures is { } failures)
        {
            if (failures.Select(item => item.IncidentId).Distinct(StringComparer.Ordinal).Count() != failures.Count ||
                failures.Select(item => item.ResultInputId).Distinct(StringComparer.Ordinal).Count() != failures.Count)
                throw new BpmnModelValidationException("The retained service failure inventory contains duplicates.");
            foreach (var failure in failures)
            {
                var failedResult = result.NativeResults?.SingleOrDefault(item => item.Origin.ResultInputId == failure.ResultInputId);
                var snapshot = failure.SemanticState.ValueKind == JsonValueKind.Object
                    ? failure.SemanticState.Deserialize<BpmnExecutionState>(Web) : null;
                if (failedResult is null || failedResult.Status != WorkflowExecutionStatus.Faulted || failedResult.FaultCode is not null && string.IsNullOrWhiteSpace(failedResult.FaultCode) ||
                    !failedResult.Origin.MatchesModel(state) || failedResult.Origin.InvocationId != failure.InvocationId || failedResult.Origin.AttemptId != failure.FailedAttemptId ||
                    failedResult.Origin.BindingId != failure.BindingId || failure.IncidentId != "bpmn-incident:" + BpmnModelIdentity.Create(state.ExecutionId, failure.InvocationId, failure.FailedAttemptId) ||
                    failure.AttemptNumber < 1 || failure.MaxAttempts is < 1 or > BpmnServiceFaultPolicy.MaximumAttempts || failure.AttemptNumber > failure.MaxAttempts ||
                    failure.Status is not ("Open" or "Retried" or "Retired" or "Final") ||
                    (failure.Status == "Open") != (failure.ResolutionInputId is null) || (failure.Status == "Retried") != (failure.RetryAttemptId is not null) ||
                    snapshot?.ActiveWork.Any(item => item.TokenId == failure.TokenId && item.ElementId == failure.ElementId && item.NodeId == failure.BindingId && item.IterationId == failure.IterationId) != true ||
                    failure.Status == "Open" && (state.Lifecycle != RuntimeModelLifecycle.WaitingIncident || state.TerminalResult is not null ||
                        failure.AttemptNumber >= failure.MaxAttempts || !result.Work.Any(work => work.InvocationId == failure.InvocationId && work.AttemptId == failure.FailedAttemptId && work.AttemptNumber == failure.AttemptNumber) ||
                        !JsonElement.DeepEquals(failure.SemanticState, JsonSerializer.SerializeToElement(result.State, Web))))
                    throw new BpmnModelValidationException("A retained service failure lost its original attempt or active semantic snapshot.");
            }
            if ((state.Lifecycle == RuntimeModelLifecycle.WaitingIncident) != failures.Any(item => item.Status == "Open"))
                throw new BpmnModelValidationException("The model incident lifecycle differs from its current held failure.");
        }
        return result;
    }

    private static Document Apply(RuntimeModelExecutionContext context, Document prior, BpmnEvaluation evaluation, string commitId, DateTimeOffset at)
    {
        var definition = Definition(context.Material.Model!);
        var timed = definition.FormatVersion == 4;
        if (evaluation.Commands.Any(command => command is not BpmnHostCommand.StartWork &&
                (command is not BpmnHostCommand.CancelWorkSubtree cancel || !prior.Work.Any(work => work.InvocationId == cancel.Handle &&
                    work.ElementId == cancel.ElementId && (timed || work.NativeWork is not null && prior.NativeResults?.Any(result =>
                        result.Origin.IsEquivalentTo(work.NativeWork) && result.Status.IsTerminal()) == true)))))
            throw new BpmnModelValidationException("The BPMN evaluation requires an unsupported direct-host binding.");
        if (timed)
        {
            var cancellations = (prior.Cancellations ?? []).ToList();
            foreach (var cancel in evaluation.Commands.OfType<BpmnHostCommand.CancelWorkSubtree>())
            {
                var origin = prior.Work.Single(work => work.InvocationId == cancel.Handle && work.ElementId == cancel.ElementId).NativeWork;
                if (origin is null || prior.NativeResults?.Any(result => result.Origin.IsEquivalentTo(origin)) == true ||
                    cancellations.Any(item => item.Origin.IsEquivalentTo(origin))) continue;
                cancellations.Add(new(origin, RuntimePostCommitOutboxItems.OutboxItemId(commitId, BpmnNativeWorkBoundary.CancellationIntentId(origin))));
            }
            if (cancellations.Count > 0) prior = prior with { Cancellations = cancellations };
        }
        var state = evaluation.State.Prune();
        var work = state.ActiveWork.Select(active =>
        {
            if (prior.Work.SingleOrDefault(item => item.TokenId == active.TokenId) is { } retained) return retained;
            if (!evaluation.Commands.OfType<BpmnHostCommand.StartWork>().Any(start => start.TokenId == active.TokenId && start.ElementId == active.ElementId && start.BindingRef == active.NodeId))
                throw new BpmnModelValidationException("An active BPMN work token has no matching start command.");
            var invocation = "bpmn-work:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, active.TokenId, active.ElementId, active.NodeId);
            var attempt = "attempt:" + BpmnModelIdentity.Create(invocation, "1");
            var binding = definition.ServiceBindings?.SingleOrDefault(item => item.BindingId == active.NodeId);
            var eventBinding = definition.NativeEventBindings?.SingleOrDefault(item => item.BindingId == active.NodeId);
            RuntimeModelNativeWorkOrigin? origin = null;
            if (binding is not null)
            {
                origin = NativeOrigin(context, binding, invocation, attempt, commitId);
            }
            else if (eventBinding is not null)
                origin = NativeOrigin(context, eventBinding.BindingId, eventBinding.Executable, invocation, attempt, commitId);
            BpmnDurationTimer? timer = null;
            if (definition.TimerBindings?.Contains(active.NodeId, StringComparer.Ordinal) == true)
            {
                var element = definition.Process.Deserialize<BpmnProcessDefinition>(Strict)!.Elements.Single(item => item.ElementId == active.ElementId);
                DateTimeOffset due;
                try { due = at.ToUniversalTime().Add(BpmnDurationTimer.Duration(element)); }
                catch (ArgumentOutOfRangeException exception) { throw new BpmnModelValidationException("The duration timer deadline is not representable.", exception); }
                var intent = "bpmn-duration:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, invocation, attempt);
                timer = new(1, context.Scope, context.WorkItem.WorkflowExecutionId, context.Material.Identity, context.Material.IncarnationId,
                    active.NodeId, invocation, attempt, due, RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent));
            }
            return new Work(active.ElementId, active.NodeId, active.TokenId, active.IterationId, invocation, attempt, origin,
                definition.FormatVersion is 3 or 4 ? 1 : null, timer);
        }).ToArray();
        return prior with { State = state, Work = work };
    }

    private static RuntimeModelNativeWorkOrigin NativeOrigin(RuntimeModelExecutionContext context, BpmnNativeServiceBinding binding,
        string invocation, string attempt, string commitId)
        => NativeOrigin(context, binding.BindingId, binding.Executable, invocation, attempt, commitId);

    private static RuntimeModelNativeWorkOrigin NativeOrigin(RuntimeModelExecutionContext context, string bindingId, WorkflowExecutableIdentity executable,
        string invocation, string attempt, string commitId)
    {
        var intent = "bpmn-start-native:" + BpmnModelIdentity.Create(invocation, attempt);
        return new(1, context.Scope, context.WorkItem.WorkflowExecutionId, context.Material.Model!.Protocol,
            context.Material.Identity, context.Material.IncarnationId, bindingId, invocation, attempt, executable,
            BpmnNativeWorkBoundary.WorkerIdentity(context.Scope, context.WorkItem.WorkflowExecutionId, invocation, attempt, executable).WorkflowExecutionId,
            RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent), intent, BpmnNativeWorkBoundary.ResultKind,
            "native-result:" + BpmnModelIdentity.Create(context.Scope.Value, context.WorkItem.WorkflowExecutionId, invocation, attempt));
    }

    private static RetryInput Retry(JsonElement payload)
    {
        var result = payload.Deserialize<RetryInput>(Strict) ?? throw new BpmnModelValidationException("Service retry is missing.");
        if (new[] { result.IncidentId, result.InvocationId, result.FailedAttemptId }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512))
            throw new BpmnModelValidationException("Service retry requires its exact incident, invocation and failed attempt.");
        return result;
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
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BpmnNativeWorkCancellation>? Cancellations = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ServiceFailure>? Failures = null);
    internal sealed record Work(string ElementId, string BindingId, string TokenId, string? IterationId, string InvocationId, string AttemptId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeModelNativeWorkOrigin? NativeWork = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AttemptNumber = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BpmnDurationTimer? Timer = null);
    internal sealed record ServiceFailure(string IncidentId, string ElementId, string BindingId, string TokenId, string? IterationId,
        string InvocationId, string FailedAttemptId, int AttemptNumber, int MaxAttempts, DateTimeOffset FailedAt, JsonElement SemanticState,
        string ResultInputId, string Status, string? RetryAttemptId = null, string? ResolutionInputId = null);
    private sealed record Start(IReadOnlyDictionary<string, JsonElement>? Inputs = null, string? SourceReferenceId = null);
    private sealed record CompletionInput(string InvocationId, string AttemptId, string[]? Outcomes = null);
    private sealed record CancelInput(string? Reason = null);
    private sealed record RetryInput(string IncidentId, string InvocationId, string FailedAttemptId);
}
