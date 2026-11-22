using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;

namespace Elsa3.Mapping.Resolution;

/// <summary>
/// The input contracts of the engine intrinsics the map targets. Intrinsics are built-in authoring descriptors with no
/// catalog row, so they resolve without the catalog (<c>IntrinsicAuthoringDescriptorProvider</c>). Every input is
/// required.
/// </summary>
internal static class Elsa3IntrinsicContracts
{
    public const string Set = "elsa.intrinsic.set@1";
    public const string SetOutput = "elsa.intrinsic.set-output@1";
    public const string SetCorrelationId = "elsa.intrinsic.set-correlation-id@1";
    public const string SetInstanceName = "elsa.intrinsic.set-instance-name@1";
    public const string Finish = "elsa.intrinsic.finish@1";

    public const string ValueKey = "value";
    public const string NameKey = "name";
    public const string OutcomeKey = "outcome";

    private static readonly IReadOnlyDictionary<string, (AuthoredWorkflowIntrinsicKind Kind, (string Key, string Type)[] Inputs)> Contracts =
        new Dictionary<string, (AuthoredWorkflowIntrinsicKind, (string, string)[])>(StringComparer.Ordinal)
        {
            [Set] = (AuthoredWorkflowIntrinsicKind.Set, [(ValueKey, "Object")]),
            [SetOutput] = (AuthoredWorkflowIntrinsicKind.SetOutput, [(NameKey, "String"), (ValueKey, "Object")]),
            [SetCorrelationId] = (AuthoredWorkflowIntrinsicKind.SetCorrelationId, [(ValueKey, "String")]),
            [SetInstanceName] = (AuthoredWorkflowIntrinsicKind.SetInstanceName, [(ValueKey, "String")]),
            [Finish] = (AuthoredWorkflowIntrinsicKind.Finish, [(OutcomeKey, "String")]),
        };

    public static bool IsKnown(string activityVersionId) => Contracts.ContainsKey(activityVersionId);

    public static Elsa3ResolvedActivity Resolve(string activityVersionId, Elsa3ActivityMapEntry? entry)
    {
        if (!Contracts.TryGetValue(activityVersionId, out var contract))
            throw new InvalidOperationException($"Unknown intrinsic '{activityVersionId}'.");

        var inputs = contract.Inputs
            .Select(input => new InputDefinition(
                input.Key,
                input.Key,
                new TypeReference(input.Type),
                null,
                input.Key,
                null,
                false,
                IsRequired: true))
            .ToArray();
        return new Elsa3ResolvedActivity(
            Elsa3ResolvedSource.Intrinsic,
            entry,
            activityVersionId,
            null,
            inputs,
            [],
            null,
            contract.Kind);
    }
}
