using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

internal static class RuntimeArtifactEfPersistenceBoundary
{
    public static async Task<T> QueryAsync<T>(
        DbContext context,
        string operation,
        string identity,
        Func<Task<T>> query)
    {
        try
        {
            return await query();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw new RuntimeArtifactEntityFrameworkPersistenceException(
                operation,
                identity,
                $"The EF runtime artifact store failed while {operation} runtime artifact '{identity}'.",
                exception);
        }
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is not (InvalidDataException or OperationCanceledException) &&
        exception is (DbException or DbUpdateException or InvalidOperationException);
}
