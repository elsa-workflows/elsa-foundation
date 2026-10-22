using System.Text.Json;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Workflows.Runtime.Activities;

/// <summary>Suspends the native activity until its pinned Rules invocation has a verified durable result.</summary>
[Activity(Icon = "Workflow")]
[ActivityOutcome("Done"), ActivityOutcome("Refused")]
[RefusesSecretBinding(nameof(Program), SecretBindingRefusalReason.FixedAtPublish)]
public sealed class RunRules(IWorkflowExecutionStateStore workflows, IWorkflowExecutableStore executables,
    IActivityExecutionStateStore activities, IRuleRunReader runs, IPersistenceAccessContextAccessor access)
    : StatefulActivity<RuleTerminalResult, RunRulesState, RunRulesCompletion>
{
    public const string ActivityType = "Elsa.Rules.Workflows.Runtime.Activities.RunRules";
    public const string PinMetadataKey = "rules.program.v1";

    [ActivityInput(Key = nameof(Program), Order = 0)]
    [Required]
    public string Program { get; set; } = string.Empty;

    [ActivityInput(Key = nameof(InitialFacts), Order = 1)]
    public string[] InitialFacts { get; set; } = [];

    [ActivityInput(Key = nameof(MaxRounds), Order = 2)]
    public int? MaxRounds { get; set; }

    protected override async ValueTask<ActivityTransition<RuleTerminalResult, RunRulesState>> ExecuteAsync(ActivityExecutionContext context)
    {
        var activity = await activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken)
            ?? throw RulesNativeBoundary.Invalid();
        var parent = await workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken)
            ?? throw RulesNativeBoundary.Invalid();
        var partition = access.Current.RequireScope();
        if (activity.Execution.ActivityType != ActivityType || activity.Execution.WorkflowExecutionId != context.WorkflowExecutionId ||
            activity.InvocationId != context.InvocationId || activity.Execution.ExecutableNodeId != context.ExecutableNodeId ||
            parent.WorkflowExecutionId != context.WorkflowExecutionId || parent.Partition?.Value != partition.Value)
            throw RulesNativeBoundary.Invalid();
        var executable = await executables.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken)
            ?? throw RulesNativeBoundary.Invalid();
        if (executable.Identity.ArtifactId != parent.PinnedExecutable.ArtifactId || executable.Identity.ArtifactHash != parent.PinnedExecutable.ArtifactHash ||
            !executable.NodesById.TryGetValue(context.ExecutableNodeId, out var node) || node.ActivityType != ActivityType ||
            !node.Metadata.TryGetValue(PinMetadataKey, out var serializedPin))
            throw RulesNativeBoundary.Invalid();
        if (node.InputBindings.GetValueOrDefault(nameof(Program)) is not
            { Source: RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: JsonValueKind.String } literal } || literal.GetString() != Program)
            throw RulesNativeBoundary.Invalid();
        var program = RuleWorkflowProgramPin.Parse(serializedPin, Program);
        var target = WorkflowExecutableResumeTarget.FindForNode(executable, node.ExecutableNodeId, RulesNativeBoundary.ResumeTargetKey);
        if (target is null || target.ExecutableNodeId != node.ExecutableNodeId || target.HandlerKey != nameof(ResumeAsync) ||
            !executable.ResumeTargets.TryGetValue(target.ResumeTargetId, out var declared) || declared != target)
            throw RulesNativeBoundary.Invalid();
        var facts = RuleRecordCodec.CanonicalFacts(InitialFacts.Select(atom => new RuleFact(atom)));
        // A malformed fact set larger than the vocabulary must reach semantic admission refusal,
        // rather than being misclassified as an explicitly invalid round limit.
        var rounds = MaxRounds ?? Math.Max(1, program.VocabularyCount - facts.Length + 1);
        var scope = new RuleScope(parent.TenantId, partition.Value);
        var request = new RunRulesRequest(scope, RulesNativeBoundary.RunId(scope, context.WorkflowExecutionId, context.InvocationId),
            program, facts, rounds, context.WorkflowExecutionId, context.InvocationId, executable.Identity.ArtifactId,
            executable.Identity.ArtifactHash, context.ExecutableNodeId, target.ResumeTargetId);
        // Invalid round limits still create a real native wait. Its checkpoint owns dispatch refusal;
        // there is no synthetic Rules invocation/row for an invalid common envelope.
        return Suspend(new(1, RulesNativeBoundary.Write(request)),
            [new ActivityTriggerRegistration<RunRulesCompletion>(RulesNativeBoundary.ResumeTargetKey,
                RulesNativeBoundary.StimulusType, RulesNativeBoundary.StimulusHash(request))]);
    }

    [ResumeTarget(RulesNativeBoundary.ResumeTargetKey)]
    protected override async ValueTask<ActivityTransition<RuleTerminalResult, RunRulesState>> ResumeAsync(
        ActivityResumeContext<RunRulesState, RunRulesCompletion> context)
    {
        var request = RulesNativeBoundary.ReadState(context.State);
        var completion = context.Trigger;
        if (completion.FormatVersion != 1 || context.WorkflowExecutionId != request.WorkflowExecutionId ||
            context.InvocationId != request.ActivityExecutionId || context.ExecutableNodeId != request.NodeId)
            throw RulesNativeBoundary.Invalid();
        access.Current.EnsureScope(new(request.Scope.PersistencePartition));
        RulesNativeBoundary.EnsureAddress(request, completion.Destination, context.RegistrationId);
        var native = await activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken)
            ?? throw RulesNativeBoundary.Invalid();
        if (RulesNativeBoundary.ReadAddress(native) != completion.Destination ||
            native.TriggerDeliveries?.SingleOrDefault(item => item.DeliveryId == context.DeliveryId) is not { } receipt ||
            receipt.Status != ActivityTriggerDeliveryStatus.Consumed || receipt.RegistrationId != context.RegistrationId ||
            receipt.ProviderId != RulesNativeBoundary.ProviderId || receipt.PayloadType != RulesNativeBoundary.CompletionType ||
            receipt.DeduplicationKey != RulesNativeBoundary.DeliveryDeduplicationKey(completion.Destination, completion.DeliveryId))
            throw RulesNativeBoundary.Invalid();
        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        if (RulesNativeBoundary.HasInvalidRoundLimit(request))
        {
            if (completion.Result is not null || completion.InvalidCode != RulesNativeBoundary.InvalidRoundLimit ||
                completion.DeliveryId != RulesNativeBoundary.InvalidDeliveryId(request, completion.Destination) ||
                (await runs.FindAsync(identity, context.CancellationToken)).Disposition != RuleRunReadDisposition.NotFound)
                throw RulesNativeBoundary.Invalid();
            return Fault(new(completion.InvalidCode, "The Rules round limit must be positive and within the finite limit."));
        }
        if (completion.InvalidCode is not null || completion.Result is not { } json)
            throw RulesNativeBoundary.Invalid();
        var resultBytes = System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(json);
        if (resultBytes.Length > RuleLimits.MaxEnvelopeBytes) throw RulesNativeBoundary.Invalid();
        var result = RuleRecordCodec.ReadTerminal(resultBytes.ToArray());
        var invocation = RulesNativeBoundary.Invocation(request, completion.Destination);
        var retained = await runs.FindAsync(identity, context.CancellationToken);
        var terminal = await runs.ReadTerminalAsync(identity, context.CancellationToken);
        if (retained.View?.Fingerprint != RuleRecordCodec.Fingerprint(invocation) || terminal.Result is null ||
            !RuleRecordCodec.TerminalBytes(terminal.Result).AsSpan().SequenceEqual(RuleRecordCodec.TerminalBytes(result)))
            throw RulesNativeBoundary.Invalid();
        _ = RuleRecordCodec.DeliveryBytes(completion.DeliveryId, completion.Destination, result);
        return result.Kind switch
        {
            RuleTerminalKind.Succeeded => Complete(result),
            RuleTerminalKind.Refused => Complete(result, "Refused"),
            RuleTerminalKind.Faulted => Fault(new(result.Diagnostic?.Code ?? "rules.run-faulted", "The Rules run failed.",
                faultType: RulesNativeBoundary.TerminalMarker(completion, result))),
            RuleTerminalKind.Cancelled => Cancel("The Rules run was cancelled."),
            _ => throw RulesNativeBoundary.Invalid()
        };
    }
}
