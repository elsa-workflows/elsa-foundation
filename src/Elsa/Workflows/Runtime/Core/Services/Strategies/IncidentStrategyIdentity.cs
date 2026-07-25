using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Strategies;

internal static class IncidentStrategyIdentity
{
    public static void ValidateDescriptor(IncidentStrategyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var reference = descriptor.Reference;
        var isBuiltIn = StringComparer.OrdinalIgnoreCase.Equals(reference.Alias, IncidentStrategyBuiltIns.FaultReference.Alias) ||
                        StringComparer.OrdinalIgnoreCase.Equals(reference.Alias, IncidentStrategyBuiltIns.ContinueWithIncidentsReference.Alias);

        if (isBuiltIn)
        {
            if (!StringComparer.Ordinal.Equals(reference.Version, "1"))
                throw new ArgumentException($"Built-in incident strategy alias '{reference.Alias}' is reserved at version '1'.", nameof(descriptor));
            return;
        }

        if (!IsNamespaced(reference.Alias))
            throw new ArgumentException($"Custom incident strategy alias '{reference.Alias}' must be dotted and namespaced.", nameof(descriptor));
    }

    public static void ValidateSafeIntentKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!IsNamespaced(kind) || kind.StartsWith("Elsa.", StringComparison.Ordinal))
            throw new ArgumentException($"Incident-strategy safe intent kind '{kind}' must be a third-party dotted namespace.", nameof(kind));

        if (StringComparer.Ordinal.Equals(kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) ||
            kind.Contains("Dispatch", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("Stimulus", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("Scheduler", StringComparison.OrdinalIgnoreCase) ||
            kind.Contains("Retry", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Incident-strategy safe intent kind '{kind}' is reserved for runtime control.", nameof(kind));
        }
    }

    private static bool IsNamespaced(string value) =>
        value.Split('.', StringSplitOptions.None) is var segments &&
        segments.Length >= 2 &&
        segments.All(segment => !String.IsNullOrWhiteSpace(segment));
}
