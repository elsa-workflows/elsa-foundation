using CShells;
using Elsa.Api.AspNetCore;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using System.Reflection;

namespace Elsa.Http;

/// <summary>
/// Projects root and active-shell ASP.NET endpoints into the HTTP manifest used by workflow route publication.
/// The adapter belongs to Elsa.Http because its output is an Elsa HTTP contract; shared endpoint metadata remains
/// in the framework-neutral Elsa.Api.AspNetCore package.
/// </summary>
internal sealed class AspNetCoreHttpRouteManifestProvider(
    IEnumerable<EndpointDataSource> endpointDataSources,
    ShellSettings shellSettings) : IHttpRouteManifestProvider
{
    private const string ShellEndpointMetadataName = "ShellEndpointMetadata";

    private readonly IReadOnlyCollection<EndpointDataSource> _endpointDataSources = endpointDataSources.ToArray();
    private readonly string _shellId = shellSettings.Id.ToString();

    public IEnumerable<HttpRouteData> GetRoutes() => _endpointDataSources
        .SelectMany(source => source.Endpoints)
        .OfType<RouteEndpoint>()
        .Where(BelongsToCurrentComposition)
        .Select(Project)
        .ToArray();

    private bool BelongsToCurrentComposition(RouteEndpoint endpoint)
    {
        var shellMetadata = endpoint.Metadata.FirstOrDefault(metadata => metadata.GetType().Name == ShellEndpointMetadataName);
        if (shellMetadata is null)
            return true;

        var shellId = shellMetadata.GetType().GetProperty("ShellId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(shellMetadata)?.ToString();
        return string.IsNullOrWhiteSpace(shellId) || StringComparer.Ordinal.Equals(shellId, _shellId);
    }

    private static HttpRouteData Project(RouteEndpoint endpoint)
    {
        var route = endpoint.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(route))
            route = "/" + string.Join("/", endpoint.RoutePattern.PathSegments.Select(segment => segment.ToString()));

        var ownershipMetadata = endpoint.Metadata.OfType<EndpointOwnershipMetadata>().ToArray();
        if (ownershipMetadata.Length > 1)
            throw new InvalidOperationException($"Static endpoint '{route}' has {ownershipMetadata.Length} ownership metadata records; exactly zero or one is allowed.");

        var securityMetadata = endpoint.Metadata.OfType<EndpointSecurityDispositionMetadata>().ToArray();
        if (securityMetadata.Length > 1)
            throw new InvalidOperationException($"Static endpoint '{route}' has {securityMetadata.Length} security-disposition metadata records; exactly zero or one is allowed.");

        var ownership = ownershipMetadata.SingleOrDefault() ?? EndpointOwnershipMetadata.Host("host.legacy");
        var security = securityMetadata.SingleOrDefault() ??
                       EndpointSecurityDispositionMetadata.Public("compatibility", "Static endpoint without explicit security disposition.");
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .ToArray() ?? [];

        return new HttpRouteData(route)
        {
            Methods = methods,
            Metadata = [ProjectOwnership(ownership), ProjectSecurity(security)]
        };
    }

    private static HttpRouteOwnershipMetadata ProjectOwnership(EndpointOwnershipMetadata ownership) =>
        ownership.Kind switch
        {
            EndpointOwnerKind.Host => HttpRouteOwnershipMetadata.Host(ownership.OwnerId),
            EndpointOwnerKind.Module => HttpRouteOwnershipMetadata.Module(ownership.OwnerId),
            EndpointOwnerKind.DynamicShell when ownership.ShellId is not null && ownership.Generation is not null =>
                HttpRouteOwnershipMetadata.DynamicShell(ownership.OwnerId, ownership.ShellId, ownership.Generation.Value),
            _ => throw new InvalidOperationException($"Endpoint owner '{ownership.OwnerId}' has incomplete dynamic-shell identity.")
        };

    private static HttpRouteSecurityDispositionMetadata ProjectSecurity(EndpointSecurityDispositionMetadata security) =>
        security.Kind switch
        {
            EndpointSecurityDispositionKind.Permission => HttpRouteSecurityDispositionMetadata.Permission(security.Value!),
            EndpointSecurityDispositionKind.Public => HttpRouteSecurityDispositionMetadata.Public(security.Category!, security.Reason!),
            EndpointSecurityDispositionKind.HostCredential => HttpRouteSecurityDispositionMetadata.HostCredential(security.Value!, security.Owner!),
            EndpointSecurityDispositionKind.NamedPolicy => HttpRouteSecurityDispositionMetadata.NamedPolicy(security.Value!, security.Owner!),
            _ => throw new InvalidOperationException($"Unsupported endpoint security disposition '{security.Kind}'.")
        };
}
