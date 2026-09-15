using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowTriggerBindingPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_trigger_binding_smoke() => RuntimeWorkflowTriggerBindingProviderSmoke.RunAsync(fixture, c => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(c).Options), BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowTriggerBindingSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_trigger_binding_smoke() => RuntimeWorkflowTriggerBindingProviderSmoke.RunAsync(fixture, c => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(c).Options), BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowTriggerBindingMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_trigger_binding_smoke() => RuntimeWorkflowTriggerBindingProviderSmoke.RunAsync(fixture, c => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(c).Options), BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowTriggerBindingProviderSmoke
{
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, BookmarkStateDbContext> createContext, string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = createContext(fixture.ConnectionString);
        Assert.Equal(expectedProvider, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        var store = new EfWorkflowTriggerBindingStore(context, new Accessor($"native-{Guid.NewGuid():N}"));
        var binding = new WorkflowTriggerBinding(
            WorkflowTriggerBinding.BuildId("artifact-a", "node-a", "native-event"), "artifact-a", "definition-a", "1", "artifact-hash", "node-a", "Event", "native-event", null, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var second = new WorkflowTriggerBinding(
            WorkflowTriggerBinding.BuildId("artifact-b", "node-b", "native-event-2"), "artifact-b", "definition-a", "1", "artifact-hash", "node-b", "Event", "native-event-2", null, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        await store.SaveAsync(binding);
        await store.SaveAsync(second);
        var firstPage = await store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event", 1));
        Assert.NotNull(firstPage.NextContinuationToken);
        Assert.Single((await store.ListByStimulusTypeAsync(new WorkflowTriggerBindingTypePageQuery("Event", 1, firstPage.NextContinuationToken))).Items);
        Assert.Single((await store.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("Event", "native-event"))).Items);

        var activationBinding = binding with
        {
            TriggerBindingId = WorkflowTriggerBinding.BuildId("native-activation", "artifact-a", "node-a", "native-event"),
            ActivationId = "native-activation", SlotId = "slot-a", IsActive = true
        };
        await store.PrepareActivationAsync("native-activation", [activationBinding]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareActivationAsync("native-activation", [activationBinding with { StimulusHash = "different" }]).AsTask());
        await store.ActivateAsync("native-activation", null);
    }

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
