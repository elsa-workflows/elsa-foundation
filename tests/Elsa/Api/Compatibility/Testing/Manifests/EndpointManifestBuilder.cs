using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using System.Text.Json.Serialization;

namespace Elsa.Api.Compatibility.Testing.Manifests;

/// <summary>Captures the published ASP.NET Core endpoint surface in a stable, reviewable form.</summary>
public sealed class EndpointManifestBuilder
{
    private readonly IReadOnlyList<EndpointDataSource> _dataSources;

    public EndpointManifestBuilder(IEnumerable<EndpointDataSource> dataSources)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        _dataSources = dataSources.Where(x => x is not null).ToArray();
    }

    public EndpointManifest Build() => Build(new EndpointManifestBuilderOptions());

    public EndpointManifest Build(EndpointManifestBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpoints = _dataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Select(endpoint => CreateEntry(endpoint, options))
            .OrderBy(entry => entry.Route.Value, StringComparer.Ordinal)
            .ThenBy(entry => string.Join(',', entry.Methods), StringComparer.Ordinal)
            .ThenBy(entry => entry.Owner, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceIdentity ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => CompatibilityJson.Serialize(entry), StringComparer.Ordinal)
            .ToArray();

        return new EndpointManifest(endpoints);
    }

    public string BuildJson(EndpointManifestBuilderOptions? options = null) =>
        CompatibilityJson.Serialize(Build(options ?? new EndpointManifestBuilderOptions()));

    public static EndpointManifest Capture(IEnumerable<EndpointDataSource> dataSources, EndpointManifestBuilderOptions? options = null) =>
        new EndpointManifestBuilder(dataSources).Build(options ?? new EndpointManifestBuilderOptions());

    private static EndpointManifestEntry CreateEntry(Endpoint endpoint, EndpointManifestBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var route = endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? RenderRoutePattern(routeEndpoint.RoutePattern)
            : endpoint.Metadata.OfType<IRouteDiagnosticsMetadata>().FirstOrDefault()?.Route ?? endpoint.DisplayName ?? "/";
        route ??= "/";
        var normalizedRoute = new NormalizedRoute(route);

        var methods = endpoint.Metadata.OfType<IHttpMethodMetadata>()
            .SelectMany(metadata => metadata.HttpMethods)
            .Select(method => new NormalizedHttpMethod(method).Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToArray();
        if (methods.Length == 0)
            methods = ["*"];

        var ownerMetadata = endpoint.Metadata.OfType<EndpointOwnershipMetadata>().ToArray();
        var owners = ownerMetadata.Select(x => x.Owner).Distinct(StringComparer.Ordinal).ToArray();
        var authoringMetadata = endpoint.Metadata.OfType<EndpointAuthoringMetadata>().ToArray();
        var dispositions = endpoint.Metadata.OfType<EndpointSecurityDispositionMetadata>().ToArray();
        var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var hasAuthorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
        var problems = new List<string>();
        if (ownerMetadata.Length != 1)
            problems.Add(ownerMetadata.Length == 0
                ? "missing endpoint owner"
                : owners.Length == 1 ? "duplicate endpoint owner metadata" : "conflicting endpoint owners");
        if (dispositions.Length != 1)
            problems.Add(dispositions.Length == 0 ? "missing security disposition" : "ambiguous security dispositions");
        if (authoringMetadata.Length != 1)
            problems.Add(authoringMetadata.Length == 0 ? "missing endpoint authoring model" : "ambiguous endpoint authoring models");
        if (dispositions.Length == 1)
        {
            if (dispositions[0].Kind != EndpointSecurityDispositionKind.Public && allowsAnonymous)
                problems.Add("protected security disposition conflicts with anonymous endpoint metadata");
            if (dispositions[0].Kind == EndpointSecurityDispositionKind.Public)
            {
                if (!allowsAnonymous)
                    problems.Add("public security disposition is missing anonymous endpoint metadata");
                if (hasAuthorization)
                    problems.Add("public security disposition conflicts with authorization endpoint metadata");
            }
            else if (!hasAuthorization)
            {
                problems.Add("protected security disposition is missing authorization endpoint metadata");
            }
        }

        if (options.ValidateMetadata && problems.Count > 0)
            throw new EndpointManifestValidationException(endpoint, normalizedRoute, methods, problems);

        var security = dispositions.SingleOrDefault();
        var requestContentTypes = endpoint.Metadata.OfType<IAcceptsMetadata>()
            .SelectMany(metadata => metadata.ContentTypes)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var responses = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Select(metadata => new EndpointResponseMetadata(
                metadata.StatusCode,
                metadata.Type?.FullName,
                metadata.ContentTypes.Where(type => !string.IsNullOrWhiteSpace(type))
                    .Select(type => type.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(response => response.StatusCode)
            .ThenBy(response => response.BodyType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(response => string.Join(',', response.ContentTypes), StringComparer.Ordinal)
            .ToArray();

        var contentTypes = requestContentTypes
            .Concat(responses.SelectMany(response => response.ContentTypes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceIdentity = endpoint.Metadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName;
        var ownership = ownerMetadata.SingleOrDefault();
        return new EndpointManifestEntry(
            normalizedRoute,
            methods,
            endpoint.DisplayName ?? endpoint.GetType().FullName ?? "<anonymous>",
            owners.SingleOrDefault() ?? string.Empty,
            authoringMetadata.SingleOrDefault()?.Model ?? "Unknown",
            security,
            contentTypes,
            responses,
            sourceIdentity)
        {
            OwnerKind = ownership?.Kind ?? EndpointOwnerKind.Module,
            ShellId = ownership?.ShellId,
            Generation = ownership?.Generation
        };
    }

    private static string RenderRoutePattern(RoutePattern pattern) => "/" + string.Join("/", pattern.PathSegments.Select(segment =>
        string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => RenderParameter(parameter),
            _ => throw new InvalidOperationException($"Unsupported route-pattern part '{part.GetType().FullName}'.")
        }))));

    private static string RenderParameter(RoutePatternParameterPart parameter)
    {
        var catchAll = parameter.IsCatchAll ? parameter.EncodeSlashes ? "*" : "**" : "";
        var policies = string.Concat(parameter.ParameterPolicies.Select(policy => $":{policy.Content}"));
        var defaultValue = parameter.Default is null ? "" : $"={parameter.Default}";
        var optional = parameter.IsOptional ? "?" : "";
        return $"{{{catchAll}{parameter.Name}{policies}{defaultValue}{optional}}}";
    }
}

public sealed record EndpointManifestBuilderOptions(bool ValidateMetadata = true);

public sealed record EndpointManifest(IReadOnlyList<EndpointManifestEntry> Entries)
{
    public string Serialize() => CompatibilityJson.Serialize(this);
}

public sealed record EndpointManifestEntry(
    NormalizedRoute Route,
    IReadOnlyList<string> Methods,
    string DisplayName,
    string Owner,
    string AuthoringModel,
    EndpointSecurityDispositionMetadata? SecurityDisposition,
    IReadOnlyList<string> ContentTypes,
    IReadOnlyList<EndpointResponseMetadata> Responses,
    string? SourceIdentity)
{
    public EndpointOwnerKind OwnerKind { get; init; } = EndpointOwnerKind.Module;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShellId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Generation { get; init; }

    [JsonIgnore]
    public IReadOnlyList<EndpointIdentity> Identities => Methods.Select(method => new EndpointIdentity(Route.Value, method)).ToArray();
}

public sealed record EndpointResponseMetadata(int StatusCode, string? BodyType, IReadOnlyList<string> ContentTypes);

public sealed class EndpointManifestValidationException : InvalidOperationException
{
    public EndpointManifestValidationException(Endpoint endpoint, NormalizedRoute route, IReadOnlyList<string> methods, IReadOnlyList<string> problems)
        : base($"Endpoint '{endpoint.DisplayName ?? endpoint.GetType().Name}' ({string.Join(',', methods)} {route}) is invalid: {string.Join(", ", problems)}.")
    {
        Route = route;
        Methods = methods;
        Problems = problems;
        DisplayName = endpoint.DisplayName;
    }

    public NormalizedRoute Route { get; }
    public IReadOnlyList<string> Methods { get; }
    public IReadOnlyList<string> Problems { get; }
    public string? DisplayName { get; }
}
