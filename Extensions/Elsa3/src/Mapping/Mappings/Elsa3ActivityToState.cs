using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa3.Mapping.Mappings;

/// <summary>
/// Maps a normalized and lowered Elsa 3 activity tree to Elsa 4 <see cref="ActivityNode"/>s: each node references the
/// activity version its plan resolved, keeps the bindings stored under declared input and output keys, carries its
/// intrinsic contract, and gets the native structure of its target (research R-6). Reached through
/// <see cref="IElsa3DefinitionConverter"/>, which runs normalization and lowering first.
/// </summary>
public sealed class Elsa3ActivityToState
{
    /// <summary>The mapped root, or null when the plan holds no root node.</summary>
    internal ActivityNode? Map(Elsa3ConversionPlan plan, Elsa3Activity root)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(root);
        var mapped = new Dictionary<Elsa3Activity, ActivityNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(Elsa3Activity Activity, bool ChildrenVisited)>();
        stack.Push((root, false));
        while (stack.TryPop(out var frame))
        {
            if (!frame.ChildrenVisited)
            {
                stack.Push((frame.Activity, true));
                var children = frame.Activity.Activities ?? [];
                for (var index = children.Count - 1; index >= 0; index--)
                    stack.Push((children[index], false));
                continue;
            }

            if (plan.Find(frame.Activity) is not { Resolved: { } resolved } nodePlan)
                continue;

            var inputs = new List<ArgumentState>();
            var outputs = new List<ArgumentState>();
            foreach (var (key, value) in frame.Activity.AdditionalProperties)
            {
                var isInput = resolved.Inputs.Any(input => StringComparer.Ordinal.Equals(input.ReferenceKey, key));
                var isOutput = !isInput && resolved.Outputs.Any(output => StringComparer.Ordinal.Equals(output.ReferenceKey, key));
                if (!isInput && !isOutput)
                    continue;

                // A value that carries no expression (an output's bare memory reference, already lowered away) is
                // dropped; a JSON null is kept as an explicit null binding.
                if (!TryGetArgument(key, value, out var argument) && value.ValueKind != JsonValueKind.Null)
                    continue;
                (isInput ? inputs : outputs).Add(argument);
            }

            var node = new ActivityNode(
                Elsa3ConversionDiagnostics.NodeId(frame.Activity),
                resolved.ActivityVersionId,
                inputs,
                outputs,
                Elsa3NativeStructureBuilder.Build(nodePlan, mapped))
            {
                Intrinsic = nodePlan.Intrinsic
            };
            mapped.Add(frame.Activity, node);
        }

        return mapped.GetValueOrDefault(root);
        // NOTE: Elsa-3 per-activity designer position/size in source.Metadata.Designer is not carried into
        // ActivityNode; display metadata lives on the WorkflowDefinitionVersionLayout sibling, and no issue tracks
        // populating it from the import.
    }

    private static bool TryGetArgument(string key, JsonElement jsonElement, out ArgumentState argument)
    {
        argument = ArgumentState.Null(key);
        if (!Elsa3TypedValues.TryGetExpression(jsonElement, out var expressionType, out var expressionValue))
            return false;

        object value = expressionValue.ValueKind == JsonValueKind.String
            ? expressionValue.GetString()!
            : expressionValue.Clone();
        argument = new ArgumentState(key, new ArgumentValue(value, expressionType), null, null, null, null);
        return true;
    }
}

/// <summary>
/// The exact reusable activity version a reference node is rewritten to, with the inputs that version declares (their
/// reference keys are what the node's bindings are stored under), its output names and the outcome names it completes
/// with, which a caller's flowchart connections route by (null: only <c>Done</c>).
/// </summary>
public sealed record Elsa3ActivityExactReplacement(
    string ActivityVersionId,
    IReadOnlyCollection<InputDefinition> Inputs,
    IReadOnlySet<string> OutputNames,
    IReadOnlySet<string>? OutcomeNames = null);
