using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Decisions.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

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
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access, IDecisionWorkflowAuthorizer authorization) : Activity<DecisionEvaluation>
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
        await authorization.EnsureAsync(tenant, state.Authority, pin.AssetId, context.CancellationToken);
        var result = evaluator.Evaluate(pin.Artifact, Inputs, OperationBudget, context.CancellationToken);
        return result.Status == "Cancelled"
            ? ActivityTransition.Cancel<DecisionEvaluation>("Decision evaluation was cancelled.")
            : ActivityTransition.Complete(result, result.Status switch { "Success" => "Done", "NoMatch" => "NoMatch", _ => "Refused" });
    }

    private static DecisionInvocationException Invalid() => new("invalid-pin", "The exact published decision or execution scope could not be verified.");
}
