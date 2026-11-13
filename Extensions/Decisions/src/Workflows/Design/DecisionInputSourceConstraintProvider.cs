using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Decisions.Workflows.Design;

/// <summary>Exposes the same publication-fixed selector restriction enforced by DecisionPinSource.</summary>
internal sealed class DecisionInputSourceConstraintProvider : IActivityInputSourceConstraintProvider
{
    public ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(
        IActivityDefinitionVersion activityVersion,
        string inputKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(activityVersion);
        var applies = IsEvaluateDecision(activityVersion)
            && StringComparer.Ordinal.Equals(inputKey, nameof(EvaluateDecision.Decision));
        return ValueTask.FromResult<ActivityDataInputConstraint?>(applies
            ? new("Elsa.Decisions", ["Literal"], "decisions.selector-fixed-at-publication",
                "Select the decision to evaluate before publishing.")
            : null);
    }

    // Catalog FindVersion often returns the version row without the definition navigation. Identify
    // Evaluate Decision from the loaded definition when present, otherwise from the CLR descriptor.
    private static bool IsEvaluateDecision(IActivityDefinitionVersion version)
    {
        if (TryActivityTypeKey(version) is { } key)
            return StringComparer.Ordinal.Equals(key, EvaluateDecision.ActivityType);
        return HasTypeAlias(version.DescriptorPayload, EvaluateDecision.ActivityType);
    }

    private static string? TryActivityTypeKey(IActivityDefinitionVersion version)
    {
        try
        {
            return version.Definition.ActivityTypeKey;
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static bool HasTypeAlias(JsonElement payload, string typeAlias)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var property in payload.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                (property.NameEquals("typeAlias") ||
                 string.Equals(property.Name, "TypeAlias", StringComparison.OrdinalIgnoreCase)))
                return StringComparer.Ordinal.Equals(property.Value.GetString(), typeAlias);
        }
        return false;
    }
}
