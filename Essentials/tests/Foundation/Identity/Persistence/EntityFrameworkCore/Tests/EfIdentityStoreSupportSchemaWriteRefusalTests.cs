using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="EfIdentityStoreSupport.Failure"/> is the one wrapper every Identity failure ladder builds. A schema write
/// refusal raised while a store saves must reach the caller as itself, the same way <see cref="EfSchemaVersionSkewException"/>
/// already does, never wrapped in <see cref="IdentityEntityFrameworkPersistenceException"/> (#2101).
/// </summary>
public sealed class EfIdentityStoreSupportSchemaWriteRefusalTests
{
    [Fact]
    public void Failure_lets_a_schema_write_refusal_pass_through_unwrapped()
    {
        var refusal = new EfSchemaWriteRefusedException("Identity.Test", "1", "2");

        var thrown = Assert.Throws<EfSchemaWriteRefusedException>(() => EfIdentityStoreSupport.Failure("saving a probe row", refusal));

        Assert.Same(refusal, thrown);
    }

    [Fact]
    public async Task A_refusal_raised_by_the_gate_during_save_reaches_the_caller_unwrapped()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-identity-write-refusal-{Guid.NewGuid():N}.db");
        var interceptor = new ProviderFailures.FailingSaveInterceptor(() => new EfSchemaWriteRefusedException("IdentityIam", "1", "2"));
        try
        {
            await using var context = CreateContext(databasePath, interceptor);
            await context.Database.EnsureCreatedAsync();
            var access = new FixedAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var users = new EfUserStore(context, access);

            var refusal = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => users.SaveAsync(new UserRecord(
                "user-a", "tenant-a", "Ada", "ada@example.test", "Ada", UserStatus.Active, ResourceOwnership.Foundation,
                new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal))).AsTask());

            Assert.Equal("IdentityIam", refusal.Family);
        }
        finally
        {
            TemporarySqliteDatabase.ClearPoolAndDeleteFiles(databasePath);
        }
    }

    private static IdentityIamSqliteDbContext CreateContext(string databasePath, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<IdentityIamSqliteDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5");
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new IdentityIamSqliteDbContext(builder.Options);
    }

    private sealed class FixedAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
