using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Decisions.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Decisions.Workflows.Runtime.Activities;

/// <summary>Evaluates exact published decision content under the caller's ordinary execution/checkpoint lifecycle.</summary>
[Activity(Icon = "TableProperties")]
[ActivityOutcome("Done")]
[ActivityOutcome("NoMatch")]
[ActivityOutcome("Refused")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
[RefusesSecretBinding(nameof(Decision), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(Inputs), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class EvaluateDecision(IDecisionEvaluator evaluator, IWorkflowExecutionStateStore workflows,
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access, IDecisionWorkflowAuthorizer authorization,
    IActivityExecutionStateStore activities) : Activity<DecisionEvaluation>
{
    public const string ActivityType = "Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision";

    [ActivityInput(UIHint = "decision-version"), Required]
    public string Decision { get; set; } = string.Empty;

    [ActivityInput(DefaultValue = "{}", DefaultSyntax = "Object")]
    public Dictionary<string, DecisionValue> Inputs { get; set; } = new(StringComparer.Ordinal);

    [ActivityInput(DefaultValue = "1000000", DefaultSyntax = "Literal")]
    public int OperationBudget { get; set; } = 1000000;

    protected override async ValueTask<ActivityTransition<DecisionEvaluation>> ExecuteAsync(ActivityExecutionContext context)
    {
        var state = await workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken)
            ?? throw Invalid();
        if (state.WorkflowExecutionId != context.WorkflowExecutionId || state.Authority is null) throw Invalid();
        var scope = access.Current.RequireScope();
        if (state.Partition?.Value != scope.Value) throw Invalid();
        var executable = await executables.FindAsync(state.PinnedExecutable.ArtifactId, context.CancellationToken)
            ?? throw Invalid();
        if (executable.Identity != state.PinnedExecutable || !executable.NodesById.TryGetValue(context.ExecutableNodeId, out var node) ||
            node.ActivityType != ActivityType || !node.Metadata.TryGetValue(DecisionWorkflowPin.MetadataKey, out var text)) throw Invalid();
        var pin = DecisionWorkflowPin.Parse(text);
        var tenant = state.TenantId ?? scope.Value;
        if (pin.Selection != Decision || pin.TenantId != tenant ||
            node.InputBindings.GetValueOrDefault(nameof(Decision)) is not { Source: Elsa.Workflows.Runtime.Core.Models.RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: System.Text.Json.JsonValueKind.String } selection } || selection.GetString() != Decision ||
            node.InputBindings.GetValueOrDefault(nameof(Inputs))?.EffectivePolicy.HidesValue() == true) throw Invalid();
        // The static binding policy can be strengthened by a workflow input, variable or prior result.
        // Read the same invocation snapshot the runtime hydrated; the store's coalescing overlay also
        // exposes ActivityStarted inside a live Balanced/Ephemeral burst before the durable flush.
        var invocation = await activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken)
            ?? throw Invalid();
        var contract = node.ActivityContract ?? throw Invalid();
        var snapshot = invocation.InputSnapshot;
        if (invocation.Execution.WorkflowExecutionId != context.WorkflowExecutionId || invocation.InvocationId != context.InvocationId ||
            invocation.Execution.ExecutableNodeId != context.ExecutableNodeId || invocation.Execution.ActivityType != ActivityType ||
            invocation.ContractIdentity is not { } identity || identity.ActivityTypeKey != contract.ActivityTypeKey ||
            identity.ContractVersion != contract.ContractVersion || identity.SchemaFingerprint != contract.SchemaFingerprint ||
            snapshot is null || snapshot.InvocationId != context.InvocationId || snapshot.ContractFingerprint != contract.SchemaFingerprint ||
            !snapshot.Values.ContainsKey(nameof(Inputs))) throw Invalid();
        if (snapshot.Values.Values.Any(value => value.Policy.HidesValue() || value.Presence == ValuePresence.Withheld))
            throw new DecisionInvocationException("protected-binding-unsupported", "This decision invocation cannot preserve the materialized input value protection.");
        await authorization.EnsureAsync(tenant, state.Authority, pin.AssetId, context.CancellationToken);
        var result = evaluator.Evaluate(pin.Artifact, Inputs, OperationBudget, context.CancellationToken);
        return result.Status == "Cancelled"
            ? ActivityTransition.Cancel<DecisionEvaluation>("Decision evaluation was cancelled.")
            : ActivityTransition.Complete(result, result.Status switch { "Success" => "Done", "NoMatch" => "NoMatch", _ => "Refused" });
    }

    private static DecisionInvocationException Invalid() => new("invalid-pin", "The exact published decision or execution scope could not be verified.");
}
