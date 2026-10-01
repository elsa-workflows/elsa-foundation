using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class EfRuntimeOperationalStoreSupport
{
    /// <summary>
    /// The retry an activation projection switch of either EF projection store runs through, and the trigger-binding
    /// store's read of a projection's state (#2265). An attempt that read a state that moved asks for another itself. One
    /// whose write lost to a revision another writer moved, or that the provider chose as a deadlock victim, is retried
    /// once its own transaction has rolled back (<see cref="TryCommitAndClearAsync"/>). Each call says what running out means.
    /// </summary>
    public static readonly EfWriteRetry ProjectionSwitches = new(
        EfWriteRetry.DefaultMaxAttempts,
        exception => EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency | EfWriteConflict.Transient));

    /// <summary>
    /// Whether the activation projection state <paramref name="state"/> selects no longer stands where <paramref name="read"/>
    /// saw it, or no longer stands missing when it was. Its generation is compared (<see cref="IActivationProjectionState"/>),
    /// so a projection deleted and prepared again with other content has moved, though its revision is back where it was.
    /// Read without tracking, so a tracked state cannot answer for the row.
    /// </summary>
    public static async Task<bool> StateMovedAsync<TState>(IQueryable<TState> state, TState? read, CancellationToken ct)
        where TState : class, IActivationProjectionState =>
        await state.AsNoTracking().Select(x => new ProjectionGeneration(x.Revision, x.ProjectionFingerprint)).SingleOrDefaultAsync(ct)
        != (read is null ? null : new ProjectionGeneration(read.Revision, read.ProjectionFingerprint));

    private sealed record ProjectionGeneration(long Revision, string ProjectionFingerprint);

    /// <summary>Saves the context's changes, unless there are none, commits the store's own transaction, and clears the tracker.</summary>
    public static async Task CommitAndClearAsync(this DbContext context, IDbContextTransaction transaction, CancellationToken ct, bool noChanges = false)
    {
        if (!noChanges)
            await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        context.ChangeTracker.Clear();
    }

    /// <summary>Rolls back the store's own transaction, whatever state the provider left it in, and clears the tracker.</summary>
    public static async Task RollbackAndClearAsync(this DbContext context, IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(); } catch { }
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Commits the store's own transaction, or, when the commit fails, rolls it back and disposes it, then returns the failure
    /// if <paramref name="retry"/> retries it, so the attempt can ask for another, and otherwise throws it as
    /// <paramref name="operation"/>'s <see cref="CommitFailure"/>.
    /// </summary>
    public static async Task<Exception?> TryCommitAndClearAsync(this DbContext context, IDbContextTransaction transaction, EfWriteRetry? retry, string operation, CancellationToken ct)
    {
        try
        {
            await context.CommitAndClearAsync(transaction, ct);
            return null;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            await context.RollbackAndClearAsync(transaction);
            // Disposed before the retry judges the failure, so its rule against retrying a transient conflict inside an open
            // transaction meets a caller's transaction only, never this store's own, even when the rollback failed.
            await transaction.DisposeAsync();
            return retry?.ShouldRetry(context, exception) == true ? exception : throw CommitFailure(operation, exception);
        }
    }

    /// <summary>Commits the store's own transaction once, with no retry (<see cref="TryCommitAndClearAsync"/>).</summary>
    public static Task CommitMutationAndClearAsync(this DbContext context, IDbContextTransaction transaction, string operation, CancellationToken ct) =>
        context.TryCommitAndClearAsync(transaction, retry: null, operation, ct);

    /// <summary>
    /// What <paramref name="operation"/> reports for a commit that failed with <paramref name="failure"/>, or for a retry that
    /// ran out on it: a lost race, or no failure at all when the last attempt read a state that moved, changed concurrently;
    /// a transient conflict, as one; any other provider failure, as not committed.
    /// </summary>
    public static InvalidOperationException CommitFailure(string operation, Exception? failure) =>
        failure is null || EfRelationalExceptionClassifier.IsSaveConflict(failure, EfWriteConflict.Concurrency) ? ChangedConcurrently(operation, failure)
        : EfRelationalExceptionClassifier.IsSaveConflict(failure, EfWriteConflict.Transient) ? new($"{operation} encountered a transient write conflict; retry the operation.", failure)
        : new($"{operation} could not be committed.", failure);

    public static InvalidOperationException ChangedConcurrently(string operation, Exception? conflict) =>
        new($"{operation} changed concurrently; retry the operation.", conflict);

    public static string RequireScope(IPersistenceAccessContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        var scope = accessor.Current.RequireScope().Value;
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(accessor));
        return scope;
    }

    public static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    public static string Encode(string value) => EfRelationalIdentity.Encode(value);
    public static string Decode(string value) => EfRelationalIdentity.Decode(value);
    public static string Hash(string value) => EfRelationalIdentity.Hash(value);
    public static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    /// <summary>
    /// An order key over the first <see cref="RuntimeOperationalStateEfModule.IdentityMaximumLength"/> code
    /// units of a composed identity. The runtime composes work-item and commit identities well past that, and
    /// a full-width key would exceed the index-key budget of SQL Server and MySQL. Every query that orders by
    /// such a key carries the identity hash as its tie-break, so paging stays total and stable.
    /// </summary>
    public static string OrderPrefix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Order(value.Length <= RuntimeOperationalStateEfModule.IdentityMaximumLength
            ? value
            : value[..RuntimeOperationalStateEfModule.IdentityMaximumLength]);
    }
    public static string CompositeId(string scope, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(values);

        return EfRelationalIdentity.HashLengthFramed([scope, .. values]);
    }

    /// <summary>The offset of <paramref name="value"/> in whole minutes, as the <c>*OffsetMinutes</c> columns store it.</summary>
    public static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    /// <summary>Rebuilds an instant from its <c>*UtcTicks</c> and <c>*OffsetMinutes</c> columns.</summary>
    public static DateTimeOffset FromUtcTicks(long utcTicks, int offsetMinutes) =>
        new DateTimeOffset(new DateTime(utcTicks, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    /// <summary>
    /// An optional instant as its <c>*UtcTicks</c> and <c>*OffsetMinutes</c> column pair, both null when it is absent, so a
    /// claim column pair is always written together: <c>(row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = TimestampColumns(value)</c>.
    /// </summary>
    public static (long? UtcTicks, int? OffsetMinutes) TimestampColumns(DateTimeOffset? value) =>
        value is { } instant ? (instant.UtcTicks, OffsetMinutes(instant)) : (null, null);

    public static void EnsureScope(string actual, string expected) {
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException("The persisted runtime row belongs to another persistence scope.");
    }

    public static string Cursor(IRuntimeRecoveryContinuationCodec codec, string purpose, string scope, string workflow, string last)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join("\u001f", Encode(scope), Encode(workflow), Encode(last)));
        return codec.Encode(purpose, payload);
    }

    public static (string Scope, string Workflow, string Last) DecodeCursor(IRuntimeRecoveryContinuationCodec codec, string purpose, string token)
    {
        try
        {
            var values = Encoding.UTF8.GetString(codec.Decode(purpose, token)).Split('\u001f');
            if (values.Length != 3)
                throw new FormatException();
            return (EfRelationalIdentity.Decode(values[0]), EfRelationalIdentity.Decode(values[1]), EfRelationalIdentity.Decode(values[2]));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("The runtime operational-state continuation is invalid.", nameof(token), exception);
        }
    }

    public static void ValidateCursor(RuntimeStorePageRequest request, string scope, string workflow, (string Scope, string Workflow, string Last) cursor)
    {
        if (!StringComparer.Ordinal.Equals(cursor.Scope, scope) || !StringComparer.Ordinal.Equals(cursor.Workflow, workflow))
            throw new ArgumentException("The runtime operational-state continuation belongs to another query.", nameof(request));
        ValidateIdentity(cursor.Last, nameof(request));
    }
}
