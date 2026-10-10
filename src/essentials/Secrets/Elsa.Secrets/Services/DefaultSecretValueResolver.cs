using System.ComponentModel;
using System.Data.Common;
using System.Net.Sockets;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

/// <summary>
/// Default <see cref="ISecretValueResolver"/>: applies the lifecycle policy to the referenced secret
/// and reads the payload from the per-provider store. Owns the whole resolution path (it does not
/// route through <see cref="ISecretManager"/>, which is the lifecycle facade only).
/// </summary>
/// <remarks>
/// A failure to read the secret, from <see cref="ISecretRepository"/> (its metadata) or from its <see cref="ISecretStore"/>
/// (its payload), is a result rather than a throw, with a fixed error that carries none of the exception's text,
/// classified by what failed (spec 188, FR-003). An outage, a condition that may clear on its own, is
/// <see cref="SecretResolutionFailureCode.StoreUnavailable"/>, transient: a <see cref="TimeoutException"/>,
/// <see cref="IOException"/>, <see cref="SocketException"/> or a cancellation the caller did not request (a provider
/// timeout) in the chain, or a <see cref="DbException"/> that is an outage (<see cref="IsProviderOutage"/>); the first
/// <see cref="DbException"/> in the chain decides for everything beneath it. Anything else is
/// <see cref="SecretResolutionFailureCode.CorruptState"/>, permanent: a damaged or mismatched row, a table or column
/// the database does not have, a schema version this build cannot read, an identity the repository refuses, a store the
/// host does not register, and a payload that does not decrypt or parse. A canceled token still propagates as a
/// cancellation. Without a throw, a store that returns no payload (the built-in stores do so for a version that lacks
/// the entry they read, or for a configuration value the host does not have, which no retry supplies) and a payload
/// without a value are both <see cref="SecretResolutionFailureCode.CorruptState"/>. A name the name validator refuses is
/// <see cref="SecretResolutionFailureCode.NotFound"/>, before the repository is read.
/// </remarks>
public sealed class DefaultSecretValueResolver(
    ISecretRepository repository,
    ISecretNameValidator nameValidator,
    ISecretStoreRegistry storeRegistry,
    SecretLifecyclePolicy lifecyclePolicy,
    ISecretAuditSink auditSink,
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretValueResolver
{
    private const string ConnectionExceptionSqlStateClass = "08";
    private const string SqliteExceptionTypeName = "Microsoft.Data.Sqlite.SqliteException";
    private const string SqliteErrorCodeProperty = "SqliteErrorCode";
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    public async ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!nameValidator.IsValid(reference.Name, out var nameError))
            return await FailureAsync(tenantId, reference.Name, SecretResolutionFailureCode.NotFound, nameError ?? "Secret name is invalid.", cancellationToken);

        var normalizedName = nameValidator.Normalize(reference.Name);
        Secret? secret;
        try
        {
            secret = await repository.FindAsync(tenantId, normalizedName, cancellationToken);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The metadata repository is as much the secret's store as the payload store: an outage of either is
            // transient, and a row it cannot serve is not. Its exception is dropped rather than copied into the result,
            // because its message can name a connection target or a credential.
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadFailureAsync(tenantId, reference.Name, exception, "Secret metadata could not be read.", "Secret metadata is unusable.", cancellationToken);
        }

        var decision = lifecyclePolicy.EvaluateRuntimeResolution(secret, reference);

        if (!decision.Allowed)
            return await FailureAsync(tenantId, secret?.Name ?? reference.Name, ToResolutionFailureCode(decision.FailureCode), decision.Reason, cancellationToken);

        var resolvedSecret = secret!;
        var versionDecision = lifecyclePolicy.EvaluateRuntimeVersion(resolvedSecret);

        if (!versionDecision.Allowed)
            return await FailureAsync(tenantId, resolvedSecret.Name, ToResolutionFailureCode(versionDecision.FailureCode), versionDecision.Reason, cancellationToken);

        try
        {
            var store = storeRegistry.Get(resolvedSecret.StoreName);
            var payload = await store.ReadAsync(new SecretReadContext(resolvedSecret, versionDecision.Version!), cancellationToken);

            if (payload is null)
                return await FailureAsync(tenantId, resolvedSecret.Name, SecretResolutionFailureCode.CorruptState, "Secret payload is unusable.", cancellationToken);

            if (payload.Value is null)
                return await FailureAsync(tenantId, resolvedSecret.Name, SecretResolutionFailureCode.CorruptState, "Secret store returned an empty value.", cancellationToken);

            await auditSink.RecordAsync(new("resolve", resolvedSecret.Name, "succeeded", timeProvider.GetUtcNow(), tenantId), cancellationToken);
            return ResolvedSecret.Success(payload.Value, mapper.Map(resolvedSecret));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Classified as the repository read is: an outage of the payload store is transient, and a store the host
            // does not register or a payload that does not decrypt or parse is not. The exception is dropped likewise.
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadFailureAsync(tenantId, resolvedSecret.Name, exception, "Secret payload could not be read.", "Secret payload is unusable.", cancellationToken);
        }
    }

    private ValueTask<ResolvedSecret> ReadFailureAsync(string tenantId, string name, Exception exception, string outageError, string unusableError, CancellationToken cancellationToken) =>
        IsOutage(exception)
            ? FailureAsync(tenantId, name, SecretResolutionFailureCode.StoreUnavailable, outageError, cancellationToken)
            : FailureAsync(tenantId, name, SecretResolutionFailureCode.CorruptState, unusableError, cancellationToken);

    private async ValueTask<ResolvedSecret> FailureAsync(string tenantId, string? name, SecretResolutionFailureCode code, string error, CancellationToken cancellationToken)
    {
        await auditSink.RecordAsync(new("resolve", name ?? "", "failed", timeProvider.GetUtcNow(), tenantId, Reason: code.ToString()), cancellationToken);
        return ResolvedSecret.Failure(code, error);
    }

    /// <summary>
    /// Whether a read failed because the store could not be reached rather than because of what it holds: a timeout,
    /// I/O or socket failure, or a cancellation the caller did not request (a provider timeout), anywhere in the chain,
    /// since a provider's retry strategy wraps the failure it gave up on. The first <see cref="DbException"/> in the chain
    /// decides for the chain beneath it (<see cref="IsProviderOutage"/>).
    /// </summary>
    private static bool IsOutage(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException provider)
                return IsProviderOutage(provider);

            if (current is TimeoutException or IOException or SocketException or OperationCanceledException)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a database provider's failure is an outage. Provider-neutral first: the provider calls it transient
    /// (<see cref="DbException.IsTransient"/>; Npgsql does, for connection failures and server states such as 08, 53 and
    /// 57P), its SQLSTATE is in the standard connection-exception class 08, or it carries a timeout, I/O or operating
    /// system error (a <see cref="Win32Exception"/>, which <see cref="SocketException"/> is) beneath it, which is how
    /// Microsoft.Data.SqlClient and MySql.Data report a connection that could not be made, since neither overrides
    /// <see cref="DbException.IsTransient"/>. Microsoft.Data.Sqlite overrides nothing either, and SQLite has no
    /// connection to lose: its outage is another connection holding the database (<c>SQLITE_BUSY</c>,
    /// <c>SQLITE_LOCKED</c>), read by name as the EF exception classifier does, so this module takes no provider
    /// dependency. Anything else, a missing table or column or a damaged database file included, is not an outage.
    /// </summary>
    private static bool IsProviderOutage(DbException exception)
    {
        if (exception.IsTransient || exception.SqlState?.StartsWith(ConnectionExceptionSqlStateClass, StringComparison.Ordinal) == true || IsSqliteContention(exception))
            return true;

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is TimeoutException or IOException or Win32Exception or DbException { IsTransient: true })
                return true;
        }

        return false;
    }

    private static bool IsSqliteContention(DbException exception)
    {
        var type = exception.GetType();
        return type.FullName == SqliteExceptionTypeName && type.GetProperty(SqliteErrorCodeProperty)?.GetValue(exception) is SqliteBusy or SqliteLocked;
    }

    /// <summary>Process-level failures that are never reported as a resolution failure.</summary>
    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException or InvalidProgramException;

    private static SecretResolutionFailureCode ToResolutionFailureCode(SecretLifecycleFailureCode failureCode) => failureCode switch
    {
        SecretLifecycleFailureCode.Deleted or SecretLifecycleFailureCode.NotFound => SecretResolutionFailureCode.NotFound,
        SecretLifecycleFailureCode.Inactive or SecretLifecycleFailureCode.NoActiveVersion => SecretResolutionFailureCode.Inactive,
        SecretLifecycleFailureCode.Expired => SecretResolutionFailureCode.Expired,
        SecretLifecycleFailureCode.Revoked => SecretResolutionFailureCode.Revoked,
        SecretLifecycleFailureCode.TypeMismatch => SecretResolutionFailureCode.TypeMismatch,
        SecretLifecycleFailureCode.ScopeMismatch => SecretResolutionFailureCode.ScopeMismatch,
        _ => SecretResolutionFailureCode.Inactive
    };
}
