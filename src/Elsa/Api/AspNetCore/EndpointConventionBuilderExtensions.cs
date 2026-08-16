using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;

namespace Elsa.Api.AspNetCore;

/// <summary>Standard ASP.NET Core endpoint conventions for migration metadata.</summary>
public static class EndpointConventionBuilderExtensions
{
    public static TBuilder WithOwner<TBuilder>(this TBuilder builder, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, EndpointOwnershipMetadata.Module(owner));

    public static TBuilder WithHostOwner<TBuilder>(this TBuilder builder, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, EndpointOwnershipMetadata.Host(owner));

    public static TBuilder WithDynamicShellOwner<TBuilder>(this TBuilder builder, string owner, string shellId, int generation)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, EndpointOwnershipMetadata.DynamicShell(owner, shellId, generation));

    public static TBuilder AllowPublic<TBuilder>(this TBuilder builder, string category, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        AddMetadata(builder, EndpointSecurityDispositionMetadata.Public(category, reason));
        return AddMetadata(builder, new AllowAnonymousAttribute());
    }

    public static TBuilder RequireHostCredential<TBuilder>(this TBuilder builder, string credential, string owner)
        where TBuilder : IEndpointConventionBuilder
    {
        AddMetadata(builder, EndpointSecurityDispositionMetadata.HostCredential(credential, owner));
        AddMetadata(builder, new EndpointHostCredentialEnforcementMetadata(credential, owner));
        return AddMetadata(builder, new AuthorizeAttribute { AuthenticationSchemes = credential });
    }

    /// <summary>
    /// Records a host-credential filter that is enforced by an endpoint filter
    /// rather than ASP.NET Core authorization middleware.
    /// </summary>
    public static TBuilder WithHostCredentialEnforcement<TBuilder>(this TBuilder builder, string credential, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, new EndpointHostCredentialEnforcementMetadata(credential, owner));

    public static TBuilder RequireNamedPolicy<TBuilder>(this TBuilder builder, string policy, string owner)
        where TBuilder : IEndpointConventionBuilder
    {
        AddMetadata(builder, EndpointSecurityDispositionMetadata.NamedPolicy(policy, owner));
        return AddMetadata(builder, new AuthorizeAttribute(policy));
    }

    public static TBuilder WithSecurityDisposition<TBuilder>(this TBuilder builder, EndpointSecurityDispositionMetadata disposition)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, disposition);

    public static TBuilder WithAuthoringModel<TBuilder>(this TBuilder builder, string model)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, new EndpointAuthoringMetadata(model));

    private static TBuilder AddMetadata<TBuilder, TMetadata>(TBuilder builder, TMetadata metadata)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(metadata);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(metadata));
        return builder;
    }
}
