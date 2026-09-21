using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Api.AspNetCore;

/// <summary>Elsa's endpoint metadata vocabulary, as standard ASP.NET Core endpoint conventions.</summary>
/// <remarks>
/// The endpoint pipeline itself is <see href="https://www.nuget.org/packages/NativeEndpoints">NativeEndpoints</see>.
/// What stays here is what the package has no opinion about: which module owns an endpoint, how its
/// access is dispositioned, which authoring model published it, and which host credential enforces
/// it. Those are Elsa's inventory and governance concepts, they are read by the endpoint manifest and
/// the security tests, and they are attached through the package's ordinary convention seams.
/// </remarks>
public static class ElsaEndpointConventions
{
    private const string JsonContentType = "application/json";

    public static TBuilder WithOwner<TBuilder>(this TBuilder builder, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(EndpointOwnershipMetadata.Module(owner));

    public static TBuilder WithHostOwner<TBuilder>(this TBuilder builder, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(EndpointOwnershipMetadata.Host(owner));

    public static TBuilder WithDynamicShellOwner<TBuilder>(this TBuilder builder, string owner, string shellId, int generation)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(EndpointOwnershipMetadata.DynamicShell(owner, shellId, generation));

    public static TBuilder AllowPublic<TBuilder>(this TBuilder builder, string category, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointMetadata(EndpointSecurityDispositionMetadata.Public(category, reason));
        return builder.AddEndpointMetadata(new AllowAnonymousAttribute());
    }

    public static TBuilder RequireHostCredential<TBuilder>(this TBuilder builder, string credential, string owner)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointMetadata(EndpointSecurityDispositionMetadata.HostCredential(credential, owner));
        builder.AddEndpointMetadata(new EndpointHostCredentialEnforcementMetadata(credential, owner));
        return builder.AddEndpointMetadata(new AuthorizeAttribute { AuthenticationSchemes = credential });
    }

    /// <summary>
    /// Records a host-credential filter that is enforced by an endpoint filter
    /// rather than ASP.NET Core authorization middleware.
    /// </summary>
    public static TBuilder WithHostCredentialEnforcement<TBuilder>(this TBuilder builder, string credential, string owner)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(new EndpointHostCredentialEnforcementMetadata(credential, owner));

    public static TBuilder RequireNamedPolicy<TBuilder>(this TBuilder builder, string policy, string owner)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointMetadata(EndpointSecurityDispositionMetadata.NamedPolicy(policy, owner));
        return builder.AddEndpointMetadata(new AuthorizeAttribute(policy));
    }

    public static TBuilder WithSecurityDisposition<TBuilder>(this TBuilder builder, EndpointSecurityDispositionMetadata disposition)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(disposition);

    public static TBuilder WithAuthoringModel<TBuilder>(this TBuilder builder, string model)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointMetadata(new EndpointAuthoringMetadata(model));

    /// <summary>
    /// The operation convention every Elsa endpoint group installs, replacing the package's default.
    /// </summary>
    /// <remarks>
    /// Two things about Elsa's published documents are deliberate rather than incidental, and both
    /// are decided here rather than inherited:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Endpoint names follow <c>{Owner}Endpoints{Operation}</c> with the dots stripped, not the
    /// package's <c>{group}_{operation}</c>. Clients generate from those identifiers, so the scheme
    /// is frozen; an operation whose published id predates the scheme pins it outright with
    /// <c>options.Name</c>, which arrives here as <see cref="EndpointOperationContext.Name"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// The 401/403 pair is documented unconditionally. The package infers it from the authorization
    /// metadata an endpoint actually carries, which is the better default and the wrong answer here:
    /// inferring would drop the pair from every <c>AllowPublic</c> endpoint and move documents that
    /// are already published.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public static readonly EndpointOperationConvention ElsaModuleOperation = (builder, context) =>
        builder
            // The package attaches this itself only from its own default convention, and the lifetime
            // validator names the group from it when it reports a violation.
            .WithEndpointGroup(context.GroupName)
            .WithModuleOperation(
                context.Name ?? $"{context.GroupName.Replace(".", string.Empty, StringComparison.Ordinal)}Endpoints{context.Operation}",
                context.GroupName,
                context.ResponseType,
                context.RequestType,
                context.Accepts,
                context.DocumentedStatus,
                context.Tag,
                context.DocumentAuthResponses ?? true,
                context.SuccessContentType);

    /// <summary>
    /// Applies the endpoint metadata every module REST operation carries: its name and tag, its
    /// owner, its authoring model, the JSON response plus the shared 401/403 pair, an optional
    /// request body, the API Explorer description method, and the unload-safety validation.
    /// </summary>
    /// <remarks>
    /// Authorization is deliberately not applied here. Permissions are owned by Foundation Identity,
    /// and pulling them in would give the shared endpoint layer a dependency on it. Call
    /// <c>RequirePermission</c> at the mapping site.
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

        // Ownership is validated as a final convention alongside the package's lifetime check, so
        // both see the completed metadata rather than whatever had been added by this point.
        builder.Finally(endpointBuilder => EndpointOwnershipValidator.Validate(endpointBuilder));
        return builder.WithApiExplorerDescription().RequireStableEndpointMetadata();
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
            : builder.AddEndpointMetadata(new HostApplicationOpenApiTagMetadata(applicationName));
    }

    private sealed record HostApplicationOpenApiTagMetadata(string ApplicationName) : ITagsMetadata
    {
        public IReadOnlyList<string> Tags { get; } = [ApplicationName];
    }
}

/// <summary>
/// The attribute form of <see cref="ElsaEndpointConventions.AllowPublic{TBuilder}"/> for endpoint
/// classes: applies the anonymous-access marker together with the public security disposition the
/// endpoint inventory demands, so the attribute and the imperative form cannot drift.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowPublicAttribute(string category, string reason) : Attribute, IEndpointConventionAttribute
{
    public string Category { get; } = category;
    public string Reason { get; } = reason;

    public void Apply(IEndpointConventionBuilder builder) => builder.AllowPublic(Category, Reason);
}
