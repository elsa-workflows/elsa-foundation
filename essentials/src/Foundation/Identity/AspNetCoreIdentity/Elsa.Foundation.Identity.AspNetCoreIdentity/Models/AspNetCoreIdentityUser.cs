using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Models;

public sealed class AspNetCoreIdentityUser : IdentityUser
{
    public string TenantId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}
