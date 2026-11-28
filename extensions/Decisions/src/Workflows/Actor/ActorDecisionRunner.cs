using System.Text.Json;
using Elsa.Decisions.Core;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Native;

namespace Elsa.Decisions.Workflows.Actor;

internal sealed class ActorDecisionRunner(
    IWorkflowExecutableStore executables,
    IPersistenceAccessContextAccessor access,
    ICurrentSubjectPermissionAuthorizer authorization,
    IDecisionEvaluator evaluator) : INativeDecisions
{
    private static readonly JsonSerializerOptions InputJson = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web);

    public async ValueTask<NativeDecisionResult> EvaluateAsync(NativeDecisionInvocation invocation, CancellationToken cancellationToken)
    {
        if (access.Current.RequireScope().Value != invocation.Tenant)
            return InvalidPin();
        var executable = await executables.FindAsync(invocation.Artifact, cancellationToken);
        if (executable is null || executable.Identity.ArtifactId != invocation.Artifact ||
            JsonSerializer.Serialize(executable.Identity) != invocation.ExecutableSelection ||
            !executable.NodesById.TryGetValue(invocation.NodeId, out var node) || node.ActivityType != EvaluateDecision.ActivityType ||
            ExecutableDecisionPinMetadata.Read(node.Metadata) is not { } serialized ||
            node.InputBindings.GetValueOrDefault(nameof(EvaluateDecision.Decision)) is not
                { Source: RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: JsonValueKind.String } selection } ||
            selection.GetString() != invocation.Selection)
            return InvalidPin();

        DecisionWorkflowPin pin;
        try { pin = DecisionWorkflowPin.Parse(serialized); }
        catch (DecisionInvocationException) { return InvalidPin(); }
        if (pin.TenantId != invocation.Tenant || pin.Selection != invocation.Selection)
            return InvalidPin();

        var grant = await authorization.AuthorizeAsync(new(invocation.Tenant, invocation.SubjectId, DecisionPermissions.Evaluate, pin.AssetId),
            cancellationToken);
        if (grant.Status == CurrentSubjectPermissionStatus.Unsupported)
            return Refuse("decisions.authority-unavailable", "Current decision execution authority cannot be verified.");
        if (grant.Status != CurrentSubjectPermissionStatus.Granted)
            return Refuse("decisions.forbidden", "The execution subject cannot evaluate this decision.");

        if (node.InputBindings.Values.Any(DecisionWorkflowPin.RequiresValueProtection) ||
            node.InputBindings.Values.Any(DecisionWorkflowPin.IsWorkflowInput) &&
            !ExecutableDecisionPinMetadata.HasVerifiedWorkflowInputs(node.Metadata))
            return Refuse("decisions.republish-required", "Republish the workflow to verify the decision input sources.");

        Dictionary<string, DecisionValue>? inputs;
        try { inputs = invocation.Inputs.Deserialize<Dictionary<string, DecisionValue>>(InputJson); }
        catch (JsonException) { return InvalidInputs(); }
        if (inputs is null || inputs.Values.Any(value => value is null))
            return InvalidInputs();

        var result = evaluator.Evaluate(pin.Artifact, inputs, invocation.OperationBudget, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var outcome = result.Status switch { "Success" => "Done", "NoMatch" => "NoMatch", "Cancelled" => "Cancelled", _ => "Refused" };
        return new(outcome, JsonSerializer.SerializeToNode(result, OutputJson));
    }

    private static NativeDecisionResult InvalidPin() =>
        Refuse("decisions.invalid-pin", "The exact published decision or execution scope could not be verified.");

    private static NativeDecisionResult InvalidInputs() =>
        Refuse("decisions.inputs-invalid", "Decision inputs must use the declared typed decision values.");

    private static NativeDecisionResult Refuse(string code, string message) => new("Refused", null, new(code, message));
}
