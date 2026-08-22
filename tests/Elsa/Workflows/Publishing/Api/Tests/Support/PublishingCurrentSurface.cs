using Elsa.Api.Compatibility.Testing.Manifests;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// The live Publishing route surface after spec 151.
/// <para>
/// <see cref="PublishingCompatibilityCases.Manifest"/> is deliberately NOT the right place for these
/// changes: it is the immutable record of the pre-migration FastEndpoints capture, it is replayed against
/// the frozen HTTP/OpenAPI corpus, and <c>publishing-before-capture-receipt.json</c> pins its file hash as
/// a capture-runner dependency. This type layers the reviewed spec-151 route-set changes on top of that
/// frozen manifest so the historical record and the current contract stay separately auditable.
/// </para>
/// <para>
/// Every difference between the frozen manifest and <see cref="Manifest"/> is recorded as a two-sided
/// approved difference in <c>Baselines/publishing-approved-differences.json</c>.
/// </para>
/// </summary>
public static class PublishingCurrentSurface
{
    /// <summary>
    /// Routes spec 151 (T117) deliberately retired from the Publishing owner. Slot READS are runtime-owned
    /// now and live at <c>runtime/workflows/activation-slots/...</c>; Publishing keeps only the slot
    /// lifecycle commands (DELETE and POST restore), whose responses still join the publication journal.
    /// </summary>
    public static IReadOnlySet<string> RetiredRouteIds { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "PublicationSlots.List", "PublicationSlots.Get" };

    /// <summary>Routes spec 151 added after the frozen capture was taken.</summary>
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

    /// <summary>The 22 routes the Publishing mapper publishes today.</summary>
    public static IReadOnlyList<PublishingRoute> Manifest { get; } =
        PublishingCompatibilityCases.Manifest
            .Where(route => !RetiredRouteIds.Contains(route.Id))
            .Concat(AddedRoutes)
            .ToArray();

    /// <summary>
    /// The success-response contract for a live route. <see cref="PublishingRoute.Response"/> only knows the
    /// frozen corpus, so routes added after the capture are resolved here.
    /// </summary>
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
