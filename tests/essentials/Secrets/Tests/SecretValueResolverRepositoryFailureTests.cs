using System.ComponentModel;
using System.Net.Sockets;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Secrets.Tests;

/// <summary>
/// When reading the secret's metadata fails, the resolver reports a result rather than a throw, with none of the
/// repository's exception text, classified by what failed (spec 188, FR-003, User Story 2): an outage of the repository
/// is <see cref="SecretResolutionFailureCode.StoreUnavailable"/>, transient, and a row or identity the repository
/// cannot serve is <see cref="SecretResolutionFailureCode.CorruptState"/>, permanent. A canceled resolution stays a
/// cancellation, and a name the validator refuses never reaches the repository.
/// </summary>
public sealed class SecretValueResolverRepositoryFailureTests : IDisposable
{
    private const string StoreDetail = "repository-detail-sentinel read from secrets-db.internal failed";
    private const int SqliteError = 1;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCorrupt = 11;
    private readonly FailingSecretRepository _repository = new();
    private readonly ServiceProvider _provider;
    private readonly ISecretValueResolver _resolver;

    public SecretValueResolverRepositoryFailureTests()
    {
        var services = new ServiceCollection().AddSecrets();
        services.Configure<SecretsOptions>(options => options.EncryptionKey = "repository-failure-test-encryption-key");
        services.RemoveAll<ISecretRepository>();
        services.AddSingleton<ISecretRepository>(_repository);
        _provider = services.BuildServiceProvider();
        _resolver = _provider.GetRequiredService<ISecretValueResolver>();
    }

    public void Dispose() => _provider.Dispose();

    public static TheoryData<Exception, SecretResolutionFailureCode> RepositoryFailures => new()
    {
        // Outages: the repository could not be reached. A provider failure the provider calls transient, as Npgsql's
        // connection failure is.
        { new ProviderException(StoreDetail, isTransient: true), SecretResolutionFailureCode.StoreUnavailable },
        // A provider retry strategy gives up by wrapping the provider failure.
        { new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", new ProviderException(StoreDetail, isTransient: true)), SecretResolutionFailureCode.StoreUnavailable },
        // A provider that never calls a failure transient: SQL Server's network failure or timeout, which carries the
        // operating system error beneath it, MySQL's host that cannot be reached, its command timeout, and a server
        // reporting a connection exception by SQLSTATE.
        { new ProviderException(StoreDetail, innerException: new Win32Exception(258)), SecretResolutionFailureCode.StoreUnavailable },
        { new ProviderException(StoreDetail, innerException: new SocketException((int)SocketError.HostUnreachable)), SecretResolutionFailureCode.StoreUnavailable },
        { new ProviderException(StoreDetail, innerException: new ProviderException(StoreDetail, innerException: new TimeoutException())), SecretResolutionFailureCode.StoreUnavailable },
        // MySQL's connection lost mid-read: the stream ended beneath the provider failure.
        { new ProviderException(StoreDetail, innerException: new EndOfStreamException()), SecretResolutionFailureCode.StoreUnavailable },
        { new ProviderException(StoreDetail, sqlState: "08004"), SecretResolutionFailureCode.StoreUnavailable },
        // A provider failure wrapping one the provider calls transient.
        { new ProviderException(StoreDetail, innerException: new ProviderException(StoreDetail, isTransient: true)), SecretResolutionFailureCode.StoreUnavailable },
        // SQLite: another connection holds the database.
        { new SqliteException(StoreDetail, SqliteBusy), SecretResolutionFailureCode.StoreUnavailable },
        { new SqliteException(StoreDetail, SqliteLocked), SecretResolutionFailureCode.StoreUnavailable },
        { new TimeoutException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },
        { new IOException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },
        { new SocketException((int)SocketError.ConnectionRefused), SecretResolutionFailureCode.StoreUnavailable },
        // The repository's own timeout, not the caller's cancellation.
        { new OperationCanceledException(StoreDetail), SecretResolutionFailureCode.StoreUnavailable },

        // The repository was read, and what it holds cannot be served.
        // A provider failure that is not transient: a schema error such as PostgreSQL's undefined column, directly or
        // wrapped, and SQLite's missing table and damaged database file.
        { new ProviderException(StoreDetail, sqlState: "42703"), SecretResolutionFailureCode.CorruptState },
        { new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", new ProviderException(StoreDetail, sqlState: "42703")), SecretResolutionFailureCode.CorruptState },
        { new SqliteException(StoreDetail, SqliteError), SecretResolutionFailureCode.CorruptState },
        { new SqliteException(StoreDetail, SqliteCorrupt), SecretResolutionFailureCode.CorruptState },
        { new SecretsProjectionException("tenant-1", "payments.api", StoreDetail), SecretResolutionFailureCode.CorruptState },
        // EfSecretRepository: a document whose tenant does not match its row, or that does not parse.
        { new InvalidOperationException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new JsonException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new NotSupportedException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new InvalidDataException(StoreDetail), SecretResolutionFailureCode.CorruptState },
        { new EfSchemaVersionSkewException("secrets", "99", "1", ["1"]), SecretResolutionFailureCode.CorruptState },
        // A tenant or name the repository's identity validation refuses.
        { new ArgumentException(StoreDetail), SecretResolutionFailureCode.CorruptState }
    };

    [Theory]
    [MemberData(nameof(RepositoryFailures))]
    public async Task A_repository_read_failure_is_classified_without_its_text(Exception failure, SecretResolutionFailureCode code)
    {
        _repository.Failure = failure;

        var resolved = await _resolver.ResolveAsync("tenant-1", new SecretReference("payments.api"));

        Assert.False(resolved.Succeeded);
        Assert.Null(resolved.Value);
        Assert.Equal(code, resolved.FailureCode);
        Assert.DoesNotContain("repository-detail-sentinel", resolved.Error ?? "", StringComparison.Ordinal);
        Assert.Equal(1, _repository.Reads);
    }

    [Fact]
    public async Task A_canceled_resolution_propagates_as_a_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _repository.Failure = new OperationCanceledException(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _resolver.ResolveAsync("tenant-1", new SecretReference("payments.api"), cancellation.Token).AsTask());
        Assert.Equal(1, _repository.Reads);
    }

    [Fact]
    public async Task A_name_the_validator_refuses_is_not_found_without_reading_the_repository()
    {
        var resolved = await _resolver.ResolveAsync("tenant-1", new SecretReference("payments api!"));

        Assert.Equal(SecretResolutionFailureCode.NotFound, resolved.FailureCode);
        Assert.Equal(0, _repository.Reads);
    }

    /// <summary>A secret repository whose reads throw <see cref="Failure"/>; nothing else is called.</summary>
    private sealed class FailingSecretRepository : ISecretRepository
    {
        public Exception Failure { get; set; } = new InvalidOperationException(StoreDetail);

        public int Reads { get; private set; }

        public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
        {
            Reads++;
            throw Failure;
        }

        public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
