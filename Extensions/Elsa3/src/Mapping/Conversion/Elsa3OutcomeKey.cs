using Elsa.Activities.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// How an Elsa 3 outcome name becomes a reusable activity's contract outcome (plan D-4 to D-7, data-model §7a): the
/// name is kept verbatim, so caller ports keep their Elsa 3 names; the reference key is the same string, except that
/// Elsa 3 <c>Done</c> becomes Elsa 4's natural outcome key <c>done</c>.
/// </summary>
internal static class Elsa3OutcomeKey
{
    public const string Done = "Done";
    public const string DoneKey = "done";

    public static string ReferenceKey(string elsa3Outcome) =>
        StringComparer.Ordinal.Equals(elsa3Outcome, Done) ? DoneKey : elsa3Outcome;

    /// <summary>
    /// Assembles a reusable workflow's boundary outcomes from the outcomes its Complete/End nodes name, reporting the
    /// D-4 key collision, undeclared outcomes (D-6) and unreached declared outcomes (D-7).
    /// </summary>
    public static IReadOnlyList<ActivityOutcomeContract> Assemble(Elsa3ConversionPlan plan)
    {
        var named = plan.CompleteOutcomes;
        // 'Done' is always declared (D-5) under key 'done', so an Elsa 3 outcome spelled 'done' collides with it.
        foreach (var (_, nodeId, path) in named.Where(item => item.Outcome == DoneKey))
        {
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                plan,
                Elsa3MigrationDiagnosticSeverity.Error,
                Elsa3ActivityDiagnosticCodes.UnsupportedMode,
                $"Complete node '{nodeId}' names outcome 'done', which shares Elsa 4's reference key 'done' with the natural outcome 'Done'.",
                "Rename the outcome in the Elsa 3 workflow (for example to 'Completed'), and change every workflow that routes on 'done' to the new name.",
                path,
                new Dictionary<string, string> { ["ActivityNodeId"] = nodeId }));
        }

        var declared = (plan.Definition.Outcomes ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var (outcome, nodeId, path) in named.Where(item => item.Outcome != Done && !declared.Contains(item.Outcome)))
        {
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                plan,
                Elsa3MigrationDiagnosticSeverity.Warning,
                Elsa3ActivityDiagnosticCodes.OutcomeUndeclared,
                $"Complete node '{nodeId}' completes with outcome '{outcome}', which the workflow does not declare; the converted activity declares it.",
                "Declare the outcome on the Elsa 3 workflow, or confirm callers handle it.",
                path,
                new Dictionary<string, string> { ["ActivityNodeId"] = nodeId }));
        }

        var reached = named.Select(item => item.Outcome).ToHashSet(StringComparer.Ordinal);
        foreach (var outcome in declared.Where(outcome => outcome != Done && !reached.Contains(outcome)).Order(StringComparer.Ordinal))
        {
            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                plan,
                Elsa3MigrationDiagnosticSeverity.Warning,
                Elsa3ActivityDiagnosticCodes.OutcomeUnreached,
                $"The workflow declares outcome '{outcome}', but no Complete completes with it; the converted activity leaves it out, so callers get no port for it.",
                "Remove the outcome, or complete with it somewhere in the workflow.",
                "$.outcomes"));
        }

        return reached.Append(Done)
            .Distinct(StringComparer.Ordinal)
            .Select(outcome => new ActivityOutcomeContract(ReferenceKey(outcome), outcome, true))
            .DistinctBy(contract => contract.ReferenceKey, StringComparer.Ordinal)
            .OrderBy(contract => contract.ReferenceKey, StringComparer.Ordinal)
            .ToArray();
    }
}
