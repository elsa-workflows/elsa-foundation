using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.ControlFlow.Loops;

/// <summary>
/// Design-side handler that projects and compiles a loop activity's single body slot, registered once per
/// <see cref="LoopKind"/>. Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the
/// runtime loop activity classes reference no Design types, preserving the runtime/design split.
/// </summary>
internal sealed class LoopStructureHandler(LoopKind loop) : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global web JSON options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => loop.StructureKind;

    public string SchemaVersion => loop.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(LoopAuthoredStructure);

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
        [new ActivityChildProjection(loop.BodySlotName, ReadAuthoredStructure(activity).Body is { } body ? [body] : [])];

    // A loop has one child, its body, and promises no order for it. The loop schedules every pass itself, so an
    // earlier pass is never among the runs that led to a later one.
    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = false };

    // The body is the only child. Elsa 4 loops run their passes one after another, so passes never overlap.
    public ActivityChildTimeRelation RelateChildren(ActivityNode activity, string firstChildNodeId, string secondChildNodeId) =>
        ActivityChildTimeRelation.FirstBeforeSecond;

    public bool? CompletesAfterItsChildren(ActivityNode activity) => true;

    /// <summary>
    /// The iteration values a script in the body reads as variables declared by this loop node (spec 1222 FR-004a).
    /// ForEach's <c>currentIndex</c> is declared unless <c>ExposeIndex</c> is a literal <c>false</c>: an unauthored
    /// input defaults to <c>true</c>, and an expression is only resolved at run time.
    /// </summary>
    public IReadOnlyCollection<VariableDefinition> ProjectIterationVariables(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var names = loop.IterationVariableNames;
        if (loop.IndexSwitchInputKey is { } switchKey && IsLiteralFalse(activity.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, switchKey))))
            names = names.Where(name => !StringComparer.Ordinal.Equals(name, loop.SwitchedIndexVariableName)).ToArray();

        return names
            .Select(name => new VariableDefinition(name, name, new TypeReference(TypeAliasConvention.AnyAlias), null, null))
            .ToArray();
    }

    /// <summary>Whether a loop input is authored as the literal <c>false</c> (not an expression, not unauthored).</summary>
    internal static bool IsLiteralFalse(ArgumentState? input)
    {
        if (input?.Value is not { } value ||
            (value.ExpressionType is { } type && !StringComparer.OrdinalIgnoreCase.Equals(type, "Literal")))
            return false;

        return value.Value switch
        {
            bool flag => !flag,
            string text => bool.TryParse(text.Trim(), out var parsed) && !parsed,
            JsonElement { ValueKind: JsonValueKind.False } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString()?.Trim(), out var parsed) && !parsed,
            _ => false
        };
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, loop.BodySlotName));
        return activity with { Structure = ToStructure(new LoopAuthoredStructure(slot?.Activities.FirstOrDefault())) };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) =>
        ToStructure(new LoopExecutableStructure(ReadAuthoredStructure(activity).Body?.NodeId));

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<LoopExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException($"{loop.Name} executable structure payload is invalid.");
        var remapped = executable.Body is not null && authoredToExecutableNodeIds.TryGetValue(executable.Body, out var executableNodeId)
            ? executableNodeId
            : executable.Body;
        return ToStructure(new LoopExecutableStructure(remapped));
    }

    private ActivityNodeStructure ToStructure<TPayload>(TPayload payload) =>
        new(loop.StructureKind, loop.StructureSchemaVersion, JsonSerializer.SerializeToElement(payload, SerializerOptions));

    private static LoopAuthoredStructure ReadAuthoredStructure(ActivityNode activity) =>
        activity.Structure?.Payload.Deserialize<LoopAuthoredStructure>(SerializerOptions) ?? new LoopAuthoredStructure();
}
