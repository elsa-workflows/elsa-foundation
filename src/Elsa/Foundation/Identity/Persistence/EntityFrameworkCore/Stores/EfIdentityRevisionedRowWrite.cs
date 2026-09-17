using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>How one store finds, creates and fills the single revisioned row that holds the record being saved.</summary>
/// <param name="Subject">What the failure messages call the record, for example "Identity application".</param>
/// <param name="FindForWriteAsync">Reads the record's row tracked, or returns <c>null</c> when no row matches the record.</param>
/// <param name="ExistsAsync">Whether the record's row still exists, read untracked after a lost compare-and-swap.</param>
/// <param name="Create">An empty row of the type the record is stored in.</param>
/// <param name="Apply">Copies the record onto a row, leaving its revision alone.</param>
internal sealed record EfIdentityRevisionedRow<TEntity>(
    string Subject,
    Func<CancellationToken, Task<TEntity?>> FindForWriteAsync,
    Func<CancellationToken, Task<bool>> ExistsAsync,
    Func<TEntity> Create,
    Action<TEntity> Apply)
    where TEntity : class, IRevisionedIdentityEntity;

/// <summary>
/// The saves of the Identity stores that keep one revisioned row per record and no mutation receipt. Unlike
/// <see cref="EfIdentityAtomicWrite"/>, nothing is replayed: an identical second create and a stale revision stay
/// conflicts. A lost race is a conflict for create-only and compare-and-swap saves, which retry only a transient provider
/// failure, while an unconditional save retries every lost race and overwrites the winner.
/// </summary>
internal static class EfIdentityRevisionedRowWrite
{
    public static ValueTask SaveUnconditionallyAsync<TEntity>(
        DbContext context,
        EfIdentityRevisionedRow<TEntity> row,
        CancellationToken cancellationToken)
        where TEntity : class, IRevisionedIdentityEntity =>
        EfIdentityStoreSupport.UnconditionalWrites.RunAsync(context, async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = await row.FindForWriteAsync(cancellationToken);
                if (entity is null)
                    context.Add(NewEntity(row));
                else
                {
                    row.Apply(entity);
                    entity.Revision = checked(entity.Revision + 1);
                }

                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
            catch (Exception exception) when (EfIdentityStoreSupport.UnconditionalWrites.ShouldRetry(context, exception))
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw EfIdentityStoreSupport.Failure($"Unable to save the {row.Subject}.", exception);
            }
        }, _ => throw EfIdentityStoreSupport.Failure(
            $"Unable to save the {row.Subject} after bounded concurrency retries.",
            new InvalidOperationException($"The {row.Subject} was concurrently modified.")), cancellationToken);

    public static ValueTask<IamRevisionSaveResult> SaveCreateOnlyAsync<TEntity>(
        DbContext context,
        EfIdentityRevisionedRow<TEntity> row,
        CancellationToken cancellationToken)
        where TEntity : class, IRevisionedIdentityEntity =>
        EfIdentityStoreSupport.TransientWrites.RunUntilSettledAsync<IamRevisionSaveResult>(context, async () =>
        {
            try
            {
                if (await row.FindForWriteAsync(cancellationToken) is not null)
                {
                    context.ChangeTracker.Clear();
                    return Conflict();
                }

                context.Add(NewEntity(row));
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Saved(1);
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }
            catch (Exception exception) when (EfIdentityStoreSupport.TransientWrites.ShouldRetry(context, exception))
            {
                context.ChangeTracker.Clear();
                return EfWriteAttempt<IamRevisionSaveResult>.Retry(exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw EfIdentityStoreSupport.Failure($"Unable to create the {row.Subject}.", exception);
            }
        }, _ => throw EfIdentityStoreSupport.Failure(
            $"Unable to create the {row.Subject} after bounded transient retries.",
            new InvalidOperationException($"The {row.Subject} could not be created.")), cancellationToken);

    public static ValueTask<IamRevisionSaveResult> SaveCompareAndSwapAsync<TEntity>(
        DbContext context,
        EfIdentityRevisionedRow<TEntity> row,
        long expectedVersion,
        CancellationToken cancellationToken)
        where TEntity : class, IRevisionedIdentityEntity =>
        EfIdentityStoreSupport.TransientWrites.RunUntilSettledAsync<IamRevisionSaveResult>(context, async () =>
        {
            try
            {
                var entity = await row.FindForWriteAsync(cancellationToken);
                if (entity is null)
                {
                    context.ChangeTracker.Clear();
                    return NotFound();
                }

                if (entity.Revision != expectedVersion)
                {
                    context.ChangeTracker.Clear();
                    return Conflict();
                }

                var nextRevision = checked(entity.Revision + 1);
                row.Apply(entity);
                entity.Revision = nextRevision;
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Saved(nextRevision);
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                try
                {
                    var exists = await row.ExistsAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return exists ? Conflict() : NotFound();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    context.ChangeTracker.Clear();
                    throw;
                }
                catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
                {
                    context.ChangeTracker.Clear();
                    throw EfIdentityStoreSupport.Failure($"Unable to classify the {row.Subject} concurrency conflict.", exception);
                }
            }
            catch (Exception exception) when (EfIdentityStoreSupport.TransientWrites.ShouldRetry(context, exception))
            {
                context.ChangeTracker.Clear();
                return EfWriteAttempt<IamRevisionSaveResult>.Retry(exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw EfIdentityStoreSupport.Failure($"Unable to update the {row.Subject}.", exception);
            }
        }, _ => throw EfIdentityStoreSupport.Failure(
            $"Unable to update the {row.Subject} after bounded transient retries.",
            new InvalidOperationException($"The {row.Subject} could not be updated.")), cancellationToken);

    private static TEntity NewEntity<TEntity>(EfIdentityRevisionedRow<TEntity> row)
        where TEntity : class, IRevisionedIdentityEntity
    {
        var entity = row.Create();
        row.Apply(entity);
        entity.Revision = 1;
        return entity;
    }

    private static IamRevisionSaveResult Saved(long revision) =>
        new(IamRevisionSaveStatus.Saved, IdentityEntityFrameworkRevisionCodec.FromVersion(revision));

    private static IamRevisionSaveResult Conflict() => new(IamRevisionSaveStatus.Conflict);

    private static IamRevisionSaveResult NotFound() => new(IamRevisionSaveStatus.NotFound);
}
