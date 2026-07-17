using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityVersionLifecycleTests
{
    [Fact]
    public async Task Retire_restore_and_revoke_are_optimistic_and_revocation_is_terminal()
    {
        var stores = new InMemoryReusableActivityStores();
        stores.SeedPublication(Publication(), new()
        {
            Id = "layout-1",
            DefinitionVersionId = "version-1",
            TenantId = "tenant-a",
            CreatedAt = Now,
            LastModifiedAt = Now
        });
        var service = new ActivityVersionLifecycleService(stores, stores, new Context(), new Clock());

        var retired = await service.RetireAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Superseded"), default);
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Restore"), default));
        var restored = await service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Retired, "Restore"), default);
        var revoked = await service.RevokeAsync(new("version-1", ActivityDefinitionVersionLifecycle.Active, "Unsafe"), default);
        var terminal = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            service.RestoreAsync(new("version-1", ActivityDefinitionVersionLifecycle.Revoked, "Restore"), default));

        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, retired.Lifecycle);
        Assert.Equal("activity.version.stale-lifecycle", stale.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Active, restored.Lifecycle);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Revoked, revoked.Lifecycle);
        Assert.Equal("activity.version.lifecycle-conflict", terminal.ErrorCode);
        Assert.Equal(ActivityDefinitionVersionLifecycle.Revoked,
            (await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync("version-1"))!.Lifecycle);
    }

    [Fact]
    public void Selection_policy_blocks_retired_direct_selection_but_preserves_closed_parent_dependencies()
    {
        var policy = new DefaultActivityVersionSelectionPolicy();

        Assert.False(policy.CanSelectDirectly(ActivityDefinitionVersionLifecycle.Retired));
        Assert.True(policy.CanUseClosedDependency(ActivityDefinitionVersionLifecycle.Retired));
        Assert.False(policy.CanUseClosedDependency(ActivityDefinitionVersionLifecycle.Revoked));
    }

    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static ActivityDefinitionVersionPublication Publication() => new()
    {
        Id = "publication-1",
        DefinitionVersionId = "version-1",
        DefinitionId = "definition-1",
        TenantId = "tenant-a",
        Version = "1.0.0",
        ActivityTypeKey = "acme.test",
        Contract = new("1", [], [], [new("done", "Done", true)]),
        Provider = new("test.provider", "1", JsonSerializer.SerializeToElement(new { })),
        TemplateId = "template-1",
        TemplateHash = "sha256:1",
        SourceReferenceId = "source-ref-1",
        ProviderFingerprint = "test/1",
        DirectDependencyCount = 0,
        ClosedTemplateCount = 0,
        RuntimeRequirements = [],
        Lifecycle = ActivityDefinitionVersionLifecycle.Active,
        PublishedAt = Now,
        CreatedAt = Now,
        LastModifiedAt = Now
    };

    private sealed class Context : IActivityAuthoringContext
    {
        public string? TenantId => "tenant-a";
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => true;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
