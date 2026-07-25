namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>Configuration owned solely by the transitional EF Core oracle.</summary>
public sealed class OpenIddictEntityFrameworkCoreOptions
{
    public string? ConnectionString { get; set; }

    public bool AutoMigrate { get; set; } = true;
}
