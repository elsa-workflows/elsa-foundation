namespace Elsa.Connections.Persistence.EntityFrameworkCore.Entities;

internal sealed class ConnectionRecord
{
    public string TenantId { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string NameSearchKey { get; set; } = "";
    public string DisplayNameSearchKey { get; set; } = "";
    public string TypeLookupKey { get; set; } = "";
    public string SchemeLookupKey { get; set; } = "";
    public int Version { get; set; }
    public string Payload { get; set; } = "{}";
    public string SchemaVersion { get; set; } = ConnectionsEfModule.SchemaVersion;
}

internal sealed class ConnectionSecretReferenceRecord
{
    public string TenantId { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string SecretLookupHash { get; set; } = "";
}
