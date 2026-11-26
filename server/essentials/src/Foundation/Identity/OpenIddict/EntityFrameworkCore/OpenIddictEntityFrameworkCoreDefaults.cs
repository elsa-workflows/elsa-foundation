namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>Defaults of the host-composed OpenIddict token store.</summary>
public static class OpenIddictEntityFrameworkCoreDefaults
{
    public const string DefaultConnectionString = "Data Source=identity.db";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory_OpenIddict";
}
