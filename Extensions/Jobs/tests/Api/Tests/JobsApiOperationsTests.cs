using System.Net;
using System.Text.Json;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Jobs.Api.Tests.JobsApiSecurityTests;

namespace Elsa.Jobs.Api.Tests;

public sealed class JobsApiOperationsTests
{
    [Fact]
    public async Task Pause_release_and_capacity_use_current_actor_durable_replay_and_exact_revisions()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn(); await host.SeedAsync();
        var gate = new JobGateKey(JobGateKind.JobType, JobsApiHost.Handler.Handler.JobType);
        var pause = new { gate, holdId = "maintenance", operationId = "pause-one", reason = "Deploy reports" };
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/holds", pause)).StatusCode);
        Assert.Equal("replayed", (await Json(await host.PostAsync("/jobs/holds", pause))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/jobs/holds", pause with { reason = "Changed immutable operation" })).StatusCode);
        host.SignIn("bob");
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/holds", new { gate, holdId = "other-operator", operationId = "pause-two", reason = "Data maintenance" })).StatusCode);
        var holds = await host.AuthorityAsync(async (_, queries) => (await queries.ListHoldsAsync(new(JobsApiHost.Partition, "tenant-a"), gate, new(25, null))).Items);
        Assert.Equal("alice", holds.Single(x => x.HoldId == "maintenance").Actor); Assert.Equal("bob", holds.Single(x => x.HoldId == "other-operator").Actor);
        var jobHolds = await Json(await host.Client.GetAsync("/jobs/job/holds?limit=1")); Assert.False(jobHolds.GetProperty("complete").GetBoolean());
        var matchingGate = jobHolds.GetProperty("gates").EnumerateArray().Single(x => x.GetProperty("gate").GetProperty("kind").GetString() == "jobType");
        var gateCursor = Uri.EscapeDataString(matchingGate.GetProperty("next").GetString()!);
        var continued = await host.Client.GetAsync("/jobs/holds?limit=1&kind=jobType&name=" + gate.Name + "&after=" + gateCursor);
        Assert.Equal(HttpStatusCode.OK, continued.StatusCode); Assert.Single((await Json(continued)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs/holds?limit=1&kind=jobType&name=other&after=" + gateCursor)).StatusCode);
        var release = new { gate, expectedRevision = 1, operationId = "release", reason = "Deployment finished" };
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/holds/maintenance/release", release)).StatusCode);
        var after = await host.AuthorityAsync(async (_, queries) => (await queries.ListHoldsAsync(new(JobsApiHost.Partition, "tenant-a"), gate, new(25, null))).Items);
        Assert.Equal("bob", after.Single(x => x.HoldId == "maintenance").ReleasedBy); Assert.Null(after.Single(x => x.HoldId == "other-operator").ReleasedAt);
        var capacity = new { gate, limit = 2, expectedRevision = 0, operationId = "capacity", reason = "Limit report workers" };
        Assert.Equal(HttpStatusCode.OK, (await host.PutAsync("/jobs/capacity", capacity)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.PutAsync("/jobs/capacity", capacity with { operationId = "stale-capacity", limit = 3 })).StatusCode);
        var policies = await Json(await host.Client.GetAsync("/jobs/capacity")); Assert.Equal(2, policies.GetProperty("items")[0].GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Manual_retry_is_a_linked_idempotent_new_run_and_preserves_the_prior_result_and_association()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        var association = new JobAssociation("native", "definition", "v1", "execution", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, new(new("native-address", 1), JsonSerializer.SerializeToElement(new { Bookmark = "old-wait" })));
        var accepted = await host.SeedAsync(association: association);
        await host.AuthorityAsync(async (commands, _) =>
            await commands.CancelAsync(new(new(JobsApiHost.Partition, "tenant-a"), accepted.JobId, new("finish-old", "model", "Owner cancelled"))));
        var old = (await host.ReadAsync())!;
        var retry = new { expectedRevision = old.Revision, idempotencyKey = "retry-key", operationId = "retry-as-new", reason = "Investigated and retrying" };
        var first = await host.PostAsync("/jobs/job/retry", retry); Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var freshId = (await Json(first)).GetProperty("jobId").GetString()!; Assert.NotEqual(old.JobId, freshId);
        var replay = await host.PostAsync("/jobs/job/retry", retry); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(freshId, (await Json(replay)).GetProperty("jobId").GetString());
        var fresh = (await host.ReadAsync(freshId))!;
        Assert.Equal(old.JobId, fresh.RetriedFromJobId); Assert.Equal(JobAssociationMode.Independent, fresh.Request!.Association!.Mode);
        Assert.Null(fresh.Request.Association.Continuation); Assert.Equal(association.ExecutionId, fresh.Request.Association.ExecutionId);
        Assert.Equal(old.Result!.ResultId, (await host.ReadAsync())!.Result!.ResultId);
        host.Authorization.Granted.Remove(JobsPermissions.Retry);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/jobs/job/retry", retry with { operationId = "denied-retry" })).StatusCode);
    }

    [Fact]
    public async Task Schedule_create_edit_and_disable_keep_input_private_and_preserve_accepted_history()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn(); var accepted = await host.SeedAsync();
        var input = JobsApiHost.Request(new { Protected = "SCHEDULE_INPUT_CANARY" });
        var save = new { invocation = Invocation(input), recurrence = new JobRecurrence(JobRecurrenceKind.Interval, "PT1H"),
            firstOccurrence = DateTimeOffset.UtcNow.AddHours(1), missedPolicy = JobMissedOccurrencePolicy.CoalesceOne,
            expectedRevision = 0, operationId = "create-hourly", reason = "Hourly report" };
        Assert.Equal(HttpStatusCode.OK, (await host.PutAsync("/jobs/schedules/hourly", save)).StatusCode);
        Assert.Equal("replayed", (await Json(await host.PutAsync("/jobs/schedules/hourly", save))).GetProperty("status").GetString());
        var page = await host.Client.GetAsync("/jobs/schedules"); Assert.DoesNotContain("SCHEDULE_INPUT_CANARY", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await host.PutAsync("/jobs/schedules/hourly", save with { expectedRevision = 1, operationId = "edit-hourly", reason = "Adjust retry policy" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync("/jobs/schedules/hourly/enabled", new { enabled = false, expectedRevision = 1, operationId = "stale-disable", reason = "Stop future work" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/schedules/hourly/enabled", new { enabled = false, expectedRevision = 2, operationId = "disable", reason = "Stop future work" })).StatusCode);
        var current = await Json(await host.Client.GetAsync("/jobs/schedules/hourly")); Assert.False(current.GetProperty("enabled").GetBoolean());
        Assert.Equal(accepted.JobId, (await host.ReadAsync())!.JobId);
        host.Authorization.Granted.Remove(JobsPermissions.InputsRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/jobs/schedules/hourly/input")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discarded_schedule_save_reply_replays_after_restart_without_available_recurrence_arithmetic(bool installUnavailableCalculator)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-api-replay-").FullName;
        var save = new { invocation = Invocation(JobsApiHost.Request()), recurrence = new JobRecurrence(JobRecurrenceKind.Interval, "PT1H"),
            firstOccurrence = DateTimeOffset.UtcNow.AddHours(1), missedPolicy = JobMissedOccurrencePolicy.CoalesceOne,
            expectedRevision = 0, operationId = "committed-save", reason = "Create report schedule" };
        try
        {
            await using (var original = await JobsApiHost.StartAsync(storageDirectory: directory))
            {
                original.SignIn();
                // Discard the successful response without reading its receipt, as after a lost client reply.
                using var discarded = await original.PutAsync("/jobs/schedules/hourly", save);
                Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
            }
            await using var restarted = await JobsApiHost.StartAsync(storageDirectory: directory, installJobsProducer: false,
                configure: services => { if (installUnavailableCalculator) services.AddSingleton<IJobRecurrenceCalculator>(new UnavailableRecurrenceCalculator()); });
            restarted.SignIn();
            var scope = new JobScope(JobsApiHost.Partition, "tenant-a");
            var before = (await restarted.AuthorityAsync(async (_, queries) => await queries.FindScheduleAsync(scope, "hourly")))!;
            Assert.NotNull(before.Definition); Assert.Equal("jobs.recurrence.unavailable", before.UnavailableDiagnostic?.Code);
            var replay = await restarted.PutAsync("/jobs/schedules/hourly", save);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("replayed", (await Json(replay)).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await restarted.PutAsync("/jobs/schedules/hourly", save with { reason = "Changed operation content" })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await restarted.PutAsync("/jobs/schedules/hourly", save with { expectedRevision = 1, operationId = "new-edit" })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await restarted.PutAsync("/jobs/schedules/new", save with { operationId = "new-create" })).StatusCode);
            Assert.Null(await restarted.AuthorityAsync(async (_, queries) => await queries.FindScheduleAsync(scope, "new")));
            var after = (await restarted.AuthorityAsync(async (_, queries) => await queries.FindScheduleAsync(scope, "hourly")))!;
            Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.NextOccurrence, after.NextOccurrence);
            Assert.Equal(JsonSerializer.Serialize(before.Definition, JobsApiHost.Json), JsonSerializer.Serialize(after.Definition, JobsApiHost.Json));
            restarted.Authorization.Granted.Remove(JobsPermissions.SchedulesManage);
            Assert.Equal(HttpStatusCode.Forbidden, (await restarted.PutAsync("/jobs/schedules/hourly", save)).StatusCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Malformed_retained_schedule_definition_stays_unavailable_even_for_a_previously_committed_save()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-api-malformed-").FullName;
        try
        {
            await using var host = await JobsApiHost.StartAsync(storageDirectory: directory); host.SignIn();
            var save = new { invocation = Invocation(JobsApiHost.Request()), recurrence = new JobRecurrence(JobRecurrenceKind.Interval, "PT1H"),
                firstOccurrence = DateTimeOffset.UtcNow.AddHours(1), missedPolicy = JobMissedOccurrencePolicy.CoalesceOne,
                expectedRevision = 0, operationId = "save", reason = "Create report schedule" };
            Assert.Equal(HttpStatusCode.OK, (await host.PutAsync("/jobs/schedules/hourly", save)).StatusCode);
            // Public SQLite access corrupts the actual retained bytes; no provider internals or query substitute.
            await using var connection = new SqliteConnection("Data Source=" + Path.Combine(directory, "jobs.db") + ";Pooling=False");
            await connection.OpenAsync();
            await using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "UPDATE elsa_jobs_schedules SET DefinitionJson = $definition";
            corrupt.Parameters.AddWithValue("$definition", "{"); Assert.Equal(1, await corrupt.ExecuteNonQueryAsync());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.PutAsync("/jobs/schedules/hourly", save)).StatusCode);
            var retained = (await host.AuthorityAsync(async (_, queries) => await queries.FindScheduleAsync(new(JobsApiHost.Partition, "tenant-a"), "hourly")))!;
            Assert.Null(retained.Definition); Assert.Equal(1, retained.Revision);
            await using var read = connection.CreateCommand(); read.CommandText = "SELECT DefinitionJson FROM elsa_jobs_schedules";
            Assert.Equal("{", await read.ExecuteScalarAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Acknowledged_parent_terminal_delivery_is_settled_without_claiming_model_consumption_or_exposing_proof()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        var association = new JobAssociation("native", "definition", "v1", "execution", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, new(new("native-address", 1), JsonSerializer.SerializeToElement(new { Bookmark = "PRIVATE_BOOKMARK" })));
        await host.SeedAsync(association: association);
        Assert.Equal("pending-result", (await Json(await host.Client.GetAsync("/jobs/job"))).GetProperty("consumptionState").GetString());
        await host.AuthorityAsync(async (commands, _) => await commands.CancelAsync(new(new(JobsApiHost.Partition, "tenant-a"), "job", new("cancel", "model", "Parent cancelled"))));
        var before = await host.Client.GetAsync("/jobs/job"); Assert.Equal("owed", (await Json(before)).GetProperty("consumptionState").GetString());
        var result = (await host.ReadAsync())!.Result!;
        await host.AuthorityAsync(async (commands, _) => await commands.AcknowledgeDeliveryAsync(new(new(JobsApiHost.Partition, "tenant-a"), "job", "PRIVATE_CONTINUATION",
            new(JobDeliveryProofKind.ParentProvenTerminal, "PRIVATE_TERMINAL_PROOF", result.PayloadSha256))));
        var settled = await host.Client.GetAsync("/jobs/job"); var text = await settled.Content.ReadAsStringAsync();
        Assert.Equal("settled", (await Json(settled)).GetProperty("consumptionState").GetString());
        Assert.DoesNotContain("PRIVATE_", text); Assert.DoesNotContain("consumed", text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_pages_are_refused_instead_of_widening_the_query(int limit)
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs?limit=" + limit)).StatusCode);
    }

    private sealed class UnavailableRecurrenceCalculator : IJobRecurrenceCalculator
    {
        public DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after) => throw new NotSupportedException("RECURRENCE_UNAVAILABLE_CANARY");
    }
}
