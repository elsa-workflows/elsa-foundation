using CShells.Features;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.ExternalAuthentication;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(name: "FoundationIdentityExternalAuthentication", DisplayName = "External authentication",
    Description = "Host-wide external identity connections with deployment ownership and opt-in managed overrides.")]
public class ExternalAuthenticationFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Identity tenant", Description = "Deployment-owned host identity and administration tenant; not a per-connection field.", Category = "Identity", DefaultValue = "default")]
    public string TenantId { get; set; } = "default";
    [ManifestSetting(DisplayName = "Allow database management", Description = "Opt in to managed connections using the selected provider-configuration store.", Category = "Identity", DefaultValue = "false")]
    public bool AllowDatabaseManagement { get; set; }
    [ManifestSetting(DisplayName = "Allow explicit deployment overrides", Description = "Permit authorized full Studio overrides; a same-key row never overrides automatically.", Category = "Security", DefaultValue = "false")]
    public bool AllowDeploymentOverrides { get; set; }
    public List<string> AllowedOverrideKeys { get; set; } = [];
    public List<string> AllowedRoleIds { get; set; } = [];
    public List<ConnectionRecord> Connections { get; set; } = [];

    public virtual void ConfigureServices(IServiceCollection services) => services.AddFoundationExternalAuthentication(options =>
    {
        options.TenantId = TenantId;
        options.AllowDatabaseManagement = AllowDatabaseManagement;
        options.AllowDeploymentOverrides = AllowDeploymentOverrides;
        options.AllowedOverrideKeys = AllowedOverrideKeys;
        options.AllowedRoleIds = AllowedRoleIds;
        options.Connections = Connections;
    });
}
