using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing;

namespace Elsa.Cluster.Tests.Core;

public sealed class MemberReportTests
{
    private static readonly ReadabilityEntry Orders = new("orders", "OrdersModule", ["1", "2"], "db-a");

    [Fact]
    public void Reports_with_the_same_content_are_equal()
    {
        var copy = new ReadabilityEntry("orders", "OrdersModule", ["1", "2"], "db-a");

        Assert.Equal(Report(Orders), Report(copy));
        Assert.Equal(Report(Orders).GetHashCode(), Report(copy).GetHashCode());
    }

    public static TheoryData<ReadabilityEntry> Differences =>
    [
        new("customers", "OrdersModule", ["1", "2"], "db-a"),
        new("orders", "SalesModule", ["1", "2"], "db-a"),
        new("orders", "OrdersModule", ["2", "1"], "db-a"),
        new("orders", "OrdersModule", ["1"], "db-a"),
        new("orders", "OrdersModule", ["1", "2"], "db-b"),
        new("orders", "OrdersModule", ["1", "2"])
    ];

    [Theory]
    [MemberData(nameof(Differences))]
    public void Any_difference_in_an_entry_makes_a_different_report(ReadabilityEntry different) =>
        Assert.NotEqual(Report(Orders), Report(different));

    [Fact]
    public void An_unknown_report_differs_from_every_known_one()
    {
        Assert.True(MemberReport.Unknown.IsUnknown);
        Assert.NotEqual(MemberReport.Empty, MemberReport.Unknown);
        Assert.Null(MemberReport.Unknown.Readability);
    }

    [Fact]
    public void An_entry_needs_a_family_a_module_and_non_blank_version_labels()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ReadabilityEntry(" ", "OrdersModule", ["1"]));
        Assert.ThrowsAny<ArgumentException>(() => new ReadabilityEntry("orders", "", ["1"]));
        Assert.ThrowsAny<ArgumentException>(() => new ReadabilityEntry("orders", "OrdersModule", ["1", " "]));
        Assert.ThrowsAny<ArgumentException>(() => new ReadabilityEntry("orders", "OrdersModule", ["1"], " "));
    }

    [Fact]
    public async Task A_report_holds_what_its_source_reports_at_that_moment()
    {
        var source = new ConformanceReadabilitySource([Orders]);

        Assert.Equal(Report(Orders), await MemberReportComposition.ComposeAsync(source));
        Assert.Equal(new MemberReport(), await MemberReportComposition.ComposeAsync(readability: null));
    }

    [Fact]
    public void A_section_has_at_most_one_source()
    {
        Assert.Null(MemberReportComposition.SingleSource(Array.Empty<IMemberReportSource<ReadabilitySection>>()));
        var only = new ConformanceReadabilitySource([]);
        Assert.Same(only, MemberReportComposition.SingleSource([only]));

        var refusal = Assert.Throws<ClusterMembershipConfigurationException>(() =>
            MemberReportComposition.SingleSource<ReadabilitySection>([only, new SecondSource()]));
        Assert.Contains(typeof(ConformanceReadabilitySource).FullName!, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondSource).FullName!, refusal.Message, StringComparison.Ordinal);
    }

    private static MemberReport Report(ReadabilityEntry entry) => new(new ReadabilitySection([entry]));

    private sealed class SecondSource : IMemberReportSource<ReadabilitySection>
    {
        public ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ReadabilitySection([]));
    }
}
