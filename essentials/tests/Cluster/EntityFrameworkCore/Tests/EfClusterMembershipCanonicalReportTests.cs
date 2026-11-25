using Elsa.Cluster.Core.Models;
using Elsa.Cluster.EntityFrameworkCore.Stores;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

public sealed class EfClusterMembershipCanonicalReportTests
{
    [Theory]
    [InlineData("{\"text\":\"Ren\\u00e9\",\"count\":1.0}", " { \"count\":1e0, \"text\":\"René\" } ", false)]
    [InlineData("{\"count\":9007199254740993}", "{\"count\":9007199254740992}", true)]
    [InlineData("{\"items\":[1,2]}", "{\"items\":[2,1]}", true)]
    [InlineData("{}", "{\"x\":null}", true)]
    [InlineData("{\"x\":null}", "{}", true)]
    [InlineData("null", "{}", true)]
    [InlineData("{", "{}", true)]
    public async Task Publication_compares_JSON_values_and_repairs_a_damaged_previous_report(string stored, string current, bool changed)
    {
        await using var fixture = new EfClusterMembershipConformanceFixture(EfClusterMembershipTestStore.CreateSqlite());
        var row = fixture.NewRow("canonical-report") with { ReportJson = stored };
        await fixture.WriteRowAsync(row);
        var services = new ServiceCollection();
        fixture.ComposeProvider(services);
        await using var provider = services.BuildServiceProvider();
        var store = new EfClusterMembershipStore(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await store.PublishAsync(new(row.HostId, new MemberIncarnation(row.Incarnation)), current, CancellationToken.None);

        Assert.Equal(OwnWriteResult.Written, result.Result);
        Assert.Equal(changed, result.ReportChanged);
        Assert.Equal(changed ? 2 : 1, result.ReportRevision);
        var written = Assert.Single(await fixture.ReadRowsAsync());
        Assert.Equal(changed ? current : stored, written.ReportJson);
        Assert.Equal(changed ? 2 : 1, written.Revision);
        Assert.Equal(ClusterMembershipEfModule.SchemaVersion, written.SchemaVersion);
    }

    [Fact]
    public async Task An_equal_report_with_an_older_stamp_is_still_rewritten_in_the_current_format()
    {
        await using var fixture = new EfClusterMembershipConformanceFixture(EfClusterMembershipTestStore.CreateSqlite());
        const string report = "{\"readability\":null,\"runnability\":null}";
        var row = fixture.NewRow("older-report") with { ReportJson = report, SchemaVersion = "older" };
        await fixture.WriteRowAsync(row);
        var services = new ServiceCollection();
        fixture.ComposeProvider(services);
        await using var provider = services.BuildServiceProvider();
        var store = new EfClusterMembershipStore(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await store.PublishAsync(new(row.HostId, new MemberIncarnation(row.Incarnation)), report, CancellationToken.None);

        Assert.True(result.ReportChanged);
        Assert.Equal(2, result.ReportRevision);
        Assert.Equal(ClusterMembershipEfModule.SchemaVersion, Assert.Single(await fixture.ReadRowsAsync()).SchemaVersion);
    }
}
