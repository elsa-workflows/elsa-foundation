using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// The live Publishing route surface after the executable-closure export and the publication-record read
/// and modular designer Test Drive routes were added.
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
            "none"),
        new PublishingRoute(
            "Publications.Get",
            new EndpointIdentity("/publishing/publications/{publicationId}", "GET"),
            "/publishing/publications/publication-route",
            "read",
            200,
            "GetPublicationRecord"),
        new("TestRuns.Options", new EndpointIdentity("/publishing/workflows/drafts/test-run-options", "POST"),
            "/publishing/workflows/drafts/test-run-options", "manage", 200, "DraftTestRunOptionsRequest"),
        new("TestRuns.Get", new EndpointIdentity("/publishing/workflow-test-runs/{testRunId}", "GET"),
            "/publishing/workflow-test-runs/test-route", "manage", 200, "GetWorkflowTestRun"),
        new("TestRuns.Stop", new EndpointIdentity("/publishing/workflow-test-runs/{testRunId}/stop", "POST"),
            "/publishing/workflow-test-runs/test-route/stop", "manage", 202, "none"),
        new("TestRuns.Waits", new EndpointIdentity("/publishing/workflow-test-runs/{testRunId}/waits", "GET"),
            "/publishing/workflow-test-runs/test-route/waits", "manage", 200, "TestRunWaitsRequest"),
        new("TestRuns.DeliverEvent", new EndpointIdentity("/publishing/workflow-test-runs/{testRunId}/events", "POST"),
            "/publishing/workflow-test-runs/test-route/events", "manage", 200, "TestRunEventRequest")
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
            "Publications.Get" => "PublicationView",
            "TestRuns.Options" => "DraftTestRunOptionsView",
            "TestRuns.Get" or "TestRuns.Stop" => "WorkflowTestRunView",
            "TestRuns.Waits" => "TestRunWaitsView",
            "TestRuns.DeliverEvent" => "TestRunEventView",
            _ => route.Response
        };
    }
}
