using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    /// <summary>
    /// Strips compiler-only handler metadata as the final standard ASP.NET Core convention.
    /// The returned builder is the original builder and no request, routing, authorization, binding,
    /// serialization, or result behavior is changed.
    /// </summary>
    /// <remarks>
    /// DISABLED: this convention no longer runs <see cref="OpenApiLifetimeValidator"/>. The validator
    /// rejects endpoint metadata that references a type from a collectible assembly. A module's request
    /// and response types live in its own package, so in any Nuplane-composed host - where every module
    /// loads collectible - every module endpoint is a violation. The validator throws inside the shell
    /// endpoint registration handler, so the shell activates with zero endpoints while still reporting
    /// healthy and the whole API returns 404. That makes package-composed hosting impossible, which is
    /// the deployment model Elsa.Foundation.Host exists for.
    ///
    /// The retention it guards against is real but narrower than the check: a contract type reaching the
    /// live OpenAPI document service pins its assembly for the host lifetime. A host with no document
    /// service - Elsa.Foundation.Host composes none - retains nothing, so there is nothing to protect.
    /// Restoring the check therefore means gating it on OpenAPI actually being enabled, not reinstating
    /// it unconditionally. Tracked by issue #1414, which also records that a host restart for full
    /// assembly removal is the accepted baseline today.
    ///
    /// <see cref="OpenApiLifetimeValidator"/> and its unit tests are deliberately left intact so the rule
    /// is preserved for whoever re-enables it.
    /// </remarks>
    public static TBuilder RequireStableOpenApi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Finally(RemoveCompilerMetadata);
        return builder;
    }

    private static void RemoveCompilerMetadata(EndpointBuilder builder)
    {
        // RequestDelegateFactory copies handler attributes into endpoint metadata. Compiler-only
        // attributes are not part of the HTTP/OpenAPI contract, but AsyncStateMachineAttribute
        // references the handler's generated implementation type and would pin a collectible owner.
        for (var index = builder.Metadata.Count - 1; index >= 0; index--)
        {
            if (builder.Metadata[index] is System.Runtime.CompilerServices.AsyncStateMachineAttribute
                or System.Diagnostics.DebuggerStepThroughAttribute)
            {
                builder.Metadata.RemoveAt(index);
            }
        }

        // Intentionally NOT calling OpenApiLifetimeValidator.ValidateAndMark(builder).
        // See the remarks on RequireStableOpenApi above, and issue #1414.
    }

    /// <summary>
    /// Preserves the host application's OpenAPI tag without introducing a module-specific endpoint
    /// authoring abstraction. The tag is ordinary ASP.NET Core endpoint metadata and is resolved
    /// from the host environment at mapping time.
    /// </summary>
    public static TBuilder WithHostApplicationOpenApiTag<TBuilder>(
        this TBuilder builder,
        IServiceProvider services)
        where TBuilder : IEndpointConventionBuilder
    {
        var applicationName = services.GetService<IHostEnvironment>()?.ApplicationName;
        return string.IsNullOrWhiteSpace(applicationName)
            ? builder
            : AddMetadata(builder, new HostApplicationOpenApiTagMetadata(applicationName));
    }

    private static TBuilder AddMetadata<TBuilder, TMetadata>(TBuilder builder, TMetadata metadata)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(metadata);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(metadata));
        return builder;
    }

    private sealed record HostApplicationOpenApiTagMetadata(string ApplicationName) : ITagsMetadata
    {
        public IReadOnlyList<string> Tags { get; } = [ApplicationName];
    }
}
