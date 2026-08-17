using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// Supplies the immutable host/module HTTP route manifest to a workflow route publisher.
/// </summary>
/// <remarks>
/// The provider is the composition seam between a shell's static endpoint publisher and Elsa's workflow-authored
/// route table. Implementations must return routes stamped with exactly one <see cref="HttpRouteOwnershipMetadata"/>
/// record identifying a host or module owner. The workflow route table uses the manifest only for validation; it does
/// not publish or mutate these static routes. Hosts that do not expose a static manifest may use the no-op default.
/// </remarks>
public interface IHttpRouteManifestProvider
{
    /// <summary>Returns the current host/module route manifest for the owning shell.</summary>
    IEnumerable<HttpRouteData> GetRoutes();
}
