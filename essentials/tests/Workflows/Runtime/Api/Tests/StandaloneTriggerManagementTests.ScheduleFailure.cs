using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
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
            7, 1, 0, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), clock.Now, clock.Now, "operator", hasActivated: true, tenantId: "tenant-a"), []);
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

    [Theory]
    [InlineData(RecurringTriggerScheduleFailureReason.NoEligibleSubscribers, "ScheduleNoEligibleSubscribers", "Last recorded failure: no eligible subscribers.")]
    [InlineData(RecurringTriggerScheduleFailureReason.AdmissionUnavailable, "ScheduleAdmissionUnavailable", "Last recorded failure: schedule admission was unavailable.")]
    public async Task Inactive_schedule_failure_observations_are_history_without_promising_a_retry(
        RecurringTriggerScheduleFailureReason reason, string code, string history)
    {
        var clock = new Clock();
        var schedule = RecurringTriggerSchedule.ForResource(new("trigger-a", 7, 1, "cron", "Cron", new("tenant-a"), "tenant-a", "UTC"),
            RecurringScheduleKind.Cron, "* * * * *", clock.Now.AddMinutes(-5), clock.Now.AddHours(-1)) with { IsActive = false, FailureReason = reason };
        var reader = new ScheduleHistoryReader(new(schedule, 2, clock.Now.AddMinutes(2), false));
        var fixture = new Fixture(new RuntimeCapability(true, true), reader);
        fixture.Store.Resource = new(new("trigger-a", "Scheduled", "cron", 1, Element("""{"expression":"0 0 10 * * *","timezone":"UTC"}"""),
            8, 2, 0, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), clock.Now, clock.Now, "operator", hasActivated: true, tenantId: "tenant-a"), []);

        var view = await fixture.Management.ReadAsync("trigger-a", default);

        Assert.Equal("Paused", view.EffectiveState);
        Assert.NotNull(view.Schedule);
        Assert.False(view.Schedule.IsActive);
        Assert.False(view.Schedule.IsOverdue);
        Assert.Equal(reason.ToString(), view.Schedule.FailureReason);
        Assert.Equal(clock.Now.AddMinutes(2), view.Schedule.NextRetryAt);
        Assert.Contains(view.Findings, finding => finding.Code == code && finding.Message.StartsWith(history, StringComparison.Ordinal));
        Assert.Contains(view.Findings, finding => finding.Code == "ScheduleRetryBackoff" && finding.Message.Contains("No retry is scheduled", StringComparison.Ordinal));
        Assert.DoesNotContain(view.Findings, finding => finding.Message.Contains("will be retried", StringComparison.Ordinal) ||
            finding.Message.Contains("retained for retry", StringComparison.Ordinal));
    }

    private sealed class ScheduleHistoryReader(RecurringTriggerScheduleObservation observation) : IRecurringTriggerScheduleStore
    {
        public ValueTask<RecurringTriggerScheduleObservation?> ObserveAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult<RecurringTriggerScheduleObservation?>(id == observation.Schedule.ScheduleId ? observation : null);
        public ValueTask<RecurringTriggerSchedule?> FindAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult<RecurringTriggerSchedule?>(id == observation.Schedule.ScheduleId ? observation.Schedule : null);
        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule value, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<RecurringTriggerOccurrenceClaim>> ClaimDueAsync(RecurringTriggerOccurrenceClaimRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<RecurringTriggerOccurrenceClaim?> RenewClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset now, TimeSpan duration, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<bool> SettleClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset next, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<bool> ReleaseClaimAsync(RecurringTriggerOccurrenceClaim claim, DateTimeOffset next, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DeleteByArtifactAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
