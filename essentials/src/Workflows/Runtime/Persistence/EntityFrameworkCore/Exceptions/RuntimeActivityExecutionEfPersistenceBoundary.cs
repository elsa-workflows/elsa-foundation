using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

internal static class RuntimeActivityExecutionEfPersistenceBoundary
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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsStoreBoundaryFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw Normalize(operation, identity, exception);
        }
    }

    public static RuntimeActivityExecutionEntityFrameworkPersistenceException Normalize(
        string operation,
        string identity,
        Exception innerException) =>
        new(operation, identity, $"The EF runtime activity-execution store failed while {operation} '{identity}'.", innerException);
}
