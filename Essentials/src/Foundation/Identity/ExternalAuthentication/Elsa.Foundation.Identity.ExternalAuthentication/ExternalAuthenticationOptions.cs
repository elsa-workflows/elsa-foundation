using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication;

public sealed class ExternalAuthenticationOptions
{
    /// <summary>Host-wide identity/administration tenant. Connections have no editable tenant field.</summary>
    public string TenantId { get; set; } = "default";
    public bool AllowDatabaseManagement { get; set; }
    public bool AllowDeploymentOverrides { get; set; }
    public List<string> AllowedOverrideKeys { get; set; } = [];
    public List<string> AllowedRoleIds { get; set; } = [];
    public List<ConnectionRecord> Connections { get; set; } = [];
}
