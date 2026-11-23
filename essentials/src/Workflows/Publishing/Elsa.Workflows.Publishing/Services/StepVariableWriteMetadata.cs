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
/// <c>[AccessesUndeclaredVariables]</c>. Inspection and Studio read them from the node. A script body adds the variables
/// it may write (it writes them literally, or lets an object value escape), and a computed or opaque write marks it as
/// possibly writing every visible variable (FR-020a).
/// </summary>
internal static class StepVariableWriteMetadata
{
    public static Dictionary<string, string> Pin(
        Dictionary<string, string> metadata,
        IReadOnlyCollection<InputDefinition> definitions,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        RuntimeActivityContract? contract,
        IReadOnlyCollection<VariableDefinition> workflowVariables,
        ScriptVariableReadPlan? scriptBody = null,
        bool explicitlyMarked = false,
        bool explicitlyAccessesState = false)
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

        foreach (var write in scriptBody?.MayWrite() ?? [])
        {
            if (!targets.Any(target => target.ScopeId == write.ScopeId && target.VariableKey == write.Variable.ReferenceKey))
                targets.Add(new MayWriteTarget(write.ScopeId, write.Variable.ReferenceKey, write.Name));
        }

        if (targets.Count > 0)
            metadata[RuntimeMetadataKeys.MayWriteVariables] = JsonSerializer.Serialize(targets, SerializerOptions);
        // An engine script activity carries the type-level marker without declaring it; its analyzed body says whether
        // it may write every visible variable (a computed or opaque write) or only its may-write set. A type that declares
        // [AccessesUndeclaredVariables] itself may write any visible variable from its C# code too (FR-028).
        if (explicitlyMarked || (scriptBody is not null ? scriptBody.HasComputedWrite : contract?.AccessesUndeclaredVariables == true))
            metadata[RuntimeMetadataKeys.MayWriteEveryVisibleVariable] = "true";
        if (contract?.AccessesWorkflowState == true)
        {
            var effects = scriptBody?.WorkflowState;
            // A native declaration also covers C# behavior outside its optional analyzed script.
            if (explicitlyAccessesState)
                effects = (effects ?? new ScriptWorkflowStateEffects()) with { AccessesState = true, Conservative = true };
            metadata["accessesWorkflowState"] = (effects?.AccessesState ?? true) ? "true" : "false";
            metadata["workflowStateEffects"] = JsonSerializer.Serialize(effects ?? new Elsa.Expressions.Core.Models.ScriptWorkflowStateEffects
                { AccessesState = true, Conservative = true }, SerializerOptions);
        }
        return metadata;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record MayWriteTarget(string ScopeId, string VariableKey, string Name);
}
