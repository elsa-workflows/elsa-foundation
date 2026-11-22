using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Checks the effective lowered tree: Elsa 3 structured Switch ignores child outcome names, whereas native
/// containers interpret Break. Declared ports alone are insufficient for known structural activities.
/// </summary>
internal static class Elsa3SwitchSafety
{
    private const string Switch = "Elsa.Activities.Switch.Activities.Switch";
    private const string Break = "Elsa.Activities.Primitives.Activities.Break";
    private const string CaseSlot = "Switch.Case";

    public static void Validate(Elsa3ConversionPlan plan)
    {
        foreach (var owner in plan.Nodes.Values.Where(node => node.Resolved?.ActivityTypeKey == Switch
                     && node.StructureFields.GetValueOrDefault("mode") is "MatchAny"
                     && node.Slots.Any(slot => slot.SlotName == CaseSlot && slot.Child is not null)))
        {
            var index = 0;
            foreach (var slot in owner.Slots.Where(slot => slot.SlotName == CaseSlot))
            {
                if (slot.Label is "Break" or "Done")
                    plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Error(plan, owner,
                        Elsa3ActivityDiagnosticCodes.UnsupportedMode,
                        $"MatchAny case label '{slot.Label}' has native control meaning and cannot faithfully name this imported case.",
                        "Rename the case to a label other than Break or Done before importing.", plan.SourceName(owner.Node, "cases"), $"[{index}].label"));
                if (slot.Child is { } child && plan.Find(child) is { } body
                    && FindUnsafe(plan, body, localLoop: false, namedBreakConsumed: false) is { } unsafeNode)
                    plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Error(plan, unsafeNode,
                        Elsa3ActivityDiagnosticCodes.UnsupportedMode,
                        HasInvalidDoneSelection(unsafeNode)
                            ? $"Case '{slot.Label}' contains an invalid or unproven selection combining Done with another label in the native outcome contract."
                            : $"Case '{slot.Label}' can expose an incompatible or unproven native Break outcome; Elsa 3 Switch would keep waiting for its other bodies.",
                        "Restructure the case so no incompatible control outcome escapes or changes its behavior. Use a case-local supported loop for a genuine Break, and rename Done/Break script outcomes or nested case labels where they have native control meaning."));
                index++;
            }
        }
    }

    private static Elsa3NodePlan? FindUnsafe(Elsa3ConversionPlan plan, Elsa3NodePlan node, bool localLoop, bool namedBreakConsumed)
    {
        var resolved = node.Resolved;
        if (resolved is null) return node;
        var type = resolved.ActivityTypeKey;
        if (type == Break) return localLoop ? null : node;
        // This is invalid before a parent can consume it, including beneath Parallel. Keep the check
        // scoped to newly enabled case bodies; existing standalone outcome-only modes are unchanged.
        if (HasInvalidDoneSelection(node)) return node;

        // Lowering records effective literal/computed selections separately from declared ports. Script-selected Break
        // is not a source BreakSignal, even inside a loop; merely consuming it would change that loop's behavior.
        if (node.ScriptProperty is not null)
        {
            var names = node.EffectiveScriptOutcomes;
            return !namedBreakConsumed && (names is null || names.Contains("Break", StringComparer.Ordinal)) ? node : null;
        }

        if (!namedBreakConsumed && type == Switch && node.Slots.Any(slot => slot.SlotName == CaseSlot && slot.Label == "Break"))
            return node;

        var loop = type is "Elsa.Activities.For.Activities.For" or "Elsa.Activities.ForEach.Activities.ForEach"
            or "Elsa.Activities.While.Activities.While" or "Elsa.Activities.Do.Activities.Do";
        var parallel = type == "Elsa.Activities.Parallel.Activities.Parallel";
        var slots = node.Slots.ToArray();
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            // Named Break can already change a loop/graph, or skip a later Sequence child, before an
            // enclosing Parallel consumes its completion. A genuine BreakSignal crosses source Parallel,
            // unlike native Parallel's child outcome, so only a loop below that boundary can consume it safely.
            var consumesName = parallel || namedBreakConsumed
                && !loop && type != "Elsa.Activities.Flowchart.Activities.Flowchart"
                && !(type == "Elsa.Activities.Sequence.Activities.Sequence" && index < slots.Length - 1);
            if (slot.Child is { } child && plan.Find(child) is { } childPlan
                && FindUnsafe(plan, childPlan, loop || localLoop && !parallel, consumesName) is { } unsafeNode)
                return unsafeNode;
        }

        // These known containers propagate actual child Break (handled above); a loop consumes a genuine signal.
        if (loop || type is Switch or "Elsa.Activities.If.Activities.If"
            or "Elsa.Activities.Sequence.Activities.Sequence" or "Elsa.Activities.Flowchart.Activities.Flowchart"
            or "Elsa.Activities.Parallel.Activities.Parallel")
            return null;

        if (!namedBreakConsumed && resolved.Outcomes.Ports.Contains("Break", StringComparer.Ordinal)) return node;
        if (resolved.Outcomes.Dynamic is { } dynamic)
        {
            var value = node.Node.AdditionalProperties.FirstOrDefault(pair =>
                pair.Key.Equals(dynamic.SourceInputKey, StringComparison.OrdinalIgnoreCase)).Value;
            var names = Elsa3ValueFlowLowerer.PossibleOutcomeItems(value);
            if (!namedBreakConsumed && (names is null || names.Contains("Break", StringComparer.Ordinal) || dynamic.UnmatchedOutcome == "Break"))
                return node;
        }
        return null;
    }

    private static bool HasInvalidDoneSelection(Elsa3NodePlan node) =>
        node.EffectiveScriptInvalidDoneSelection || node.Resolved?.ActivityTypeKey == Switch && node.StructureFields.GetValueOrDefault("mode") is "MatchAny"
        && node.Slots.Count(slot => slot.SlotName == CaseSlot) > 1
        && node.Slots.Any(slot => slot.SlotName == CaseSlot && slot.Label == "Done");
}
