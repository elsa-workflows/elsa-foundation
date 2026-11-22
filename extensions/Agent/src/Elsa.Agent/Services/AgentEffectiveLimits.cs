using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

// A caller may restrict the immutable published bounds; the protected request carries the effective
// value separately so lowering a budget never rewrites the pinned provider descriptor.
internal static class AgentEffectiveLimits
{
    internal static AgentTurnLimits For(AgentTurnRequest request)
    {
        var published = request.Descriptor.Limits ?? throw AgentModelRunner.Failure("agent.descriptor.invalid", "The agent has no published execution limits.");
        var effective = request.EffectiveLimits ?? published;
        if (effective.MaximumModelCalls < 1 || effective.MaximumModelCalls > published.MaximumModelCalls ||
            effective.TimeoutMilliseconds < 1_000 || effective.TimeoutMilliseconds > published.TimeoutMilliseconds ||
            effective.MaximumOutputTokens is <= 0 || published.MaximumOutputTokens is { } ceiling &&
            (effective.MaximumOutputTokens is null || effective.MaximumOutputTokens > ceiling))
            throw AgentModelRunner.Failure("agent.limits.invalid", "Invocation limits must be positive and cannot widen the published agent bounds.");
        return effective;
    }
}
