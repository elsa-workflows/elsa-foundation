using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Conversion;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Classifies an Elsa 3 activity type by its built-in map entry, with the rule conversion uses for the start node
/// (spec 248): a mapped type without a <c>canStartWorkflow</c> rule is a non-trigger. An unmapped type is not known.
/// </summary>
internal sealed class Elsa3ReusableActivityTriggerClassifier(IElsa3ActivityMap map) : IReusableActivityTriggerClassifier
{
    public bool? IsTrigger(string elsa3ActivityType) =>
        map.Find(elsa3ActivityType ?? string.Empty) is { } entry ? !Elsa3ActivityNormalizer.IsNonTrigger(entry) : null;
}
