using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class StandaloneTriggerAdmissionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Published_targets_remain_pageable_as_time_advances_within_and_between_requests(int limit)
    {
        using var fixture = new Fixture();
        await fixture.Publish("a"); await fixture.Publish("b");
        fixture.Clock.AutoAdvanceAmount = TimeSpan.FromMilliseconds(1);
        var first = await fixture.Discovery.ListAsync(Scope, new(limit));
        if (limit == 1)
        {
            Assert.Equal("a", Assert.Single(first.Items).DefinitionId);
            Assert.NotNull(first.Next);
            fixture.Clock.Advance(TimeSpan.FromSeconds(10));
            var second = await fixture.Discovery.ListAsync(Scope, new(limit, first.Next));
            Assert.Equal("b", Assert.Single(second.Items).DefinitionId);
            Assert.Null(second.Next);
        }
        else
        {
            Assert.Equal(["a", "b"], first.Items.Select(item => item.DefinitionId));
            Assert.Null(first.Next);
        }
    }

    [Fact]
    public async Task Target_pages_filter_retired_and_expired_sources_including_expiry_between_requests()
    {
        using var fixture = new Fixture();
        await fixture.Publish("a-expired", expiresAt: Now.AddSeconds(-1));
        await fixture.Publish("b-retired", retired: true);
        await fixture.Publish("c-live");
        await fixture.Publish("d-expires-next", expiresAt: Now.AddSeconds(5));
        var first = await fixture.Discovery.ListAsync(Scope, new(1));
        Assert.Equal("c-live", Assert.Single(first.Items).DefinitionId);
        Assert.NotNull(first.Next);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var second = await fixture.Discovery.ListAsync(Scope, new(1, first.Next));
        Assert.Empty(second.Items);
        Assert.Null(second.Next);
        Assert.Equal(0, fixture.Delivery.Admissions);
    }
}
