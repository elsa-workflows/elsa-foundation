namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

public sealed class OpenIddictEntityFrameworkCoreOptions
{
    public string? ConnectionString { get; set; }

    public bool AutoMigrate { get; set; } = true;
}
