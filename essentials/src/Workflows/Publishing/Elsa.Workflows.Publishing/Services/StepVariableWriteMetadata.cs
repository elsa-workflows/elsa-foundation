using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using RuntimeActivityContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Pins a node's may-write set onto the published node (spec 1231 FR-019, FR-024, FR-026): the variables its writable
/// <c>VariableReference&lt;T&gt;</c> inputs are bound to, as <see cref="RuntimeMetadataKeys.MayWriteVariables"/>, and
/// <see cref="RuntimeMetadataKeys.MayWriteEveryVisibleVariable"/> for an activity type marked
/// <c>[AccessesUndeclaredVariables]</c>. Inspection and Studio read them from the node.
/// </summary>
internal static class StepVariableWriteMetadata
{
    public static Dictionary<string, string> Pin(
        Dictionary<string, string> metadata,
        IReadOnlyCollection<InputDefinition> definitions,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        RuntimeActivityContract? contract,
        IReadOnlyCollection<VariableDefinition> workflowVariables)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var targets = new List<MayWriteTarget>();
        foreach (var definition in definitions.Where(definition => StringComparer.Ordinal.Equals(definition.VariableAccess, InputVariableAccess.ReadWrite)))
        {
            if (!bindings.TryGetValue(definition.ReferenceKey, out var binding) || binding.LiteralValue is not { ValueKind: JsonValueKind.Object } literal)
                continue;

            var scopeId = literal.GetProperty("scopeId").GetString()!;
            var key = literal.GetProperty("variableKey").GetString()!;
            var name = StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId)
                ? workflowVariables.FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, key))?.Name ?? key
                : literal.TryGetProperty("name", out var named) ? named.GetString() ?? key : key;
            if (!targets.Any(target => target.ScopeId == scopeId && target.VariableKey == key))
                targets.Add(new MayWriteTarget(scopeId, key, name));
        }

        if (targets.Count > 0)
            metadata[RuntimeMetadataKeys.MayWriteVariables] = JsonSerializer.Serialize(targets, SerializerOptions);
        if (contract?.AccessesUndeclaredVariables == true)
            metadata[RuntimeMetadataKeys.MayWriteEveryVisibleVariable] = "true";
        return metadata;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record MayWriteTarget(string ScopeId, string VariableKey, string Name);
}
