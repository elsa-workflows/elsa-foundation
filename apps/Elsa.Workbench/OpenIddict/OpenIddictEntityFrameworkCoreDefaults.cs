namespace Elsa.Workbench.OpenIddict;

/// <summary>Workbench defaults for the host-selected OpenIddict vendor persistence implementation.</summary>
public static class OpenIddictEntityFrameworkCoreDefaults
{
    public const string DefaultConnectionString = "Data Source=identity.db";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory_OpenIddict";
}
