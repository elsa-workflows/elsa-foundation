using System.Runtime.CompilerServices;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>
/// The engine's private record of which owner-unavailable context outcomes are genuine (spec 1193, story 5).
/// </summary>
/// <remarks>
/// <para>A diagnostic counts as an owner-unavailable refusal only if <c>CollectAgentContext</c> recorded that exact instance,
/// returned by an installed <see cref="Core.Contracts.IAgentContextProvider"/> under its own descriptor id. Any other event
/// handler can add diagnostics to the public <see cref="AgentContextCollecting.Diagnostics"/> collection, but it cannot
/// produce a recorded instance, so a forged marker is cleared and the refusal stays an ordinary incomplete-context failure.</para>
/// </remarks>
internal static class AgentContextOwnerMarkers
{
    private static readonly ConditionalWeakTable<AgentContextCollecting, HashSet<AgentContributionDiagnostic>> Recorded = new();

    internal static void Record(AgentContextCollecting collecting, AgentContributionDiagnostic diagnostic) =>
        Recorded.GetValue(collecting, _ => new HashSet<AgentContributionDiagnostic>(ReferenceEqualityComparer.Instance)).Add(diagnostic);

    /// <summary>Whether this exact diagnostic is a recorded source marker with an owner code.</summary>
    internal static bool IsRecorded(AgentContextCollecting collecting, AgentContributionDiagnostic diagnostic) =>
        Recorded.TryGetValue(collecting, out var recorded) && recorded.Contains(diagnostic) && AgentToolAccess.IsOwnerUnavailable(diagnostic);

    internal static AgentError Unavailable(string code) => new(code, AgentToolAccess.OwnerUnavailableMessage, 503);
}
