namespace Elsa.Workbench.OpenIddict;

/// <summary>Configuration owned solely by Workbench's OpenIddict vendor persistence choice.</summary>
public sealed class WorkbenchOpenIddictEntityFrameworkCoreOptions
{
    public string? ConnectionString { get; set; }

    public bool AutoMigrate { get; set; } = true;
}
