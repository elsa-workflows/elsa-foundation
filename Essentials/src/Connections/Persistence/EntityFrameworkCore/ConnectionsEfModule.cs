using Elsa.Persistence.EntityFramework;

namespace Elsa.Connections.Persistence.EntityFrameworkCore;

internal static class ConnectionsEfModule
{
    public const string HistoryModuleName = "ElsaConnections";
    public const string TableName = "elsa_connections";
    public const string ReferenceTableName = "elsa_connection_secret_references";
    public const string SchemaFamily = "Connections";
    public const string SchemaVersion = "1.0.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(ConnectionsEfModule).Assembly, SchemaFamily);
}
