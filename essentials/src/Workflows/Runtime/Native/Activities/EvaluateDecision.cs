using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>The optional shell port for an authorized evaluation against an exact published executable.</summary>
public interface INativeDecisions
{
    ValueTask<NativeDecisionResult> EvaluateAsync(NativeDecisionInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>Plain inputs and the model's trusted start attribution for one decision invocation.</summary>
public sealed record NativeDecisionInvocation(string Artifact, string ExecutableSelection, string NodeId, string Tenant,
    string SubjectId, string Selection, JsonObject Inputs, int OperationBudget);

/// <summary>The decision result and its authored outcome, or an execution refusal.</summary>
public sealed record NativeDecisionResult(string Outcome, JsonNode? Value, ActivityFault? Fault = null);

/// <summary>Evaluates frozen decision material through the shell's current permission and evaluator services.</summary>
public sealed class EvaluateDecision : Activity<JsonNode?>
{
    public const string ActivityType = "Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision";
    public const string MetadataKey = "decisions.artifact.v1";

    [Input] public string Decision { get; set; } = "";
    [Input] public JsonNode? Inputs { get; set; }
    [Input] public int OperationBudget { get; set; } = 1_000_000;

    protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        if (context.Decisions is not { } decisions || context.ExactDecisionSource is not { } source)
            return Fault("decisions.authority-unavailable", "This decision requires an authenticated workflow start and an exact executable.");
        if (Inputs is not (null or JsonObject))
            return Fault("decisions.inputs-invalid", "Decision inputs must be an object.");

        var result = await decisions.EvaluateAsync(new(source.Artifact, source.ExecutableSelection, context.NodeId,
            source.Tenant, source.SubjectId, Decision, (JsonObject?)Inputs ?? [], OperationBudget), context.CancellationToken);
        if (result.Fault is { } fault)
            return new(TransitionKind.Fault, Fault: fault);
        return result.Outcome == "Cancelled"
            ? Cancel("Decision evaluation was cancelled.")
            : Complete(result.Value, result.Outcome);
    }
}

internal sealed record NativeDecisionSource(string Artifact, string ExecutableSelection, string Tenant, string SubjectId);
