using CShells;
using Elsa.Api.AspNetCore;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using System.Globalization;
using System.Reflection;
using System.Text;

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
            route = ReconstructRoute(endpoint.RoutePattern);

        var ownershipMetadata = endpoint.Metadata.OfType<EndpointOwnershipMetadata>().ToArray();
        if (ownershipMetadata.Length > 1)
            throw new InvalidOperationException($"Static endpoint '{route}' has {ownershipMetadata.Length} ownership metadata records; exactly zero or one is allowed.");

        var securityMetadata = endpoint.Metadata.OfType<EndpointSecurityDispositionMetadata>().ToArray();
        if (securityMetadata.Length > 1)
            throw new InvalidOperationException($"Static endpoint '{route}' has {securityMetadata.Length} security-disposition metadata records; exactly zero or one is allowed.");

        var ownership = ownershipMetadata.SingleOrDefault() ?? EndpointOwnershipMetadata.Host("host.legacy");
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .ToArray() ?? [];

        return new HttpRouteData(route)
        {
            Methods = methods,
            Metadata = [ProjectOwnership(ownership), ProjectSecurity(endpoint, ownership, securityMetadata.SingleOrDefault())]
        };
    }

    private static string ReconstructRoute(RoutePattern pattern) =>
        "/" + string.Join("/", pattern.PathSegments.Select(ReconstructSegment));

    private static string ReconstructSegment(RoutePatternPathSegment segment) =>
        string.Concat(segment.Parts.Select(ReconstructPart));

    private static string ReconstructPart(RoutePatternPart part) =>
        part switch
        {
            RoutePatternLiteralPart literal => EscapeLiteral(literal.Content),
            RoutePatternSeparatorPart separator => EscapeLiteral(separator.Content),
            RoutePatternParameterPart parameter => ReconstructParameter(parameter),
            _ => throw new InvalidOperationException($"Unsupported route-pattern part '{part.GetType().FullName}'.")
        };

    private static string ReconstructParameter(RoutePatternParameterPart parameter)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        if (parameter.IsCatchAll)
            builder.Append(parameter.EncodeSlashes ? '*' : "**");
        builder.Append(parameter.Name);

        foreach (var policy in parameter.ParameterPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy.Content))
                throw new InvalidOperationException($"Route parameter '{parameter.Name}' uses an object policy that has no reconstructable inline text.");

            builder.Append(':').Append(policy.Content);
        }

        if (parameter.Default is not null)
            builder.Append('=').Append(Convert.ToString(parameter.Default, CultureInfo.InvariantCulture));
        if (parameter.IsOptional)
            builder.Append('?');
        return builder.Append('}').ToString();
    }

    private static string EscapeLiteral(string value) => value.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    private static HttpRouteOwnershipMetadata ProjectOwnership(EndpointOwnershipMetadata ownership) =>
        ownership.Kind switch
        {
            EndpointOwnerKind.Host => HttpRouteOwnershipMetadata.Host(ownership.OwnerId),
            EndpointOwnerKind.Module => HttpRouteOwnershipMetadata.Module(ownership.OwnerId),
            EndpointOwnerKind.DynamicShell when ownership.ShellId is not null && ownership.Generation is not null =>
                HttpRouteOwnershipMetadata.DynamicShell(ownership.OwnerId, ownership.ShellId, ownership.Generation.Value),
            _ => throw new InvalidOperationException($"Endpoint owner '{ownership.OwnerId}' has incomplete dynamic-shell identity.")
        };

    private static HttpRouteSecurityDispositionMetadata ProjectSecurity(
        RouteEndpoint endpoint,
        EndpointOwnershipMetadata ownership,
        EndpointSecurityDispositionMetadata? security)
    {
        if (security is not null)
            return security.Kind switch
            {
                EndpointSecurityDispositionKind.Permission => HttpRouteSecurityDispositionMetadata.Permission(security.Value!),
                EndpointSecurityDispositionKind.Public => HttpRouteSecurityDispositionMetadata.Public(security.Category!, security.Reason!),
                EndpointSecurityDispositionKind.HostCredential => HttpRouteSecurityDispositionMetadata.HostCredential(security.Value!, security.Owner!),
                EndpointSecurityDispositionKind.NamedPolicy => HttpRouteSecurityDispositionMetadata.NamedPolicy(security.Value!, security.Owner!),
                _ => throw new InvalidOperationException($"Unsupported endpoint security disposition '{security.Kind}'.")
            };

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return HttpRouteSecurityDispositionMetadata.Public("compatibility", "Static endpoint explicitly allows anonymous access.");

        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (authorizeData.Count > 0)
        {
            var policies = authorizeData
                .Select(data => data.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Select(policy => policy!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(policy => policy, StringComparer.Ordinal)
                .ToArray();
            return policies.Length == 0
                ? HttpRouteSecurityDispositionMetadata.AuthenticatedPrincipal(ownership.OwnerId)
                : HttpRouteSecurityDispositionMetadata.NamedPolicies(policies, ownership.OwnerId);
        }

        return HttpRouteSecurityDispositionMetadata.Public("compatibility", "Static endpoint without authorization metadata.");
    }
}
