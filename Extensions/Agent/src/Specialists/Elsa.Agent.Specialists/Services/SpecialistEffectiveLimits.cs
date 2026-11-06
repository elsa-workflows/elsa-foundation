using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Services;

// A caller may restrict the immutable published bounds; the protected request carries the effective
// value separately so lowering a budget never rewrites the pinned provider descriptor.
internal static class SpecialistEffectiveLimits
{
    internal static SpecialistTurnLimits For(SpecialistTurnRequest request)
    {
        var published = request.Descriptor.Limits ?? throw SpecialistModelRunner.Failure("specialist.descriptor.invalid", "The specialist has no published execution limits.");
        var effective = request.EffectiveLimits ?? published;
        if (effective.MaximumModelCalls < 1 || effective.MaximumModelCalls > published.MaximumModelCalls ||
            effective.TimeoutMilliseconds < 1_000 || effective.TimeoutMilliseconds > published.TimeoutMilliseconds ||
            effective.MaximumOutputTokens is <= 0 || published.MaximumOutputTokens is { } ceiling &&
            (effective.MaximumOutputTokens is null || effective.MaximumOutputTokens > ceiling))
            throw SpecialistModelRunner.Failure("specialist.limits.invalid", "Invocation limits must be positive and cannot widen the published specialist bounds.");
        return effective;
    }
}
