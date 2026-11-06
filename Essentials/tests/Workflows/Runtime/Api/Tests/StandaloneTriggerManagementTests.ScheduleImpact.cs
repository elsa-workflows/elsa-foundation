using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task Schedule_preview_uses_the_selected_pure_planner_with_actual_cursor_prepared_write_and_trusted_time()
    {
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        ConfigurationFixture fixture = null!;
        RecurringTriggerSchedule cursor = null!;
        RecurringTriggerSchedule proposed = null!;
        var planner = new PreviewSchedulePlanner((scope, previous, write, current, now) =>
        {
            Assert.Same(fixture.Store.Resource, previous);
            Assert.Same(cursor, current);
            Assert.Equal(fixture.Access.Scope, scope);
            Assert.Equal(fixture.Clock.Now, now);
            Assert.Equal(previous!.Trigger.Revision, write.ExpectedRevision);
            Assert.Equal(previous.Trigger.Revision + 1, write.Trigger.Revision);
            Assert.Equal(previous.Trigger.ConfigurationRevision + 1, write.Trigger.ConfigurationRevision);
            Assert.Equal("America/New_York", write.Trigger.Configuration.GetProperty("timezone").GetString());
            proposed = ScheduleImpactReplacement(scope, write, now.AddHours(15), now);
            return new(StandaloneScheduleAction.Replace, proposed);
        });
        fixture = new("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules, planners: [planner]);
        cursor = ScheduleImpactCursor(fixture);
        await schedules.SaveAsync(cursor);
        var originalResource = fixture.Store.Resource;

        var preview = await fixture.Preview(ConfigurationCommand(Element("""{"expression":"0 0 10 * * *","timezone":"America/New_York"}""")));

        Assert.True(preview.CanApply);
        var firing = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Equal(ScheduleImpactText(cursor), firing.Before);
        Assert.Equal(ScheduleImpactText(proposed), firing.After);
        Assert.EndsWith(" | America/New_York | -05:00", firing.After!, StringComparison.Ordinal);
        Assert.StartsWith("Action: Replace.", firing.Description!, StringComparison.Ordinal);
        Assert.Equal(1, planner.Calls);
        Assert.Same(originalResource, fixture.Store.Resource);
        Assert.Same(cursor, await schedules.FindAsync(cursor.ScheduleId));
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData(StandaloneScheduleAction.Preserve, "SaveConfiguration")]
    [InlineData(StandaloneScheduleAction.Deactivate, "PauseTrigger")]
    [InlineData(StandaloneScheduleAction.Remove, "DeleteTrigger")]
    public async Task Schedule_preview_reports_actual_preserve_pause_and_remove_plan_without_mutating_the_cursor(StandaloneScheduleAction action, string commandKind)
    {
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var planner = new PreviewSchedulePlanner((_, _, _, _, _) => new(action));
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules, planners: [planner]);
        var cursor = ScheduleImpactCursor(fixture);
        await schedules.SaveAsync(cursor);
        var command = commandKind == "SaveConfiguration" ? ConfigurationCommand(fixture.Store.Resource.Trigger.Configuration)
            : JsonSerializer.SerializeToElement(new { kind = commandKind });

        var preview = await fixture.Preview(command);

        Assert.True(preview.CanApply);
        var firing = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Equal(ScheduleImpactText(cursor), firing.Before);
        Assert.Equal(action switch
        {
            StandaloneScheduleAction.Preserve => ScheduleImpactText(cursor),
            StandaloneScheduleAction.Deactivate => "Paused; saved cursor: " + ScheduleImpactText(cursor),
            _ => "No future firing (schedule removed)"
        }, firing.After);
        Assert.StartsWith("Action: " + action + ".", firing.Description!, StringComparison.Ordinal);
        Assert.Same(cursor, await schedules.FindAsync(cursor.ScheduleId));
        Assert.Equal(1, planner.Calls);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData("foreign-scope")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-type")]
    [InlineData("old-configuration")]
    public async Task Schedule_preview_refuses_to_display_or_plan_an_unowned_or_stale_cursor(string mismatch)
    {
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var planner = new PreviewSchedulePlanner((_, _, _, _, _) => throw new InvalidOperationException("must not plan a foreign cursor"));
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules, planners: [planner]);
        var cursor = ScheduleImpactCursor(fixture);
        var owner = cursor.Resource!;
        if (mismatch == "old-configuration")
        {
            var row = fixture.Store.Resource.Trigger;
            fixture.Store.Resource = fixture.Store.Resource with { Trigger = new(row.Id, row.Name, row.TypeKey, row.SchemaVersion, row.Configuration,
                row.Revision + 1, row.ConfigurationRevision + 1, row.AssociationRevision, row.DesiredState, row.Origin, row.CreatedAt, row.UpdatedAt,
                row.CreatedBy, hasActivated: row.HasActivated, tenantId: row.TenantId) };
        }
        else cursor = cursor with { Resource = new(owner.TriggerId, owner.Generation, owner.ConfigurationRevision,
            mismatch == "foreign-type" ? "timer" : owner.SourceTypeKey, owner.SourceFamily,
            mismatch == "foreign-scope" ? new("other-scope") : owner.Scope,
            mismatch == "foreign-tenant" ? "other-tenant" : owner.TenantId, owner.TimeZoneId) };
        await schedules.SaveAsync(cursor);

        var preview = await fixture.Preview(ConfigurationCommand(fixture.Store.Resource.Trigger.Configuration));

        var firing = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Null(firing.Before); Assert.Null(firing.After);
        Assert.Contains("does not match this resource and scope", firing.Description!, StringComparison.Ordinal);
        Assert.Equal(0, planner.Calls);
        Assert.Same(cursor, await schedules.FindAsync(cursor.ScheduleId));
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Schedule_preview_reports_missing_reader_or_duplicate_planners_without_calling_a_plan(bool duplicate)
    {
        var schedules = duplicate ? new InMemoryRecurringTriggerScheduleStore() : null;
        var planner = new PreviewSchedulePlanner((_, _, _, _, _) => throw new InvalidOperationException("must not choose an ambiguous planner"));
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules,
            planners: duplicate ? [planner, planner] : [planner]);

        var preview = await fixture.Preview(ConfigurationCommand(fixture.Store.Resource.Trigger.Configuration));

        var firing = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Null(firing.After);
        Assert.Contains(duplicate ? "several source schedule planners" : "actual schedule reader is unavailable", firing.Description!, StringComparison.Ordinal);
        Assert.Equal(0, planner.Calls);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Schedule_preview_distinguishes_no_future_firing_from_a_safe_calculation_failure(bool exhausted)
    {
        const string secret = "private-planner-exception";
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var planner = new PreviewSchedulePlanner((_, _, _, _, _) =>
        {
            if (exhausted) throw new TriggerOperationException("validation", "This recurrence has no future firing.");
            throw new InvalidOperationException(secret);
        });
        var fixture = new ConfigurationFixture("cron", Element("""{"expression":"0 0 9 * * *","timezone":"UTC"}"""), schedules: schedules, planners: [planner]);
        var cursor = ScheduleImpactCursor(fixture);
        await schedules.SaveAsync(cursor);

        var preview = await fixture.Preview(ConfigurationCommand(fixture.Store.Resource.Trigger.Configuration));

        var firing = Assert.Single(preview.Impact.Where(item => item.Label == "Next firing"));
        Assert.Equal(ScheduleImpactText(cursor), firing.Before);
        if (exhausted) { Assert.Equal("No future firing", firing.After); Assert.Contains("this recurrence has no future firing", firing.Description!, StringComparison.Ordinal); }
        else { Assert.Null(firing.After); Assert.Contains("cannot calculate a supported proposed firing", firing.Description!, StringComparison.Ordinal); }
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(preview), StringComparison.Ordinal);
        Assert.Same(cursor, await schedules.FindAsync(cursor.ScheduleId));
        Assert.Equal(0, fixture.Store.Writes);
    }

    private static RecurringTriggerSchedule ScheduleImpactCursor(ConfigurationFixture fixture)
    {
        var trigger = fixture.Store.Resource.Trigger;
        return RecurringTriggerSchedule.ForResource(new(trigger.Id, trigger.Revision, trigger.ConfigurationRevision,
            trigger.TypeKey, "Test.Cron", fixture.Access.Scope.Persistence, fixture.Access.Scope.TenantId, "UTC"),
            RecurringScheduleKind.Cron, trigger.Configuration.GetProperty("expression").GetString()!, fixture.Clock.Now.AddHours(9), fixture.Clock.Now);
    }

    private static RecurringTriggerSchedule ScheduleImpactReplacement(TriggerScope scope, TriggerStoreWrite write, DateTimeOffset due, DateTimeOffset now) =>
        RecurringTriggerSchedule.ForResource(new(write.Trigger.Id, write.Trigger.Revision, write.Trigger.ConfigurationRevision,
            write.Trigger.TypeKey, "Test.Cron", scope.Persistence, scope.TenantId, write.Trigger.Configuration.GetProperty("timezone").GetString()!),
            RecurringScheduleKind.Cron, write.Trigger.Configuration.GetProperty("expression").GetString()!, due, now);

    private static string ScheduleImpactText(RecurringTriggerSchedule cursor) => cursor.NextOccurrence.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        + " | " + cursor.Resource!.TimeZoneId + " | " + (cursor.Resource.TimeZoneId == "UTC" ? "+00:00" : "-05:00");

    private sealed class PreviewSchedulePlanner(Func<TriggerScope, TriggerAggregate?, TriggerStoreWrite, RecurringTriggerSchedule?, DateTimeOffset, StandaloneSchedulePlan> plan) : IStandaloneSchedulePlanner
    {
        internal int Calls { get; private set; }
        public bool Supports(string typeKey) => typeKey == "cron";
        public StandaloneSchedulePlan Plan(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write, RecurringTriggerSchedule? current, DateTimeOffset now)
        { Calls++; return plan(scope, previous, write, current, now); }
    }
}
