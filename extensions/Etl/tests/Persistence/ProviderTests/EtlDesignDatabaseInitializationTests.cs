using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Persistence.ProviderTests;

/// <summary>Initialization failure must release the allocated database, including externally supplied servers.</summary>
public sealed class EtlDesignDatabaseInitializationTests
{
    [Fact]
    public async Task A_context_creation_failure_releases_the_allocated_database()
    {
        var cleaned = false;
        var allocated = new EtlProviderDatabase("Unsupported", "", () => { cleaned = true; return Task.CompletedTask; });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => EtlProviderDesignDatabase.CreateTypedAsync(allocated));

        Assert.True(cleaned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_migration_failure_releases_the_database_and_preserves_the_original_failure(bool cleanupFails)
    {
        var cleaned = false;
        var original = new InitializationFailure();
        var allocated = new EtlProviderDatabase("PostgreSql", "Host=127.0.0.1;Database=unused;Username=postgres", () =>
        {
            cleaned = true;
            return cleanupFails ? Task.FromException(new InvalidOperationException("cleanup failed")) : Task.CompletedTask;
        });

        var actual = await Assert.ThrowsAsync<InitializationFailure>(() =>
            EtlProviderDesignDatabase.CreateTypedAsync(allocated, new FailBeforeConnectionOpens(original)));

        Assert.Same(original, actual);
        Assert.True(cleaned);
    }

    private sealed class InitializationFailure : Exception;

    private sealed class FailBeforeConnectionOpens(Exception failure) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) => throw failure;
    }
}
