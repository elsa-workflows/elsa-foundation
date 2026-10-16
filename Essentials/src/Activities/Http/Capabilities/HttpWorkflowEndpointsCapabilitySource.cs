using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Activities.Http.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Capabilities;

/// <summary>Advertises the workflow HTTP endpoint base path configured for the active shell.</summary>
internal sealed class HttpWorkflowEndpointsCapabilitySource : IApiCapabilitySource
{
    private const string CapabilityId = "elsa.http.workflow-endpoints";
    private const string SourceFeatureId = "ActivitiesHttp";
    private readonly IOptions<HttpEndpointOptions> _options;

    public HttpWorkflowEndpointsCapabilitySource(IOptions<HttpEndpointOptions> options) => _options = options;

    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var href = GetRelativeBasePath(_options.Value.BasePath);
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = href is null
            ? []
            : [new(
                CapabilityId,
                1,
                [new ApiCapabilityLink("workflow-http-base", href)],
                SourceFeatureId)];

        return ValueTask.FromResult(declarations);
    }

    private static string? GetRelativeBasePath(string? basePath)
    {
        if (basePath is null)
            throw InvalidBasePath();

        if (basePath.Length == 0)
            return null;

        // The endpoint middleware treats an empty or slash-only base path as disabled.
        var href = basePath.Trim('/');
        if (href.Length == 0)
            return null;

        // The middleware consumes an origin-form path. Do not silently publish a plausible-looking default when
        // the effective option is malformed: a capability would then direct clients to a route the middleware
        // does not own. Requiring one leading slash also rules out URI and protocol-relative host values.
        if (basePath[0] != '/'
            || basePath.StartsWith("//", StringComparison.Ordinal)
            || basePath.Contains('?')
            || basePath.Contains('#')
            || basePath.Contains('%')
            || basePath.Contains('\\')
            || basePath.Any(char.IsControl)
            || basePath.Any(char.IsWhiteSpace))
            throw InvalidBasePath();

        // ASP.NET decodes request paths before matching BasePath. URI escapes in this option could therefore
        // advertise an alias outside the middleware's literal mount. Hosts must configure the decoded path.
        if (href.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw InvalidBasePath();

        if (HasUriScheme(href))
            throw InvalidBasePath();

        // ApiCapabilityLink is the final guard against URI-shaped hrefs as well.
        return href;
    }

    private static bool HasUriScheme(string path)
    {
        var colonIndex = path.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        var slashIndex = path.IndexOf('/');
        return (slashIndex < 0 || colonIndex < slashIndex)
            && Uri.CheckSchemeName(path[..colonIndex]);
    }

    private static InvalidOperationException InvalidBasePath() =>
        new("HttpEndpointOptions.BasePath must be a valid origin-form path without URI escapes or traversal segments.");
}
