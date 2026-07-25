using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Strategies;

internal static class IncidentStrategyIdentity
{
    public static void ValidateDescriptor(IncidentStrategyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var reference = descriptor.Reference;
        if (IsBuiltInAlias(reference.Alias))
            throw new ArgumentException(
                $"Built-in incident strategy alias '{reference.Alias}' is reserved for runtime-owned registrations.",
                nameof(descriptor));

        if (!IsNamespaced(reference.Alias))
            throw new ArgumentException($"Custom incident strategy alias '{reference.Alias}' must be dotted and namespaced.", nameof(descriptor));
    }

    public static void ValidateRegisteredDescriptor(IncidentStrategyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsBuiltInAlias(descriptor.Reference.Alias))
        {
            ValidateDescriptor(descriptor);
            return;
        }

        if (!StringComparer.Ordinal.Equals(descriptor.Reference.Version, "1"))
            throw new ArgumentException($"Built-in incident strategy alias '{descriptor.Reference.Alias}' is reserved at version '1'.", nameof(descriptor));
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

    private static bool IsBuiltInAlias(string alias) =>
        StringComparer.OrdinalIgnoreCase.Equals(alias, IncidentStrategyBuiltIns.FaultReference.Alias) ||
        StringComparer.OrdinalIgnoreCase.Equals(alias, IncidentStrategyBuiltIns.ContinueWithIncidentsReference.Alias);
}
