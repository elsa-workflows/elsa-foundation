using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Thrown when a pinned conversion plan cannot be safely applied to a runtime value.</summary>
public sealed class RuntimeValueConversionException : InvalidOperationException
{
    public RuntimeValueConversionException(ValueConversionPlan plan, string reason)
        : base(FormatMessage(plan, reason))
    {
        Plan = plan;
        Reason = reason;
    }

    public ValueConversionPlan Plan { get; }
    public string Reason { get; }

    private static string FormatMessage(ValueConversionPlan plan, string reason)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var profile = plan.Profile is null ? "none" : $"{plan.Profile.Id}@{plan.Profile.Version}";
        return $"Value conversion rejected: source representation '{plan.SourceRepresentation}', " +
               $"target contract '{plan.TargetType.Alias} ({plan.TargetType.CollectionKind})', " +
               $"mode/profile '{plan.Mode}/{profile}', reason: {reason}";
    }
}
