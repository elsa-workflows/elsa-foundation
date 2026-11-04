using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Host authority is required; an installed tool may add its owner's current resource rules.</summary>
internal static class AgentToolAccess
{
    internal static async ValueTask<AgentPolicyDecision> AuthorizeAsync(IAgentTool tool, IAgentToolAuthorizer host,
        string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken)
    {
        // Only the owning tool's own decision may say its owner could not decide. A host denial stays a denial, and a
        // host's advisory violation can never turn an owner's plain denial into the unavailable outcome.
        var admission = HostOnly(await host.AuthorizeAsync(tool.Descriptor, actorId, tenantId, target, cancellationToken));
        if (!admission.Allowed || tool is not IAgentToolAuthorization owner) return admission;
        var owned = await owner.AuthorizeAsync(actorId, tenantId, target, cancellationToken);
        return new(owned.Allowed, admission.Violations.Concat(owned.Violations).ToArray());
    }

    private static AgentPolicyDecision HostOnly(AgentPolicyDecision decision) =>
        decision.Violations.Any(violation => violation.OwnerUnavailable)
            ? decision with { Violations = decision.Violations.Select(violation => violation with { OwnerUnavailable = false }).ToArray() }
            : decision;

    /// <summary>The fixed message of the owner-unavailable outcome; an owner's own violation message is never forwarded.</summary>
    internal const string OwnerUnavailableMessage = "The owning module could not establish current authority for this operation.";

    /// <summary>
    /// The unavailable outcome (503, with the owner's stable code) when a refused decision from <see cref="AuthorizeAsync"/>
    /// says the owning tool's owner could not decide; otherwise null, and the caller keeps its own denial.
    /// </summary>
    /// <remarks>
    /// Only an owner-namespaced code is reported. A code in the engine's reserved <c>agent.</c> namespace (such as
    /// <c>agent.proposal.expired</c>), or one that is empty, overlong or not a plain dotted identifier, cannot be spoofed
    /// through this outcome: the refusal stays the caller's ordinary denial.
    /// </remarks>
    internal static AgentError? OwnerUnavailable(AgentPolicyDecision decision) =>
        !decision.Allowed && decision.Violations.FirstOrDefault(violation => violation.OwnerUnavailable && IsOwnerCode(violation.Code)) is { } unavailable
            ? new(unavailable.Code, OwnerUnavailableMessage, 503)
            : null;

    /// <summary>Whether a context diagnostic is a source's owner-unavailable refusal with an owner code (never the engine's).</summary>
    internal static bool IsOwnerUnavailable(AgentContributionDiagnostic diagnostic) =>
        diagnostic.OwnerUnavailable && !string.Equals(diagnostic.ContributorId, "host", StringComparison.Ordinal) && IsOwnerCode(diagnostic.Code);

    private static bool IsOwnerCode(string? code) =>
        code is { Length: > 0 and <= 128 } && code.Contains('.')
        && !code.StartsWith("agent.", StringComparison.OrdinalIgnoreCase)
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
