using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Applies the public diagnostics contract's stable response ordering.</summary>
public static class ActivityDiagnosticOrderer
{
    public static IReadOnlyList<ActivityDiagnostic> Order(IEnumerable<ActivityDiagnostic> diagnostics) => diagnostics
        .OrderBy(x => SeverityRank(x.Severity))
        .ThenBy(x => x.Code, StringComparer.Ordinal)
        .ThenBy(x => x.Subject.Kind, StringComparer.Ordinal)
        .ThenBy(x => x.Subject.Id, StringComparer.Ordinal)
        .ThenBy(x => x.Location?.JsonPointer ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(x => x.Location?.ReferenceKey ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(DependencyPathKey, StringComparer.Ordinal)
        .ToArray();

    private static int SeverityRank(ActivityDiagnosticSeverity severity) => severity switch
    {
        ActivityDiagnosticSeverity.Error => 0,
        ActivityDiagnosticSeverity.Warning => 1,
        ActivityDiagnosticSeverity.Info => 2,
        _ => 3
    };

    private static string DependencyPathKey(ActivityDiagnostic diagnostic) => string.Join(
        '\u001f',
        diagnostic.Location?.DependencyPath?.Select(x =>
            $"{x.DefinitionId}\u001e{x.VersionId}\u001e{x.Version}\u001e{x.TemplateHash}") ?? []);
}
