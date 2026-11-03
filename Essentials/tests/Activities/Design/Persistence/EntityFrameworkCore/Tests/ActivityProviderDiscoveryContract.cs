using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.DiscoveryContract;

/// <summary>Same temporal membership behavior against each provider's real query translation.</summary>
internal static class ActivityProviderDiscoveryContract
{
    public static async Task RunAsync(ActivitiesDesignDbContext db)
    {
        var marker = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var provider = $"specialists-{marker}";
        var watermark = await db.ActivityManagementProjectionWatermarks.SingleOrDefaultAsync();
        var sequence = (watermark?.Sequence ?? 0) + 1;
        var previousRetention = watermark?.RetainedFromSequence ?? 0;
        if (watermark is null)
        {
            watermark = new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId };
            db.ActivityManagementProjectionWatermarks.Add(watermark);
        }
        watermark.Sequence = sequence + 2;
        watermark.RetainedFromSequence = sequence;
        watermark.AdvancedAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
            db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot
            {
                Id = (sequence + i).ToString("D20"), Sequence = sequence + i, AsOf = watermark.AdvancedAt
            });

        string Id(string name) => $"{marker}-{name}";
        ActivityDefinitionManagementProjectionRevision Definition(string name, string? tenant = "tenant-a", string? head = null, string? recommendation = null,
            long? from = null, long to = long.MaxValue, string? revision = null) => new()
        {
            Id = Id(revision is null ? name : $"{name}-{revision}"), ResourceId = Id(name), DefinitionId = Id(name), TenantId = tenant,
            ValidFromSequence = from ?? sequence, ValidToSequenceExclusive = to,
            ValidFromKey = (from ?? sequence).ToString("D20"), ValidToKey = to.ToString("D20"),
            VisibilityKey = tenant ?? "global", SortKey = Id(name), SearchText = marker,
            ActivityTypeKey = "Acme.Specialist", Category = "Tests", ContentAuthority = new(ActivityContentAuthorityKind.Design, "design"),
            ContentAuthorityKind = ActivityContentAuthorityKind.Design, HeadProviderKey = head, RecommendationProviderKey = recommendation,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ActivityDefinitionDraftManagementProjectionRevision Draft(string name, string? tenant = "tenant-a", long? from = null,
            long to = long.MaxValue, ActivityDefinitionDraftStatus status = ActivityDefinitionDraftStatus.Active, string? revision = null,
            string? draftKey = null, string? manifestProvider = null) => new()
        {
            Id = Id($"draft-{name}-{draftKey ?? "primary"}-{revision ?? "active"}"),
            ResourceId = Id($"draft-{name}-{draftKey ?? "primary"}"), DraftId = Id($"draft-{name}-{draftKey ?? "primary"}"),
            DefinitionId = Id(name), TenantId = tenant, ValidFromSequence = from ?? sequence, ValidToSequenceExclusive = to,
            ValidFromKey = (from ?? sequence).ToString("D20"), ValidToKey = to.ToString("D20"), VisibilityKey = tenant ?? "global",
            SortKey = Id(name), SearchText = marker, Revision = 1, Status = status, ProviderKey = manifestProvider ?? provider, ProviderSchemaVersion = "1",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ActivityDefinitionManagementProjections.AddRange(
            Definition("shared", null), Definition("shared"), Definition("global-only", null),
            Definition("opaque"), Definition("OPAQUE"), Definition("padded"), Definition("padded "),
            Definition("head", head: provider), Definition("recommended", recommendation: provider),
            Definition("mixed", head: "other"),
            Definition("multiple", to: sequence + 1),
            Definition("multiple", head: provider, from: sequence + 1, revision: "published"), Definition("history"), Definition("future"), Definition("inactive"),
            Definition("foreign"), Definition("foreign", "tenant-b"), Definition("wrong-scope"), Definition("wrong-owner"));
        var wrongScope = Draft("wrong-scope");
        var wrongOwner = Draft("wrong-owner", "tenant-b");
        db.ActivityDraftManagementProjections.AddRange(
            Draft("shared"), Draft("global-only", null), Draft("OPAQUE"), Draft("padded "), Draft("mixed"),
            Draft("multiple", to: sequence + 1, draftKey: "first"),
            Draft("multiple", to: sequence + 1, draftKey: "second"),
            Draft("multiple", draftKey: "other", manifestProvider: "other"),
            Draft("history", to: sequence + 1),
            Draft("history", from: sequence + 1, status: ActivityDefinitionDraftStatus.Discarded, revision: "discarded"),
            Draft("future", from: sequence + 2), Draft("inactive", status: ActivityDefinitionDraftStatus.Discarded),
            Draft("foreign", "tenant-b"), wrongScope, wrongOwner);
        await db.SaveChangesAsync();
        // Corrupt physical ownership independently of raw TenantId: neither column alone is authority.
        await db.ActivityDraftManagementProjections.Where(x => x.Id == wrongScope.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(row => EF.Property<string>(row, "TenantScopeKey"), ActivitiesDesignDbContext.GlobalTenantKey));
        await db.ActivityDraftManagementProjections.Where(x => x.Id == wrongOwner.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(row => EF.Property<string>(row, "TenantScopeKey"), ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a")));
        db.ChangeTracker.Clear();

        var store = new EfActivityDesignStores(db);
        Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> Read(long snapshot, int offset = 0, int limit = 100) =>
            store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", snapshot, offset, limit,
                Search: marker, ProviderKey: $"  {provider}  "));
        string Key(ActivityDefinitionManagementProjectionRevision row) => $"{row.TenantId ?? "global"}:{row.ResourceId}";
        var expected = new[] { $"tenant-a:{Id("shared")}", $"global:{Id("global-only")}", $"tenant-a:{Id("OPAQUE")}",
            $"tenant-a:{Id("padded ")}", $"tenant-a:{Id("head")}", $"tenant-a:{Id("recommended")}",
            $"tenant-a:{Id("mixed")}", $"tenant-a:{Id("multiple")}", $"tenant-a:{Id("history")}" };
        var historical = await Read(sequence);
        Assert.Equal(expected.Order(StringComparer.Ordinal), historical.Items.Select(Key).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, historical.TotalCount);
        var first = await Read(sequence, limit: 3);
        var second = await Read(sequence, first.NextOffset!.Value, 3);
        var third = await Read(sequence, second.NextOffset!.Value, 3);
        Assert.Equal(historical.Items.Select(Key), first.Items.Concat(second.Items).Concat(third.Items).Select(Key));
        Assert.Equal(expected.Length, first.TotalCount);
        Assert.Equal(first.TotalCount, second.TotalCount);
        Assert.Null(third.NextOffset);
        var discarded = await Read(sequence + 1);
        Assert.Equal(expected.Length - 1, discarded.TotalCount);
        Assert.DoesNotContain(discarded.Items, x => x.ResourceId == Id("history"));
        var current = await Read(sequence + 2);
        Assert.Equal(expected.Length, current.TotalCount);
        Assert.Contains(current.Items, x => x.ResourceId == Id("future"));
        Assert.Equal(historical.Items.Select(Key), (await Read(sequence)).Items.Select(Key));
        var global = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery(null, sequence, 0, 100, Search: marker, ProviderKey: provider));
        Assert.Equal([Id("global-only")], global.Items.Select(x => x.ResourceId));
        // An unfiltered view retains every definition; the provider predicate does not replace total semantics.
        var unfiltered = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", sequence, 0, 100, Search: marker));
        Assert.Equal(17, unfiltered.TotalCount);
        var otherProvider = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", sequence + 1, 0, 100, Search: marker, ProviderKey: "other"));
        Assert.Equal(new[] { Id("mixed"), Id("multiple") }.Order(StringComparer.Ordinal), otherProvider.Items.Select(x => x.ResourceId).Order(StringComparer.Ordinal));
        watermark = await db.ActivityManagementProjectionWatermarks.SingleAsync();
        watermark.RetainedFromSequence = sequence + 1;
        await db.SaveChangesAsync();
        try
        {
            await Assert.ThrowsAsync<ActivityManagementSnapshotExpiredException>(() => Read(sequence));
        }
        finally
        {
            watermark.RetainedFromSequence = previousRetention;
            await db.SaveChangesAsync();
        }
    }
}
