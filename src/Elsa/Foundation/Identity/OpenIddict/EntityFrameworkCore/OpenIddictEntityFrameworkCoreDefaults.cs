namespace Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

/// <summary>EF-oracle-only defaults for the transitional OpenIddict persistence implementation.</summary>
public static class OpenIddictEntityFrameworkCoreDefaults
{
    public const string DefaultConnectionString = "Data Source=identity.db";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory_OpenIddict";
}
