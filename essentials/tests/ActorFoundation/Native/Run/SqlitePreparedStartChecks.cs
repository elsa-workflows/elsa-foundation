using System.Data.Common;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class SqlitePreparedStartChecks
{
    private const string ActorType = "held-root-material-fixture";

    internal static async Task<int> RunAsync(bool fenceInBatch, bool shipped = false)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = budget.Token;
        var scope = ContractScopes.Bind("default");
        var folder = Directory.CreateTempSubdirectory(ActorType);
        try
        {
            var offered = new TaskCompletionSource<PendingRootStart>(TaskCreationOptions.RunContinuationsAsynchronously);
            var refused = new TaskCompletionSource<PendingRootStart>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waitingToLose = new TaskCompletionSource<PendingRootStart>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var expired = new TaskCompletionSource<PendingRootStart>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expiredProtection = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var oneWayPrepared = new TaskCompletionSource<PendingRootStart>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowOneWay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var path = Path.Combine(folder.FullName, "held.db");
            var foundation = OpenFoundation();
            StoreFormat? format = shipped ? null : StoreFormat.RootPreparedStartsQualification;
            await foundation.InstallShippedAsync(token);
            if (!shipped) await CheckDeadlineAfterEnqueueAsync(path, foundation, scope, token);
            var ingress = await foundation.OpenIngressAsync(token);
            var oneWay = foundation.BindProtectedRootStarts(scope);
            var oneWaySpec = new Contract.StartSpec(ActorType, "one-way");
            const string oneWayOperation = "protected-one-way";
            var ordinary = await ingress.SendAsync(scope, new Contract.Target.Start(oneWaySpec), oneWayOperation,
                Contract.Kinds.Start, cancellationToken: token);
            var pending = oneWay.StartAsync(oneWayOperation, oneWaySpec, token).AsTask();
            if (await Task.WhenAny(pending, oneWayPrepared.Task).WaitAsync(token) == pending)
                throw new InvalidOperationException("The one-way call completed before material protection.");
            var oneWayOffer = await oneWayPrepared.Task;
            Require(await foundation.ConfirmPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token) == RootStartProof.Unresolved &&
                    !await foundation.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token) &&
                    await foundation.CreateReads().GetAsync(scope, oneWayOffer.Actor, token) is null,
                "a committed preparation alone provides no positive birth proof");
            allowOneWay.SetResult();
            var accepted = await pending;
            Require(ordinary is { Outcome: Contract.AdmissionOutcome.Accepted } && ordinary.Actor != oneWayOffer.Actor &&
                    accepted is { Outcome: Contract.AdmissionOutcome.Accepted } && accepted.Actor == oneWayOffer.Actor &&
                    await HasOneWayCreatorAsync(path, format, oneWayOffer, token) &&
                    await foundation.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token),
                "one-way start returns its own durable creator without a held reply or a running owner");
            await using (var proof = Open(path, format))
            {
                var wrongDigest = await PreparedBirthProofWitness.DifferentDigestAsync(proof, scope, oneWayOffer, token);
                Require(!await foundation.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, wrongDigest, token) &&
                        !await foundation.HasAcceptedPreparedStartAsync(ContractScopes.Bind("other"), oneWayOffer.Actor, oneWayOffer.Issuance, token) &&
                        !await foundation.HasAcceptedPreparedStartAsync(scope, ordinary.Actor!.Value, oneWayOffer.Issuance, token),
                    "positive birth proof requires the signed fingerprint, actor and tenant of the accepted birth");
            }
            Require(await oneWay.StartAsync(oneWayOperation, oneWaySpec, token) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } replay } && replay == oneWayOffer.Actor,
                "one-way duplicate returns its exact durable receipt without repinning");
            Require(await oneWay.StartAsync("one-way-wrong-type", oneWaySpec with { Type = "other-model" }, token) is
                    { Outcome: Contract.AdmissionOutcome.Refused, Actor: null }, "one-way start is bound to the configured actor type");
            Require(calls == 1, "the one-way receipt and wrong-type refusal make no additional protection offers");
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, ForeignKeys = true, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(token);
                await using var writer = connection.BeginTransaction(deferred: false);
                var contended = oneWay.StartAsync("one-way-contended", oneWaySpec, token).AsTask();
                using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
                cancelled.CancelAfter(TimeSpan.FromMilliseconds(150));
                try
                {
                    await oneWay.StartAsync("one-way-cancelled", oneWaySpec, cancelled.Token).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(4), token);
                    throw new InvalidOperationException("A canceled one-way start returned an admission.");
                }
                catch (OperationCanceledException) when (cancelled.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    Console.WriteLine("ok one-way caller cancellation remains cancellation under SQLite contention");
                }
                Require(await contended.WaitAsync(RootPreparedStarts.OneWayBudget + TimeSpan.FromSeconds(4), token) is
                        { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy, RetryAfter: { }, Actor: null } &&
                        Volatile.Read(ref calls) == 1,
                    "the one-way budget bounds contended SQLite BEGIN IMMEDIATE retries before preparation");
            }

            await using var owner = foundation.CreateHostedServiceWithAuxiliaryActor(new TerminalActor(), new Kernel.WorkflowActor.ScheduleActor(),
                reason => throw new InvalidOperationException(reason), _ => TimeSpan.FromMilliseconds(1), fenceInBatch: fenceInBatch);
            await owner.StartAsync(token);

            var request = CallAsync("same-operation", "workflow", 20_000);
            if (await Task.WhenAny(request, offered.Task).WaitAsync(token) == request)
                throw new InvalidOperationException("The held request completed before preparation: " + (await request).Error);
            var first = await offered.Task;
            Require(first.Tenant == scope.Tenant && first.Type == ActorType && first.TypeData == "workflow" &&
                    await foundation.ConfirmPreparedStartAsync(scope, first.Actor, first.Issuance, token) == RootStartProof.Unresolved &&
                    await foundation.CreateReads().GetAsync(scope, first.Actor, token) is null,
                "committed preparation can be read while protection runs and publishes no actor before it returns");

            release.SetResult();
            Require(await request is { Channel: Request.Response, Item: "started", Error: null, Actor: { } actor } && actor == first.Actor,
                "the held caller receives its exact protected birth after the owner commits");
            Require(await foundation.ConfirmPreparedStartAsync(scope, first.Actor, first.Issuance, token) == RootStartProof.Unresolved,
                "a finished root keeps its protection before terminal retirement");
            var duplicate = await CallAsync("same-operation", "workflow", 600);
            Require(duplicate.Error == "timeout" && Volatile.Read(ref calls) == 2,
                $"a live operation receipt bypasses new protection and keeps the legacy duplicate reply behavior ({duplicate.Error}, {Volatile.Read(ref calls)} protection calls)");

            var refusalReply = await CallAsync("refused-operation", "refuse", 10_000);
            var second = await refused.Task.WaitAsync(token);
            Require(refusalReply.RefusalReason == Contract.RefusalReason.TargetMismatch &&
                    await foundation.ConfirmPreparedStartAsync(scope, second.Actor, second.Issuance, token) == RootStartProof.PreparationDisarmed &&
                    !await foundation.HasAcceptedPreparedStartAsync(scope, second.Actor, second.Issuance, token),
                "a definitive protection refusal disarms the exact preparation without an actor birth");

            var competing = CallAsync("competing-operation", "loses", 2_500);
            if (await Task.WhenAny(competing, waitingToLose.Task).WaitAsync(token) == competing)
                throw new InvalidOperationException("The competing request did not enter protection.");
            var loser = await waitingToLose.Task;
            Require((await ingress.SendAsync(scope, new Contract.Target.Start(new(ActorType, "winner")),
                        "competing-operation", Contract.Kinds.Start, cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "ordinary ingress can win the operation while the protected caller waits");
            lose.SetResult();
            Require(await competing is { Error: "timeout", RefusalReason: null } &&
                    await foundation.ConfirmPreparedStartAsync(scope, loser.Actor, loser.Issuance, token) == RootStartProof.PreparationDisarmed,
                "the refused preparation replays its live winning receipt without attaching a caller");

            var expiredCall = CallAsync("expired-operation", "expire", 1_500);
            if (await Task.WhenAny(expiredCall, expired.Task).WaitAsync(token) == expiredCall)
                throw new InvalidOperationException("The expiry witness did not enter protection.");
            var late = await expired.Task;
            Require((await expiredCall).Error == "timeout", "the held deadline expires while material protection is pending");
            expiredProtection.TrySetResult(true);
            while (await foundation.PrunePreparedStartsAsync(scope, token) == 0) await Task.Delay(20, token);
            Require(await foundation.ConfirmPreparedStartAsync(scope, late.Actor, late.Issuance, token) == RootStartProof.PreparationDisarmed &&
                    !await foundation.HasAcceptedPreparedStartAsync(scope, late.Actor, late.Issuance, token),
                "pruning durably disarms an expired preparation before reporting absence");

            var policy = await ((Contract.IActorRetentionPolicies)foundation).TryApplyRetentionPolicyAsync(
                scope, 0, new(Finished: TimeSpan.Zero), long.MaxValue, cancellationToken: token);
            Require(policy.Outcome == Contract.ActorRetentionPolicyChangeOutcome.Applied, "the host configures eligibility for terminal retirement");
            var terminal = await foundation.CreateReads().GetAsync(scope, first.Actor, token);
            Require(terminal?.Selection is not null, "the committed root exposes an exact retirement selection");
            Require((await foundation.AdmitRetentionDeleteAsync(scope, first.Actor, terminal!.Selection!, token)).Outcome ==
                    Contract.AdmissionOutcome.Accepted, "ordinary retention admits deletion of the protected root");
            while (await foundation.ConfirmPreparedStartAsync(scope, first.Actor, first.Issuance, token) != RootStartProof.BirthRetired)
                await Task.Delay(25, token);
            Require(await foundation.ConfirmPreparedStartAsync(ContractScopes.Bind("other"), first.Actor, first.Issuance, token) ==
                    RootStartProof.Unresolved, "retirement proof stays bound to the original tenant");
            var oneWayTerminal = await foundation.CreateReads().GetAsync(scope, oneWayOffer.Actor, token);
            Require(oneWayTerminal is { Header.Status: Contract.ActorStatus.Finished, Selection: { } } &&
                    await foundation.ConfirmPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token) == RootStartProof.Unresolved,
                "the one-way creator finishes on the owner and remains protected until retirement");
            await using (var proof = Open(path, format))
                await PreparedBirthProofWitness.ExpireReceiptAsync(proof, scope, oneWayOffer, token);
            Require(await foundation.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token),
                "the exact birth still proves commitment after its ordinary operation receipt expires and is removed");
            Require((await foundation.AdmitRetentionDeleteAsync(scope, oneWayOffer.Actor, oneWayTerminal!.Selection!, token)).Outcome ==
                    Contract.AdmissionOutcome.Accepted, "ordinary retention admits deletion of the exact one-way root");
            while (await foundation.ConfirmPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token) != RootStartProof.BirthRetired)
                await Task.Delay(25, token);
            Require(await foundation.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token),
                "the retained birth survives terminal actor retirement and receipt removal");
            await owner.StopAsync(token);
            Require(owner.LastError is null, "the SQLite owner stops cleanly with prepared starts installed");
            var reopened = OpenFoundation();
            await reopened.InstallShippedAsync(token);
            Require(await reopened.HasAcceptedPreparedStartAsync(scope, oneWayOffer.Actor, oneWayOffer.Issuance, token),
                "a fresh SQLite Foundation can prove the same retired birth after the owner stops");
            return 0;

            SqliteEmbeddedFoundation OpenFoundation() => shipped
                ? SqliteEmbeddedFoundation.WithRootStartProtection(path, "prepared-start-owner", ActorType, ProtectAsync)
                : SqliteEmbeddedFoundation.ForPreparedStartsQualification(path, "prepared-start-owner", ActorType, ProtectAsync);

            async ValueTask<bool> ProtectAsync(PendingRootStart start, CancellationToken ct)
            {
                Interlocked.Increment(ref calls);
                if (start.TypeData == "one-way")
                {
                    oneWayPrepared.SetResult(start);
                    await allowOneWay.Task.WaitAsync(ct);
                    return true;
                }
                if (start.TypeData == "refuse")
                {
                    refused.SetResult(start);
                    return false;
                }
                if (start.TypeData == "expire")
                {
                    expired.SetResult(start);
                    return expiredProtection.Task.GetAwaiter().GetResult();
                }
                if (start.TypeData == "loses")
                {
                    waitingToLose.SetResult(start);
                    await lose.Task.WaitAsync(ct);
                    return false;
                }
                offered.SetResult(start);
                await release.Task.WaitAsync(ct);
                return true;
            }

            async Task<Contract.Reply> CallAsync(string operation, string definition, int milliseconds)
            {
                var deadline = await foundation.ReadClockAsync(token) + milliseconds;
                return await ingress.RequestAsync(scope, new Contract.Target.Start(new(ActorType, definition)), operation,
                    Contract.Kinds.Start, null, null, new(null, deadline), token);
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private static KernelDb Open(string path, StoreFormat? format)
    {
        var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = path, ForeignKeys = true, Pooling = false }.ToString()).Options;
        return KernelDb.ForSqliteRootShipped(options, format);
    }

    private static async Task<bool> HasOneWayCreatorAsync(string path, StoreFormat? format, PendingRootStart offer, CancellationToken token)
    {
        await using var db = Open(path, format);
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "FoundationRootBirths" b JOIN "Messages" m ON m."Id" = b."Creator"
                WHERE b."Tenant" = {offer.Tenant} AND b."Reference" = {offer.Actor.Id} AND m."ReplyTo" IS NULL)
                AND NOT EXISTS (SELECT 1 FROM "FoundationPreparedRootStarts" WHERE "Reference" = {offer.Actor.Id}) AS "Value"
            """).SingleAsync(token);
    }

    private static async Task CheckDeadlineAfterEnqueueAsync(string path, SqliteEmbeddedFoundation foundation, Contract.BoundScope scope,
        CancellationToken token)
    {
        var deadline = await foundation.ReadClockAsync(token) + 2_000;
        var pause = new WaitForEnqueueDeadline(deadline);
        var options = new DbContextOptionsBuilder<KernelDb>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString())
            .AddInterceptors(pause).Options;
        KernelDb Open() => KernelDb.ForSqliteRootShipped(options, StoreFormat.RootPreparedStartsQualification);
        var admission = new SqliteRootAdmission(Open, TimeSpan.FromHours(1), new());
        var caller = new Requests().OpenBound("deadline-during-enqueue", deadline, scope.Tenant, null);
        var request = new HeldRootRequest(scope.Tenant, "prepared-start-owner", caller, ReleaseBeforeCommit: false, KeepFor: null);
        var spec = new StartSpec(ActorType, "deadline");
        var prepared = await admission.PrepareProtectedStartAsync(request.Tenant, caller.Id, spec, request, null, token);
        var stage = prepared.Stage ?? throw new InvalidOperationException("The deadline witness did not prepare an exact root.");
        var offer = prepared.Offer ?? throw new InvalidOperationException("The deadline witness did not issue a protection offer.");
        var result = await admission.StartProtectedAsync(request.Tenant, caller.Id, spec, request, token, null, stage);
        await using var db = Open();
        var rolledBack = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "FoundationPreparedRootStarts" WHERE "Reference" = {stage.Reference})
                AND NOT EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Reference" = {stage.Reference})
                AND NOT EXISTS (SELECT 1 FROM "FoundationRootReceipts" WHERE "Operation" = {stage.Operation})
                AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {stage.Actor}) AS "Value"
            """).SingleAsync(token);
        Require(result is null && pause.Reached && rolledBack,
            "a protected held start crossing the SQLite deadline after enqueue rolls back the birth, receipt, creator and stage consumption");
        Require(await foundation.ConfirmPreparedStartAsync(scope, offer.Actor, offer.Issuance, token) == RootStartProof.PreparationDisarmed,
            "the expired preparation can subsequently be disarmed and durably proved absent");
    }

    private sealed class WaitForEnqueueDeadline(long deadline) : DbCommandInterceptor
    {
        internal bool Reached { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("INSERT INTO \"Messages\"", StringComparison.Ordinal))
                return result;
            await using var clock = command.Connection!.CreateCommand();
            clock.Transaction = command.Transaction;
            clock.CommandText = "SELECT " + SqliteCommitDialect.MillisecondClock;
            while ((long)(await clock.ExecuteScalarAsync(cancellationToken))! < deadline)
                await Task.Delay(15, cancellationToken);
            Reached = true;
            return result;
        }
    }

    private static void Require(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class TerminalActor : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            turn.State = "\"started\"";
            turn.Emit(Request.Response, "started");
            turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}
