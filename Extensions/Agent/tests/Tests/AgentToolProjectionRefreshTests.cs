using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed partial class AgentCumulativeDisclosureTests
{
    [Theory]
    [InlineData(false, "scope")]
    [InlineData(true, "scope")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "data")]
    [InlineData(true, "data")]
    public async Task R10_current_module_scope_controls_initial_advertisement_without_replacing_checkpointed_context(bool delegated, string change)
    {
        using var fixture = new Fixture(delegated, Script.Answer, services => AddBoundaryStores(services, new RecordingLogger()));
        fixture.Read.RequireScope = true;
        fixture.Source.OfferToolScope = true;
        Assert.Equal("scope-s", Assert.Single(fixture.Read.Descriptor.AgentScopes));
        await fixture.Post("Explain the authorized resource using currently available tools");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.HoldAssistantAt = 1;
        var running = fixture.Stream();
        await store.SuccessfulWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (change == "scope") fixture.Source.RevokeToolScope = true;
        if (change == "data") fixture.Source.AllowedDetailRevisions.Remove("detail-v1");
        store.ReleaseSuccessfulWrite.TrySetResult();

        var events = await running.WaitAsync(TimeSpan.FromSeconds(30));

        if (change == "data")
        {
            Assert.Empty(fixture.Probe.Inputs);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error);
        }
        else
        {
            var input = Assert.Single(fixture.Probe.Inputs);
            Assert.Equal(change == "unchanged", input.Tools.Contains(fixture.Read.Descriptor.Name, StringComparer.Ordinal));
            // Tool discovery uses current scopes; the provider still receives only the frozen,
            // authorized evidence whose provenance was checkpointed before the grant changed.
            Assert.Contains(input.Context, item => item.AgentScopes.Contains("scope-s", StringComparer.Ordinal));
            Assert.Contains(events, item => item.Content == DerivedText);
            Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        }
        Assert.Equal(0, fixture.Read.Preparations);
        Assert.Equal(0, fixture.Read.Executions);
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
    }

    [Theory]
    [InlineData(false, "label")]
    [InlineData(true, "label")]
    [InlineData(false, "route")]
    [InlineData(true, "route")]
    [InlineData(false, "both")]
    [InlineData(true, "both")]
    public async Task R10_authorized_target_presentation_refresh_keeps_canonical_binding_and_checkpointed_tool_context(bool delegated, string presentation)
    {
        using var fixture = new Fixture(delegated, Script.ReadThenAnswer, services => AddBoundaryStores(services, new RecordingLogger()));
        fixture.Probe.BeforeOperation = () => fixture.Source.AddSection = true;
        await fixture.Post("Read the original canonical resource binding");
        var store = fixture.Services.GetRequiredService<SessionBoundary>();
        store.HoldAssistantAt = 2;
        var running = fixture.Stream();
        await store.SuccessfulWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        fixture.Source.TargetPresentation = presentation;
        store.ReleaseSuccessfulWrite.TrySetResult();

        var events = await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Completed);
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.Error);
        Assert.Equal(1, fixture.Read.Preparations);
        Assert.Equal(1, fixture.Read.Executions);
        Assert.NotNull(fixture.Read.PreparedContext);
        Assert.All(fixture.Read.PreparedContext!, item =>
        {
            Assert.Null(item.Target?.DisplayName);
            Assert.Null(item.Target?.Route);
        });
        Assert.Equal(0, fixture.Mutation.ExecuteCalls);
        Assert.False(fixture.Services.GetRequiredService<IAgentTurnRegistry>().IsActive(fixture.Session!.Id));
    }
}
