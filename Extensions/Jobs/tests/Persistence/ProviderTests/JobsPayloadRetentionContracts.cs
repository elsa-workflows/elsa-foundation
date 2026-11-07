using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    private static readonly long PayloadRetentionEpoch = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
    private static readonly TimeSpan PayloadLifetime = TimeSpan.FromSeconds(10);
    private const string RetainedInputCanary = "RETENTION_INPUT_BYTES_CANARY";
    private const string RetainedResultCanary = "RETENTION_RESULT_BYTES_CANARY";
    private const string RetainedProgressCanary = "RETENTION_PROGRESS_BYTES_CANARY";

    [SkippableTheory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", true)]
    public async Task Default_and_legacy_payload_promises_remain_indefinite_after_restart_with_shorter_host_settings(string provider, bool legacy)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var original = PayloadStore(database, clock);
        var completed = await CompleteForPayloadRetention(original, "indefinite-" + legacy);
        if (legacy)
        {
            await using var db = await database.Contexts.CreateDbContextAsync();
            var row = await db.Jobs.SingleAsync();
            row.RequestMetadataJson = null; row.RequestMetadataSha256 = null;
            row.ResultMetadataJson = null; row.ResultMetadataSha256 = null;
            row.RetentionPolicySha256 = null;
            row.TerminalInputRetentionTicks = null; row.TerminalResultRetentionTicks = null;
            row.InputExpiresAt = null; row.ResultExpiresAt = null;
            foreach (var receipt in await db.Receipts.ToArrayAsync())
            { receipt.MetadataJson = null; receipt.MetadataSha256 = null; }
            await db.SaveChangesAsync();
        }
        var restarted = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        clock.Set(checked(PayloadRetentionEpoch + TimeSpan.FromDays(3651).Ticks));
        var cleanup = await restarted.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(0, cleanup.ExpiredInputs); Assert.Equal(0, cleanup.ExpiredResults);
        var retained = (await restarted.FindAsync(Scope, completed.Acceptance.JobId))!;
        Assert.Equal(JobPayloadState.Available, retained.InputPayloadState);
        Assert.Equal(JobPayloadState.Available, retained.ResultPayloadState);
        Assert.Null(retained.PayloadRetention!.TerminalInputRetention);
        Assert.Null(retained.PayloadRetention.TerminalResultRetention);
        Assert.Null(retained.PayloadRetention.InputExpiresAt); Assert.Null(retained.PayloadRetention.ResultExpiresAt);
        Assert.Equal(completed.Admission.RequestSha256, retained.RequestSha256);
        Assert.Equal(completed.Snapshot.Revision, retained.Revision);
        Assert.Null(retained.UnavailableDiagnostic);
        await AssertRetentionBytesPresent(database, completed.Acceptance.JobId);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Accepted_retention_is_captured_before_terminal_time_and_exact_boundaries_use_provider_time(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var original = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime + PayloadLifetime);
        var admission = RetentionAdmission("captured-before-terminal");
        var acceptance = (await original.AdmitAsync(admission)).Acceptance!;
        clock.Set(checked(clock.Now + TimeSpan.FromHours(1).Ticks));
        Assert.Equal(0, (await original.ExpirePayloadsAsync(new(Scope))).ExaminedJobs);
        var pending = (await original.FindAsync(Scope, acceptance.JobId))!;
        Assert.Equal(JobState.Accepted, pending.State); Assert.Null(pending.PayloadRetention!.InputExpiresAt);
        Assert.Null(pending.PayloadRetention.ResultExpiresAt); Assert.NotNull(pending.Request);

        // A new authority over the same real store has different settings before completion.
        var restarted = PayloadStore(database, clock, TimeSpan.FromTicks(1), TimeSpan.FromTicks(1));
        var lease = (await restarted.ClaimAsync(Claim())).Lease!;
        Assert.Equal(acceptance.JobId, lease.JobId);
        await restarted.CompleteAsync(lease, RetentionReceipt(lease));
        var terminal = (await restarted.FindAsync(Scope, acceptance.JobId))!;
        Assert.Equal(PayloadLifetime, terminal.PayloadRetention!.TerminalInputRetention);
        Assert.Equal(PayloadLifetime + PayloadLifetime, terminal.PayloadRetention.TerminalResultRetention);
        var inputDue = terminal.Result!.CompletedAt + PayloadLifetime;
        var resultDue = inputDue + PayloadLifetime;
        Assert.Equal(inputDue, terminal.PayloadRetention.InputExpiresAt);
        Assert.Equal(resultDue, terminal.PayloadRetention.ResultExpiresAt);

        clock.Set(inputDue.UtcTicks - 1);
        var before = await restarted.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(0, before.ExpiredInputs); Assert.Equal(0, before.ExpiredResults);
        clock.Set(inputDue.UtcTicks);
        var first = await restarted.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(1, first.ExpiredInputs); Assert.Equal(0, first.ExpiredResults);
        var inputExpired = (await restarted.FindAsync(Scope, acceptance.JobId))!;
        Assert.Null(inputExpired.Request); Assert.NotNull(inputExpired.Result);
        Assert.Equal(JobPayloadState.Expired, inputExpired.InputPayloadState);
        Assert.Equal(JobPayloadState.Available, inputExpired.ResultPayloadState);
        Assert.Equal(inputDue, inputExpired.PayloadRetention!.InputExpiredAt);
        clock.Set(resultDue.UtcTicks - 1);
        Assert.Equal(0, (await restarted.ExpirePayloadsAsync(new(Scope))).ExpiredResults);
        clock.Set(resultDue.UtcTicks);
        Assert.Equal(1, (await restarted.ExpirePayloadsAsync(new(Scope))).ExpiredResults);
        var expired = (await restarted.FindAsync(Scope, acceptance.JobId))!;
        AssertExpiredMetadata(terminal, expired);
        var again = await restarted.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(0, again.ExpiredInputs); Assert.Equal(0, again.ExpiredResults);
        Assert.True(clock.Reads > 0);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Pending_and_physically_active_jobs_do_not_expire_their_accepted_payloads(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        var accepted = (await store.AdmitAsync(RetentionAdmission("active-payload"))).Acceptance!;
        var lease = (await store.ClaimAsync(Claim())).Lease!;
        await store.RecordPhysicalStartAsync(lease);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks * 2));
        var cleanup = await store.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(0, cleanup.ExaminedJobs); Assert.Equal(0, cleanup.ExpiredInputs); Assert.Equal(0, cleanup.ExpiredResults);
        var active = (await store.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobState.AttemptActive, active.State); Assert.NotNull(active.ActiveAttempt);
        Assert.NotNull(active.Request); Assert.Equal(JobPayloadState.NotProduced, active.ResultPayloadState);
        Assert.Null(active.PayloadRetention!.InputExpiresAt); Assert.Null(active.PayloadRetention.ResultExpiresAt);
        await store.CompleteAsync(lease, RetentionReceipt(lease));
        var terminal = (await store.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobStoreCodec.At(clock.Now) + PayloadLifetime, terminal.PayloadRetention!.InputExpiresAt);
    }

    [SkippableTheory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", true)]
    public async Task Owed_model_delivery_blocks_expiry_and_exact_consumption_or_parent_terminal_proof_survives_cleanup(string provider, bool parentTerminal)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        var origin = new JobOrigin("native", "retention-parent", "retention-invocation", new('a', 64));
        var completed = await CompleteForPayloadRetention(store, "owed-retention", RetentionAssociation(), origin);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks * 2));
        var owed = await store.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(1, owed.ExaminedJobs); Assert.Equal(0, owed.ExpiredInputs); Assert.Equal(0, owed.ExpiredResults);
        await AssertRetentionBytesPresent(database, completed.Acceptance.JobId);
        JobContinuationPermission? permission = null;
        if (!parentTerminal)
        {
            permission = (await store.GrantContinuationAsync(new(Scope, completed.Acceptance.JobId,
                completed.Snapshot.Result!.ResultId, "retained-continuation", "retained-model-input"))).Permission!;
            Assert.NotNull(permission);
            var grantedButOwed = await store.ExpirePayloadsAsync(new(Scope));
            Assert.Equal(0, grantedButOwed.ExpiredInputs); Assert.Equal(0, grantedButOwed.ExpiredResults);
        }
        var proof = new JobDeliveryProof(parentTerminal ? JobDeliveryProofKind.ParentProvenTerminal : JobDeliveryProofKind.Consumed,
            "actual-owner-terminal-receipt", completed.Snapshot.Result!.PayloadSha256);
        var acknowledgement = new JobAcknowledgeDeliveryRequest(Scope, completed.Acceptance.JobId, "retained-continuation", proof);
        Assert.Equal(JobCommandStatus.Applied, (await store.AcknowledgeDeliveryAsync(acknowledgement)).Status);
        var restarted = PayloadStore(database, clock);
        var expired = await restarted.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(1, expired.ExpiredInputs); Assert.Equal(1, expired.ExpiredResults);
        AssertExpiredMetadata(completed.Snapshot, (await restarted.FindAsync(Scope, completed.Acceptance.JobId))!);
        var delivery = (await restarted.FindDeliveryAsync(Scope, completed.Acceptance.JobId))!;
        Assert.True(delivery.IsAcknowledged); Assert.Equal(proof, delivery.Acknowledgement!.Proof);
        Assert.Equal("retained-continuation", delivery.Acknowledgement.ContinuationId);
        Assert.Equal(JobStoreCodec.Json(permission), JobStoreCodec.Json(delivery.Permission));
        Assert.Equal(JobStoreCodec.Json(permission), JobStoreCodec.Json(await restarted.FindContinuationAsync(Scope,
            completed.Acceptance.JobId, "retained-continuation")));
        Assert.Equal(JobCommandStatus.Replayed, (await restarted.AcknowledgeDeliveryAsync(acknowledgement)).Status);
        Assert.Equal(completed.Acceptance.JobId, (await restarted.FindOriginAsync(Scope, origin))!.Acceptance!.JobId);
        Assert.Empty((await restarted.ListPendingDeliveriesAsync(Scope, new(10, null))).Items);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Expired_payload_preserves_ordinary_window_origin_replay_and_old_job_identity_then_allows_only_disclosed_key_reuse(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        var origin = new JobOrigin("producer-outbox", "producer", "retained-origin", new('b', 64));
        var completed = await CompleteForPayloadRetention(store, "retained-ordinary-key", origin: origin);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        await store.ExpirePayloadsAsync(new(Scope));
        var sameKey = await store.AdmitAsync(completed.Admission with { Origin = null, ProposedJobId = "must-not-recreate" });
        Assert.Equal(JobCommandStatus.Replayed, sameKey.Status); Assert.Equal(completed.Acceptance.JobId, sameKey.Acceptance!.JobId);
        var changed = completed.Admission.Request with { Queue = "different-content" };
        Assert.Equal(JobCommandStatus.Conflict, (await store.AdmitAsync(completed.Admission with
        { Origin = null, Request = changed, RequestSha256 = JobRequestDigest.Compute(changed) })).Status);
        var originalOrigin = await store.AdmitAsync(completed.Admission with { IdempotencyKey = "origin-after-expiry", ProposedJobId = "must-not-recreate-origin" });
        Assert.Equal(JobCommandStatus.Replayed, originalOrigin.Status); Assert.Equal(completed.Acceptance.JobId, originalOrigin.Acceptance!.JobId);
        var wrongOrigin = await Assert.ThrowsAsync<JobException>(() => store.FindOriginAsync(Scope,
            origin with { PayloadSha256 = new('c', 64) }).AsTask());
        Assert.Equal("jobs.origin.conflict", wrongOrigin.Code);
        var refused = await store.RetryAsNewRunAsync(new(Scope, completed.Acceptance.JobId, completed.Snapshot.Revision,
            "no-input-retry", "no-input-retry-key", new("no-input-retry-operation", "operator", "Retry retained report")));
        Assert.Equal(JobCommandStatus.PayloadExpired, refused.Status); Assert.Null(refused.Acceptance);

        clock.Set(checked(completed.Acceptance.AcceptedAt.UtcTicks + completed.Admission.Request.Policy.MinimumKeyRecognition.Ticks + 1));
        var fresh = await store.AdmitAsync(completed.Admission with { Origin = null, ProposedJobId = "fresh-after-key-window" });
        Assert.Equal(JobCommandStatus.Applied, fresh.Status); Assert.NotEqual(completed.Acceptance.JobId, fresh.Acceptance!.JobId);
        var protectedReplay = await store.AdmitAsync(completed.Admission with { IdempotencyKey = "late-original-origin", ProposedJobId = "still-not-original-id" });
        Assert.Equal(JobCommandStatus.Replayed, protectedReplay.Status); Assert.Equal(completed.Acceptance.JobId, protectedReplay.Acceptance!.JobId);
        AssertExpiredMetadata(completed.Snapshot, (await store.FindAsync(Scope, completed.Acceptance.JobId))!);
        await using var db = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(2, await db.Jobs.CountAsync()); Assert.Single(await db.Origins.ToArrayAsync());
        Assert.Equal(JobStoreCodec.Key(Scope, "job", fresh.Acceptance.JobId),
            (await db.Recognition.SingleAsync(row => row.Key == JobStoreCodec.Key(Scope, "recognition", completed.Admission.IdempotencyKey))).JobKey);
    }

    [SkippableTheory]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", false)]
    public async Task Input_and_result_payload_retention_can_expire_independently_without_expiring_the_other_promise(string provider, bool expireInput)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, expireInput ? PayloadLifetime : null, expireInput ? null : PayloadLifetime);
        var completed = await CompleteForPayloadRetention(store, "separate-retention");
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        var cleanup = await store.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(expireInput ? 1 : 0, cleanup.ExpiredInputs); Assert.Equal(expireInput ? 0 : 1, cleanup.ExpiredResults);
        var snapshot = (await store.FindAsync(Scope, completed.Acceptance.JobId))!;
        Assert.Equal(expireInput ? JobPayloadState.Expired : JobPayloadState.Available, snapshot.InputPayloadState);
        Assert.Equal(expireInput ? JobPayloadState.Available : JobPayloadState.Expired, snapshot.ResultPayloadState);
        Assert.Equal(expireInput, snapshot.Request is null); Assert.Equal(!expireInput, snapshot.Result is null);
        Assert.NotNull(snapshot.RequestMetadata); Assert.NotNull(snapshot.ResultMetadata); Assert.Null(snapshot.UnavailableDiagnostic);
        Assert.Equal(completed.Snapshot.Revision, snapshot.Revision);
        if (!expireInput)
        {
            var retry = await store.RetryAsNewRunAsync(new(Scope, completed.Acceptance.JobId, snapshot.Revision,
                "available-input-new-run", "available-input-retry-key", Op()));
            Assert.Equal(JobCommandStatus.Applied, retry.Status); Assert.NotNull(retry.Acceptance);
            Assert.Equal(completed.Acceptance.JobId, (await store.FindAsync(Scope, retry.Acceptance.JobId))!.RetriedFromJobId);
        }
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Result_expiry_clears_progress_and_all_physical_payload_copies_and_late_reports_keep_only_metadata(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, null, PayloadLifetime);
        var completed = await CompleteForPayloadRetention(store, "physical-retention");
        var originalReceiptDigest = JobStoreCodec.Hash(JobStoreCodec.Json(completed.Receipt));
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        Assert.Equal(1, (await store.ExpirePayloadsAsync(new(Scope))).ExpiredResults);
        var latePayload = Payload(Handler.ResultSchema, new { Value = "LATE_PHYSICAL_PAYLOAD_CANARY" });
        var late = completed.Receipt with { ReceiptId = "late-retained-receipt", Payload = latePayload, PayloadSha256 = JobPayloadDigest.Compute(latePayload) };
        Assert.Equal(JobCommandStatus.Applied, (await store.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId, late)).Status);
        Assert.Equal(JobCommandStatus.Replayed, (await store.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId, late)).Status);
        Assert.Equal(JobCommandStatus.Conflict, (await store.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId,
            late with { Retryable = true })).Status);
        var fullRead = await Assert.ThrowsAsync<JobException>(() => store.ListPhysicalReceiptsAsync(Scope,
            completed.Acceptance.JobId, new(10, null)).AsTask());
        Assert.Equal("jobs.payload.expired", fullRead.Code);
        var inspections = (await store.ListPhysicalReceiptInspectionsAsync(Scope, completed.Acceptance.JobId, new(10, null))).Items;
        Assert.Equal(2, inspections.Count);
        Assert.All(inspections, item => { Assert.Equal(JobPayloadState.Expired, item.PayloadState); Assert.Null(item.Receipt); Assert.NotNull(item.ExpiredAt); });
        Assert.Equal(originalReceiptDigest, inspections.Single(item => item.Metadata.ReceiptId == completed.Receipt.ReceiptId).Metadata.ReceiptSha256);
        Assert.Equal(JobStoreCodec.Hash(JobStoreCodec.Json(late)), inspections.Single(item => item.Metadata.ReceiptId == late.ReceiptId).Metadata.ReceiptSha256);
        await using var db = await database.Contexts.CreateDbContextAsync();
        var job = await db.Jobs.SingleAsync(); Assert.Null(job.ResultJson); Assert.Null(job.ProgressJson); Assert.NotNull(job.RequestJson);
        foreach (var row in await db.Receipts.ToArrayAsync())
        {
            Assert.Null(row.ReceiptJson); Assert.NotNull(row.PayloadExpiredAt); Assert.NotNull(row.MetadataJson);
            Assert.DoesNotContain(RetainedResultCanary, row.MetadataJson); Assert.DoesNotContain(RetainedProgressCanary, row.MetadataJson);
            Assert.DoesNotContain("LATE_PHYSICAL_PAYLOAD_CANARY", row.MetadataJson);
        }
        var snapshot = (await store.FindAsync(Scope, completed.Acceptance.JobId))!;
        Assert.Equal(completed.Snapshot.Result!.PayloadSha256, snapshot.ResultMetadata!.PayloadSha256);
        Assert.Equal(completed.Snapshot.Revision, snapshot.Revision); Assert.Null(snapshot.Result); Assert.Null(snapshot.UnavailableDiagnostic);
    }

    [SkippableTheory]
    [InlineData("PostgreSql", false)]
    [InlineData("PostgreSql", true)]
    public async Task Concurrent_expiry_and_physical_report_retry_preserves_only_metadata_whichever_commit_wins(string provider, bool cleanupSelectedFirst)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        var completed = await CompleteForPayloadRetention(store, "concurrent-payload-retention");
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        var latePayload = Payload(Handler.ResultSchema, new { Value = "CONCURRENT_LATE_PAYLOAD_CANARY" });
        var late = completed.Receipt with { ReceiptId = "concurrent-late-report", Payload = latePayload, PayloadSha256 = JobPayloadDigest.Compute(latePayload) };
        var barrier = new BeforeOneSave();
        var suspended = new EfJobStore(new InterceptedFactory(new InterceptedFactory(database.Contexts, clock), barrier),
            new(100, 3, 16, PayloadLifetime, PayloadLifetime));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task<JobPayloadCleanupResult>? cleaning = null;
        Task<JobTransition>? reporting = null;
        try
        {
            // PostgreSQL lets both transactions select the old row. Force the other operation to commit
            // after selection and before save, proving that the losing operation reloads its proof.
            if (cleanupSelectedFirst) cleaning = suspended.ExpirePayloadsAsync(new(Scope), cancellation.Token).AsTask();
            else reporting = suspended.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId, late, cancellation.Token).AsTask();
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation.Token);
            if (cleanupSelectedFirst)
            {
                reporting = store.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId, late, cancellation.Token).AsTask();
                Assert.Equal(JobCommandStatus.Applied, (await reporting).Status);
            }
            else
            {
                cleaning = store.ExpirePayloadsAsync(new(Scope), cancellation.Token).AsTask();
                Assert.Equal(1, (await cleaning).ExpiredResults);
            }
        }
        finally
        {
            barrier.Release.TrySetResult();
            // Drain the suspended transaction even if a barrier or competing write fails.
            await Task.WhenAll((Task?)cleaning ?? Task.CompletedTask, (Task?)reporting ?? Task.CompletedTask);
        }
        Assert.Equal(1, cleaning!.Result.ExpiredInputs); Assert.Equal(1, cleaning.Result.ExpiredResults);
        Assert.Equal(JobCommandStatus.Applied, reporting!.Result.Status);
        AssertExpiredMetadata(completed.Snapshot, (await store.FindAsync(Scope, completed.Acceptance.JobId))!);
        var reports = (await store.ListPhysicalReceiptInspectionsAsync(Scope, completed.Acceptance.JobId, new(10, null))).Items;
        Assert.Equal(2, reports.Count);
        Assert.All(reports, item => { Assert.Equal(JobPayloadState.Expired, item.PayloadState); Assert.Null(item.Receipt); });
        Assert.Equal(JobPayloadDigest.Compute(latePayload), reports.Single(item => item.Metadata.ReceiptId == late.ReceiptId).Metadata.PayloadSha256);
        Assert.Equal(JobCommandStatus.Replayed, (await store.RecordPhysicalReceiptAsync(Scope, completed.Acceptance.JobId, late)).Status);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.All(await check.Receipts.ToArrayAsync(), item => { Assert.Null(item.ReceiptJson); Assert.NotNull(item.PayloadExpiredAt); });
    }

    [SkippableTheory]
    [InlineData("Sqlite", "missing-request")]
    [InlineData("PostgreSql", "missing-request")]
    [InlineData("Sqlite", "missing-result")]
    [InlineData("PostgreSql", "missing-result")]
    [InlineData("Sqlite", "request-metadata")]
    [InlineData("PostgreSql", "request-metadata")]
    [InlineData("Sqlite", "result-metadata")]
    [InlineData("PostgreSql", "result-metadata")]
    [InlineData("Sqlite", "policy")]
    [InlineData("PostgreSql", "policy")]
    [InlineData("Sqlite", "missing-delivery")]
    [InlineData("PostgreSql", "missing-delivery")]
    [InlineData("Sqlite", "missing-grant")]
    [InlineData("PostgreSql", "missing-grant")]
    [InlineData("Sqlite", "missing-ack-proof")]
    [InlineData("PostgreSql", "missing-ack-proof")]
    [InlineData("Sqlite", "ack-result-binding")]
    [InlineData("PostgreSql", "ack-result-binding")]
    [InlineData("Sqlite", "grant-result-binding")]
    [InlineData("PostgreSql", "grant-result-binding")]
    [InlineData("Sqlite", "receipt-metadata")]
    [InlineData("PostgreSql", "receipt-metadata")]
    public async Task Corrupt_or_missing_retention_and_delivery_evidence_refuses_cleanup_without_changing_any_retained_bytes(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        var completed = await CompleteForPayloadRetention(store, "invalid-retention", RetentionAssociation());
        await AcknowledgeForPayloadRetention(store, completed);
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var job = await db.Jobs.SingleAsync(); var delivery = await db.Deliveries.SingleAsync();
            switch (corruption)
            {
                case "missing-request": job.RequestJson = null; break;
                case "missing-result": job.ResultJson = null; break;
                case "request-metadata": job.RequestMetadataSha256 = new('c', 64); break;
                case "result-metadata": job.ResultMetadataSha256 = new('c', 64); break;
                case "policy": job.RetentionPolicySha256 = new('c', 64); break;
                case "missing-delivery": db.Deliveries.Remove(delivery); break;
                case "missing-grant": delivery.PermissionJson = null; break;
                case "missing-ack-proof": delivery.ProofJson = null; break;
                case "ack-result-binding":
                    var acknowledgement = JobStoreCodec.Read<JobDeliveryAcknowledgement>(delivery.ProofJson!);
                    delivery.ProofJson = JobStoreCodec.Json(acknowledgement with { Proof = acknowledgement.Proof with { ResultSha256 = new('d', 64) } });
                    break;
                case "grant-result-binding":
                    delivery.PermissionJson = JobStoreCodec.Json(JobStoreCodec.Read<JobContinuationPermission>(delivery.PermissionJson!) with { ResultId = "different-result" });
                    break;
                case "receipt-metadata": (await db.Receipts.SingleAsync()).MetadataSha256 = new('c', 64); break;
                default: throw new ArgumentOutOfRangeException(nameof(corruption));
            }
            await db.SaveChangesAsync();
        }
        var before = await RetentionBytes(database);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        var failure = await Assert.ThrowsAsync<JobException>(() => store.ExpirePayloadsAsync(new(Scope)).AsTask());
        Assert.Equal("jobs.persistence.invalid-record", failure.Code);
        Assert.Equal(before, await RetentionBytes(database));
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task A_corrupt_later_cleanup_candidate_rolls_back_earlier_candidates_in_the_same_provider_transaction(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        await CompleteForPayloadRetention(store, "atomic-expiry-a");
        await CompleteForPayloadRetention(store, "atomic-expiry-b");
        string corruptId;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.OrderBy(item => item.Key).LastAsync();
            corruptId = row.JobId; row.ResultMetadataSha256 = new('d', 64); await db.SaveChangesAsync();
        }
        var before = await RetentionBytes(database);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        await Assert.ThrowsAsync<JobException>(() => store.ExpirePayloadsAsync(new(Scope)).AsTask());
        Assert.Equal(before, await RetentionBytes(database));
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Jobs.SingleAsync(item => item.JobId == corruptId);
            row.ResultMetadataSha256 = JobStoreCodec.Hash(row.ResultMetadataJson!); await db.SaveChangesAsync();
        }
        var confirmed = await store.ExpirePayloadsAsync(new(Scope));
        Assert.Equal(2, confirmed.ExpiredInputs); Assert.Equal(2, confirmed.ExpiredResults);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.All(await check.Jobs.ToArrayAsync(), item => { Assert.Null(item.RequestJson); Assert.Null(item.ResultJson); Assert.NotNull(item.InputExpiredAt); Assert.NotNull(item.ResultExpiredAt); });
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Payload_cleanup_is_bounded_cursor_scoped_and_does_not_cross_tenant_or_partition(string provider)
    {
        await using var database = await Database(provider);
        var clock = new ControlledProviderClock(PayloadRetentionEpoch);
        var store = PayloadStore(database, clock, PayloadLifetime, PayloadLifetime);
        for (var index = 0; index < 5; index++) await CompleteForPayloadRetention(store, "page-" + index);
        var foreignTenant = Scope with { TenantId = "tenant-b" };
        var foreignPartition = Scope with { PersistencePartition = "other-partition" };
        var otherTenant = await CompleteForPayloadRetention(store, "other-tenant", scope: foreignTenant);
        var otherPartition = await CompleteForPayloadRetention(store, "other-partition", scope: foreignPartition);
        clock.Set(checked(clock.Now + PayloadLifetime.Ticks));
        var page = await store.ExpirePayloadsAsync(new(Scope, 2));
        Assert.Equal(2, page.ExaminedJobs); Assert.Equal(2, page.ExpiredInputs); Assert.Equal(2, page.ExpiredResults); Assert.NotNull(page.ContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ExpirePayloadsAsync(new(foreignTenant, 2, page.ContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ExpirePayloadsAsync(new(foreignPartition, 2, page.ContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ExpirePayloadsAsync(new(Scope, 0)).AsTask());
        var inputs = page.ExpiredInputs; var outputs = page.ExpiredResults; var traversals = 1;
        while (page.ContinuationToken is { } next)
        {
            Assert.True(traversals++ < 5, "A bounded traversal did not finish.");
            page = await store.ExpirePayloadsAsync(new(Scope, 2, next));
            Assert.InRange(page.ExaminedJobs, 0, 2); inputs += page.ExpiredInputs; outputs += page.ExpiredResults;
        }
        Assert.Equal(5, inputs); Assert.Equal(5, outputs);
        Assert.NotNull((await store.FindAsync(foreignTenant, otherTenant.Acceptance.JobId))!.Request);
        Assert.NotNull((await store.FindAsync(foreignPartition, otherPartition.Acceptance.JobId))!.Result);
        Assert.Null(await store.FindAsync(Scope, otherTenant.Acceptance.JobId));
        Assert.Null(await store.FindAsync(Scope, otherPartition.Acceptance.JobId));
    }

    private static EfJobStore PayloadStore(JobsProviderDatabase database, ControlledProviderClock clock,
        TimeSpan? inputRetention = null, TimeSpan? resultRetention = null) =>
        new(new InterceptedFactory(database.Contexts, clock), new(100, 3, 16, inputRetention, resultRetention));

    private static JobAdmission RetentionAdmission(string key, JobAssociation? association = null, JobOrigin? origin = null, JobScope? scope = null) =>
        Admission(key, Request(association) with { Input = Payload(Handler.InputSchema, new { Value = RetainedInputCanary }) }, origin, scope);

    private static JobAssociation RetentionAssociation() => new("native", "retention-definition", "v1", "retention-parent", "retention-invocation",
        "retention-material", new('a', 64), JobAssociationMode.Awaited, Payload(new("native-retention-address", 1), new { Bookmark = "actual-saved-wait" }));

    private static JobPhysicalReceipt RetentionReceipt(JobAttemptLease lease)
    {
        var payload = Payload(Handler.ResultSchema, new { Value = RetainedResultCanary });
        return Receipt(lease) with { Payload = payload, PayloadSha256 = JobPayloadDigest.Compute(payload) };
    }

    private sealed record PayloadRetentionCase(JobAdmission Admission, JobAcceptance Acceptance, JobAttemptLease Lease, JobPhysicalReceipt Receipt, JobSnapshot Snapshot);

    private static async Task<PayloadRetentionCase> CompleteForPayloadRetention(EfJobStore store, string key,
        JobAssociation? association = null, JobOrigin? origin = null, JobScope? scope = null)
    {
        var admission = RetentionAdmission(key, association, origin, scope);
        var acceptance = (await store.AdmitAsync(admission)).Acceptance!;
        var lease = (await store.ClaimAsync(Claim() with { Scope = admission.Scope })).Lease!;
        Assert.Equal(acceptance.JobId, lease.JobId);
        var progress = new JobProgress(.5, RetainedProgressCanary, Payload(new("retention-progress", 1), new { Value = RetainedProgressCanary }));
        Assert.Equal(JobCommandStatus.Applied, (await store.RecordProgressAsync(lease, progress)).Status);
        var receipt = RetentionReceipt(lease);
        Assert.Equal(JobCommandStatus.Applied, (await store.CompleteAsync(lease, receipt)).Status);
        return new(admission, acceptance, lease, receipt, (await store.FindAsync(admission.Scope, acceptance.JobId))!);
    }

    private static async Task AcknowledgeForPayloadRetention(EfJobStore store, PayloadRetentionCase completed)
    {
        var result = completed.Snapshot.Result!;
        var grant = await store.GrantContinuationAsync(new(completed.Admission.Scope, completed.Acceptance.JobId,
            result.ResultId, "retained-continuation", "retained-model-input"));
        Assert.Equal(JobCommandStatus.Applied, grant.Status);
        Assert.Equal(JobCommandStatus.Applied, (await store.AcknowledgeDeliveryAsync(new(completed.Admission.Scope,
            completed.Acceptance.JobId, "retained-continuation", new(JobDeliveryProofKind.Consumed, "owner-proof", result.PayloadSha256)))).Status);
    }

    private static void AssertExpiredMetadata(JobSnapshot completed, JobSnapshot expired)
    {
        Assert.Equal(JobState.Terminal, expired.State); Assert.Equal(completed.JobId, expired.JobId);
        Assert.Equal(completed.RequestSha256, expired.RequestSha256); Assert.Equal(completed.Revision, expired.Revision);
        Assert.Null(expired.Request); Assert.Null(expired.Result); Assert.Null(expired.Progress); Assert.Null(expired.UnavailableDiagnostic);
        Assert.Equal(JobPayloadState.Expired, expired.InputPayloadState); Assert.Equal(JobPayloadState.Expired, expired.ResultPayloadState);
        Assert.Equal(JobStoreCodec.Json(completed.RequestMetadata), JobStoreCodec.Json(expired.RequestMetadata));
        Assert.Equal(JobStoreCodec.Json(completed.ResultMetadata), JobStoreCodec.Json(expired.ResultMetadata));
        Assert.Equal(JobOutcome.Succeeded, expired.ResultMetadata!.Outcome);
        Assert.NotNull(expired.PayloadRetention!.InputExpiredAt); Assert.NotNull(expired.PayloadRetention.ResultExpiredAt);
    }

    private static async Task AssertRetentionBytesPresent(JobsProviderDatabase database, string jobId)
    {
        await using var db = await database.Contexts.CreateDbContextAsync();
        var job = await db.Jobs.SingleAsync(item => item.JobId == jobId);
        Assert.Contains(RetainedInputCanary, job.RequestJson!); Assert.Contains(RetainedResultCanary, job.ResultJson!);
        Assert.Contains(RetainedProgressCanary, job.ProgressJson!);
        Assert.Null(job.InputExpiredAt); Assert.Null(job.ResultExpiredAt);
        Assert.All(await db.Receipts.Where(row => row.JobKey == job.Key).ToArrayAsync(), row => Assert.Contains(RetainedResultCanary, row.ReceiptJson!));
    }

    private static async Task<string> RetentionBytes(JobsProviderDatabase database)
    {
        await using var db = await database.Contexts.CreateDbContextAsync();
        return JobStoreCodec.Json(new
        {
            Jobs = await db.Jobs.AsNoTracking().OrderBy(item => item.Key).ToArrayAsync(),
            Receipts = await db.Receipts.AsNoTracking().OrderBy(item => item.Key).ToArrayAsync(),
            Deliveries = await db.Deliveries.AsNoTracking().OrderBy(item => item.Key).ToArrayAsync()
        });
    }
}
