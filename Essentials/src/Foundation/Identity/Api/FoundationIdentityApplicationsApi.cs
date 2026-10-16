using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api;

/// <summary>Maps the application-management endpoint classes under <c>/_elsa/identity/applications</c>.</summary>
internal static class FoundationIdentityApplicationsApi
{
    /// <summary>The owner of every <c>/_elsa/identity/*</c> route this assembly maps.</summary>
    internal const string OwnerId = "Elsa.Foundation.Identity.Api";

    public static void MapFoundationIdentityApplicationsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapEndpointGroup(OwnerId, FoundationIdentityApplicationsJsonContext.Default, tag: "Identity");
        api.MapEndpointsFrom(typeof(FoundationIdentityApplicationsApi).Assembly);
    }
}
