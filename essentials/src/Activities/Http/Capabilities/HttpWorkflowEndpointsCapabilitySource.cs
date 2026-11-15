using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Activities.Http.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Capabilities;

/// <summary>Advertises the workflow HTTP endpoint base path configured for the active shell.</summary>
internal sealed class HttpWorkflowEndpointsCapabilitySource : IApiCapabilitySource
{
    private const string CapabilityId = "elsa.http.workflow-endpoints";
    private const string SourceFeatureId = "ActivitiesHttp";
    private readonly IOptions<HttpEndpointOptions> _options;
    private readonly ILogger<HttpWorkflowEndpointsCapabilitySource> _logger;

    public HttpWorkflowEndpointsCapabilitySource(
        IOptions<HttpEndpointOptions> options,
        ILogger<HttpWorkflowEndpointsCapabilitySource> logger)
    {
        _options = options;
        _logger = logger;
    }

    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRelativeBasePath(_options.Value.BasePath, out var href))
        {
            _logger.LogWarning(
                "Omitting the workflow HTTP base-path capability because HttpEndpointOptions.BasePath is not a usable origin-form path.");
            return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([]);
        }

        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = href is null
            ? []
            : [new(
                CapabilityId,
                1,
                [new ApiCapabilityLink("workflow-http-base", href)],
                SourceFeatureId)];

        return ValueTask.FromResult(declarations);
    }

    private static bool TryGetRelativeBasePath(string? basePath, out string? href)
    {
        href = null;
        if (basePath is null)
            return false;

        if (basePath.Length == 0)
            return true;

        // The endpoint middleware treats an empty or slash-only base path as disabled.
        var path = basePath.Trim('/');
        if (path.Length == 0)
            return true;

        // The middleware consumes an origin-form path. Do not silently publish a plausible-looking default when
        // the effective option is malformed: a capability would then direct clients to a route the middleware
        // does not own. Requiring one leading slash also rules out URI and protocol-relative host values.
        if (basePath[0] != '/'
            || basePath.StartsWith("//", StringComparison.Ordinal)
            || basePath.Contains('?')
            || basePath.Contains('#')
            || basePath.Contains('%')
            || basePath.Contains('\\')
            || basePath.Any(char.IsControl))
            return false;

        // ASP.NET decodes request paths before matching BasePath. URI escapes in this option could therefore
        // advertise an alias outside the middleware's literal mount. Hosts must configure the decoded path.
        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            return false;

        // Capability links are resolved as relative URLs. Escape whitespace and a colon in the first segment so
        // a literal mount remains relative (for example, "api:v1" cannot be read as a URI scheme).
        href = string.Join("/", segments.Select((segment, index) =>
            segment.Any(char.IsWhiteSpace) || (index == 0 && segment.Contains(':'))
                ? Uri.EscapeDataString(segment)
                : segment));

        // Do not let the capability link's absolute-URI guard turn an unusable option into a catalog failure.
        return !Uri.TryCreate(href, UriKind.Absolute, out _);
    }
}
