using System.Text.Json;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shared_change_review_exposes_only_authorized_target_identity_for_optional_names(bool readable)
    {
        var fixture = new Fixture();
        fixture.Access.TargetRead = readable;
        fixture.Store.Resource = fixture.Store.Resource with { Subscriptions = [new("subscription-a", "trigger-a", "definition-a", "default", "native", 1, 1, false, [], new(TriggerManagementKind.Studio), null)] };

        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command = new { kind = "PauseTrigger" } }), default);

        var impact = Assert.Single(preview.Impact, item => item.Label == (readable ? "Target" : "Restricted workflow"));
        if (readable)
        {
            Assert.Equal("definition-a / default", impact.Description);
            Assert.Equal(new TriggerTargetSelector("definition-a", "default"), impact.Target);
        }
        else
        {
            var json = JsonSerializer.Serialize(preview);
            Assert.Null(impact.Target);
            Assert.DoesNotContain("definition-a", json);
        }
    }
}
