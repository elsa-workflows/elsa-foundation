using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task Resource_views_redact_sensitive_literal_and_nested_paths_and_preserve_omitted_values_when_editing()
    {
        var fixture = new ConfigurationFixture("event", Element("""{"setting":"before","token":"private-token","auth":{"key":"private-key","public":"visible"},"auth.key":"private-literal"}"""),
            fields: [new("setting", "Setting"), new("token", "Credential", Sensitive: true), new("auth.key", "Nested credential", Sensitive: true)]);
        var view = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.False(view.Configuration.TryGetProperty("token", out _)); Assert.False(view.Configuration.TryGetProperty("auth.key", out _));
        Assert.False(view.Configuration.GetProperty("auth").TryGetProperty("key", out _));
        Assert.Equal("visible", view.Configuration.GetProperty("auth").GetProperty("public").GetString());
        Assert.Equal(3, view.ConfigurationRedactions!.Count);
        var list = await fixture.Management.ListAsync(new(), default);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(list));
        var command = ConfigurationCommand(Element("""{"setting":"after","auth":{"public":"edited"}}"""));
        var applied = await fixture.Apply(command, await fixture.Preview(command));
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(applied));
        var stored = fixture.Store.Resource.Trigger.Configuration;
        Assert.Equal("private-token", stored.GetProperty("token").GetString());
        Assert.Equal("private-key", stored.GetProperty("auth").GetProperty("key").GetString());
        Assert.Equal("private-literal", stored.GetProperty("auth.key").GetString());
        Assert.Equal("edited", stored.GetProperty("auth").GetProperty("public").GetString());
    }

    [Theory]
    [InlineData("SaveConfiguration")] [InlineData("EnableTrigger")]
    public async Task Restricted_impact_blocks_preview_and_losing_review_access_blocks_fresh_apply(string kind)
    {
        var fixture = new ConfigurationFixture("event", Element("""{"eventName":"before"}"""));
        var command = kind == "SaveConfiguration" ? ConfigurationCommand(Element("""{"eventName":"after"}""")) : Element("""{"kind":"EnableTrigger"}""");
        var allowed = await fixture.Preview(command); Assert.True(allowed.CanApply);
        fixture.Access.TargetRead = false;
        var blocked = await fixture.Preview(command); Assert.False(blocked.CanApply);
        Assert.DoesNotContain("definition-", JsonSerializer.Serialize(blocked));
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Apply(command, allowed).AsTask());
        Assert.Equal("validation", error.Code); Assert.Equal(0, fixture.Store.Writes);
    }
}
