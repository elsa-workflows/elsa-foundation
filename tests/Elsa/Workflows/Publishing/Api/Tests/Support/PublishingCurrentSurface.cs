using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// The live Publishing route surface after the executable-closure export was added.
/// </summary>
/// <remarks>
/// <see cref="PublishingCompatibilityCases.Manifest"/> remains the immutable pre-export corpus. This type
/// layers the reviewed additive route on top so historical HTTP/OpenAPI evidence remains separately auditable.
/// </remarks>
public static class PublishingCurrentSurface
{
    public static IReadOnlyList<PublishingRoute> AddedRoutes { get; } =
    [
        new PublishingRoute(
            "WorkflowExecutable.Export",
            new EndpointIdentity("/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/executable-export", "GET"),
            "/publishing/workflows/version-route/executable-export",
            "read",
            200,
            "none")
    ];

    public static IReadOnlyList<PublishingRoute> Manifest { get; } =
        PublishingCompatibilityCases.Manifest
            .Concat(AddedRoutes)
            .ToArray();

    public static string ResponseFor(PublishingRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Id switch
        {
            "WorkflowExecutable.Export" => "WorkflowArtifactClosure",
            _ => route.Response
        };
    }
}
