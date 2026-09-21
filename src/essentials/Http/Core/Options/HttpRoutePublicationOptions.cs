namespace Elsa.Http.Core.Options;

/// <summary>
/// Describes where endpoint-relative workflow HTTP routes are externally published. The activity-side middleware
/// configures this together with its request base path so collision validation uses the same address clients use.
/// </summary>
public sealed class HttpRoutePublicationOptions
{
    public string BasePath { get; set; } = "/workflows/http";
}
