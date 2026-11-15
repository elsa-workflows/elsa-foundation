using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData(true, "Reconcile Accounts", "Reconcile Accounts")]
    [InlineData(true, null, "Target")]
    [InlineData(true, " ", "Target")]
    [InlineData(false, "Restricted secret name", "Restricted workflow")]
    public async Task Shared_change_review_names_authorized_targets_and_preserves_restricted_details(bool readable, string? name, string expected)
    {
        var fixture = new Fixture();
        fixture.Discovery.DisplayName = name;
        fixture.Access.TargetRead = readable;
        fixture.Store.Resource = fixture.Store.Resource with { Subscriptions = [new("subscription-a", "trigger-a", "definition-a", "default", "native", 1, 1, false, [], new(TriggerManagementKind.Studio), null)] };

        var preview = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command = new { kind = "PauseTrigger" } }), default);

        var impact = Assert.Single(preview.Impact, item => item.Label == expected);
        if (readable) Assert.Equal("definition-a / default", impact.Description);
        else
        {
            var json = JsonSerializer.Serialize(preview);
            Assert.DoesNotContain("Restricted secret name", json);
            Assert.DoesNotContain("definition-a", json);
        }
    }
}
