using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Host authority is required; an installed tool may add its owner's current resource rules.</summary>
internal static class WeaverToolAccess
{
    internal static ValueTask<WeaverPolicyDecision> AuthorizeAsync(IWeaverTool tool, IWeaverToolAuthorizer host,
        string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken)
        => AuthorizeAsync(tool, tool.Descriptor, host, actorId, tenantId, target, cancellationToken);

    internal static async ValueTask<WeaverPolicyDecision> AuthorizeAsync(IWeaverTool tool, WeaverToolDescriptor descriptor, IWeaverToolAuthorizer host,
        string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WeaverModuleCodes.Validate(descriptor.ModuleCodes);
        // Only the owning tool's own decision may say its owner could not decide. A host denial stays a denial, and a
        // host's advisory violation can never turn an owner's plain denial into the unavailable outcome.
        var hostDecision = await host.AuthorizeAsync(descriptor, actorId, tenantId, target, cancellationToken);
        // A host can ignore cancellation. Its late answer must not start an owning module's callback.
        cancellationToken.ThrowIfCancellationRequested();
        var admission = HostOnly(hostDecision);
        if (!admission.Allowed || tool is not IWeaverToolAuthorization owner) return admission;
        var owned = await owner.AuthorizeAsync(actorId, tenantId, target, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(owned.Allowed, admission.Violations.Concat(owned.Violations
            .Select(violation => WeaverModuleCodes.Violation(violation, descriptor.ModuleCodes, ownerOrigin: true))).ToArray());
    }

    private static WeaverPolicyDecision HostOnly(WeaverPolicyDecision decision) =>
        decision.Violations.Any(violation => violation.OwnerUnavailable)
            ? decision with { Violations = decision.Violations.Select(violation => violation with { OwnerUnavailable = false, Message = WeaverModuleCodes.UnsupportedMessage }).ToArray() }
            : decision;

    /// <summary>The fixed message of the owner-unavailable outcome; an owner's own violation message is never forwarded.</summary>
    internal const string OwnerUnavailableMessage = "The owning module could not establish current authority for this operation.";

    /// <summary>
    /// The unavailable outcome (503, with the owner's stable code) when a refused decision from <see cref="AuthorizeAsync"/>
    /// says the owning tool's owner could not decide; otherwise null, and the caller keeps its own denial.
    /// </summary>
    /// <remarks>
    /// Only an owner-namespaced code is reported. A code in the engine's reserved <c>weaver.</c> namespace (such as
    /// <c>weaver.proposal.expired</c>), or one that is empty, overlong or not a plain dotted identifier, cannot be spoofed
    /// through this outcome: the refusal stays the caller's ordinary denial.
    /// </remarks>
    internal static WeaverError? OwnerUnavailable(WeaverPolicyDecision decision, bool declaredCodes = false) =>
        !decision.Allowed && decision.Violations.FirstOrDefault(violation => violation.OwnerUnavailable && IsOwnerCode(violation.Code)) is { } unavailable
            ? new WeaverError(unavailable.Code, OwnerUnavailableMessage, 503) { OwnerUnavailable = declaredCodes }
            : null;

    /// <summary>Whether a context diagnostic is a source's owner-unavailable refusal with an owner code (never the engine's).</summary>
    internal static bool IsOwnerUnavailable(WeaverContributionDiagnostic diagnostic) =>
        diagnostic.OwnerUnavailable && !string.Equals(diagnostic.ContributorId, "host", StringComparison.Ordinal) && IsOwnerCode(diagnostic.Code);

    private static bool IsOwnerCode(string? code) =>
        WeaverModuleCodes.LegacyOwnerCode(code);
}
