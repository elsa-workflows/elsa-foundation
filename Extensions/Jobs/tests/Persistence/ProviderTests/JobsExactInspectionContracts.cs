using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Successful_receipt_requires_its_schema_envelope_and_preserves_a_valid_json_null_value(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("null-result-envelope"))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        var invalid = Receipt(lease) with { Payload = null, PayloadSha256 = JobPayloadDigest.Compute(null) };
        await Assert.ThrowsAsync<ArgumentException>(() => database.Commands.CompleteAsync(lease, invalid).AsTask());
        var active = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobState.AttemptActive, active.State); Assert.Null(active.Result);
        Assert.Empty((await database.Queries.ListPhysicalReceiptsAsync(Scope, accepted.JobId, new(10, null))).Items);
        var payload = Payload(Handler.ResultSchema, null!);
        var valid = invalid with { Payload = payload, PayloadSha256 = JobPayloadDigest.Compute(payload) };
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.CompleteAsync(lease, valid)).Status);
        var selected = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobOutcome.Succeeded, selected.Result!.Outcome);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, selected.Result.Payload!.Value.ValueKind);
        Assert.Null(selected.UnavailableDiagnostic);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "outcome")]
    [InlineData("PostgreSql", "outcome")]
    [InlineData("Sqlite", "schema")]
    [InlineData("PostgreSql", "schema")]
    [InlineData("Sqlite", "null-payload")]
    [InlineData("PostgreSql", "null-payload")]
    public async Task Digest_consistent_but_unsupported_result_shape_is_unavailable_to_delivery_inspection(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "definition", "v1", "parent", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("native-address", 1), new { Bookmark = "saved-wait" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("result-shape", Request(association)))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!; await database.Commands.CompleteAsync(lease, Receipt(lease));
        var selected = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        var changed = corruption switch
        {
            "outcome" => selected with { Outcome = (JobOutcome)987 },
            "null-payload" => selected with { Payload = null },
            _ => selected with { Payload = new JobPayload(Handler.ResultSchema with { Alias = "wrong-result-schema" }, selected.Payload!.Value) }
        };
        changed = changed with { PayloadSha256 = JobPayloadDigest.Compute(changed.Payload) };
        var retained = JobStoreCodec.Json(changed);
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            (await db.Jobs.SingleAsync()).ResultJson = retained; await db.SaveChangesAsync();
        }
        var snapshot = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.NotNull(snapshot.UnavailableDiagnostic); Assert.Null(snapshot.Result);
        var failure = await Assert.ThrowsAsync<JobException>(() => database.Queries.FindDeliveryAsync(Scope, accepted.JobId).AsTask());
        Assert.Equal("jobs.persistence.invalid-record", failure.Code);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(retained, (await check.Jobs.SingleAsync()).ResultJson); Assert.False((await check.Deliveries.SingleAsync()).Acknowledged);
    }

    [SkippableTheory]
    [InlineData("Sqlite", false)]
    [InlineData("PostgreSql", false)]
    [InlineData("Sqlite", true)]
    [InlineData("PostgreSql", true)]
    public async Task Exact_delivery_retains_consumption_or_parent_terminal_acknowledgement_without_changing_the_job(string provider, bool parentTerminal)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "definition", "v1", "parent", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("native-address", 1), new { Bookmark = "saved-wait" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("delivery-inspection", Request(association)))).Acceptance!;
        Assert.Null(await database.Queries.FindDeliveryAsync(Scope, accepted.JobId));
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        var terminal = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        var owed = (await database.Queries.FindDeliveryAsync(Scope, accepted.JobId))!;
        Assert.False(owed.IsAcknowledged); Assert.Null(owed.Acknowledgement); Assert.Null(owed.Permission);
        Assert.Equal(terminal.Result!.ResultId, owed.ResultId); Assert.Equal(accepted.JobId, owed.JobId);
        if (!parentTerminal)
            await database.Commands.GrantContinuationAsync(new(Scope, accepted.JobId, owed.ResultId, "continuation", "model-input"));
        var proof = new JobDeliveryProof(parentTerminal ? JobDeliveryProofKind.ParentProvenTerminal : JobDeliveryProofKind.Consumed,
            "exact-owner-receipt", terminal.Result.PayloadSha256);
        await database.Commands.AcknowledgeDeliveryAsync(new(Scope, accepted.JobId, "continuation", proof));
        var fresh = new EfJobStore(database.Contexts, new(100, 3, 16));
        var settled = (await fresh.FindDeliveryAsync(Scope, accepted.JobId))!;
        Assert.True(settled.IsAcknowledged); Assert.Equal("continuation", settled.Acknowledgement!.ContinuationId);
        Assert.Equal(proof, settled.Acknowledgement.Proof); Assert.Equal(!parentTerminal, settled.Permission is not null);
        Assert.Null(await fresh.FindDeliveryAsync(Scope with { TenantId = "tenant-b" }, accepted.JobId));
        Assert.Null(await fresh.FindDeliveryAsync(Scope, "missing"));
        Assert.Equal(terminal.Revision, (await database.Queries.FindAsync(Scope, accepted.JobId))!.Revision);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Exact_origin_distinguishes_unknown_preadmission_tombstone_and_the_canonical_accepted_cancelled_job(string provider)
    {
        await using var database = await Database(provider);
        var request = Request(); var requestDigest = JobRequestDigest.Compute(request);
        var cancelled = new JobOrigin("native", "parent", "cancel-first", new('a', 64));
        Assert.Null(await database.Queries.FindOriginAsync(Scope, cancelled));
        await database.Commands.CancelOriginAsync(new(Scope, cancelled, requestDigest, Op()));
        var tombstone = (await database.Queries.FindOriginAsync(Scope, cancelled))!;
        Assert.True(tombstone.Cancelled); Assert.False(tombstone.Retired); Assert.Null(tombstone.Acceptance);
        Assert.Equal(requestDigest, tombstone.RequestSha256); Assert.Equal(cancelled, tombstone.Origin);
        Assert.Equal(JobCommandStatus.OriginCancelled, (await database.Commands.AdmitAsync(Admission("late-cancelled", request, cancelled))).Status);
        var original = Admission("existing-producer-key", request); var accepted = (await database.Commands.AdmitAsync(original)).Acceptance!;
        var origin = cancelled with { OriginId = "accepted", PayloadSha256 = new('b', 64) };
        var replay = await database.Commands.AdmitAsync(original with { ProposedJobId = "different-proposal", Origin = origin });
        Assert.Equal(accepted.JobId, replay.Acceptance!.JobId);
        await database.Commands.CancelOriginAsync(new(Scope, origin, requestDigest, Op()));
        await database.Commands.CancelOriginAsync(new(Scope, origin, requestDigest, Op()));
        var fresh = new EfJobStore(database.Contexts, new(100, 3, 16));
        var retained = (await fresh.FindOriginAsync(Scope, origin))!;
        Assert.True(retained.Cancelled); Assert.False(retained.Retired); Assert.Equal(accepted.JobId, retained.Acceptance!.JobId);
        Assert.Null(await fresh.FindOriginAsync(Scope with { TenantId = "tenant-b" }, origin));
        var conflict = await Assert.ThrowsAsync<JobException>(() => fresh.FindOriginAsync(Scope, origin with { PayloadSha256 = new('c', 64) }).AsTask());
        Assert.Equal("jobs.origin.conflict", conflict.Code);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "missing-job")]
    [InlineData("PostgreSql", "missing-job")]
    [InlineData("Sqlite", "request-digest")]
    [InlineData("PostgreSql", "request-digest")]
    [InlineData("Sqlite", "source-digest")]
    [InlineData("PostgreSql", "source-digest")]
    [InlineData("Sqlite", "empty-binding")]
    [InlineData("PostgreSql", "empty-binding")]
    [InlineData("Sqlite", "unavailable")]
    [InlineData("PostgreSql", "unavailable")]
    public async Task Corrupt_protected_origin_never_becomes_absence_or_a_preadmission_tombstone(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var origin = new JobOrigin("native", "parent", "retained-origin", new('a', 64));
        var accepted = (await database.Commands.AdmitAsync(Admission("origin-corruption", origin: origin))).Acceptance!;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Origins.SingleAsync();
            switch (corruption)
            {
                case "missing-job": row.JobKey = JobStoreCodec.Key(Scope, "job", "missing"); break;
                case "request-digest": row.RequestSha256 = new('b', 64); break;
                case "source-digest": row.OriginSha256 = "invalid"; break;
                case "empty-binding": row.JobKey = null; break;
                case "unavailable": (await db.Jobs.SingleAsync()).UnavailableDiagnosticJson = JobStoreCodec.Json(new JobDiagnostic("jobs.test.unavailable", "Unavailable.")); break;
            }
            await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<JobException>(() => database.Queries.FindOriginAsync(Scope, origin).AsTask());
        Assert.Equal("jobs.persistence.invalid-record", failure.Code); Assert.NotNull(failure.InnerException);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.False((await check.Origins.SingleAsync()).Cancelled); Assert.Single(await check.Jobs.ToArrayAsync());
        Assert.NotNull(await database.Queries.FindAsync(Scope, accepted.JobId));
    }

    [SkippableTheory]
    [InlineData("Sqlite", "missing-proof")]
    [InlineData("PostgreSql", "missing-proof")]
    [InlineData("Sqlite", "wrong-result")]
    [InlineData("PostgreSql", "wrong-result")]
    [InlineData("Sqlite", "oversized-receipt")]
    [InlineData("PostgreSql", "oversized-receipt")]
    public async Task Corrupt_acknowledgement_cannot_be_returned_as_settled_responsibility(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "definition", "v1", "parent", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("native-address", 1), new { Bookmark = "saved-wait" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("ack-corruption", Request(association)))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!; await database.Commands.CompleteAsync(lease, Receipt(lease));
        var selected = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        var proof = new JobDeliveryProof(JobDeliveryProofKind.ParentProvenTerminal, "terminal-receipt", selected.PayloadSha256);
        await database.Commands.AcknowledgeDeliveryAsync(new(Scope, accepted.JobId, "continuation", proof));
        var bad = corruption == "missing-proof" ? null : JobStoreCodec.Json(new JobDeliveryAcknowledgement("continuation",
            corruption == "wrong-result" ? proof with { ResultSha256 = new('b', 64) } : proof with { ModelReceiptId = new('x', 257) }));
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            (await db.Deliveries.SingleAsync()).ProofJson = bad; await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<JobException>(() => database.Queries.FindDeliveryAsync(Scope, accepted.JobId).AsTask());
        Assert.Equal("jobs.persistence.invalid-record", failure.Code);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var unchanged = await check.Deliveries.SingleAsync(); Assert.True(unchanged.Acknowledged); Assert.Equal(bad, unchanged.ProofJson);
    }
}
