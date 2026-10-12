using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>
/// Converts provider-owned findings into the public, non-disclosing diagnostic subset. Provider code is an
/// extension boundary: arbitrary messages, subjects, paths, remediation, and metadata are not trusted as safe.
/// Clients use the stable code to select provider documentation or localized detail.
/// </summary>
public static partial class ActivityProviderDiagnosticSanitizer
{
    private const string FallbackCode = ActivityErrorCodes.ProviderDiagnostic;

    public static IReadOnlyList<ActivityDiagnostic> Sanitize(
        IEnumerable<ActivityDiagnostic>? diagnostics,
        string providerKey) =>
        ActivityDiagnosticOrderer.Order((diagnostics ?? []).Select(x => Sanitize(x, providerKey)));

    private static ActivityDiagnostic Sanitize(ActivityDiagnostic diagnostic, string providerKey)
    {
        var code = diagnostic.Code is { } candidateCode && DiagnosticCodePattern().IsMatch(candidateCode)
            ? candidateCode
            : FallbackCode;
        var safeProviderKey = providerKey is { } candidateProviderKey && ProviderKeyPattern().IsMatch(candidateProviderKey)
            ? candidateProviderKey
            : "activity-provider";

        return new(
            code,
            diagnostic.Severity,
            $"The activity provider reported diagnostic '{code}'.",
            new("ActivityDefinition", safeProviderKey),
            new(ProviderKey: safeProviderKey),
            "Review the provider documentation for this diagnostic code.",
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticCodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKeyPattern();
}
