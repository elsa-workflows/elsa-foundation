using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Core.Models;
using Elsa.Agent.JsonSchema;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Design.Services;

internal static class AgentControlContract
{
    private static readonly ActivityInputContract Input = new(AgentInvocationControls.InputKey, AgentInvocationControls.InputName,
        new(AgentInvocationControls.JsonType), false, false, null, "elsa.json", DisplayName: "Invocation options",
        Description: "Omit for a fresh conversation, reuse an accepted Conversation output, or lower the published call, time and output limits.");
    private static readonly ActivityOutputContract Output = new(AgentInvocationControls.OutputKey, AgentInvocationControls.OutputName,
        new(AgentInvocationControls.JsonType), true, false, "elsa.json", DisplayName: "Conversation",
        Description: "Accepted conversation ID and revision; private history is not exposed.", SourceRepresentation: ValueRepresentation.StructuredValue);

    internal static IReadOnlyList<ActivityContractProposalChange> Propose(ActivityContract contract, bool enabled)
    {
        var changes = new List<ActivityContractProposalChange>();
        if (enabled)
        {
            if (!contract.Inputs.Any(x => x.ReferenceKey == Input.ReferenceKey) && !contract.Inputs.Any(x => InputCollision(x)))
                changes.Add(new("input:agent-options", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Input, Input.ReferenceKey, Input: Input));
            if (!contract.Outputs.Any(x => x.ReferenceKey == Output.ReferenceKey) && !contract.Outputs.Any(x => OutputCollision(x)))
                changes.Add(new("output:agent-conversation", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Output, Output.ReferenceKey, Output: Output));
        }
        else
        {
            if (contract.Inputs.Any(IsInput)) changes.Add(new("input:agent-options", ActivityContractProposalOperation.Remove, ActivityContractMemberKind.Input, Input.ReferenceKey));
            if (contract.Outputs.Any(IsOutput)) changes.Add(new("output:agent-conversation", ActivityContractProposalOperation.Remove, ActivityContractMemberKind.Output, Output.ReferenceKey));
        }
        return changes;
    }

    internal static IReadOnlyList<AgentSchemaProblem> Validate(ActivityContract contract, bool enabled, bool proposing = false)
    {
        var problems = new List<AgentSchemaProblem>();
        foreach (var input in contract.Inputs)
            if (InputCollision(input) && !((enabled || proposing) && IsInput(input))) problems.Add(new("/inputs", "agent.controls.collision", "Conversation options conflict with an existing input name, key or shape."));
        foreach (var output in contract.Outputs)
            if (OutputCollision(output) && !((enabled || proposing) && IsOutput(output))) problems.Add(new("/outputs", "agent.controls.collision", "Conversation metadata conflicts with an existing output name, key or shape."));
        if (enabled && !proposing && (!contract.Inputs.Any(IsInput) || !contract.Outputs.Any(IsOutput)))
            problems.Add(new("/contract", "agent.controls.required", "Accept the provider's conversation input/output proposals before publishing this agent."));
        return problems;
    }

    internal static bool IsInput(ActivityInputContract input) => input.ReferenceKey == Input.ReferenceKey && input.Name == Input.Name &&
        input.Type == Input.Type && !input.IsRequired && !input.IsNullable && input.Default is null && input.StorageDriverKey == Input.StorageDriverKey && input.Durability == Input.Durability;
    internal static bool IsOutput(ActivityOutputContract output) => output.ReferenceKey == Output.ReferenceKey && output.Name == Output.Name &&
        output.Type == Output.Type && output.IsRequired && !output.IsNullable && output.StorageDriverKey == Output.StorageDriverKey && output.Durability == Output.Durability &&
        output.SourceRepresentation == Output.SourceRepresentation;
    private static bool InputCollision(ActivityInputContract input) => Reserved(input.ReferenceKey) || StringComparer.OrdinalIgnoreCase.Equals(input.Name, Input.Name);
    private static bool OutputCollision(ActivityOutputContract output) => Reserved(output.ReferenceKey) || StringComparer.OrdinalIgnoreCase.Equals(output.Name?.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(), Output.Name);
    private static bool Reserved(string key) => key?.StartsWith("$agent.", StringComparison.OrdinalIgnoreCase) == true;
}
