using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed partial class JobsProviderContracts
{
    [SkippableTheory]
    [InlineData("Sqlite", JobOperationKind.Cancel)]
    [InlineData("PostgreSql", JobOperationKind.Cancel)]
    [InlineData("Sqlite", JobOperationKind.CancelOrigin)]
    [InlineData("PostgreSql", JobOperationKind.CancelOrigin)]
    [InlineData("Sqlite", JobOperationKind.RetryAsNewRun)]
    [InlineData("PostgreSql", JobOperationKind.RetryAsNewRun)]
    [InlineData("Sqlite", JobOperationKind.Pause)]
    [InlineData("PostgreSql", JobOperationKind.Pause)]
    [InlineData("Sqlite", JobOperationKind.ReleaseHold)]
    [InlineData("PostgreSql", JobOperationKind.ReleaseHold)]
    [InlineData("Sqlite", JobOperationKind.SetCapacity)]
    [InlineData("PostgreSql", JobOperationKind.SetCapacity)]
    [InlineData("Sqlite", JobOperationKind.SaveSchedule)]
    [InlineData("PostgreSql", JobOperationKind.SaveSchedule)]
    [InlineData("Sqlite", JobOperationKind.SetScheduleEnabled)]
    [InlineData("PostgreSql", JobOperationKind.SetScheduleEnabled)]
    public async Task Original_operation_audit_survives_commit_reply_loss_provider_reconstruction_and_exact_replay(string provider, JobOperationKind kind)
    {
        await using var database = await Database(provider);
        var prepared = await PrepareAuditedCommand(database, kind);
        var operation = new JobOperation("audited-operation", "original-operator", "Original operator recovery reason.");
        long before; int priorCount;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        { before = await db.ReadUtcTicksAsync(CancellationToken.None); priorCount = await db.Operations.CountAsync(); }
        var acknowledgement = new LoseOneCommitReply();
        var uncertain = new EfJobStore(new InterceptedFactory(database.Contexts, acknowledgement), new(100, 1000, 16), database.Recurrence);
        await Assert.ThrowsAsync<JobPersistenceException>(() => prepared.Invoke(uncertain, operation, "accepted-new-job"));
        Assert.True(acknowledgement.Fired);
        // A new authority with fresh contexts has no in-memory audit or response state.
        var restarted = new EfJobStore(database.Contexts, new(100, 1000, 16), database.Recurrence);
        var original = Assert.IsType<JobOperationAudit>((await restarted.FindOperationAuditAsync(Scope, operation.OperationId))!.Audit);
        Assert.Equal(operation, original.Operation); Assert.Equal(Scope, original.Scope); Assert.Equal(kind, original.Kind);
        Assert.Equal(prepared.Target, original.Target); Assert.Equal(JobCommandStatus.Applied, original.Confirmation.Status);
        string originalBytes; string resultBytes; string digest; byte[] token;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId));
            originalBytes = row.AuditJson!; resultBytes = row.ResultJson; digest = row.Digest; token = row.Token.ToArray();
            Assert.Equal(priorCount + 1, await db.Operations.CountAsync());
            Assert.InRange(original.RecordedAt.UtcTicks, before, await db.ReadUtcTicksAsync(CancellationToken.None));
            Assert.Equal(digest, original.CommandSha256); Assert.True(Encoding.UTF8.GetByteCount(originalBytes) <= 32 * 1024);
            Assert.DoesNotContain("AUDIT_INPUT_PAYLOAD_CANARY", originalBytes);
        }
        // Move only the provider-clock seam for replay. Restamping audit on replay must fail
        // deterministically even when the physical engine's clock has millisecond resolution.
        var futureClock = new ControlledProviderClock(checked(original.RecordedAt.UtcTicks + TimeSpan.FromDays(2).Ticks));
        var replayAuthority = new EfJobStore(new InterceptedFactory(database.Contexts, futureClock), new(100, 1000, 16), database.Recurrence);
        var replay = await prepared.Invoke(replayAuthority, operation, "different-proposed-job-id");
        Assert.Equal(JobCommandStatus.Replayed, replay.Status); Assert.Equal(original.Confirmation.JobId, replay.JobId);
        Assert.True(futureClock.Reads > 0);
        await Assert.ThrowsAsync<JobException>(() => prepared.Invoke(replayAuthority, operation with { Actor = "different-operator" }, "changed-hint"));
        await Assert.ThrowsAsync<JobException>(() => prepared.Invoke(replayAuthority, operation with { Reason = "different reason" }, "changed-hint"));
        Assert.Equal(original, (await restarted.FindOperationAuditAsync(Scope, operation.OperationId))!.Audit);
        Assert.Null(await restarted.FindOperationAuditAsync(Scope with { TenantId = "tenant-b" }, operation.OperationId));
        await VerifyAuditedEffect(restarted, original);
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId));
        Assert.Equal(originalBytes, retained.AuditJson); Assert.Equal(resultBytes, retained.ResultJson); Assert.Equal(digest, retained.Digest);
        Assert.Equal(token, retained.Token); Assert.Equal(priorCount + 1, await check.Operations.CountAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite", "legacy")]
    [InlineData("PostgreSql", "legacy")]
    [InlineData("Sqlite", "json")]
    [InlineData("PostgreSql", "json")]
    [InlineData("Sqlite", "actor")]
    [InlineData("PostgreSql", "actor")]
    [InlineData("Sqlite", "operation-null")]
    [InlineData("PostgreSql", "operation-null")]
    [InlineData("Sqlite", "missing-kind")]
    [InlineData("PostgreSql", "missing-kind")]
    [InlineData("Sqlite", "missing-time")]
    [InlineData("PostgreSql", "missing-time")]
    [InlineData("Sqlite", "scope")]
    [InlineData("PostgreSql", "scope")]
    [InlineData("Sqlite", "target")]
    [InlineData("PostgreSql", "target")]
    [InlineData("Sqlite", "confirmation")]
    [InlineData("PostgreSql", "confirmation")]
    [InlineData("Sqlite", "version")]
    [InlineData("PostgreSql", "version")]
    [InlineData("Sqlite", "oversize")]
    [InlineData("PostgreSql", "oversize")]
    public async Task Legacy_or_corrupt_audit_is_explicitly_unavailable_without_repairing_history_or_blocking_a_healthy_row(string provider, string corruption)
    {
        await using var database = await Database(provider);
        var operation = new JobOperation("unavailable-audit", "operator", "Retain this original reason.");
        var request = new JobCancelRequest(Scope, "missing-job", operation);
        Assert.Equal(JobCommandStatus.NotFound, (await database.Commands.CancelAsync(request)).Status);
        await database.Commands.SetCapacityAsync(new(Scope, new(JobGateKind.Queue, "healthy"), 2, 0, Op()));
        string? unavailableBytes; string digest; string result;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId));
            var audit = JsonNode.Parse(row.AuditJson!)!.AsObject();
            switch (corruption)
            {
                case "actor": audit["Operation"]!["Actor"] = null; break;
                case "operation-null": audit["Operation"] = null; break;
                case "missing-kind": audit.Remove("Kind"); break;
                case "missing-time": audit.Remove("RecordedAt"); break;
                case "scope": audit["Scope"]!["TenantId"] = "other-tenant-canary"; break;
                case "target": audit["Target"]!["JobId"] = null; break;
                case "confirmation": audit["Confirmation"]!["Status"] = (int)JobCommandStatus.Conflict; break;
                case "version": audit["FormatVersion"] = 2; break;
                case "oversize": audit["Operation"]!["Reason"] = new string('x', 33 * 1024); break;
            }
            unavailableBytes = corruption == "legacy" ? null : corruption == "json" ? "protected-audit-canary{" : audit.ToJsonString();
            row.AuditJson = unavailableBytes; digest = row.Digest; result = row.ResultJson; await db.SaveChangesAsync();
        }
        var restarted = new EfJobStore(database.Contexts, new(100, 1000, 16));
        var unavailable = (await restarted.FindOperationAuditAsync(Scope, operation.OperationId))!;
        Assert.Equal(operation.OperationId, unavailable.OperationId); Assert.Null(unavailable.Audit);
        Assert.Equal(corruption == "legacy" ? "jobs.audit.legacy-unavailable" : "jobs.audit.record-unavailable", unavailable.UnavailableDiagnostic!.Code);
        Assert.DoesNotContain("canary", unavailable.UnavailableDiagnostic.Summary);
        var page = await restarted.ListOperationAuditsAsync(Scope, new(10, null));
        Assert.Equal(2, page.Items.Count); Assert.Single(page.Items, item => item.Audit is not null); Assert.Single(page.Items, item => item.Audit is null);
        Assert.Equal(JobCommandStatus.NotFound, (await restarted.CancelAsync(request)).Status);
        Assert.Null(await restarted.FindOperationAuditAsync(Scope, "no-such-operation"));
        await using var check = await database.Contexts.CreateDbContextAsync();
        var retained = await check.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId));
        Assert.Equal(unavailableBytes, retained.AuditJson); Assert.Equal(digest, retained.Digest); Assert.Equal(result, retained.ResultJson);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Scoped_audit_pages_traverse_each_receipt_once_and_refuse_cross_scope_or_collection_cursors(string provider)
    {
        await using var database = await Database(provider);
        for (var i = 0; i < 3; i++) await database.Commands.SetCapacityAsync(new(Scope, new(JobGateKind.Queue, "queue-" + i), i, 0,
            new("operation-" + i, "operator", "Capacity administration.")));
        await database.Commands.SetCapacityAsync(new(Scope with { TenantId = "tenant-b" }, new(JobGateKind.Queue, "other-tenant"), 1, 0,
            new("operation-0", "other-tenant-operator", "Other tenant canary.")));
        var first = await database.Queries.ListOperationAuditsAsync(Scope, new(1, null)); Assert.NotNull(first.ContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListOperationAuditsAsync(Scope with { TenantId = "tenant-b" }, new(1, first.ContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListSchedulesAsync(Scope, new(1, first.ContinuationToken)).AsTask());
        var ids = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal); string? after = null;
        do
        {
            var page = await database.Queries.ListOperationAuditsAsync(Scope, new(1, after));
            var item = Assert.Single(page.Items); Assert.True(ids.Add(item.OperationId)); Assert.Equal("operator", item.Audit!.Operation.Actor);
            after = page.ContinuationToken; if (after is not null) Assert.True(cursors.Add(after));
        } while (after is not null);
        Assert.Equal(3, ids.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => database.Queries.ListOperationAuditsAsync(Scope, new(0, null)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => database.Queries.ListOperationAuditsAsync(Scope, new(1, "bnVsbA==")).AsTask());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Refused_commit_rolls_back_cancellation_and_its_audit_receipt_together(string provider)
    {
        await using var database = await Database(provider);
        var accepted = (await database.Commands.AdmitAsync(Admission("audit-rollback"))).Acceptance!;
        var operation = Op();
        var refusing = new EfJobStore(new InterceptedFactory(database.Contexts, new RefuseAuditCommit()), new(100, 1000, 1));
        await Assert.ThrowsAsync<JobPersistenceException>(() => refusing.CancelAsync(new(Scope, accepted.JobId, operation)).AsTask());
        var job = (await database.Queries.FindAsync(Scope, accepted.JobId))!;
        Assert.Equal(JobState.Accepted, job.State); Assert.False(job.CancellationRequested); Assert.Null(job.Result);
        Assert.Null(await database.Queries.FindOperationAuditAsync(Scope, operation.OperationId));
        await using var check = await database.Contexts.CreateDbContextAsync(); Assert.Empty(await check.Operations.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Every_valid_maximum_length_operation_and_target_fits_the_finite_audit_envelope(string provider)
    {
        await using var database = await Database(provider);
        var name = new string('\u0001', 255) + "x"; var scope = new JobScope(name, name);
        var operation = new JobOperation(name, name, new string('\u0001', 2048));
        var origin = new JobOrigin(name, name, name, new('a', 64));
        await database.Commands.CancelOriginAsync(new(scope, origin, new('b', 64), operation));
        var audit = (await database.Queries.FindOperationAuditAsync(scope, operation.OperationId))!.Audit!;
        Assert.Equal(operation, audit.Operation); Assert.Equal(origin, audit.Target.Origin);
        await using var db = await database.Contexts.CreateDbContextAsync();
        var row = await db.Operations.SingleAsync(); Assert.True(Encoding.UTF8.GetByteCount(row.AuditJson!) <= 32 * 1024);
    }

    [SkippableTheory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    public async Task Missing_capacity_limit_is_unavailable_and_cannot_impersonate_an_explicit_unlimited_decision(string provider)
    {
        await using var database = await Database(provider);
        var operation = new JobOperation("limited-capacity", "operator", "Apply a finite limit."); var gate = new JobGateKey(JobGateKind.Queue, "limited");
        await database.Commands.SetCapacityAsync(new(Scope, gate, 2, 0, operation));
        var unlimited = new JobOperation("unlimited-capacity", "operator", "Explicitly remove the limit.");
        await database.Commands.SetCapacityAsync(new(Scope, new(JobGateKind.Queue, "unlimited"), null, 0, unlimited));
        var explicitUnlimited = (await database.Queries.FindOperationAuditAsync(Scope, unlimited.OperationId))!;
        Assert.Null(Assert.IsType<JobOperationAudit>(explicitUnlimited.Audit).Target.CapacityLimit);
        string originalBytes;
        await using (var db = await database.Contexts.CreateDbContextAsync())
        {
            var row = await db.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId));
            var audit = JsonNode.Parse(row.AuditJson!)!.AsObject(); audit["Target"]!.AsObject().Remove("CapacityLimit");
            row.AuditJson = originalBytes = audit.ToJsonString(); await db.SaveChangesAsync();
        }
        var unavailable = (await database.Queries.FindOperationAuditAsync(Scope, operation.OperationId))!;
        Assert.Null(unavailable.Audit); Assert.Equal("jobs.audit.record-unavailable", unavailable.UnavailableDiagnostic!.Code);
        Assert.Equal(JobCommandStatus.Replayed, (await database.Commands.SetCapacityAsync(new(Scope, gate, 2, 0, operation))).Status);
        Assert.Equal(2, (await database.Queries.ListCapacityAsync(Scope, new(10, null))).Items.Single(item => item.Gate == gate).Limit);
        await using var check = await database.Contexts.CreateDbContextAsync();
        Assert.Equal(originalBytes, (await check.Operations.SingleAsync(item => item.Key == JobStoreCodec.Key(Scope, "operation", operation.OperationId))).AuditJson);
    }

    private sealed record PreparedAuditCommand(JobOperationTarget Target, Func<IJobCommands, JobOperation, string, Task<JobTransition>> Invoke);
    private static async Task<PreparedAuditCommand> PrepareAuditedCommand(JobsProviderDatabase database, JobOperationKind kind)
    {
        var gate = new JobGateKey(JobGateKind.WorkflowDefinition, "same-definition", "native");
        var request = Request() with { Input = Payload(Handler.InputSchema, new { Value = "AUDIT_INPUT_PAYLOAD_CANARY" }) };
        switch (kind)
        {
            case JobOperationKind.Cancel:
            {
                var job = (await database.Commands.AdmitAsync(Admission("cancel-audit", request))).Acceptance!;
                return new(new(JobId: job.JobId), (commands, operation, _) => commands.CancelAsync(new(Scope, job.JobId, operation)).AsTask());
            }
            case JobOperationKind.CancelOrigin:
            {
                var origin = new JobOrigin("native", "parent", "invocation", new('a', 64));
                return new(new(Origin: origin), (commands, operation, _) => commands.CancelOriginAsync(new(Scope, origin, JobRequestDigest.Compute(request), operation)).AsTask());
            }
            case JobOperationKind.RetryAsNewRun:
            {
                var job = (await database.Commands.AdmitAsync(Admission("retry-audit", request))).Acceptance!;
                await database.Commands.CancelAsync(new(Scope, job.JobId, Op()));
                var terminal = (await database.Queries.FindAsync(Scope, job.JobId))!;
                return new(new(JobId: job.JobId, ExpectedRevision: terminal.Revision), async (commands, operation, proposal) =>
                {
                    var result = await commands.RetryAsNewRunAsync(new(Scope, job.JobId, terminal.Revision, proposal, "retry-audit-new-key", operation));
                    return new(result.Status, result.Acceptance?.Revision, result.Acceptance?.JobId, null);
                });
            }
            case JobOperationKind.Pause:
                return new(new(Gate: gate, HoldId: "audit-hold"), (commands, operation, _) => commands.PauseAsync(new(Scope, gate, "audit-hold", operation)).AsTask());
            case JobOperationKind.ReleaseHold:
                await database.Commands.PauseAsync(new(Scope, gate, "audit-hold", Op()));
                return new(new(Gate: gate, HoldId: "audit-hold", ExpectedRevision: 1), (commands, operation, _) => commands.ReleaseHoldAsync(new(Scope, gate, "audit-hold", 1, operation)).AsTask());
            case JobOperationKind.SetCapacity:
                return new(new(Gate: gate, ExpectedRevision: 0, CapacityLimit: 2), (commands, operation, _) => commands.SetCapacityAsync(new(Scope, gate, 2, 0, operation)).AsTask());
            case JobOperationKind.SaveSchedule:
            case JobOperationKind.SetScheduleEnabled:
            {
                var definition = new JobScheduleDefinition("audit-schedule", request, new(JobRecurrenceKind.Interval, "01:00:00"), DateTimeOffset.UtcNow.AddDays(1), JobMissedOccurrencePolicy.CoalesceOne);
                if (kind == JobOperationKind.SaveSchedule)
                    return new(new(ScheduleId: definition.ScheduleId, ExpectedRevision: 0), (commands, operation, _) => commands.SaveScheduleAsync(new(Scope, definition, 0, operation)).AsTask());
                await database.Commands.SaveScheduleAsync(new(Scope, definition, 0, Op()));
                return new(new(ScheduleId: definition.ScheduleId, ExpectedRevision: 1, Enabled: false), (commands, operation, _) => commands.SetScheduleEnabledAsync(new(Scope, definition.ScheduleId, false, 1, operation)).AsTask());
            }
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static async Task VerifyAuditedEffect(IJobQueries queries, JobOperationAudit audit)
    {
        switch (audit.Kind)
        {
            case JobOperationKind.Cancel:
                Assert.True((await queries.FindAsync(Scope, audit.Target.JobId!))!.CancellationRequested); break;
            case JobOperationKind.CancelOrigin:
                Assert.True((await queries.FindOriginAsync(Scope, audit.Target.Origin!))!.Cancelled); break;
            case JobOperationKind.RetryAsNewRun:
                Assert.Equal(audit.Target.JobId, (await queries.FindAsync(Scope, audit.Confirmation.JobId!))!.RetriedFromJobId); break;
            case JobOperationKind.Pause:
            case JobOperationKind.ReleaseHold:
            {
                var hold = Assert.Single((await queries.ListHoldsAsync(Scope, audit.Target.Gate, new(10, null))).Items);
                Assert.Equal(audit.Kind == JobOperationKind.ReleaseHold, hold.ReleasedAt is not null); break;
            }
            case JobOperationKind.SetCapacity:
                Assert.Equal(2, Assert.Single((await queries.ListCapacityAsync(Scope, new(10, null))).Items).Limit); break;
            case JobOperationKind.SaveSchedule:
                Assert.NotNull((await queries.FindScheduleAsync(Scope, audit.Target.ScheduleId!))!.Definition); break;
            case JobOperationKind.SetScheduleEnabled:
                Assert.False((await queries.FindScheduleAsync(Scope, audit.Target.ScheduleId!))!.Enabled); break;
            default: throw new ArgumentOutOfRangeException(nameof(audit));
        }
    }

    private sealed class RefuseAuditCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new LostCommitReplyException();
    }
}
