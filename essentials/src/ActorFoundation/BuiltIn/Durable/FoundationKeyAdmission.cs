using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Shared admission decisions for an exact caller-key binding and the observed creation at its address.</summary>
internal static class FoundationKeyAdmission
{
    internal enum Outcome { Accepted, Duplicate, Repeat, Missing, Conflict, ObligationOutstanding, Invalid }
    internal sealed record Receipt(Outcome Outcome, string? Reference = null);
    internal sealed record Binding(InstanceRef Address, long Creation, string Reference, long ReuseAfter);
    internal readonly record struct Head(long Creation, bool Retired, bool HasCascadeObligation);
    internal readonly record struct StartPlan(Outcome Outcome, CreationStart? Creation = null, long ReuseAfter = 0);

    internal static string QualifiedName(string tenant, string key) => tenant + "_" + key;

    internal static InstanceRef ProposedAddress(string qualifiedName, int partitions) =>
        InstanceRef.In(InstanceRef.Spread(qualifiedName, partitions), qualifiedName);

    internal static bool FitsRequest(string key, string operation, string kind, string? payload, StartSpec? start) =>
        FoundationMetadataLimits.FitsName(key) && FoundationMetadataLimits.FitsName(operation, preservesCodeUnits: true) &&
        (start is null
            ? FoundationMetadataLimits.FitsKind(kind) && FoundationMetadataLimits.FitsContent(payload)
            : kind == Kinds.Start && payload is null && start is { Parent: null, ParentCreation: 0, Creation: null } &&
              Enum.IsDefined(start.History) && HeaderLimits.FitsStart(start.Model, start.Definition, start.Input));

    internal static StartPlan PlanStart(Binding? binding, Head? head, long now, long window, string operationDigest)
    {
        if (binding is { } bound)
        {
            // Admission may have advanced the binding while the owner still sees the predecessor's retirement.
            if (head is not { Retired: true } previous || previous.Creation != bound.Creation) return new(Outcome.Repeat);
            if (previous.HasCascadeObligation) return new(Outcome.ObligationOutstanding);
            if (now < bound.ReuseAfter) return new(Outcome.Repeat);
        }

        var generation = binding?.Creation ?? 0;
        return new(Outcome.Accepted, new(checked(generation + 1), generation, operationDigest), checked(now + window));
    }
}
