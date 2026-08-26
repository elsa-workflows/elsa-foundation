using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Api.AspNetCore;

/// <summary>Standard ASP.NET Core endpoint conventions for migration metadata.</summary>
public static class EndpointConventionBuilderExtensions
{
    private const string JsonContentType = "application/json";

    private static readonly MethodInfo ApiExplorerDescriptionMethod =
        typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
        ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

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
    /// Applies the endpoint metadata every module REST operation carries: its name and tag, its
    /// owner, its authoring model, the JSON response plus the shared 401/403 pair, an optional
    /// request body, the API Explorer description method, and the unload-safety validation.
    /// </summary>
    /// <remarks>
    /// This is ordinary ASP.NET Core metadata applied through the standard convention builder, not
    /// an endpoint abstraction: routing, binding, serialization, results, and policy execution are
    /// untouched, and the handler is still mapped with <c>MapGet</c>, <c>MapPost</c>, and friends.
    /// <para>
    /// Authorization is deliberately not applied here. Permissions are owned by Foundation Identity,
    /// and pulling them in would give the shared endpoint layer a dependency on it. Call
    /// <c>RequirePermission</c> at the mapping site.
    /// </para>
    /// </remarks>
    /// <param name="name">The endpoint name, unique across the host.</param>
    /// <param name="owner">The stable owning module identifier.</param>
    /// <param name="responseType">The success response body type. Null or <see langword="void"/> means no body.</param>
    /// <param name="requestType">The request body type, if the operation accepts one.</param>
    /// <param name="accepts">Content types the request body is accepted as. Defaults to JSON.</param>
    /// <param name="responseStatus">The success status code. Defaults to 200, or 204 when there is no response body.</param>
    /// <param name="tag">The OpenAPI tag. Defaults to <paramref name="owner"/>.</param>
    /// <param name="documentAuthResponses">
    /// Whether the shared 401/403 response pair is documented. Public operations that never
    /// challenge keep their published 200-only documents by opting out.
    /// </param>
    /// <param name="successContentType">
    /// The documented success content type. Defaults to JSON; a streaming operation documents its
    /// event-stream media type here.
    /// </param>
    public static TBuilder WithModuleOperation<TBuilder>(
        this TBuilder builder,
        string name,
        string owner,
        Type? responseType = null,
        Type? requestType = null,
        string[]? accepts = null,
        int? responseStatus = null,
        string? tag = null,
        bool documentAuthResponses = true,
        string? successContentType = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        // A void response carries no body, so it advertises no content type. Callers spell this as
        // either a null responseType or typeof(void); both must produce identical metadata.
        var hasBody = responseType is not null && responseType != typeof(void);
        var status = responseStatus ?? (hasBody ? StatusCodes.Status200OK : StatusCodes.Status204NoContent);
        var metadata = new List<object>
        {
            new ProducesResponseTypeMetadata(status, responseType ?? typeof(void), hasBody ? [successContentType ?? JsonContentType] : [])
        };

        if (documentAuthResponses)
        {
            metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []));
            metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));
        }

        if (requestType is not null)
            metadata.Add(new AcceptsMetadata(accepts ?? [JsonContentType], requestType, false));

        builder.WithName(name)
            .WithTags(tag ?? owner)
            .WithOwner(owner)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithMetadata(metadata.ToArray());

        return builder.WithApiExplorerDescription().RequireStableOpenApi();
    }

    /// <summary>
    /// Publishes <see cref="RequestDelegate.Invoke"/> as the endpoint's description method.
    /// </summary>
    /// <remarks>
    /// API Explorer needs a <see cref="MethodInfo"/> in endpoint metadata to derive an
    /// <c>ApiDescription</c>; without one the endpoint never reaches the OpenAPI document, and a
    /// test that inspects the document passes vacuously. It must also be the last <see cref="MethodInfo"/>
    /// in the collection, because <c>EndpointMetadataCollection.GetMetadata&lt;T&gt;()</c> selects
    /// the last match. Publishing the handler's own method instead would root its declaring
    /// assembly through API Explorer, so the stable framework method is used deliberately.
    /// </remarks>
    public static TBuilder WithApiExplorerDescription<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        AddMetadata(builder, ApiExplorerDescriptionMethod);

    /// <summary>
    /// Validates the completed endpoint metadata as the final standard ASP.NET Core convention.
    /// The returned builder is the original builder and no request, routing, authorization, binding,
    /// serialization, or result behavior is changed.
    /// </summary>
    public static TBuilder RequireStableOpenApi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Finally(RemoveCompilerMetadataAndValidate);
        return builder;
    }

    private static void RemoveCompilerMetadataAndValidate(EndpointBuilder builder)
    {
        // RequestDelegateFactory copies handler attributes into endpoint metadata. Compiler-only
        // attributes are not part of the HTTP/OpenAPI contract, but AsyncStateMachineAttribute
        // references the handler's generated implementation type and would pin a collectible owner.
        // This runs whether or not the boundary is enforced: a state machine pins its owner even in
        // a host that never builds a document.
        for (var index = builder.Metadata.Count - 1; index >= 0; index--)
        {
            if (builder.Metadata[index] is System.Runtime.CompilerServices.AsyncStateMachineAttribute
                or System.Diagnostics.DebuggerStepThroughAttribute)
            {
                builder.Metadata.RemoveAt(index);
            }
        }

        if (!EnforcementEnabled(builder.ApplicationServices))
            return;

        OpenApiLifetimeValidator.ValidateAndMark(builder);
    }

    /// <summary>
    /// Fail-closed: anything other than an explicit, resolvable suppression enforces the boundary,
    /// so an unconfigured host or one with no service provider keeps the guard.
    /// </summary>
    private static bool EnforcementEnabled(IServiceProvider? services) =>
        services?.GetService<IOptions<OpenApiLifetimeEnforcementOptions>>()?.Value.Enabled ?? true;

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

/// <summary>
/// The attribute form of <see cref="EndpointConventionBuilderExtensions.AllowPublic{TBuilder}"/> for
/// endpoint classes: applies the anonymous-access marker together with the public security
/// disposition the endpoint inventory demands, so the attribute and the imperative form cannot drift.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowPublicAttribute(string category, string reason) : Attribute, IEndpointConventionAttribute
{
    public string Category { get; } = category;
    public string Reason { get; } = reason;

    public void Apply(IEndpointConventionBuilder builder) => builder.AllowPublic(Category, Reason);
}
