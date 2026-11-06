using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData(RecurringTriggerScheduleFailureReason.NoEligibleSubscribers, "NoEligibleSubscribers", "ScheduleNoEligibleSubscribers",
        "Waiting for eligible subscribers. The due occurrence is retained and will be retried.")]
    [InlineData(RecurringTriggerScheduleFailureReason.AdmissionUnavailable, "AdmissionUnavailable", "ScheduleAdmissionUnavailable",
        "Schedule admission is unavailable. The due occurrence is retained for retry.")]
    [InlineData(null, null, null, null)]
    public async Task Schedule_projection_reads_the_persisted_reason_distinguishes_legacy_unknown_and_clears_after_settlement(
        RecurringTriggerScheduleFailureReason? reason, string? wireReason, string? findingCode, string? message)
    {
        var clock = new Clock(); var schedules = new InMemoryRecurringTriggerScheduleStore(clock);
        var fixture = new Fixture(new RuntimeCapability(true, true), schedules);
        fixture.Store.Resource = new(new("trigger-a", "Scheduled", "cron", 1, Element("""{"expression":"* * * * *","timezone":"UTC"}"""),
            1, 1, 0, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), clock.Now, clock.Now, "operator", hasActivated: true, tenantId: "tenant-a"), []);
        var schedule = RecurringTriggerSchedule.ForResource(new("trigger-a", 7, 1, "cron", "Cron", fixture.Access.Scope.Persistence,
            fixture.Access.Scope.TenantId, "UTC"), RecurringScheduleKind.Cron, "* * * * *", clock.Now.AddMinutes(-5), clock.Now.AddHours(-1));
        await schedules.SaveAsync(schedule);
        Assert.Null((await fixture.Management.ReadAsync("trigger-a", default)).Schedule!.FailureReason);
        var claim = Assert.Single(await schedules.ClaimDueAsync(new("source-worker", clock.Now, TimeSpan.FromMinutes(1), 1)));
        Assert.True(reason is { } known
            ? await schedules.ReleaseClaimWithFailureAsync(claim, clock.Now.AddMinutes(2), known)
            : await schedules.ReleaseClaimAsync(claim, clock.Now.AddMinutes(2)));

        var view = await fixture.Management.ReadAsync("trigger-a", default);

        Assert.Equal(wireReason, view.Schedule!.FailureReason);
        Assert.Equal(schedule.NextOccurrence, view.Schedule.NextDueUtc); Assert.Equal(7, view.Schedule.Generation);
        Assert.Equal(1, view.Schedule.FailureCount); Assert.Equal(clock.Now.AddMinutes(2), view.Schedule.NextRetryAt);
        Assert.Contains(view.Findings, finding => finding.Code == "ScheduleRetryBackoff");
        var causes = view.Findings.Where(finding => finding.Code is "ScheduleNoEligibleSubscribers" or "ScheduleAdmissionUnavailable").ToArray();
        if (findingCode is null) Assert.Empty(causes);
        else { var cause = Assert.Single(causes); Assert.Equal(findingCode, cause.Code); Assert.Equal(message, cause.Message); }
        var wire = JsonSerializer.SerializeToElement(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(wireReason, wire.GetProperty("schedule").GetProperty("failureReason").GetString());

        var retried = Assert.Single(await schedules.ClaimDueAsync(new("recovered-worker", clock.Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
        Assert.True(await schedules.SettleClaimAsync(retried, clock.Now.AddHours(1)));
        var settled = await fixture.Management.ReadAsync("trigger-a", default);
        Assert.Null(settled.Schedule!.FailureReason); Assert.Equal(0, settled.Schedule.FailureCount);
        Assert.Null(settled.Schedule.NextRetryAt); Assert.False(settled.Schedule.IsOverdue);
        Assert.DoesNotContain(settled.Findings, finding => finding.Code is "ScheduleNoEligibleSubscribers" or "ScheduleAdmissionUnavailable" or "ScheduleRetryBackoff" or "ScheduleRetryWaiting");
    }
}
