using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;
using System.Text.Json;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa3.Mapping.Mappings;

/// <summary>Converts an Elsa-3 activity to an Elsa-4 <see cref="ActivityNode"/>.</summary>
public sealed class Elsa3ActivityToState(IActivityDefinitionLookup activityLookup)
{
    public ValueTask<ActivityNode> Map(Elsa3Activity source, CancellationToken cancellationToken) =>
        Map(source, new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal), cancellationToken);

    public async ValueTask<ActivityNode> Map(
        Elsa3Activity source,
        IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> replacements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacements);
        var mapped = new Dictionary<Elsa3Activity, ActivityNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(Elsa3Activity Activity, bool ChildrenVisited)>();
        stack.Push((source, false));
        while (stack.TryPop(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!frame.ChildrenVisited)
            {
                stack.Push((frame.Activity, true));
                var children = frame.Activity.Activities ?? [];
                for (var index = children.Count - 1; index >= 0; index--)
                    stack.Push((children[index], false));
                continue;
            }

            if (frame.Activity.Connections?.Any() == true)
                throw new NotSupportedException("Elsa 3 activity graph connections require a Flowchart-owned importer module.");

            string activityVersionId;
            IReadOnlyCollection<InputDefinition> declaredInputs;
            IReadOnlySet<string> outputNames;
            if (replacements.TryGetValue(frame.Activity.NodeId, out var replacement))
            {
                activityVersionId = replacement.ActivityVersionId;
                declaredInputs = replacement.Inputs;
                outputNames = replacement.OutputNames;
            }
            else
            {
                var version = await GetVersion(frame.Activity, cancellationToken);
                activityVersionId = version.Id;
                declaredInputs = version.Inputs.ToArray();
                outputNames = version.Outputs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var inputs = new List<ArgumentState>();
            var outputs = new List<ArgumentState>();
            ExtractInputsAndOutputs(frame.Activity.NodeId, inputs, outputs, declaredInputs, outputNames, frame.Activity.AdditionalProperties);
            var childActivities = (frame.Activity.Activities ?? []).Select(x => mapped[x]).ToArray();
            var structure = Elsa3ImportedActivityStructure.Create(childActivities);
            mapped.Add(frame.Activity, new ActivityNode(
                frame.Activity.NodeId,
                activityVersionId,
                inputs,
                outputs,
                structure));
        }

        return mapped[source];
        // NOTE (Unit C, 2026-05-28): Elsa3 per-activity designer position/size in
        // source.Metadata.Designer is no longer carried into ActivityNode — display metadata
        // now lives on WorkflowDefinitionVersionLayout sibling as DesignMetadataRecord (§E2.9.2).
        // Wiring the importer to populate the layout sibling alongside the version is a
        // separate task — flagged in the Unit C follow-up as Elsa3-import layout-carryover.
    }

    /// <summary>
    /// Keeps each property that names a declared input or output. An input binding is stored under the declared input's
    /// reference key, the key design validation and publication match it by (ordinally), whatever casing the Elsa 3
    /// property name has; an output keeps the Elsa 3 property name.
    /// </summary>
    /// <exception cref="ArgumentException">Two properties bind the same declared input, or one property matches more than
    /// one declared input; the mapping refuses rather than keep one binding and drop the other.</exception>
    private static void ExtractInputsAndOutputs(
        string nodeId,
        List<ArgumentState> inputs,
        List<ArgumentState> outputs,
        IReadOnlyCollection<InputDefinition> declaredInputs,
        IReadOnlySet<string> outputNames,
        IDictionary<string, JsonElement> properties
    )
    {
        var boundInputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (propertyName, value) in properties)
        {
            var parsed = TryGetArgument(propertyName, value, out var argument);

            // Skip only properties that carry a value but do not parse as an argument. Null-valued
            // properties fall through and are recorded as ArgumentState.Null below. (The original
            // guard used `||`, which — by De Morgan's law — skipped every property and made this
            // whole loop body dead code, silently dropping all imported inputs/outputs. See #378.)
            if (value.ValueKind != JsonValueKind.Null && !parsed)
            {
                continue;
            }

            var inputKey = DeclaredInputKey(nodeId, declaredInputs, propertyName);
            var isOutput = outputNames.Contains(propertyName);

            if (inputKey is not null && !boundInputs.TryAdd(inputKey, propertyName))
                throw new ArgumentException(
                    $"Elsa 3 activity '{nodeId}' has properties '{boundInputs[inputKey]}' and '{propertyName}', which both bind its input '{inputKey}'.");

            if (inputKey is not null && value.ValueKind != JsonValueKind.Null)
                inputs.Add(argument with { ReferenceKey = inputKey });
            else if (inputKey is not null)
                inputs.Add(ArgumentState.Null(inputKey));

            else if (isOutput && value.ValueKind != JsonValueKind.Null)
                outputs.Add(argument);
            else if (isOutput)
                outputs.Add(ArgumentState.Null(propertyName));
        }
    }

    /// <summary>
    /// The reference key of the declared input <paramref name="propertyName"/> names: the input whose name equals it, else
    /// the only one whose name equals it ignoring case, or null when none does.
    /// </summary>
    private static string? DeclaredInputKey(string nodeId, IReadOnlyCollection<InputDefinition> declaredInputs, string propertyName)
    {
        var exact = declaredInputs.Where(input => StringComparer.Ordinal.Equals(input.Name, propertyName)).ToArray();
        var candidates = exact.Length > 0
            ? exact
            : declaredInputs.Where(input => StringComparer.OrdinalIgnoreCase.Equals(input.Name, propertyName)).ToArray();
        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0].ReferenceKey,
            _ => throw new ArgumentException(
                $"Elsa 3 activity '{nodeId}' property '{propertyName}' matches more than one declared input: {string.Join(", ", candidates.Select(input => $"'{input.Name}'"))}.")
        };
    }

    private async Task<IActivityDefinitionVersion> GetVersion(Elsa3Activity source, CancellationToken cancellationToken)
    {
        var activity = await activityLookup.GetDefinition(source.Type, cancellationToken);
        var versions = await activityLookup.ListVersions(activity.Id, cancellationToken);
        // Elsa-3 carries an int version; map it onto a semver string (FR-007: n → "n.0.0").
        var sourceSemVer = $"{source.Version ?? 0}.0.0";
        var version = versions.FirstOrDefault(x => x.Version == sourceSemVer)
            ?? throw new ArgumentException($"Activity '{source.Type}' does not have version '{sourceSemVer}'");

        return await activityLookup.GetVersion(version.Id, cancellationToken);
    }

    private static bool TryGetArgument(string objectKey, JsonElement jsonElement, out ArgumentState argument)
    {
        argument = null!;

        if (jsonElement.ValueKind != JsonValueKind.Object)
            return false;
        if (!jsonElement.TryGetProperty("expression", out var expression))
            return false;
        if (expression.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null)
            return false;
        if (!expression.TryGetProperty("type", out var expressionType) || expressionType.ValueKind != JsonValueKind.String)
            return false;
        if (!expression.TryGetProperty("value", out var expressionValue) ||
            expressionValue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;

        object value = expressionValue.ValueKind == JsonValueKind.String
            ? expressionValue.GetString()!
            : expressionValue.Clone();

        argument = new ArgumentState(
            objectKey,
            new ArgumentValue(value, expressionType.GetString()),
            null,
            null,
            null,
            null
        );

        return true;
    }
}

/// <summary>
/// The exact reusable activity version a reference node is rewritten to, with the inputs that version declares (their
/// reference keys are what the node's bindings are stored under) and its output names.
/// </summary>
public sealed record Elsa3ActivityExactReplacement(
    string ActivityVersionId,
    IReadOnlyCollection<InputDefinition> Inputs,
    IReadOnlySet<string> OutputNames);
