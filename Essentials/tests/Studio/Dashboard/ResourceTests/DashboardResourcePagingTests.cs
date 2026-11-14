using System.Data.Common;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardResourcePagingTests
{
    [Fact]
    public async Task Store_bounds_database_reads_and_collection_pages_preserve_current_audience()
    {
        var observer = new PageObserver();
        await using var host = await ResourceHost.CreateAsync(interceptor: observer);
        var viewer = DashboardResourceTests.Actor("viewer");
        var sources = new List<(DashboardActor Actor, DashboardResourceView View)>();
        for (var index = 0; index < 103; index++)
        {
            var actor = DashboardResourceTests.Actor(index < 60 ? "owner" : "editor");
            var saved = await host.With(service => service.CreateAsync(actor, "question",
                DashboardResourceTests.Question("page-" + index) with { Name = $"Question {index:D3}" }));
            saved = await host.With(service => service.SetAudienceAsync(actor, "question", saved.Id, saved.Revision,
                [new("user", "viewer", "viewer")]));
            sources.Add((actor, saved));
        }
        var firstStore = await host.WithStore(store => store.ReadPageAsync("tenant-a", "question"));
        var secondStore = await host.WithStore(store => store.ReadPageAsync("tenant-a", "question", firstStore[^1].Id));
        Assert.Equal(16, firstStore.Count); Assert.Equal(16, secondStore.Count);
        Assert.All(firstStore, item => Assert.Empty(item.History));
        var full = await host.WithStore(store => store.FindAsync("tenant-a", "question", firstStore[0].Id));
        Assert.NotNull(full); Assert.NotEmpty(full.History);
        Assert.Empty(firstStore.Select(item => item.Id).Intersect(secondStore.Select(item => item.Id)));
        Assert.Empty(await host.WithStore(store => store.ReadPageAsync("other-tenant", "question")));

        var first = await host.With(service => service.ListAsync(viewer, "question", "shared"));
        Assert.Equal(100, first.Items.Count); Assert.NotNull(first.ContinuationToken);
        Assert.Equal("Question 000", first.Items[0].Name);
        var second = await host.With(service => service.ListAsync(viewer, "question", "shared", continuationToken: first.ContinuationToken));
        Assert.Equal(3, second.Items.Count); Assert.Null(second.ContinuationToken);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        var removed = sources[102];
        await host.With(service => service.SetAudienceAsync(removed.Actor, "question", removed.View.Id, removed.View.Revision, []));
        var refreshed = await host.With(service => service.ListAsync(viewer, "question", "shared", continuationToken: first.ContinuationToken));
        Assert.Equal(2, refreshed.Items.Count);
        Assert.DoesNotContain(refreshed.Items, item => item.Id == removed.View.Id);
        Assert.True(observer.BoundedReads > 0);
    }

    private sealed class PageObserver : DbCommandInterceptor
    {
        public int BoundedReads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("elsa_studio_dashboard_resources", StringComparison.Ordinal) &&
                command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                Assert.Contains("LIMIT", command.CommandText, StringComparison.Ordinal);
                Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => parameter.Value is int value && value == 16);
                BoundedReads++;
            }
            return ValueTask.FromResult(result);
        }
    }
}
