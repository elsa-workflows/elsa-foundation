using System.Text.Json;
using Elsa.Activities.If.Models;
using Elsa.Workflows.Design.Core.Authoring;
using Elsa.Workflows.Design.Core.Models;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Authoring;

public static class IfBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ActivityCall<object?> If(
        this ISequenceBuilder sequence,
        WorkflowValue<bool> condition,
        Action<ISequenceBuilder> then,
        Action<ISequenceBuilder>? @else = null,
        string activityVersionId = "elsa.if@1",
        string? nodeId = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);

        return sequence.AddStructured<IfActivity, object?>(
            activityVersionId,
            structure =>
            {
                var thenNode = structure.Sequence("then", then);
                var elseNode = @else is null ? null : structure.Sequence("else", @else);
                var payload = JsonSerializer.SerializeToElement(new IfAuthoredStructure(thenNode, elseNode), JsonOptions);
                return new ActivityNodeStructure(IfActivity.StructureKind, IfActivity.StructureSchemaVersion, payload);
            },
            inputs => inputs.From("Condition", condition),
            nodeId);
    }
}
