using System.Text.Json;
using System.Text;
using System.Data.Common;
using System.Data;
using System.Text.Json.Nodes;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Jobs.Persistence.ProviderTests;

[Collection("jobs-postgresql")]
public sealed partial class JobsProviderContracts(JobsPostgreSqlFixture postgres)
{
    private static readonly JobScope Scope = new("jobs", "tenant-a");
    private static readonly JobHandlerSupport Handler = new(new("reports", "1"), new("report-input", 1), new("report-result", 1), "reports-build-1");
    private static JobOperation Op(string reason = "provider contract") => new(Guid.NewGuid().ToString("N"), "operator", reason);
    private static JobPayload Payload(JobSchema schema, object value) => new(schema, JsonSerializer.SerializeToElement(value));
    private static JobRequest Request(JobAssociation? association = null, int attempts = 3) => new(Handler.Handler, Payload(Handler.InputSchema, new { Order = 42 }), Handler.ResultSchema,
        "shared", new(new(attempts, TimeSpan.Zero, TimeSpan.Zero, 1, 0, true), TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), null, association);
    private static JobAdmission Admission(string key, JobRequest? request = null, JobOrigin? origin = null, JobScope? scope = null)
    {
        request ??= Request(); return new(scope ?? Scope, Guid.NewGuid().ToString("N"), key, JobRequestDigest.Compute(request), request, origin);
    }
    private static JobClaimRequest Claim(TimeSpan? duration = null, string? owner = null, JobHandlerSupport? handler = null) =>
        new(Scope, owner ?? Guid.NewGuid().ToString("N"), ["shared"], [handler ?? Handler], duration ?? TimeSpan.FromMinutes(1), Guid.NewGuid().ToString("N"));
    private static JobPhysicalReceipt Receipt(JobAttemptLease lease, JobAttemptOutcome outcome = JobAttemptOutcome.Succeeded, bool retryable = false)
    {
        var payload = outcome == JobAttemptOutcome.Succeeded ? Payload(Handler.ResultSchema, new { Reference = "report-42" }) : null;
        return new(Guid.NewGuid().ToString("N"), lease.AttemptId, lease.Owner, lease.Fence, outcome, payload,
            JobPayloadDigest.Compute(payload), null, retryable, DateTimeOffset.UtcNow);
    }
    private async Task<JobsProviderDatabase> Database(string provider, bool defaultArithmetic = false)
    {
        if (provider == "PostgreSql" && postgres.ConnectionString is null)
        {
            if (Environment.GetEnvironmentVariable("ELSA_JOBS_REQUIRE_POSTGRESQL") == "1") throw new InvalidOperationException(postgres.Unavailable);
            Skip.If(true, postgres.Unavailable);
        }
        return await JobsProviderDatabase.Create(provider, postgres.ConnectionString, defaultArithmetic);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Admission_replay_conflict_scope_and_origin_cancellation(string provider)
    {
        await using var database = await Database(provider);
        var original = Admission("same-key"); var admitted = await database.Commands.AdmitAsync(original);
        Assert.Equal(JobCommandStatus.Applied, admitted.Status);
        var replay = await database.Commands.AdmitAsync(original with { ProposedJobId = "different-server-id" });
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(admitted.Acceptance!.JobId, replay.Acceptance!.JobId);
        var changedRequest = original.Request with { Queue = "other" };
        Assert.Equal(JobCommandStatus.Conflict, (await database.Commands.AdmitAsync(original with { Request = changedRequest, RequestSha256 = JobRequestDigest.Compute(changedRequest) })).Status);
        Assert.Null(await database.Queries.FindAsync(Scope with { TenantId = "TENANT-A" }, admitted.Acceptance.JobId));
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.AdmitAsync(Admission("same-key", scope: Scope with { TenantId = null }))).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.AdmitAsync(original with { RequestSha256 = new('0', 64) }).AsTask());
        var origin = new JobOrigin("native", "parent", "input", new('a', 64));
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CancelOriginAsync(new(Scope, origin, original.RequestSha256, Op()))).Status);
        Assert.Equal(JobCommandStatus.OriginCancelled, (await database.Commands.AdmitAsync(Admission("cancelled-origin", origin: origin))).Status);
        Assert.Equal(JobCommandStatus.Conflict, (await database.Commands.AdmitAsync(Admission("conflicting-origin", origin: origin with { PayloadSha256 = new('b', 64) }))).Status);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Result_receipt_replay_and_observation_do_not_invalidate_ownership(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("result"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.RecordPhysicalStartAsync(lease)).Status);
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.RecordProgressAsync(lease, new(.5, "half", null))).Status);
        var renewed = await database.Commands.RenewAsync(lease, TimeSpan.FromMinutes(1));
        Assert.Equal(lease.Revision + 1, renewed.Lease!.Revision);
        var receipt = Receipt(renewed.Lease);
        Assert.Equal(JobCommandStatus.LeaseLost, (await database.Commands.CompleteAsync(lease, receipt)).Status);
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(renewed.Lease, receipt)).Status);
        Assert.Equal(JobCommandStatus.Replayed, (await database.Commands.CompleteAsync(renewed.Lease, receipt)).Status);
        var result = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        Assert.Equal(JobOutcome.Succeeded, result.Outcome); Assert.Equal("report-42", result.Payload!.Value.GetProperty("Reference").GetString());
        var changed = receipt with { Outcome = JobAttemptOutcome.Failed };
        Assert.Equal(JobCommandStatus.Conflict, (await database.Commands.RecordPhysicalReceiptAsync(Scope, accepted.JobId, changed)).Status);
        Assert.Single((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Protected_origin_outlives_ordinary_key_and_retry_keeps_pause_association(string provider)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "orders", "v1", "instance", "activity", "artifact", new('a', 64), JobAssociationMode.Awaited, Payload(new("continue", 1), new { Wait = "bookmark" }));
        var original = Admission("original", Request(association), new("native", "instance", "activity", new('b', 64)));
        var accepted = (await database.Commands.AdmitAsync(original)).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        var snapshot = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        var operation = Op("retry with no old wait");
        var retry = await database.Commands.RetryAsNewRunAsync(new(Scope, accepted.JobId, snapshot.Revision, "retry-job", "retry-key", operation));
        var retryReplay = await database.Commands.RetryAsNewRunAsync(new(Scope, accepted.JobId, snapshot.Revision, "changed-proposal", "retry-key", operation));
        Assert.Equal(retry.Acceptance!.JobId, retryReplay.Acceptance!.JobId);
        var retried = (await database.Queries.FindAsync(Scope, retry.Acceptance.JobId))!;
        Assert.Equal(association.ExecutionId, retried.Request!.Association!.ExecutionId); Assert.Equal(JobAssociationMode.Independent, retried.Request.Association.Mode); Assert.Null(retried.Request.Association.Continuation);
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.WorkflowInstance, association.ExecutionId, association.ModelKind), "instance-hold", Op()));
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var key = await db.Recognition.SingleAsync(x => x.JobKey == JobStoreCodec.Key(Scope, "job", accepted.JobId)); key.RecognizeUntil = 0; await db.SaveChangesAsync();
        }
        var originReplay = await database.Commands.AdmitAsync(original with { IdempotencyKey = "late-origin-new-producer-key", ProposedJobId = "not-new" });
        Assert.Equal(accepted.JobId, originReplay.Acceptance!.JobId);
        Assert.Equal(snapshot.Result!.ResultId, (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!.ResultId);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Overlapping_holds_and_bounded_selection_preserve_same_queue_progress(string provider)
    {
        await using var database = await Database(provider);
        var typeGate = new JobGateKey(JobGateKind.JobType, Handler.Handler.JobType);
        await database.Commands.PauseAsync(new(Scope, typeGate, "first", Op())); await database.Commands.PauseAsync(new(Scope, typeGate, "second", Op()));
        for (var i = 0; i < 1001; i++) await database.Commands.AdmitAsync(Admission("held-" + i));
        var other = Handler with { Handler = new("other", "1"), ImplementationIdentity = "other-build" };
        var otherRequest = Request() with { Handler = other.Handler };
        var eligible = (await database.Commands.AdmitAsync(Admission("eligible", otherRequest))).Acceptance!;
        var work = await database.Commands.ClaimAsync(new(Scope, "worker", ["shared"], [Handler, other], TimeSpan.FromMinutes(1), Guid.NewGuid().ToString("N")));
        Assert.Equal(eligible.JobId, work.Lease!.JobId);
        var holds = (await database.Queries.ListHoldsAsync(Scope, typeGate, new(10, null))).Items;
        var first = holds.Single(x => x.HoldId == "first");
        await database.Commands.ReleaseHoldAsync(new(Scope, typeGate, first.HoldId, first.Revision, Op()));
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        var second = holds.Single(x => x.HoldId == "second"); await database.Commands.ReleaseHoldAsync(new(Scope, typeGate, second.HoldId, second.Revision, Op()));
        Assert.NotNull((await database.Commands.ClaimAsync(Claim())).Lease);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Competing_claims_obey_deployment_capacity_and_first_implementation_pin(string provider)
    {
        await using var database = await Database(provider);
        await database.Commands.AdmitAsync(Admission("first")); await database.Commands.AdmitAsync(Admission("second"));
        await database.Commands.SetCapacityAsync(new(Scope, new(JobGateKind.JobType, "reports"), 1, 0, Op()));
        var claims = await Task.WhenAll(Task.Run(async () => await database.Commands.ClaimAsync(Claim())), Task.Run(async () => await database.Commands.ClaimAsync(Claim())));
        var claimed = Assert.Single(claims, x => x.Lease is not null).Lease!;
        await database.Commands.CompleteAsync(claimed, Receipt(claimed, JobAttemptOutcome.Failed, retryable: true));
        var otherJob = (await database.Queries.ListJobsAsync(Scope, new(new(10, null), null, null, null))).Items.Single(x => x.JobId != claimed.JobId);
        await database.Commands.CancelAsync(new(Scope, otherJob.JobId, Op()));
        Assert.Null((await database.Commands.ClaimAsync(Claim(handler: Handler with { ImplementationIdentity = "different-build" }))).Lease);
        Assert.Equal(claimed.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Continuation_uses_workflow_holds_and_same_grant_replays_while_paused(string provider)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("bpmn", "definition", "1", "execution", "invocation", "model", new('a', 64), JobAssociationMode.Awaited, Payload(new("bpmn-input", 1), new { Token = "wait" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("awaited", Request(association)))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.WorkflowDefinition, "definition", "bpmn"), "definition-hold", Op()));
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        var result = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        var request = new JobContinuationRequest(Scope, accepted.JobId, result.ResultId, "continuation", "model-input");
        Assert.Equal(JobCommandStatus.Held, (await database.Commands.GrantContinuationAsync(request)).Status);
        var hold = Assert.Single((await database.Queries.ListHoldsAsync(Scope, null, new(10, null))).Items);
        await database.Commands.ReleaseHoldAsync(new(Scope, hold.Gate, hold.HoldId, hold.Revision, Op()));
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.Queue, "shared"), "queue-hold", Op()));
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.GrantContinuationAsync(request)).Status);
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.WorkflowDefinition, "definition", "bpmn"), "again", Op()));
        Assert.Equal(JobCommandStatus.Replayed, (await database.Commands.GrantContinuationAsync(request)).Status);
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.AcknowledgeDeliveryAsync(new(Scope, accepted.JobId, "continuation", new(JobDeliveryProofKind.Consumed, "exact-model-receipt", result.PayloadSha256)))).Status);
        Assert.Empty((await database.Queries.ListPendingDeliveriesAsync(Scope, new(10, null))).Items);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Recurrence_pause_coalescing_and_explicit_replay_preserve_all_accepted_ids(string provider)
    {
        await using var database = await Database(provider);
        await using var db = await database.Contexts.CreateDbContextAsync();
        var now = JobStoreCodec.At(await db.ReadUtcTicksAsync(CancellationToken.None));
        var definition = new JobScheduleDefinition("coalesce", Request(), new(JobRecurrenceKind.Interval, "01:00:00"), now - TimeSpan.FromHours(3), JobMissedOccurrencePolicy.CoalesceOne);
        await database.Commands.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.Queue, "shared"), "schedule-hold", Op()));
        Assert.Empty((await database.Commands.MaterializeDueAsync(new(Scope, 10))).Occurrences);
        var hold = Assert.Single((await database.Queries.ListHoldsAsync(Scope, null, new(10, null))).Items);
        await database.Commands.ReleaseHoldAsync(new(Scope, hold.Gate, hold.HoldId, hold.Revision, Op()));
        JobOccurrenceReceipt? receipt = null;
        for (var i = 0; i < 5 && receipt is null; i++) receipt = (await database.Commands.MaterializeDueAsync(new(Scope, 10))).Occurrences.SingleOrDefault();
        Assert.NotNull(receipt); Assert.Equal(3, receipt.CoalescedCount);
        Assert.Equal(definition.FirstOccurrence, receipt.ScheduledAt);
        await database.Commands.SaveScheduleAsync(new(Scope, definition with { ScheduleId = "replay", MissedPolicy = JobMissedOccurrencePolicy.ReplayEveryOccurrence }, 0, Op()));
        var replayed = new List<JobOccurrenceReceipt>();
        for (var i = 0; i < 8 && replayed.Count < 4; i++) replayed.AddRange((await database.Commands.MaterializeDueAsync(new(Scope, 2))).Occurrences);
        Assert.Equal(4, replayed.Count); Assert.Equal(4, replayed.Select(x => x.JobId).Distinct().Count());
        var jobs = (await database.Queries.ListJobsAsync(Scope, new(new(20, null), null, null, null))).Items;
        Assert.Equal(5, jobs.Count); Assert.Contains(jobs, x => x.JobId == receipt.JobId);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Same_model_local_workflow_ids_do_not_alias_between_native_and_bpmn(string provider)
    {
        await using var database = await Database(provider);
        var native = new JobAssociation("native", "same-definition", "1", "same-execution", "input", "material", new('a', 64), JobAssociationMode.Independent, null);
        var bpmn = native with { ModelKind = "bpmn" };
        var held = (await database.Commands.AdmitAsync(Admission("native", Request(native)))).Acceptance!;
        var eligible = (await database.Commands.AdmitAsync(Admission("bpmn", Request(bpmn)))).Acceptance!;
        var gate = new JobGateKey(JobGateKind.WorkflowDefinition, native.DefinitionId, native.ModelKind);
        await database.Commands.PauseAsync(new(Scope, gate, "native-only", Op()));
        Assert.Equal(eligible.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        Assert.Empty((await database.Queries.ListHoldsAsync(Scope, gate with { ModelKind = "bpmn" }, new(10, null))).Items);
        var hold = Assert.Single((await database.Queries.ListHoldsAsync(Scope, gate, new(10, null))).Items);
        await database.Commands.ReleaseHoldAsync(new(Scope, gate, hold.HoldId, hold.Revision, Op()));
        Assert.Equal(held.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Malformed_retained_candidate_is_observable_and_does_not_poison_discovery(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("malformed"))).Acceptance!;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.SingleAsync(x => x.JobId == accepted.JobId); row.RequestJson = "{broken-json"; await db.SaveChangesAsync();
        }
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        var unavailable = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(accepted.JobId, unavailable.JobId); Assert.Null(unavailable.Request); Assert.NotNull(unavailable.UnavailableDiagnostic);
        var listed = (await database.Queries.ListJobsAsync(Scope, new(new(10, null), null, null, null))).Items;
        Assert.NotNull(Assert.Single(listed).UnavailableDiagnostic);
        var healthy = (await database.Commands.AdmitAsync(Admission("healthy"))).Acceptance!;
        Assert.Equal(healthy.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var preserved = await check.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
        Assert.Equal("{broken-json", preserved.RequestJson); Assert.NotNull(preserved.UnavailableDiagnosticJson);
        Assert.Equal(0, preserved.AttemptCount); Assert.Null(preserved.ResultJson);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Lease_expiry_after_save_rolls_back_permission_and_permits(string provider)
    {
        await using var database = await Database(provider);
        await database.Commands.AdmitAsync(Admission("expiry-rollback"));
        var delayed = new DelayOneSaveFactory(database.Contexts);
        var store = new EfJobStore(delayed, new(100, 1000, 16));
        var result = await store.ClaimAsync(Claim(TimeSpan.FromSeconds(1)));
        Assert.True(delayed.Observed); Assert.Equal(JobCommandStatus.Unavailable, result.Status); Assert.Null(result.Lease);
        await using var db = await database.Contexts.CreateDbContextAsync();
        Assert.Empty(await db.Attempts.ToArrayAsync()); Assert.Empty(await db.Permits.ToArrayAsync());
        Assert.Equal((int)JobState.Accepted, (await db.Jobs.SingleAsync()).State);
        Assert.NotNull((await database.Commands.ClaimAsync(Claim())).Lease);
    }

    private sealed class DelayOneSaveFactory(IDbContextFactory<JobsDbContext> inner) : IDbContextFactory<JobsDbContext>
    {
        private int _remaining = 1;
        internal bool Observed { get; private set; }
        public JobsDbContext CreateDbContext() => Observe(inner.CreateDbContext());
        public async Task<JobsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Observe(await inner.CreateDbContextAsync(cancellationToken));
        private JobsDbContext Observe(JobsDbContext db)
        {
            db.SavedChanges += (_, _) =>
            {
                if (Interlocked.Exchange(ref _remaining, 0) == 0) return;
                Observed = true;
                Thread.Sleep(1150); // Intentionally cross the real provider-clock deadline after writes, before commit validation.
            };
            return db;
        }
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Claim_commit_acknowledgement_loss_resolves_the_same_grant_even_after_pause(string provider)
    {
        await using var database = await Database(provider);
        await database.Commands.AdmitAsync(Admission("claim-ack-a")); await database.Commands.AdmitAsync(Admission("claim-ack-b"));
        var acknowledgement = new LoseOneCommitReply();
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, acknowledgement), new(100, 1000, 16));
        var request = Claim();
        await Assert.ThrowsAsync<JobPersistenceException>(() => store.ClaimAsync(request).AsTask());
        Assert.True(acknowledgement.Fired);
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.Queue, "shared"), "post-commit-hold", Op()));
        var replay = await store.ClaimAsync(request);
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.NotNull(replay.Lease);
        await using var db = await database.Contexts.CreateDbContextAsync();
        Assert.Single(await db.Attempts.ToArrayAsync()); Assert.Single(await db.ClaimGrants.ToArrayAsync());
        Assert.Equal(JobCommandStatus.Conflict, (await store.ClaimAsync(request with { LeaseDuration = TimeSpan.FromMinutes(2) })).Status);
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        Assert.Equal(JobCommandStatus.Applied, (await store.RenewAsync(replay.Lease, TimeSpan.FromMinutes(1))).Status);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Renewal_commit_acknowledgement_loss_replays_latest_token_without_extending_again(string provider)
    {
        await using var database = await Database(provider);
        await database.Commands.AdmitAsync(Admission("renewal-ack"));
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        var acknowledgement = new LoseOneCommitReply();
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, acknowledgement), new(100, 1000, 16));
        await Assert.ThrowsAsync<JobPersistenceException>(() => store.RenewAsync(lease, TimeSpan.FromMinutes(2)).AsTask());
        Assert.True(acknowledgement.Fired);
        var replay = await store.RenewAsync(lease, TimeSpan.FromMinutes(2));
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(lease.Revision + 1, replay.Lease!.Revision);
        var secondReplay = await store.RenewAsync(lease, TimeSpan.FromMinutes(2));
        Assert.Equal(replay.Lease, secondReplay.Lease);
        Assert.Equal(JobCommandStatus.Applied, (await store.CompleteAsync(replay.Lease, Receipt(replay.Lease))).Status);
        Assert.Equal(JobCommandStatus.LeaseLost, (await store.RenewAsync(lease, TimeSpan.FromMinutes(2))).Status);
    }

    private sealed class LostCommitReplyException() : DbException("Synthetic lost Jobs commit acknowledgement.") { }

    [SkippableTheory]
    [InlineData("Sqlite", false)]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public async Task Invalid_retained_input_schema_is_quarantined_without_rolling_discovery_back(string provider, bool missing)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("invalid-input-schema"))).Acceptance!;
        string originalBytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
            var json = JsonNode.Parse(row.RequestJson!)!.AsObject();
            var input = json["Input"]!.AsObject();
            if (missing) input.Remove("Schema"); else input["Schema"] = null;
            row.RequestJson = originalBytes = json.ToJsonString();
            await db.SaveChangesAsync();
        }
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        var unavailable = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(accepted.JobId, unavailable.JobId); Assert.Null(unavailable.Request); Assert.NotNull(unavailable.UnavailableDiagnostic);
        var healthy = (await database.Commands.AdmitAsync(Admission("healthy-after-invalid-schema"))).Acceptance!;
        Assert.Equal(healthy.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var preserved = await check.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
        Assert.Equal(originalBytes, preserved.RequestJson); Assert.NotNull(preserved.UnavailableDiagnosticJson);
        Assert.Equal(0, preserved.AttemptCount); Assert.Null(preserved.ResultJson);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Coalescing_slices_finish_a_fixed_cutoff_even_when_provider_time_advances_faster_than_counting(string provider)
    {
        await using var database = await Database(provider);
        long origin;
        await using (var db = await database.Contexts.CreateDbContextAsync()) origin = await db.ReadUtcTicksAsync(CancellationToken.None);
        var providerClock = new ControlledProviderClock(origin);
        var store = new EfJobStore(new InterceptedFactory(database.Contexts, providerClock), new(100, 1, 16), new JobsProviderDatabase.IntervalCalculator());
        var first = JobStoreCodec.At(origin) - TimeSpan.FromMilliseconds(50);
        var definition = new JobScheduleDefinition("bounded-catch-up", Request(), new(JobRecurrenceKind.Interval, "00:00:00.010"), first, JobMissedOccurrencePolicy.CoalesceOne);
        await store.SaveScheduleAsync(new(Scope, definition, 0, Op()));
        JobOccurrenceReceipt? occurrence = null;
        for (var slice = 0; slice < 12 && occurrence is null; slice++)
        {
            // Move the authority's clock 100ms while each bounded slice can count only one 10ms slot.
            // A moving cutoff can never finish; the persisted initial cutoff must remain at origin.
            providerClock.Set(checked(origin + TimeSpan.FromMilliseconds(slice * 100).Ticks));
            occurrence = (await store.MaterializeDueAsync(new(Scope, 1))).Occurrences.SingleOrDefault();
        }
        Assert.True(providerClock.Reads >= 6, "The real provider clock query must be exercised by the control.");
        Assert.NotNull(occurrence);
        Assert.Equal(first, occurrence.ScheduledAt); Assert.Equal(5, occurrence.CoalescedCount);
        Assert.Equal(JobStoreCodec.At(origin), occurrence.CoalescedThrough);
        var schedule = (await database.Queries.FindScheduleAsync(Scope, definition.ScheduleId))!;
        Assert.Equal(JobStoreCodec.At(origin) + TimeSpan.FromMilliseconds(10), schedule.NextOccurrence);
        Assert.True(schedule.NextOccurrence.UtcTicks < providerClock.Now, "Newer elapsed slots remain due for the next coalescing cycle.");
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Schedules.SingleAsync(); Assert.Null(retained.CoalesceCutoff);
        Assert.Single(await check.Jobs.ToArrayAsync());
    }

    private sealed class ControlledProviderClock(long utcTicks) : DbCommandInterceptor
    {
        private long _now = utcTicks;
        private int _reads;
        internal long Now => Interlocked.Read(ref _now);
        internal int Reads => Volatile.Read(ref _reads);
        internal void Set(long now) => Interlocked.Exchange(ref _now, now);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("jobs.utc-clock", StringComparison.Ordinal)) return ValueTask.FromResult(result);
            Interlocked.Increment(ref _reads);
            var table = new DataTable(); table.Columns.Add("Value", typeof(long)); table.Rows.Add(Now);
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Zero_backoff_stays_finite_after_extreme_valid_multiplier_overflow(string provider)
    {
        await using var database = await Database(provider);
        var request = Request(attempts: 4);
        request = request with { Policy = request.Policy with { Retry = new(4, TimeSpan.Zero, TimeSpan.FromDays(1), double.MaxValue, 0, true) } };
        var accepted = (await database.Commands.AdmitAsync(Admission("zero-extreme-backoff", request))).Acceptance!;
        await using var clockContext = await database.Contexts.CreateDbContextAsync();
        for (var number = 1; number <= 3; number++)
        {
            var lease = (await database.Commands.ClaimAsync(Claim())).Lease!; Assert.Equal(number, lease.AttemptNumber);
            var before = JobStoreCodec.At(await clockContext.ReadUtcTicksAsync(CancellationToken.None));
            Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(lease, Receipt(lease, JobAttemptOutcome.Failed, retryable: true))).Status);
            var after = JobStoreCodec.At(await clockContext.ReadUtcTicksAsync(CancellationToken.None));
            var snapshot = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
            Assert.Equal(JobState.Accepted, snapshot.State); Assert.Null(snapshot.Result);
            Assert.InRange(snapshot.EligibleAt, before, after);
        }
        Assert.Equal(4, (await database.Commands.ClaimAsync(Claim())).Lease!.AttemptNumber);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Shared_payload_envelopes_accept_large_input_and_continuation_but_reject_one_byte_over(string provider)
    {
        await using var database = await Database(provider);
        var input = BoundedPayload(Handler.InputSchema, 300 * 1024);
        var request = Request() with { Input = input };
        var accepted = (await database.Commands.AdmitAsync(Admission("large-input", request))).Acceptance!;
        Assert.Equal(JobPayloadDigest.Compute(input), JobPayloadDigest.Compute((await database.Queries.FindAsync(Scope, accepted.JobId))!.Request!.Input));
        var oversized = request with { Input = BoundedPayload(Handler.InputSchema, JobPayloadLimits.MaximumBytes + 1) };
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.AdmitAsync(Admission("oversized-input", oversized)).AsTask());

        var continuation = BoundedPayload(new("continue", 1), JobPayloadLimits.MaximumBytes);
        var association = new JobAssociation("native", "definition", "1", "execution", "invocation", "model", new('a', 64), JobAssociationMode.Awaited, continuation);
        var associated = request with { Association = association };
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.AdmitAsync(Admission("large-continuation", associated))).Status);
        associated = associated with { Association = association with { Continuation = BoundedPayload(continuation.Schema, JobPayloadLimits.MaximumBytes + 1) } };
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.AdmitAsync(Admission("oversized-continuation", associated)).AsTask());
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(2, await check.Jobs.CountAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Shared_result_envelope_boundary_accepts_completion_and_refuses_oversize_without_settlement(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("large-result"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        var oversized = BoundedPayload(Handler.ResultSchema, JobPayloadLimits.MaximumBytes + 1);
        var receipt = Receipt(lease) with { Payload = oversized, PayloadSha256 = JobPayloadDigest.Compute(oversized) };
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.CompleteAsync(lease, receipt).AsTask());
        var active = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobState.AttemptActive, active.State); Assert.Null(active.Result);
        Assert.Empty((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);

        var maximum = BoundedPayload(Handler.ResultSchema, JobPayloadLimits.MaximumBytes);
        receipt = receipt with { Payload = maximum, PayloadSha256 = JobPayloadDigest.Compute(maximum) };
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(lease, receipt)).Status);
        var result = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        Assert.Equal(JobOutcome.Succeeded, result.Outcome); Assert.Equal(JobPayloadLimits.MaximumBytes, JobPayloadLimits.GetByteCount(result.Payload!));
        Assert.Equal(receipt.PayloadSha256, result.PayloadSha256);
        Assert.Single((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Progress_uses_shared_entire_utf8_envelope_bound_without_changing_ownership(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("bounded-progress"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        var empty = new JobProgress(.5, "half", Payload(new("progress", 1), ""));
        var overhead = JsonSerializer.SerializeToUtf8Bytes(empty).Length;
        var maximum = empty with { Data = Payload(empty.Data!.Schema, new string('x', JobPayloadLimits.MaximumProgressBytes - overhead)) };
        Assert.Equal(JobPayloadLimits.MaximumProgressBytes, JsonSerializer.SerializeToUtf8Bytes(maximum).Length);
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.RecordProgressAsync(lease, maximum)).Status);
        var oversized = maximum with { Data = Payload(maximum.Data!.Schema, new string('x', JobPayloadLimits.MaximumProgressBytes - overhead + 1)) };
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.RecordProgressAsync(lease, oversized).AsTask());
        var retained = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Progress!;
        Assert.Equal(JobPayloadLimits.MaximumProgressBytes, JsonSerializer.SerializeToUtf8Bytes(retained).Length);
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(lease, Receipt(lease))).Status);
    }

    private static JobPayload BoundedPayload(JobSchema schema, int bytes)
    {
        var overhead = JobPayloadLimits.GetByteCount(Payload(schema, ""));
        var payload = Payload(schema, new string('x', bytes - overhead));
        Assert.Equal(bytes, JobPayloadLimits.GetByteCount(payload));
        return payload;
    }

    [SkippableTheory]
    [InlineData("Sqlite", "null")]
    [InlineData("PostgreSql", "null")]
    [InlineData("Sqlite", "nonnumeric")]
    [InlineData("PostgreSql", "nonnumeric")]
    [InlineData("Sqlite", "overflow")]
    [InlineData("PostgreSql", "overflow")]
    public async Task Forged_null_or_invalid_attempt_cursors_are_safe_caller_errors(string provider, string kind)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("healthy-cursor-record"))).Acceptance!;
        var json = kind == "null" ? "null" : JsonSerializer.Serialize(new
        {
            Scope = JobStoreCodec.Scope(Scope), Category = "attempts", Filter = accepted.JobId,
            After = kind == "overflow" ? "2147483648" : "not-a-number"
        });
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var failure = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            if (kind == "null") await database.Queries.ListJobsAsync(Scope, new(new(1, token), null, null, null));
            else await database.Queries.ListAttemptsAsync(Scope, accepted.JobId, new(1, token));
        });
        Assert.NotNull(failure.InnerException);
        var healthy = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.NotNull(healthy.Request); Assert.Null(healthy.UnavailableDiagnostic); Assert.Equal(JobState.Accepted, healthy.State);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task List_cursors_bind_exact_scope_category_and_filters(string provider)
    {
        await using var database = await Database(provider);
        await database.Commands.AdmitAsync(Admission("cursor-first")); await database.Commands.AdmitAsync(Admission("cursor-second"));
        var first = await database.Queries.ListJobsAsync(Scope, new(new(1, null), "shared", null, null));
        Assert.NotNull(first.ContinuationToken);
        var page = new JobPageRequest(1, first.ContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListJobsAsync(Scope with { TenantId = "other" }, new(page, "shared", null, null)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListJobsAsync(Scope, new(page, "other", null, null)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListSchedulesAsync(Scope, page).AsTask());
        var second = await database.Queries.ListJobsAsync(Scope, new(page, "shared", null, null));
        Assert.NotEqual(Assert.Single(first.Items).JobId, Assert.Single(second.Items).JobId);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "admission")]
    [InlineData("PostgreSql", "admission")]
    [InlineData("Sqlite", "result")]
    [InlineData("PostgreSql", "result")]
    [InlineData("Sqlite", "progress")]
    [InlineData("PostgreSql", "progress")]
    public async Task Caller_json_beyond_supported_depth_has_a_safe_boundary_error(string provider, string kind)
    {
        await using var database = await Database(provider);
        if (kind == "admission")
        {
            // The immutable request fits the digest serializer but exceeds the shared payload depth.
            var request = Request() with { Input = DeepPayload(Handler.InputSchema, 62) };
            var admission = Admission("deep-admission", request);
            var error = await Assert.ThrowsAsync<JobException>(() => database.Commands.AdmitAsync(admission).AsTask());
            Assert.Equal("jobs.payload.invalid", error.Code); Assert.IsType<JsonException>(error.InnerException);
            Assert.Empty((await database.Queries.ListJobsAsync(Scope, new(new(10, null), null, null, null))).Items);
            return;
        }
        var accepted = (await database.Commands.AdmitAsync(Admission("deep-observation"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        if (kind == "result")
        {
            var payload = DeepPayload(Handler.ResultSchema, 63);
            var receipt = Receipt(lease) with { Payload = payload, PayloadSha256 = JobPayloadDigest.Compute(payload) };
            var error = await Assert.ThrowsAsync<JobException>(() => database.Commands.CompleteAsync(lease, receipt).AsTask());
            Assert.Equal("jobs.payload.invalid", error.Code); Assert.IsType<JsonException>(error.InnerException);
            Assert.Empty((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);
        }
        else
        {
            var progress = new JobProgress(.5, "deep", DeepPayload(new("progress", 1), 63));
            var error = await Assert.ThrowsAsync<JobException>(() => database.Commands.RecordProgressAsync(lease, progress).AsTask());
            Assert.Equal("jobs.payload.invalid", error.Code); Assert.IsType<JsonException>(error.InnerException);
        }
        var active = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobState.AttemptActive, active.State); Assert.Null(active.Result); Assert.Null(active.Progress);
    }

    private static JobPayload DeepPayload(JobSchema schema, int depth)
    {
        using var json = JsonDocument.Parse(new string('[', depth) + "0" + new string(']', depth), new JsonDocumentOptions { MaxDepth = 128 });
        return new(schema, json.RootElement);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Retained_payload_depth_failure_is_quarantined_without_poisoning_discovery(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("retained-deep-payload"))).Acceptance!;
        string originalBytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
            var json = JsonNode.Parse(row.RequestJson!)!.AsObject();
            json["Input"]!["Value"] = JsonNode.Parse(new string('[', 33) + "0" + new string(']', 33));
            row.RequestJson = originalBytes = json.ToJsonString();
            await db.SaveChangesAsync();
        }
        Assert.Null((await database.Commands.ClaimAsync(Claim())).Lease);
        var unavailable = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Null(unavailable.Request); Assert.NotNull(unavailable.UnavailableDiagnostic);
        var healthy = (await database.Commands.AdmitAsync(Admission("healthy-after-retained-depth"))).Acceptance!;
        Assert.Equal(healthy.JobId, (await database.Commands.ClaimAsync(Claim())).Lease!.JobId);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var preserved = await check.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
        Assert.Equal(originalBytes, preserved.RequestJson); Assert.NotNull(preserved.UnavailableDiagnosticJson);
        Assert.Equal(0, preserved.AttemptCount); Assert.Null(preserved.ResultJson);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Supported_payload_depth_survives_real_admission_progress_and_result_envelopes(string provider)
    {
        await using var database = await Database(provider);
        var depth = JobPayloadLimits.MaximumDepth - 1;
        var request = Request() with { Input = DeepPayload(Handler.InputSchema, depth) };
        var accepted = (await database.Commands.AdmitAsync(Admission("supported-depth", request))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.RecordProgressAsync(lease, new(.5, "depth", DeepPayload(new("progress", 1), depth)))).Status);
        Assert.NotNull((await database.Queries.FindAsync(Scope, accepted.JobId))!.Progress);
        var payload = DeepPayload(Handler.ResultSchema, depth);
        var receipt = Receipt(lease) with { Payload = payload, PayloadSha256 = JobPayloadDigest.Compute(payload) };
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(lease, receipt)).Status);
        var snapshot = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.NotNull(snapshot.Request); Assert.Null(snapshot.UnavailableDiagnostic); Assert.Equal(JobOutcome.Succeeded, snapshot.Result!.Outcome);
        Assert.Equal(receipt.PayloadSha256, snapshot.Result.PayloadSha256);
        Assert.Single((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "receipt")]
    [InlineData("PostgreSql", "receipt")]
    [InlineData("Sqlite", "terminal-result")]
    [InlineData("PostgreSql", "terminal-result")]
    public async Task Null_schema_in_retained_receipt_or_terminal_result_has_a_safe_error_and_preserves_bytes(string provider, string kind)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("retained-null-schema"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        string originalBytes; long revision;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var job = await db.Jobs.SingleAsync(x => x.JobId == accepted.JobId); revision = job.Revision;
            if (kind == "receipt")
            {
                var row = await db.Receipts.SingleAsync(x => x.JobKey == job.Key);
                var json = JsonNode.Parse(row.ReceiptJson!)!.AsObject(); json["Payload"]!["Schema"] = null;
                row.ReceiptJson = originalBytes = json.ToJsonString(); row.ReceiptSha256 = JobStoreCodec.Hash(originalBytes);
            }
            else
            {
                var json = JsonNode.Parse(job.ResultJson!)!.AsObject(); json["Payload"]!["Schema"] = null;
                job.ResultJson = originalBytes = json.ToJsonString();
            }
            await db.SaveChangesAsync();
        }
        JobException error;
        if (kind == "receipt")
        {
            error = await Assert.ThrowsAsync<JobException>(() => database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null)).AsTask());
            var healthy = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
            Assert.NotNull(healthy.Request); Assert.Null(healthy.UnavailableDiagnostic); Assert.Equal(JobOutcome.Succeeded, healthy.Result!.Outcome);
        }
        else
        {
            var unavailable = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
            Assert.Equal(JobState.Terminal, unavailable.State); Assert.Null(unavailable.Request); Assert.Null(unavailable.Result); Assert.NotNull(unavailable.UnavailableDiagnostic);
            error = await Assert.ThrowsAsync<JobException>(() => database.Commands.CancelAsync(new(Scope, accepted.JobId, Op())).AsTask());
        }
        Assert.Equal("jobs.persistence.invalid-record", error.Code);
        var invalid = Assert.IsType<InvalidDataException>(error.InnerException); Assert.IsType<ArgumentNullException>(invalid.InnerException);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var preserved = await check.Jobs.SingleAsync(x => x.JobId == accepted.JobId);
        Assert.Equal(revision, preserved.Revision); Assert.Equal((int)JobState.Terminal, preserved.State); Assert.False(preserved.CancellationRequested);
        if (kind == "receipt") Assert.Equal(originalBytes, (await check.Receipts.SingleAsync(x => x.JobKey == preserved.Key)).ReceiptJson);
        else Assert.Equal(originalBytes, preserved.ResultJson);
        Assert.Empty(await check.Operations.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite", "payload-digest")]
    [InlineData("PostgreSql", "payload-digest")]
    [InlineData("Sqlite", "diagnostic-summary")]
    [InlineData("PostgreSql", "diagnostic-summary")]
    public async Task Null_retained_receipt_scalars_are_infrastructure_errors_and_matching_caller_values_are_arguments(string provider, string kind)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("retained-null-scalar"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        var original = Receipt(lease);
        await database.Commands.CompleteAsync(lease, original);
        string originalBytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Receipts.SingleAsync();
            var json = JsonNode.Parse(row.ReceiptJson!)!.AsObject();
            if (kind == "payload-digest") json["PayloadSha256"] = null;
            else json["Diagnostic"] = new JsonObject { ["Code"] = "provider-control", ["Summary"] = null };
            row.ReceiptJson = originalBytes = json.ToJsonString(); row.ReceiptSha256 = JobStoreCodec.Hash(originalBytes);
            await db.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<JobException>(() => database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null)).AsTask());
        Assert.Equal("jobs.persistence.invalid-record", error.Code);
        var invalid = Assert.IsType<InvalidDataException>(error.InnerException); Assert.IsType<ArgumentNullException>(invalid.InnerException);
        var caller = kind == "payload-digest" ? original with { PayloadSha256 = null! }
            : original with { Diagnostic = new("provider-control", null!) };
        await Assert.ThrowsAsync<ArgumentNullException>(() => database.Commands.RecordPhysicalReceiptAsync(Scope, accepted.JobId, caller).AsTask());
        var healthy = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.NotNull(healthy.Request); Assert.Null(healthy.UnavailableDiagnostic); Assert.Equal(JobOutcome.Succeeded, healthy.Result!.Outcome);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var preserved = await check.Receipts.SingleAsync(); Assert.Equal(originalBytes, preserved.ReceiptJson); Assert.Equal(JobStoreCodec.Hash(originalBytes), preserved.ReceiptSha256);
        Assert.Empty(await check.Operations.ToArrayAsync());
    }

    private sealed class LoseOneCommitReply : DbTransactionInterceptor
    {
        private int _remaining = 1;
        internal bool Fired { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _remaining, 0) == 1) { Fired = true; throw new LostCommitReplyException(); }
            return Task.CompletedTask;
        }
    }
    private sealed class InterceptedFactory(IDbContextFactory<JobsDbContext> inner, IInterceptor interceptor) : IDbContextFactory<JobsDbContext>
    {
        public JobsDbContext CreateDbContext()
        {
            using var original = inner.CreateDbContext(); return Copy(original);
        }
        public async Task<JobsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            await using var original = await inner.CreateDbContextAsync(cancellationToken); return Copy(original);
        }
        private JobsDbContext Copy(JobsDbContext original)
        {
            var options = original.GetService<IDbContextOptions>();
            return original switch
            {
                JobsSqliteDbContext => new JobsSqliteDbContext(new DbContextOptionsBuilder<JobsSqliteDbContext>((DbContextOptions<JobsSqliteDbContext>)options).AddInterceptors(interceptor).Options),
                JobsPostgreSqlDbContext => new JobsPostgreSqlDbContext(new DbContextOptionsBuilder<JobsPostgreSqlDbContext>((DbContextOptions<JobsPostgreSqlDbContext>)options).AddInterceptors(interceptor).Options),
                _ => throw new NotSupportedException()
            };
        }
    }
}
