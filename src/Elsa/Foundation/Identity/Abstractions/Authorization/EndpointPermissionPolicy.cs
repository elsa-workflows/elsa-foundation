using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

/// <summary>
/// The single owner of Elsa's endpoint permission composition: every endpoint accepts the wildcard
/// <see cref="PermissionNames.All"/> permission in addition to its own, so a change to how
/// permissions compose lands in exactly one place (issue #414).
/// </summary>
/// <remarks>
/// This rule previously lived in the first-party FastEndpoints project as
/// <c>ElsaEndpointPermissions</c>, because the six endpoint base classes that consumed it derived
/// from disjoint FastEndpoints bases whose <c>Permissions</c> method is protected: the call site
/// could not be shared, but the composition rule could. Nothing about the rule is
/// FastEndpoints-specific — it formats a Foundation Identity policy and returns an ASP.NET Core
/// convention — so retiring that project moved it here, beside the codec it formats with.
/// </remarks>
public static class EndpointPermissionPolicy
{
    public static string[] Compose(string[] permissions) => [PermissionNames.All, .. permissions];

    /// <summary>
    /// Creates the one Foundation Identity policy an endpoint is secured with.
    /// </summary>
    /// <remarks>
    /// An endpoint without action permissions retains the historical wildcard requirement as a
    /// canonical single policy. Action-scoped endpoints retain the wildcard-plus-action OR behavior
    /// through one canonical any policy; passing separate policy names would make an authoring model
    /// that ANDs its policies — FastEndpoints among them — compose them as AND instead.
    /// </remarks>
    public static string ComposePolicy(string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var codec = new PermissionPolicyCodec();
        return permissions.Length == 0
            ? codec.Format(PermissionPolicyDescriptor.Single(PermissionNames.All))
            : codec.Format(PermissionPolicyDescriptor.Any(Compose(permissions)));
    }

    /// <summary>
    /// Builds the standard owner, authoring-model, and security-disposition metadata for an endpoint.
    /// </summary>
    /// <param name="endpointType">The endpoint type; its assembly supplies the stable owner.</param>
    /// <param name="permissions">The action permissions to compose with the wildcard.</param>
    /// <param name="authoringModel">
    /// The authoring model to record, from <see cref="EndpointAuthoringModels"/>. This was fixed to
    /// FastEndpoints while the rule lived in the FastEndpoints project; it is a parameter now so the
    /// rule stays neutral about which model applies it.
    /// </param>
    public static Action<RouteHandlerBuilder> StandardMetadata(
        Type endpointType,
        string[] permissions,
        string authoringModel)
    {
        ArgumentNullException.ThrowIfNull(endpointType);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoringModel);

        var owner = endpointType.Assembly.GetName().Name ?? throw new InvalidOperationException(
            $"Endpoint type '{endpointType.FullName}' has no stable assembly owner.");
        return builder => builder
            .WithOwner(owner)
            .WithAuthoringModel(authoringModel)
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.Permission(ComposePolicy(permissions)));
    }
}
