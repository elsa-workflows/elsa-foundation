namespace Elsa.Foundation.Host.ModuleManagement;

/// <summary>
/// Configuration for optional extra #1, the module-management surface (reconcile + shell reload endpoints).
/// The surface is off unless <see cref="Enabled"/> is true, and that decision is read once at startup — so
/// turning it on or off requires a restart, by design. Requests are authenticated with a static API key sent
/// in the <see cref="ApiKeyHeader"/> header; a blank key leaves the surface unreachable even when mapped.
/// </summary>
public sealed record ModuleManagementOptions
{
    public const string SectionName = "Elsa:ModuleManagement";
    public const string ApiKeyHeader = "X-Elsa-Module-Management-Key";

    public bool Enabled { get; init; }
    public string? ApiKey { get; init; }

    public static ModuleManagementOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new ModuleManagementOptions
        {
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
            ApiKey = section["ApiKey"]
        };
    }
}
