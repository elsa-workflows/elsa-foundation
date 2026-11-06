using System.Text.RegularExpressions;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>
/// Converts provider-owned findings into the public, non-disclosing diagnostic subset. Provider code is an
/// extension boundary: arbitrary messages, subjects, paths, remediation, and metadata are not trusted as safe.
/// Recognized codes may use explicitly declared, static public guidance. Runtime diagnostic text is never forwarded.
/// </summary>
public static partial class ActivityProviderDiagnosticSanitizer
{
    private const string FallbackCode = ActivityErrorCodes.ProviderDiagnostic;

    public static IReadOnlyList<ActivityDiagnostic> Sanitize(
        IEnumerable<ActivityDiagnostic>? diagnostics,
        string providerKey,
        IReadOnlyList<ActivityProviderDiagnosticDescriptor>? descriptors = null)
    {
        // Duplicate/invalid declarations never select an arbitrary public explanation. Bound both the
        // contribution and its strings, and do not use any of the runtime diagnostic's free-form fields.
        var guidance = (descriptors ?? []).Take(128)
            .Where(IsValidDescriptor)
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        return ActivityDiagnosticOrderer.Order((diagnostics ?? []).Select(x => Sanitize(x, providerKey, guidance)));
    }

    private static ActivityDiagnostic Sanitize(ActivityDiagnostic diagnostic, string providerKey,
        IReadOnlyDictionary<string, ActivityProviderDiagnosticDescriptor> guidance)
    {
        var code = diagnostic.Code is { } candidateCode && DiagnosticCodePattern().IsMatch(candidateCode)
            ? candidateCode
            : FallbackCode;
        var safeProviderKey = providerKey is { } candidateProviderKey && ProviderKeyPattern().IsMatch(candidateProviderKey)
            ? candidateProviderKey
            : "activity-provider";
        guidance.TryGetValue(code, out var descriptor);

        return new(
            code,
            diagnostic.Severity,
            descriptor?.Message ?? "The activity provider reported an issue.",
            new("ActivityDefinition", safeProviderKey),
            new(ProviderKey: safeProviderKey, JsonPointer: descriptor?.JsonPointer),
            descriptor?.Remediation ?? "Ask an administrator to review the diagnostic details and activity provider logs.",
            descriptor?.ActionLabel is { } action
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["actionLabel"] = action }
                : new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static bool IsValidDescriptor(ActivityProviderDiagnosticDescriptor descriptor) =>
        descriptor is not null && descriptor.Code is { } code && DiagnosticCodePattern().IsMatch(code) &&
        !string.IsNullOrWhiteSpace(descriptor.Message) && descriptor.Message.Length <= 512 &&
        !string.IsNullOrWhiteSpace(descriptor.Remediation) && descriptor.Remediation.Length <= 512 &&
        (descriptor.JsonPointer is null || descriptor.JsonPointer.Length <= 256 &&
            descriptor.JsonPointer.StartsWith('/') && !descriptor.JsonPointer.Any(char.IsControl)) &&
        (descriptor.ActionLabel is null || !string.IsNullOrWhiteSpace(descriptor.ActionLabel) && descriptor.ActionLabel.Length <= 80);

    [GeneratedRegex("^[a-z0-9][a-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticCodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKeyPattern();
}
