using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication;

public sealed class ExternalAuthenticationOptions
{
    /// <summary>Host-wide identity/administration tenant. Connections have no editable tenant field.</summary>
    public string TenantId { get; set; } = "default";
    /// <summary>Trusted public backend URL, including its path base. Never derived from incoming Host headers.</summary>
    public string? PublicBaseUrl { get; set; }
    public string CallbackPath { get; set; } = "/identity/external";
    public List<string> AllowedReturnUrlOrigins { get; set; } = [];
    public TimeSpan FlowLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PreviewResultLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public bool AllowDatabaseManagement { get; set; }
    public bool AllowDeploymentOverrides { get; set; }
    public List<string> AllowedOverrideKeys { get; set; } = [];
    public List<string> AllowedRoleIds { get; set; } = [];
    public List<ConnectionRecord> Connections { get; set; } = [];
}
