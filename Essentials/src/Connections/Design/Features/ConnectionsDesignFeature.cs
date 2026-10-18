using CShells.Features;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Design.Services;
using Elsa.Connections.Design.Validators;
using Elsa.Connections.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Connections.Design.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Connections")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ConnectionsDesign",
    DisplayName = "Connections Design",
    Description = "Publish-time connection gate: missing or wrong-type connections block; NeedsAuthorization warns only.",
    DependsOn = new object[] { "Connections", "WorkflowDesignValidations" }
)]
public class ConnectionsDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConnections();
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IConnectionPublishTenantAccessor, PersistenceScopeConnectionPublishTenantAccessor>());
        services.AddScoped<IDraftValidator, ConnectionDraftValidator>();
    }
}
