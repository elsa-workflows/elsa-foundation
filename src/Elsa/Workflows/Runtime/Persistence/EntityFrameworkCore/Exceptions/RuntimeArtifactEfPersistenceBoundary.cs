using Elsa.Persistence.EntityFramework;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

internal static class RuntimeArtifactEfPersistenceBoundary
{
    public static async Task ExecuteAsync(
        DbContext context,
        string operation,
        string identity,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (InvalidOperationException exception)
        {
            context.ChangeTracker.Clear();
            throw new RuntimeArtifactEntityFrameworkPersistenceException(
                operation,
                identity,
                $"The EF runtime artifact store failed while {operation} runtime artifact '{identity}'.",
                exception);
        }
    }

    public static async Task<T> ExecuteAsync<T>(
        DbContext context,
        string operation,
        string identity,
        Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (InvalidOperationException exception)
        {
            context.ChangeTracker.Clear();
            throw new RuntimeArtifactEntityFrameworkPersistenceException(
                operation,
                identity,
                $"The EF runtime artifact store failed while {operation} runtime artifact '{identity}'.",
                exception);
        }
    }

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
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw new RuntimeArtifactEntityFrameworkPersistenceException(
                operation,
                identity,
                $"The EF runtime artifact store failed while {operation} runtime artifact '{identity}'.",
                exception);
        }
    }

}
