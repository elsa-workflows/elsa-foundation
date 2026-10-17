using Microsoft.Data.Sqlite;

namespace Elsa.UnifiedExecution.WorkProof;

internal static class SqliteScopeParameters
{
    public static void Add(SqliteCommand command, WorkScope scope)
    {
        command.Parameters.AddWithValue("$tenant", scope.TenantId);
        command.Parameters.AddWithValue("$scope", scope.PersistenceScope);
        command.Parameters.AddWithValue("$authority", scope.Authority);
    }
}
