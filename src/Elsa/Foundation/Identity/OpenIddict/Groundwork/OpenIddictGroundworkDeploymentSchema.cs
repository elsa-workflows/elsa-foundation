using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork;

/// <summary>
/// Public parameterless deployment source for hosts that select only the
/// OpenIddict Groundwork declaration. A full host composition must select this
/// feature explicitly; this type does not register stores or select a provider.
/// </summary>
public sealed class OpenIddictGroundworkDeploymentSchema : GroundworkDeploymentSchemaManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        [typeof(OpenIddictGroundworkStorageManifestSource)];
}
