using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityDraftAuthoringReaderTests
{
    [Theory]
    [InlineData(true, "tenant-1", true, true)]
    [InlineData(false, "tenant-1", true, false)]
    [InlineData(true, "tenant-2", true, false)]
    [InlineData(true, "tenant-1", false, false)]
    [InlineData(true, null, true, true)]
    public async Task Registered_reader_checks_current_actor_read_tenant_and_provider_payload_before_releasing_source(
        bool canRead, string? draftTenant, bool canReadPayload, bool allowed)
    {
        var draft = new ActivityDefinitionDraft { Id = "draft", DefinitionId = "definition", TenantId = draftTenant, Revision = 4,
            State = new(new("1", [], [], []), new("elsa.activity-graph", "1", JsonSerializer.SerializeToElement(new { secretBodyName = "not-public" })),
                new Dictionary<string, string>()) };
        var store = new DraftStore(draft);
        var actor = new Actor(canReadPayload);
        var services = new ServiceCollection().AddSingleton<IActivityDefinitionDraftStore>(store)
            .AddSingleton<IActivityAuthoringCatalog>(new Catalog(canRead)).AddSingleton<IActivityAuthoringContextAsync>(actor);
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = Assert.Single(scope.ServiceProvider.GetServices<IActivityDraftAuthoringReader>());

        var source = await reader.FindAsync("draft");

        Assert.Equal(allowed, source is not null);
        if (allowed)
        {
            Assert.Equal("draft", source!.DraftId);
            Assert.Equal(4, source.Revision);
            Assert.Same(draft.State, source.State);
            Assert.Equal("profile-1", source.AuthorizationRevision);
        }
        Assert.Equal(canRead ? 1 : 0, store.ReadCount);
        Assert.Equal(canRead && (draftTenant is null or "tenant-1") ? 1 : 0, actor.PayloadChecks);
    }

    [Fact]
    public async Task Missing_draft_has_no_source_and_never_checks_a_provider()
    {
        var actor = new Actor(true);
        var services = new ServiceCollection().AddSingleton<IActivityDefinitionDraftStore>(new DraftStore(null))
            .AddSingleton<IActivityAuthoringCatalog>(new Catalog(true)).AddSingleton<IActivityAuthoringContextAsync>(actor);
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var source = await provider.GetRequiredService<IActivityDraftAuthoringReader>().FindAsync("missing");
        Assert.Null(source);
        Assert.Equal(0, actor.PayloadChecks);
    }

    private sealed class DraftStore(ActivityDefinitionDraft? draft) : IActivityDefinitionDraftStore
    {
        public int ReadCount { get; private set; }
        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(draft);
        }
        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Catalog(bool allowed) : IActivityAuthoringCatalog
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(allowed);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Actor(bool canReadPayload) : IActivityAuthoringContextAsync
    {
        public int PayloadChecks { get; private set; }
        public string? TenantId => "tenant-1";
        public string ActorId => "author";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("profile-1");
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default)
        {
            Assert.Equal("elsa.activity-graph", providerKey);
            PayloadChecks++;
            return ValueTask.FromResult(canReadPayload);
        }
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
