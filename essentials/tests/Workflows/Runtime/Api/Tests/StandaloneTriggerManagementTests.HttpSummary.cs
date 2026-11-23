using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData("AsyncFanOut", "RequireAll", true)]
    [InlineData("AsyncFanOut", "AcceptAvailable", true)]
    [InlineData("WorkflowResponse", null, true)]
    [InlineData("AsyncFanOut", "RequireAll", false)]
    [InlineData("AsyncFanOut", "AcceptAvailable", false)]
    [InlineData("WorkflowResponse", null, false)]
    public async Task Associations_include_only_safe_HTTP_mode_and_policy_for_both_target_visibility_states(string mode, string? policy, bool targetRead)
    {
        var fixture = new Fixture();
        fixture.Access.TargetRead = targetRead;
        var configuration = JsonSerializer.SerializeToElement(new { path = "/private-source-route", secret = "private-credential", http = new { mode, admissionPolicy = policy } });
        var original = fixture.Store.Resource.Trigger;
        var trigger = new StandaloneTrigger(original.Id, original.Name, "http", 1, configuration, original.Revision,
            original.ConfigurationRevision, original.AssociationRevision, original.DesiredState, original.Origin,
            original.CreatedAt, original.UpdatedAt, original.CreatedBy, tenantId: original.TenantId);
        fixture.Store.Resource = new(trigger, [new("subscription-a", trigger.Id, "definition-a", "default", "native", 1, 1, false, [], new(TriggerManagementKind.Studio))]);

        var page = await fixture.Management.SubscriptionsAsync(new(TriggerId: trigger.Id), default);
        var summary = JsonSerializer.SerializeToElement(Assert.Single(page.Items).TriggerSummary);
        var http = summary.GetProperty("http");
        Assert.Equal(mode, http.GetProperty("mode").GetString());
        Assert.Equal(policy, http.GetProperty("admissionPolicy").GetString());
        Assert.DoesNotContain("private-source-route", summary.GetRawText());
        Assert.DoesNotContain("private-credential", summary.GetRawText());
        Assert.Equal(0, fixture.Store.Writes);
    }
}
