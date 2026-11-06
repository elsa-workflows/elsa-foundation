using System.Text.Json.Nodes;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Expired_ordinary_key_still_replays_or_conflicts_while_the_job_is_pending_without_an_origin(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16));
        var original = Admission("ordinary-pending");
        var accepted = (await store.AdmitAsync(original)).Acceptance!;
        clock.Set(checked(clock.Now + original.Request.Policy.MinimumKeyRecognition.Ticks + 1));
        var replay = await store.AdmitAsync(original with { ProposedJobId = "later-proposal" });
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(accepted.JobId, replay.Acceptance!.JobId);
        var changed = original.Request with { Queue = "different" };
        Assert.Equal(JobCommandStatus.Conflict, (await store.AdmitAsync(original with { Request = changed, RequestSha256 = JobRequestDigest.Compute(changed) })).Status);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Empty(await check.Origins.ToArrayAsync()); Assert.Single(await check.Jobs.ToArrayAsync());
        Assert.True((await check.Recognition.SingleAsync()).RecognizeUntil < clock.Now);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Expired_ordinary_key_is_protected_by_owed_result_delivery_without_an_origin(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16));
        var association = new JobAssociation("native", "ordinary-definition", "1", "ordinary-instance", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("continue", 1), new { Wait = "saved" }));
        var original = Admission("ordinary-owed-delivery", Request(association));
        var accepted = (await store.AdmitAsync(original)).Acceptance!;
        var lease = (await store.ClaimAsync(Claim())).Lease!;
        await store.CompleteAsync(lease, Receipt(lease));
        clock.Set(checked(clock.Now + original.Request.Policy.MinimumKeyRecognition.Ticks + 1));
        var replay = await store.AdmitAsync(original with { ProposedJobId = "later-proposal" });
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(accepted.JobId, replay.Acceptance!.JobId);
        var changed = original.Request with { Queue = "different" };
        Assert.Equal(JobCommandStatus.Conflict, (await store.AdmitAsync(original with { Request = changed, RequestSha256 = JobRequestDigest.Compute(changed) })).Status);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Empty(await check.Origins.ToArrayAsync()); Assert.Single(await check.Jobs.ToArrayAsync());
        Assert.False((await check.Deliveries.SingleAsync()).Acknowledged);
        Assert.True((await check.Recognition.SingleAsync()).RecognizeUntil < clock.Now);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Expired_settled_ordinary_key_reuses_only_its_key_while_old_job_and_origin_stay_protected(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16));
        var original = Admission("ordinary-settled", origin: new("producer-outbox", "sender", "original-intent", new('b', 64)));
        var first = (await store.AdmitAsync(original)).Acceptance!;
        var lease = (await store.ClaimAsync(Claim())).Lease!; await store.CompleteAsync(lease, Receipt(lease));
        var originalResultId = (await store.FindAsync(Scope, first.JobId))!.Result!.ResultId;
        clock.Set(checked(clock.Now + original.Request.Policy.MinimumKeyRecognition.Ticks + 1));
        // Omit the origin here deliberately: this must execute ordinary-key expiry/cleanup.
        var fresh = await store.AdmitAsync(original with { Origin = null, ProposedJobId = "new-after-cleanup" });
        Assert.Equal(JobCommandStatus.Applied, fresh.Status); Assert.NotEqual(first.JobId, fresh.Acceptance!.JobId);
        Assert.Equal(originalResultId, (await store.FindAsync(Scope, first.JobId))!.Result!.ResultId);
        var originReplay = await store.AdmitAsync(original with { IdempotencyKey = "new-key-old-origin", ProposedJobId = "never-create" });
        Assert.Equal(JobCommandStatus.Replayed, originReplay.Status); Assert.Equal(first.JobId, originReplay.Acceptance!.JobId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(2, await check.Jobs.CountAsync()); Assert.Single(await check.Origins.ToArrayAsync());
        Assert.Equal(JobStoreCodec.Key(Scope, "job", fresh.Acceptance.JobId), (await check.Recognition.SingleAsync()).JobKey);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Missing_retained_input_value_is_quarantined_and_preserves_unrelated_discovery(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("missing-input-value"))).Acceptance!;
        string bytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.SingleAsync(); var json = JsonNode.Parse(row.RequestJson!)!.AsObject();
            json["Input"]!.AsObject().Remove("Value"); row.RequestJson = bytes = json.ToJsonString(); await db.SaveChangesAsync();
        }
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        var unavailable = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Null(unavailable.Request); Assert.NotNull(unavailable.UnavailableDiagnostic);
        var healthy = (await database.Commands.AdmitAsync(Admission("healthy-after-missing-value"))).Acceptance!;
        Assert.Equal(healthy.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
        Assert.Equal(bytes, retained.RequestJson); Assert.NotNull(retained.UnavailableDiagnosticJson); Assert.Equal(0, retained.AttemptCount);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "json")]
    [InlineData("PostgreSql", "json")]
    [InlineData("Sqlite", "missing-value")]
    [InlineData("PostgreSql", "missing-value")]
    [InlineData("Sqlite", "null-schema")]
    [InlineData("PostgreSql", "null-schema")]
    [InlineData("Sqlite", "recurrence")]
    [InlineData("PostgreSql", "recurrence")]
    [InlineData("Sqlite", "definition-id")]
    [InlineData("PostgreSql", "definition-id")]
    public async Task Invalid_schedule_definitions_are_visible_and_quarantined_without_losing_owed_cursor_or_healthy_progress(string provider, string kind)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var ids = OrderedScheduleIds("poison-" + kind, 2);
        var definition = new JobScheduleDefinition(ids[0], Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        await store.SaveScheduleAsync(new(Scope, definition with { ScheduleId = ids[1] }, 0, Op()));
        string bytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
            var json = JsonNode.Parse(row.DefinitionJson)!.AsObject();
            if (kind == "missing-value") json["Template"]!["Input"]!.AsObject().Remove("Value");
            if (kind == "null-schema") json["Template"]!["Input"]!["Schema"] = null;
            if (kind == "recurrence") json["Recurrence"]!["Expression"] = "not an interval";
            if (kind == "definition-id") json["ScheduleId"] = "another-retained-identity";
            row.DefinitionJson = bytes = kind == "json" ? "{" : json.ToJsonString(); await db.SaveChangesAsync();
        }
        var unavailable = (await store.FindScheduleAsync(Scope, ids[0]))!;
        Assert.Equal(ids[0], unavailable.ScheduleId); Assert.Null(unavailable.Definition); Assert.Equal("jobs.schedule.unavailable", unavailable.UnavailableDiagnostic!.Code);
        Assert.Equal(2, (await store.ListSchedulesAsync(Scope, new(10, null))).Items.Count);
        var quarantined = await store.MaterializeDueAsync(new(Scope, 1)); Assert.Empty(quarantined.Occurrences);
        Assert.Equal(JobCommandStatus.Applied, quarantined.Status);
        Assert.Equal(ids[1], Assert.Single((await store.MaterializeDueAsync(new(Scope, 1))).Occurrences).ScheduleId);
        await store.MaterializeDueAsync(new(Scope, 1));
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
        Assert.Equal(bytes, retained.DefinitionJson); Assert.Equal(clock.Now, retained.NextOccurrence); Assert.True(retained.Enabled);
        Assert.NotNull(retained.UnavailableDiagnosticJson); Assert.Equal(2, retained.Revision); Assert.Equal(1, retained.DefinitionRevision);
        Assert.Null(retained.CoalesceNext); Assert.Null(retained.CoalesceCutoff); Assert.Equal(0, retained.CoalesceCount);
        Assert.Single(await check.Jobs.ToArrayAsync()); Assert.Single(await check.Occurrences.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Default_calculator_refuses_seedless_jitter_before_persisting_a_schedule(string provider)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var definition = new JobScheduleDefinition("seedless-jitter", Request(), new(JobRecurrenceKind.Cron, "H * * * *"),
            DateTimeOffset.UtcNow, JobMissedOccurrencePolicy.CoalesceOne);
        var error = await Assert.ThrowsAsync<JobException>(() => database.Commands.SaveScheduleAsync(new(Scope, definition, 0, Op())).AsTask());
        Assert.Equal("jobs.recurrence.invalid", error.Code);
        var normalized = Assert.IsType<FormatException>(error.InnerException);
        Assert.Equal("Cronos.MissingSeedException", normalized.InnerException?.GetType().FullName);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Empty(await check.Schedules.ToArrayAsync()); Assert.Empty(await check.Jobs.ToArrayAsync()); Assert.Empty(await check.Occurrences.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Retained_seedless_jitter_is_quarantined_without_losing_its_cursor_or_healthy_same_scope_progress(string provider)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 1, 16), database.Recurrence);
        var ids = OrderedScheduleIds("seedless-jitter", 2);
        var definition = new JobScheduleDefinition(ids[0], Request(), new(JobRecurrenceKind.Interval, "PT1H"),
            JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        await store.SaveScheduleAsync(new(Scope, definition with { ScheduleId = ids[1] }, 0, Op()));
        string bytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
            var json = JsonNode.Parse(row.DefinitionJson)!.AsObject();
            json["Recurrence"]!["Kind"] = (int)JobRecurrenceKind.Cron;
            json["Recurrence"]!["Expression"] = "H * * * *";
            row.DefinitionJson = bytes = json.ToJsonString(); await db.SaveChangesAsync();
        }
        var unavailable = (await store.FindScheduleAsync(Scope, ids[0]))!;
        Assert.Equal(ids[0], unavailable.ScheduleId); Assert.Null(unavailable.Definition);
        Assert.Equal("jobs.schedule.unavailable", unavailable.UnavailableDiagnostic!.Code);
        Assert.Equal(2, (await store.ListSchedulesAsync(Scope, new(10, null))).Items.Count);
        var quarantined = await store.MaterializeDueAsync(new(Scope, 1));
        Assert.Equal(JobCommandStatus.Applied, quarantined.Status); Assert.Empty(quarantined.Occurrences);
        Assert.Equal(ids[1], Assert.Single((await store.MaterializeDueAsync(new(Scope, 1))).Occurrences).ScheduleId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
        Assert.Equal(bytes, retained.DefinitionJson); Assert.Equal(clock.Now, retained.NextOccurrence); Assert.True(retained.Enabled);
        Assert.NotNull(retained.UnavailableDiagnosticJson); Assert.Equal(2, retained.Revision); Assert.Equal(1, retained.DefinitionRevision);
        Assert.Null(retained.CoalesceNext); Assert.Null(retained.CoalesceCutoff); Assert.Equal(0, retained.CoalesceCount);
        Assert.Single(await check.Jobs.ToArrayAsync()); Assert.Single(await check.Occurrences.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Unsupported_schedule_schemas_are_filtered_before_bounded_selection_and_project_stable_legacy_identity(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 1, 16), database.Recurrence);
        var ids = OrderedScheduleIds("unsupported-prefix", 5);
        foreach (var id in ids)
            await store.SaveScheduleAsync(new(Scope, new(id, Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne), 0, Op()));
        string[] bytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            bytes = await db.Schedules.OrderBy(x => x.Key).Select(x => x.DefinitionJson).ToArrayAsync();
            var healthyKey = JobStoreCodec.Key(Scope, "schedule", ids[^1]);
            await db.Schedules.Where(x => x.Key != healthyKey).ExecuteUpdateAsync(x => x.SetProperty(row => row.SchemaVersion, "unsupported-future"));
        }
        Assert.Equal(ids[^1], Assert.Single((await store.MaterializeDueAsync(new(Scope, 1))).Occurrences).ScheduleId);
        var listed = (await store.ListSchedulesAsync(Scope, new(10, null))).Items;
        Assert.Equal(5, listed.Count);
        foreach (var id in ids[..^1])
        {
            var row = listed.Single(x => x.ScheduleId == id);
            Assert.Null(row.Definition); Assert.Equal("jobs.schedule.schema-unavailable", row.UnavailableDiagnostic!.Code);
            Assert.Equal(id, (await store.FindScheduleAsync(Scope, id))!.ScheduleId);
        }
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.OrderBy(x => x.Key).ToArrayAsync();
        Assert.Equal(bytes, retained.Select(x => x.DefinitionJson).ToArray());
        Assert.All(retained[..^1], row => { Assert.Equal(clock.Now, row.NextOccurrence); Assert.Equal(1, row.Revision); Assert.Null(row.UnavailableDiagnosticJson); });
        Assert.Single(await check.Jobs.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Unavailable_calculator_is_retryable_and_does_not_quarantine_or_block_other_schedules(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var unavailable = false;
        var calculator = new DelegateCalculator((expression, after) =>
        {
            if (unavailable && expression.Expression == "00:15:00") throw new JobException("jobs.recurrence.unavailable", "Test calculator temporarily unavailable.");
            return database.Recurrence.Next(expression, after);
        });
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 1, 16), calculator);
        var ids = OrderedScheduleIds("retry-calculator", 2);
        var definition = new JobScheduleDefinition(ids[0], Request(), new(JobRecurrenceKind.Interval, "00:15:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        await store.SaveScheduleAsync(new(Scope, definition with { ScheduleId = ids[1], Recurrence = new(JobRecurrenceKind.Interval, "01:00:00") }, 0, Op()));
        var absent = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 1, 16));
        var absentResult = await absent.MaterializeDueAsync(new(Scope, 10));
        Assert.Equal(JobCommandStatus.Unavailable, absentResult.Status); Assert.Empty(absentResult.Occurrences);
        Assert.NotNull((await absent.FindScheduleAsync(Scope, ids[0]))!.Definition);
        Assert.Equal("jobs.recurrence.unavailable", (await absent.FindScheduleAsync(Scope, ids[0]))!.UnavailableDiagnostic!.Code);
        unavailable = true;
        var skipped = await store.MaterializeDueAsync(new(Scope, 1)); Assert.Empty(skipped.Occurrences); Assert.Equal(JobCommandStatus.Unavailable, skipped.Status);
        var projection = (await store.FindScheduleAsync(Scope, ids[0]))!;
        Assert.NotNull(projection.Definition); Assert.Equal("jobs.recurrence.unavailable", projection.UnavailableDiagnostic!.Code);
        Assert.Equal(ids[1], Assert.Single((await store.MaterializeDueAsync(new(Scope, 1))).Occurrences).ScheduleId);
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
            Assert.Equal(clock.Now, row.NextOccurrence); Assert.Equal(1, row.Revision); Assert.Null(row.UnavailableDiagnosticJson);
        }
        unavailable = false;
        JobOccurrenceReceipt? recovered = null;
        for (var i = 0; i < 4 && recovered is null; i++) recovered = (await store.MaterializeDueAsync(new(Scope, 1))).Occurrences.SingleOrDefault();
        Assert.Equal(ids[0], recovered!.ScheduleId); Assert.Equal(definition.FirstOccurrence, recovered.ScheduledAt);
        Assert.Null((await store.FindScheduleAsync(Scope, ids[0]))!.UnavailableDiagnostic);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Replay_budget_preserves_discovery_progress_to_a_later_due_schedule_on_the_next_sweep(string provider)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var factory = new InterceptedFactory(database.Contexts, clock);
        var store = new EfJobStore(factory, new(100, 3, 16), database.Recurrence);
        var ids = OrderedScheduleIds("replay-fairness", 2);
        var first = JobStoreCodec.At(clock.Now) - TimeSpan.FromSeconds(100);
        await store.SaveScheduleAsync(new(Scope, new(ids[0], Request(), new(JobRecurrenceKind.Interval, "PT1S"), first,
            JobMissedOccurrencePolicy.ReplayEveryOccurrence), 0, Op()));
        await store.SaveScheduleAsync(new(Scope, new(ids[1], Request(), new(JobRecurrenceKind.Interval, "PT1H"), JobStoreCodec.At(clock.Now),
            JobMissedOccurrencePolicy.CoalesceOne), 0, Op()));
        var backlog = await store.MaterializeDueAsync(new(Scope, 32));
        Assert.Equal(32, backlog.Occurrences.Count); Assert.All(backlog.Occurrences, occurrence => Assert.Equal(ids[0], occurrence.ScheduleId));
        // Reconstruct the authority so progress must come from the committed discovery position.
        store = new(factory, new(100, 3, 16), database.Recurrence);
        var later = Assert.Single((await store.MaterializeDueAsync(new(Scope, 32))).Occurrences);
        Assert.Equal(ids[1], later.ScheduleId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var owed = await check.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
        Assert.Equal((first + TimeSpan.FromSeconds(32)).UtcTicks, owed.NextOccurrence); Assert.True(owed.NextOccurrence < clock.Now);
        Assert.Equal(33, await check.Occurrences.CountAsync()); Assert.Equal(33, await check.Jobs.CountAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Committed_schedule_save_replays_without_a_calculator_but_new_saves_still_require_live_arithmetic(string provider)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var unavailable = false; var calculations = 0;
        var calculator = new DelegateCalculator((expression, after) =>
        {
            calculations++;
            if (unavailable) throw new JobException("jobs.recurrence.unavailable", "The test calculator is temporarily unavailable.");
            return database.Recurrence.Next(expression, after);
        });
        var acknowledgement = new LoseOneCommitReply();
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, acknowledgement), new(100, 3, 16), calculator);
        var definition = new JobScheduleDefinition("save-acknowledgement", Request(), new(JobRecurrenceKind.Interval, "PT1H"),
            DateTimeOffset.UtcNow, JobMissedOccurrencePolicy.CoalesceOne);
        var request = new JobSaveScheduleRequest(Scope, definition, 0, Op());
        await Assert.ThrowsAsync<JobPersistenceException>(() => store.SaveScheduleAsync(request).AsTask());
        Assert.True(acknowledgement.Fired);
        unavailable = true; calculations = 0;
        var replay = await store.SaveScheduleAsync(request);
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(1, replay.Revision); Assert.Equal(0, calculations);
        var absent = new EfJobStore(database.Contexts, new(100, 3, 16));
        var absentReplay = await absent.SaveScheduleAsync(request);
        Assert.Equal(JobCommandStatus.Replayed, absentReplay.Status); Assert.Equal(replay.Revision, absentReplay.Revision);
        var fresh = request with { Definition = definition with { ScheduleId = "new-unavailable-save" }, Operation = Op() };
        var temporaryFailure = await Assert.ThrowsAsync<JobException>(() => store.SaveScheduleAsync(fresh).AsTask());
        Assert.Equal("jobs.recurrence.unavailable", temporaryFailure.Code); Assert.Equal(1, calculations);
        var absentFailure = await Assert.ThrowsAsync<JobException>(() => absent.SaveScheduleAsync(fresh with { Operation = Op() }).AsTask());
        Assert.Equal("jobs.recurrence.unavailable", absentFailure.Code);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = Assert.Single(await check.Schedules.ToArrayAsync());
        Assert.Equal(1, retained.Revision); Assert.Equal(JobStoreCodec.Json(definition), retained.DefinitionJson);
        Assert.Single(await check.Operations.ToArrayAsync()); Assert.Empty(await check.Jobs.ToArrayAsync()); Assert.Empty(await check.Occurrences.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Cancellation_during_first_recurrence_calculation_stops_both_missed_work_policies_without_state_changes(string provider)
    {
        foreach (var policy in new[] { JobMissedOccurrencePolicy.CoalesceOne, JobMissedOccurrencePolicy.ReplayEveryOccurrence })
        {
            await using var database = await Database(provider, defaultArithmetic: true);
            var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
            using var stopping = new CancellationTokenSource();
            var cancelInside = false; var calculations = 0;
            var calculator = new DelegateCalculator((expression, after) =>
            {
                if (cancelInside) { calculations++; stopping.Cancel(); }
                return database.Recurrence.Next(expression, after);
            });
            var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 100_000, 16), calculator);
            var definition = new JobScheduleDefinition("cancel-arithmetic", Request(), new(JobRecurrenceKind.Interval, "PT1S"),
                JobStoreCodec.At(clock.Now) - TimeSpan.FromSeconds(100), policy);
            await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
            cancelInside = true;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.MaterializeDueAsync(new(Scope, 32), stopping.Token).AsTask());
            Assert.Equal(1, calculations);
            await using var check = await database.Contexts.CreateDbContextAsync();
            var retained = Assert.Single(await check.Schedules.ToArrayAsync());
            Assert.Equal(definition.FirstOccurrence.UtcTicks, retained.NextOccurrence); Assert.Equal(1, retained.Revision);
            Assert.Equal(JobStoreCodec.Json(definition), retained.DefinitionJson); Assert.Null(retained.UnavailableDiagnosticJson);
            Assert.Null(retained.CoalesceNext); Assert.Null(retained.CoalesceCutoff); Assert.Equal(0, retained.CoalesceCount);
            Assert.Empty(await check.ClaimCursors.ToArrayAsync()); Assert.Empty(await check.Jobs.ToArrayAsync()); Assert.Empty(await check.Occurrences.ToArrayAsync());
        }
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Arithmetic_failure_later_in_a_replay_slice_cannot_accept_a_partial_slice_or_advance_its_owed_cursor(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var first = JobStoreCodec.At(clock.Now) - TimeSpan.FromHours(3);
        var fail = false;
        var calculator = new DelegateCalculator((expression, after) =>
        {
            if (fail && expression.Expression == "01:00:00" && after >= first + TimeSpan.FromHours(1)) throw new FormatException("Test retained arithmetic failure after the first candidate.");
            return database.Recurrence.Next(expression, after);
        });
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), calculator);
        var ids = OrderedScheduleIds("partial-replay", 2);
        await store.SaveScheduleAsync(new(Scope, new(ids[0], Request(), new(JobRecurrenceKind.Interval, "01:00:00"), first, JobMissedOccurrencePolicy.ReplayEveryOccurrence), 0, Op()));
        await store.SaveScheduleAsync(new(Scope, new(ids[1], Request(), new(JobRecurrenceKind.Interval, "02:00:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne), 0, Op()));
        fail = true;
        var result = await store.MaterializeDueAsync(new(Scope, 10)); Assert.Equal(ids[1], Assert.Single(result.Occurrences).ScheduleId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
        Assert.Equal(first.UtcTicks, retained.NextOccurrence); Assert.NotNull(retained.UnavailableDiagnosticJson);
        Assert.Single(await check.Jobs.ToArrayAsync()); Assert.Single(await check.Occurrences.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Schedule_quarantine_preserves_previously_accepted_occurrences_and_the_exact_partial_catchup_state(string provider)
    {
        await using var database = await Database(provider);
        var origin = DateTimeOffset.UtcNow.UtcTicks; var clock = new ControlledProviderClock(origin);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var ids = OrderedScheduleIds("retained-occurrence", 2);
        var definition = new JobScheduleDefinition(ids[0], Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(origin) - TimeSpan.FromHours(1), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var original = Assert.Single((await store.MaterializeDueAsync(new(Scope, 10))).Occurrences);
        clock.Set(checked(origin + TimeSpan.FromHours(7).Ticks));
        Assert.Empty((await store.MaterializeDueAsync(new(Scope, 10))).Occurrences);
        long due; long? next; long? cutoff; long count; long? through; string receiptBytes; string acceptedBytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Schedules.SingleAsync(); due = row.NextOccurrence; next = row.CoalesceNext; cutoff = row.CoalesceCutoff; count = row.CoalesceCount; through = row.CoalesceThrough;
            Assert.NotNull(next); Assert.NotNull(cutoff); Assert.Equal(3, count);
            receiptBytes = (await db.Occurrences.SingleAsync()).ReceiptJson; acceptedBytes = (await db.Jobs.SingleAsync()).RequestJson!;
            row.DefinitionJson = "{"; await db.SaveChangesAsync();
        }
        await store.SaveScheduleAsync(new(Scope, definition with { ScheduleId = ids[1], FirstOccurrence = JobStoreCodec.At(clock.Now) }, 0, Op()));
        Assert.Equal(ids[1], Assert.Single((await store.MaterializeDueAsync(new(Scope, 10))).Occurrences).ScheduleId);
        Assert.NotNull(await store.FindAsync(Scope, original.JobId));
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(x => x.Key == JobStoreCodec.Key(Scope, "schedule", ids[0]));
        Assert.Equal("{", retained.DefinitionJson); Assert.NotNull(retained.UnavailableDiagnosticJson);
        Assert.Equal(due, retained.NextOccurrence); Assert.Equal(next, retained.CoalesceNext); Assert.Equal(cutoff, retained.CoalesceCutoff);
        Assert.Equal(count, retained.CoalesceCount); Assert.Equal(through, retained.CoalesceThrough);
        Assert.Contains(receiptBytes, await check.Occurrences.Select(x => x.ReceiptJson).ToArrayAsync());
        Assert.Equal(acceptedBytes, (await check.Jobs.SingleAsync(x => x.JobId == original.JobId)).RequestJson);
        Assert.Equal(2, await check.Jobs.CountAsync()); Assert.Equal(2, await check.Occurrences.CountAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite", JobRecurrenceKind.Interval)]
    [InlineData("PostgreSql", JobRecurrenceKind.Interval)]
    [InlineData("Sqlite", JobRecurrenceKind.Cron)]
    [InlineData("PostgreSql", JobRecurrenceKind.Cron)]
    public async Task Default_runtime_arithmetic_materializes_paused_catchup_and_bounded_replay_without_coalescing_accepted_jobs(string provider, JobRecurrenceKind kind)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var now = DateTimeOffset.UtcNow.UtcTicks; now -= now % TimeSpan.TicksPerSecond;
        var clock = new ControlledProviderClock(now);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var acceptedBefore = (await store.AdmitAsync(Admission("already-accepted"))).Acceptance!.JobId;
        var recurrence = new JobRecurrence(kind, kind == JobRecurrenceKind.Interval ? "PT1S" : "* * * * * *");
        var definition = new JobScheduleDefinition("default-catchup", Request(), recurrence, JobStoreCodec.At(now) - TimeSpan.FromSeconds(5), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var gate = new JobGateKey(JobGateKind.Queue, "shared");
        await store.PauseAsync(new(Scope, gate, "catchup-hold", Op()));
        Assert.Empty((await store.MaterializeDueAsync(new(Scope, 2))).Occurrences);
        Assert.Equal(definition.FirstOccurrence, (await store.FindScheduleAsync(Scope, definition.ScheduleId))!.NextOccurrence);
        var hold = Assert.Single((await store.ListHoldsAsync(Scope, gate, new(10, null))).Items);
        await store.ReleaseHoldAsync(new(Scope, gate, hold.HoldId, hold.Revision, Op()));
        JobOccurrenceReceipt? catchup = null;
        for (var i = 0; i < 8 && catchup is null; i++) catchup = (await store.MaterializeDueAsync(new(Scope, 2))).Occurrences.SingleOrDefault();
        Assert.NotNull(catchup); Assert.Equal(definition.FirstOccurrence, catchup.ScheduledAt);
        Assert.Equal(5, catchup.CoalescedCount); Assert.Equal(JobStoreCodec.At(now), catchup.CoalescedThrough);
        var complete = (await store.FindScheduleAsync(Scope, definition.ScheduleId))!;
        Assert.Equal(JobStoreCodec.At(now) + TimeSpan.FromSeconds(1), complete.NextOccurrence);
        await store.SetScheduleEnabledAsync(new(Scope, definition.ScheduleId, false, complete.Revision, Op()));
        var replay = definition with { ScheduleId = "default-replay", FirstOccurrence = JobStoreCodec.At(now) - TimeSpan.FromSeconds(3), MissedPolicy = JobMissedOccurrencePolicy.ReplayEveryOccurrence };
        await store.SaveScheduleAsync(new(Scope, replay, 0, Op()));
        var occurrences = new List<JobOccurrenceReceipt>();
        for (var i = 0; i < 8 && occurrences.Count < 4; i++)
        {
            // Reconstruct the authority between bounded pages; only retained state carries responsibility.
            store = new(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
            var page = await store.MaterializeDueAsync(new(Scope, 2)); Assert.InRange(page.Occurrences.Count, 0, 2);
            occurrences.AddRange(page.Occurrences);
        }
        Assert.Equal(4, occurrences.Count); Assert.Equal(4, occurrences.Select(x => x.JobId).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 4).Select(i => replay.FirstOccurrence + TimeSpan.FromSeconds(i)), occurrences.Select(x => x.ScheduledAt));
        Assert.NotNull(await store.FindAsync(Scope, acceptedBefore)); Assert.NotNull(await store.FindAsync(Scope, catchup.JobId));
        Assert.Empty((await store.MaterializeDueAsync(new(Scope, 2))).Occurrences);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(6, await check.Jobs.CountAsync()); Assert.Equal(5, await check.Occurrences.CountAsync()); Assert.Empty(await check.Permits.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Competing_materializers_using_default_arithmetic_accept_each_occurrence_once(string provider)
    {
        await using var database = await Database(provider, defaultArithmetic: true);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var factory = new InterceptedFactory(database.Contexts, clock);
        var first = JobStoreCodec.At(clock.Now) - TimeSpan.FromSeconds(3);
        var store = new EfJobStore(factory, new(100, 3, 16), database.Recurrence);
        await store.SaveScheduleAsync(new(Scope, new("competing", Request(), new(JobRecurrenceKind.Interval, "PT1S"), first, JobMissedOccurrencePolicy.ReplayEveryOccurrence), 0, Op()));
        for (var i = 0; i < 8; i++)
            await Task.WhenAll(Task.Run(async () => await new EfJobStore(factory, new(100, 3, 16), database.Recurrence).MaterializeDueAsync(new(Scope, 1))),
                Task.Run(async () => await new EfJobStore(factory, new(100, 3, 16), database.Recurrence).MaterializeDueAsync(new(Scope, 1))));
        await using var check = await database.Contexts.CreateDbContextAsync();
        var receipts = (await check.Occurrences.ToArrayAsync()).Select(row => JobStoreCodec.ReadRetained<JobOccurrenceReceipt>(row.ReceiptJson)).ToArray();
        Assert.Equal(4, receipts.Length); Assert.Equal(4, receipts.Select(x => x.ScheduledAt).Distinct().Count()); Assert.Equal(4, receipts.Select(x => x.JobId).Distinct().Count());
        Assert.Equal(4, await check.Jobs.CountAsync()); Assert.Equal(4, await check.Origins.CountAsync()); Assert.Empty(await check.Permits.ToArrayAsync());
        Assert.Equal(first + TimeSpan.FromSeconds(4), (await store.FindScheduleAsync(Scope, "competing"))!.NextOccurrence);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Additive_schedule_isolation_migration_preserves_legacy_identity_cursor_and_accepted_occurrences(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var definition = new JobScheduleDefinition("legacy-schedule", Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(clock.Now) - TimeSpan.FromHours(3), JobMissedOccurrencePolicy.ReplayEveryOccurrence);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var accepted = Assert.Single((await store.MaterializeDueAsync(new(Scope, 1))).Occurrences);
        string bytes; long cursor; string identity;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Schedules.SingleAsync(); bytes = row.DefinitionJson; cursor = row.NextOccurrence; identity = row.Identity;
            var baseline = db.Database.GetMigrations().Single(x => x.EndsWith("_FreezeCoalescingCutoff", StringComparison.Ordinal));
            var migrator = db.GetService<IMigrator>();
            // This is an owned test store: exercise the retained pre-isolation schema, then upgrade it.
            await migrator.MigrateAsync(baseline); await migrator.MigrateAsync();
        }
        var projection = (await store.FindScheduleAsync(Scope, definition.ScheduleId))!;
        Assert.Equal(definition.ScheduleId, projection.ScheduleId); Assert.NotNull(projection.Definition); Assert.Null(projection.UnavailableDiagnostic);
        Assert.Equal(JobStoreCodec.At(cursor), projection.NextOccurrence); Assert.NotNull(await store.FindAsync(Scope, accepted.JobId));
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(); Assert.Equal(bytes, retained.DefinitionJson); Assert.Equal(identity, retained.Identity); Assert.Null(retained.UnavailableDiagnosticJson);
        Assert.Single(await check.Occurrences.ToArrayAsync()); Assert.Single(await check.Jobs.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public async Task Pause_or_disable_committed_after_selection_prevents_later_materialization_permission(string provider, bool disable)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var authority = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var definition = new JobScheduleDefinition("selection-race", Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne);
        await authority.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var barrier = new BeforeOneSave();
        var selecting = new EfJobStore(new InterceptedFactory(new InterceptedFactory(database.Contexts, clock), barrier), new(100, 3, 16), database.Recurrence);
        var materializing = selecting.MaterializeDueAsync(new(Scope, 1)).AsTask();
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var control = disable ? await authority.SetScheduleEnabledAsync(new(Scope, definition.ScheduleId, false, 1, Op()))
                : await authority.PauseAsync(new(Scope, new(JobGateKind.Queue, "shared"), "race-hold", Op()));
            Assert.Equal(JobCommandStatus.Applied, control.Status);
        }
        finally { barrier.Release.TrySetResult(); }
        Assert.Empty((await materializing).Occurrences);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Empty(await check.Jobs.ToArrayAsync()); Assert.Empty(await check.Occurrences.ToArrayAsync());
        Assert.Equal(clock.Now, (await check.Schedules.SingleAsync()).NextOccurrence);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Editing_a_due_schedule_races_safely_and_keeps_the_accepted_template_and_revision(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16), database.Recurrence);
        var definition = new JobScheduleDefinition("edit-race", Request(), new(JobRecurrenceKind.Interval, "01:00:00"), JobStoreCodec.At(clock.Now), JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var changed = definition with { Template = definition.Template with { Input = Payload(Handler.InputSchema, new { Order = 999 }) } };
        var materializing = Task.Run(async () => await store.MaterializeDueAsync(new(Scope, 1)));
        var edit = await store.SaveScheduleAsync(new(Scope, changed, 1, Op()));
        Assert.Equal(JobCommandStatus.Conflict, edit.Status);
        var receipt = Assert.Single((await materializing).Occurrences); Assert.Equal(1, receipt.ScheduleRevision);
        Assert.Equal(JobPayloadDigest.Compute(definition.Template.Input), JobPayloadDigest.Compute((await store.FindAsync(Scope, receipt.JobId))!.Request!.Input));
        Assert.Equal(JobCommandStatus.Conflict, (await store.SetScheduleEnabledAsync(new(Scope, definition.ScheduleId, false, 1, Op()))).Status);
        var snapshot = (await store.FindScheduleAsync(Scope, definition.ScheduleId))!;
        Assert.Equal(JobCommandStatus.Applied, (await store.SetScheduleEnabledAsync(new(Scope, definition.ScheduleId, false, snapshot.Revision, Op()))).Status);
        Assert.NotNull(await store.FindAsync(Scope, receipt.JobId));
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Malformed_schedule_identity_frames_fail_safely_without_fabricating_an_identifier(string provider)
    {
        await using var database = await Database(provider);
        var definition = new JobScheduleDefinition("identity-control", Request(), new(JobRecurrenceKind.Interval, "01:00:00"), DateTimeOffset.UtcNow.AddDays(1), JobMissedOccurrencePolicy.CoalesceOne);
        await database.Commands.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        var variants = new[] { "{}", "[]", "[42,null,\"schedule\",[]]", JobStoreCodec.Address(Scope, "schedule", "different-id") };
        foreach (var identity in variants)
        {
            await using (var db = await database.Contexts.CreateDbContextAsync())
            {
                var row = await db.Schedules.SingleAsync(); row.Identity = identity; await db.SaveChangesAsync();
            }
            var error = await Assert.ThrowsAsync<JobException>(() => database.Queries.FindScheduleAsync(Scope, definition.ScheduleId).AsTask());
            Assert.Equal("jobs.persistence.invalid-record", error.Code); Assert.IsType<InvalidDataException>(error.InnerException);
            await using var check = await database.Contexts.CreateDbContextAsync(); Assert.Equal(identity, (await check.Schedules.SingleAsync()).Identity);
        }
    }

    private sealed class BeforeOneSave : SaveChangesInterceptor
    {
        private int _remaining = 1;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _remaining, 0) == 1) { Reached.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }

    private static string[] OrderedScheduleIds(string prefix, int count) => Enumerable.Range(0, count).Select(i => prefix + "-" + i)
        .OrderBy(id => JobStoreCodec.Key(Scope, "schedule", id), StringComparer.Ordinal).ToArray();

    private sealed class DelegateCalculator(Func<JobRecurrence, DateTimeOffset, DateTimeOffset?> next) : IJobRecurrenceCalculator
    {
        public DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after) => next(recurrence, after);
    }
}
