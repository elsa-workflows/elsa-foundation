using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Exact_continuation_query_is_read_only_scoped_and_survives_acknowledgement_and_reconstruction(string provider)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "definition", "v1", "execution", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("native-address", 1), new { Bookmark = "exact-bookmark" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("exact-continuation", Request(association)))).Acceptance!;
        Assert.Null(await database.Queries.FindContinuationAsync(Scope, accepted.JobId, "continuation"));
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        var before = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Null(await database.Queries.FindContinuationAsync(Scope, accepted.JobId, "continuation"));
        var grant = (await database.Commands.GrantContinuationAsync(new(Scope, accepted.JobId, before.Result!.ResultId, "continuation", "input"))).Permission!;
        await database.Commands.PauseAsync(new(Scope, new(JobGateKind.WorkflowInstance, "execution", "native"), "hold", Op()));
        Assert.Equal(JobStoreCodec.Json(grant), JobStoreCodec.Json(await database.Queries.FindContinuationAsync(Scope, accepted.JobId, "continuation")));
        Assert.Null(await database.Queries.FindContinuationAsync(Scope, accepted.JobId, "other-continuation"));
        Assert.Null(await database.Queries.FindContinuationAsync(Scope with { TenantId = "tenant-b" }, accepted.JobId, "continuation"));
        Assert.Null(await database.Queries.FindContinuationAsync(Scope with { PersistencePartition = "other" }, accepted.JobId, "continuation"));
        Assert.Null(await database.Queries.FindContinuationAsync(Scope, "other-job", "continuation"));
        Assert.Equal(JobCommandStatus.Applied, (await database.Commands.AcknowledgeDeliveryAsync(new(Scope, accepted.JobId, "continuation",
            new(JobDeliveryProofKind.Consumed, "native-consumed-receipt", before.Result.PayloadSha256)))).Status);
        // A corrupt secondary binding on another retained row cannot redirect an exact-address read.
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var unrelated = JobStoreCodec.New<JobDeliveryRecord>(Scope, "delivery", "unrelated-delivery");
            unrelated.JobKey = JobStoreCodec.Key(Scope, "job", accepted.JobId); unrelated.ResultId = "unrelated-result";
            unrelated.AssociationJson = JobStoreCodec.Json(association); unrelated.Acknowledged = true;
            db.Deliveries.Add(unrelated); await db.SaveChangesAsync();
        }
        var reconstructed = new EfJobStore(database.Contexts, new(100, 3, 16));
        Assert.Equal(JobStoreCodec.Json(grant), JobStoreCodec.Json(await reconstructed.FindContinuationAsync(Scope, accepted.JobId, "continuation")));
        Assert.Empty((await database.Queries.ListPendingDeliveriesAsync(Scope, new(10, null))).Items);
        Assert.Equal(before.Revision, (await database.Queries.FindAsync(Scope, accepted.JobId))!.Revision);
    }

    [SkippableTheory]
    [InlineData("Sqlite", "null")]
    [InlineData("PostgreSql", "null")]
    [InlineData("Sqlite", "binding")]
    [InlineData("PostgreSql", "binding")]
    [InlineData("Sqlite", "job-key")]
    [InlineData("PostgreSql", "job-key")]
    [InlineData("Sqlite", "unavailable")]
    [InlineData("PostgreSql", "unavailable")]
    [InlineData("Sqlite", "model-input-id")]
    [InlineData("PostgreSql", "model-input-id")]
    [InlineData("Sqlite", "continuation-id")]
    [InlineData("PostgreSql", "continuation-id")]
    [InlineData("Sqlite", "nonterminal")]
    [InlineData("PostgreSql", "nonterminal")]
    public async Task Corrupt_exact_continuation_is_unavailable_and_preserved_rather_than_absent_proof(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var association = new JobAssociation("native", "definition", "v1", "execution", "invocation", "material", new('a', 64),
            JobAssociationMode.Awaited, Payload(new("native-address", 1), new { Bookmark = "exact-bookmark" }));
        var accepted = (await database.Commands.AdmitAsync(Admission("corrupt-continuation", Request(association)))).Acceptance!;
        var lease = (await database.Commands.ClaimAsync(Claim())).Lease!;
        await database.Commands.CompleteAsync(lease, Receipt(lease));
        var result = (await database.Queries.FindAsync(Scope, accepted.JobId))!.Result!;
        var grant = (await database.Commands.GrantContinuationAsync(new(Scope, accepted.JobId, result.ResultId, "continuation", "input"))).Permission!;
        var retained = corruption switch
        {
            "null" => "null",
            "binding" => JsonSerializer.Serialize(grant with { ResultId = "wrong-result" }),
            "model-input-id" => JsonSerializer.Serialize(grant with { ModelInputId = new('x', 257) }),
            "continuation-id" => JsonSerializer.Serialize(grant with { ContinuationId = new('x', 257) }),
            _ => JobStoreCodec.Json(grant)
        };
        var retainedJobKey = JobStoreCodec.Key(Scope, "job", corruption == "job-key" ? "wrong-job" : accepted.JobId);
        var unavailable = corruption == "unavailable" ? JobStoreCodec.Json(new JobDiagnostic("jobs.test.unavailable", "The retained job is unavailable.")) : null;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Deliveries.SingleAsync(); row.PermissionJson = retained; row.JobKey = retainedJobKey;
            var job = await db.Jobs.SingleAsync(); job.UnavailableDiagnosticJson = unavailable;
            if (corruption == "nonterminal") job.State = (int)JobState.Accepted;
            await db.SaveChangesAsync();
        }
        var failure = await Assert.ThrowsAsync<JobException>(() => database.Queries.FindContinuationAsync(Scope, accepted.JobId, "continuation").AsTask());
        Assert.Equal("jobs.persistence.invalid-record", failure.Code); Assert.NotNull(failure.InnerException);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var unchanged = await check.Deliveries.SingleAsync(); Assert.Equal(retained, unchanged.PermissionJson);
        Assert.Equal(retainedJobKey, unchanged.JobKey); Assert.False(unchanged.Acknowledged);
        Assert.Equal(unavailable, (await check.Jobs.SingleAsync()).UnavailableDiagnosticJson);
    }
}
