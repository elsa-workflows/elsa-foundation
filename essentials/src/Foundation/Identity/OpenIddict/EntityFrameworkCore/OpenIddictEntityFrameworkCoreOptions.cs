namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>The OpenIddict token store's connection and migration settings, read from the default shell's OpenIddict settings.</summary>
public sealed class OpenIddictEntityFrameworkCoreOptions
{
    public string? ConnectionString { get; set; }

    public bool AutoMigrate { get; set; } = true;
}
