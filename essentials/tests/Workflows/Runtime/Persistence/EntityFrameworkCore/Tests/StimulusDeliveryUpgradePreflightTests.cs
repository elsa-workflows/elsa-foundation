using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    // Constructed provider-format witnesses, not producer execution or host admission evidence.
    [Theory]
    [InlineData("legacy-pending")]
    [InlineData("legacy-delivered")]
    [InlineData("declared")]
    [InlineData("declared-delivered")]
    [InlineData("accepted")]
    [InlineData("accepted-empty")]
    [InlineData("accepted-delivered")]
    [InlineData("accepted-failed-final")]
    [InlineData("accepted-cancelled")]
    [InlineData("data-only")]
    [InlineData("data-only-delivered")]
    [InlineData("scope-only")]
    [InlineData("scope-only-delivered")]
    [InlineData("generic-scope-only")]
    public async Task Upgrade_preflight_exports_checked_current_and_legacy_format_boundaries(string scenario)
    {
        const string publishKind = "Elsa.Activities.Primitives.PublishStimulus";
        await using var harness = await Harness.Create(true);
        var legacy = scenario.StartsWith("legacy-", StringComparison.Ordinal);
        var kind = scenario == "generic-scope-only" ? "Test.RequiredSource" : publishKind;
        if (scenario == "legacy-pending") await harness.SeedPending(protocol: false, kind: kind);
        else
        {
            var source = new PostCommitDeliveryContext(await harness.Seed(protocol: !legacy, kind: kind), Scope);
            if (scenario.StartsWith("accepted", StringComparison.Ordinal) || scenario.StartsWith("data-only", StringComparison.Ordinal))
                await harness.Delivery.InstallAsync(source, Snapshot(source, empty: scenario == "accepted-empty"));
            if (scenario.EndsWith("delivered", StringComparison.Ordinal))
                await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
            if (scenario == "accepted-failed-final")
                await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedFinal, Now, "fixed fixture failure"));
            if (scenario == "accepted-cancelled")
                await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Cancelled, Now));
        }
        if (scenario is "data-only" or "data-only-delivered" or "scope-only" or "scope-only-delivered" or "generic-scope-only")
        {
            // Historical format controls: preserve all provider-written identities/projections while removing
            // only the newer marker. These controls do not claim those bytes came from an old execution.
            var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
            var wire = JsonNode.Parse(row.ContentJson)!.AsObject();
            if (scenario.StartsWith("data-only", StringComparison.Ordinal)) Assert.True(wire["recipientSnapshot"]!.AsObject().Remove("retentionAcceptance"));
            else Assert.True(wire.Remove("stimulusDeliveryProtocolVersion"));
            row.ContentJson = wire.ToJsonString(); await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        }
        var item = (await harness.Lookup.FindAsync("commit:send"))!;
        Assert.Equal(kind, item.Intent.Kind);
        Assert.Equal(!legacy && scenario is not ("scope-only" or "scope-only-delivered" or "generic-scope-only"), item.StimulusDeliveryProtocolVersion is not null);
        Assert.Equal(!legacy, item.StimulusDeliverySourceScope is not null);
        Assert.Equal(scenario.StartsWith("accepted", StringComparison.Ordinal) || scenario.StartsWith("data-only", StringComparison.Ordinal), item.RecipientSnapshot is not null);
        if (item.RecipientSnapshot is { } snapshot)
        {
            Assert.Equal(!scenario.StartsWith("data-only", StringComparison.Ordinal), snapshot.RetentionAcceptanceVersion is not null);
            Assert.Equal(scenario == "accepted-empty" ? 0 : 2, snapshot.Starts.Count);
        }
        if (scenario.EndsWith("delivered", StringComparison.Ordinal)) Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, item.Status);
        if (scenario == "legacy-pending") Assert.Equal(0, item.DeliveryAttemptCount);
        await harness.Export("preflight-" + scenario);
    }
}
