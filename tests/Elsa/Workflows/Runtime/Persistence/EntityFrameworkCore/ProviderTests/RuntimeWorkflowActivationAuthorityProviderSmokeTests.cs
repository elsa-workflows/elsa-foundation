using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationAuthorityPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_activation_authority_model_crud_cas_and_unique_placement() =>
        RuntimeWorkflowActivationAuthorityProviderSmoke.RunAsync(
            fixture,
            "PostgreSql",
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationAuthoritySqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_activation_authority_model_crud_cas_and_unique_placement() =>
        RuntimeWorkflowActivationAuthorityProviderSmoke.RunAsync(
            fixture,
            "SqlServer",
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowActivationAuthorityMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_activation_authority_model_crud_cas_and_unique_placement() =>
        RuntimeWorkflowActivationAuthorityProviderSmoke.RunAsync(
            fixture,
            "MySql",
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowActivationAuthorityProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource Publishing = WorkflowActivationSource.Publishing;

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"native-r28-{Guid.NewGuid():N}";
        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var authority = new EfWorkflowActivationAuthority(context, new FixedAccessor(scope));
            var first = await authority.TryActivateAsync(new("definition-native", "default", "activation-a", Publishing, 0, Now));
            Assert.True(first.Succeeded);
            Assert.Equal(1, first.Slot.Revision);
            Assert.Equal(first.Slot, await authority.FindAsync("definition-native", "default"));

            var maximumDefinitionId = new string('d', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var maximumSlotName = new string('s', RuntimeOperationalStateEfModule.IdentityMaximumLength);
            var boundary = await authority.TryActivateAsync(new(maximumDefinitionId, maximumSlotName, "activation-boundary", Publishing, 0, Now));
            Assert.True(boundary.Succeeded);
            Assert.Equal(boundary.Slot, await authority.FindAsync(maximumDefinitionId, maximumSlotName));
            var boundaryRelease = await authority.TryDeactivateAsync(maximumDefinitionId, maximumSlotName, Publishing, boundary.Slot.Revision, Now);
            Assert.True(boundaryRelease.Succeeded);

            var duplicatePlacement = await authority.TryActivateAsync(new("definition-native", "canary", "activation-a", Publishing, 0, Now));
            Assert.False(duplicatePlacement.Succeeded);
            Assert.Equal(WorkflowActivationConflict.RevisionMismatch, duplicatePlacement.Conflict);
            Assert.Equal(first.Slot, await authority.FindAsync("definition-native", "default"));

            var stale = await authority.TryDeactivateAsync("definition-native", "default", Publishing, 0, Now);
            Assert.False(stale.Succeeded);
            Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);

            await using var transaction = await context.Database.BeginTransactionAsync();
            Assert.True((await authority.TryActivateAsync(new("definition-native", "rolled-back", "activation-rollback", Publishing, 0, Now))).Succeeded);
            await transaction.RollbackAsync();
        }

        await using (var restarted = createContext(fixture.ConnectionString))
        {
            var authority = new EfWorkflowActivationAuthority(restarted, new FixedAccessor(scope));
            var restored = await authority.FindAsync("definition-native", "default");
            Assert.NotNull(restored);
            Assert.Equal("activation-a", restored!.ActiveActivationId);
            Assert.Equal(1, restored.Revision);
            Assert.Null(await authority.FindAsync("definition-native", "rolled-back"));
            var released = await authority.TryDeactivateAsync("definition-native", "default", Publishing, restored.Revision, Now);
            Assert.True(released.Succeeded);
            Assert.Null((await authority.FindAsync("definition-native", "default"))!.ActiveActivationId);
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
