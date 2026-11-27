using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class SqlitePreparedStartChecks
{
    private const string ActorType = "held-root-material-fixture";

    internal static async Task<int> RunAsync(bool fenceInBatch)
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
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var foundation = SqliteEmbeddedFoundation.ForPreparedStartsQualification(
                Path.Combine(folder.FullName, "held.db"), "prepared-start-owner", ActorType, ProtectAsync);
            await foundation.InstallShippedAsync(token);
            var ingress = await foundation.OpenIngressAsync(token);
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
            Require(duplicate.Error == "timeout" && Volatile.Read(ref calls) == 1,
                "a live operation receipt bypasses new protection and keeps the legacy duplicate reply behavior");

            var refusalReply = await CallAsync("refused-operation", "refuse", 10_000);
            var second = await refused.Task.WaitAsync(token);
            Require(refusalReply.RefusalReason == Contract.RefusalReason.TargetMismatch &&
                    await foundation.ConfirmPreparedStartAsync(scope, second.Actor, second.Issuance, token) == RootStartProof.PreparationDisarmed,
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
            Require(await foundation.ConfirmPreparedStartAsync(scope, late.Actor, late.Issuance, token) == RootStartProof.PreparationDisarmed,
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
            await owner.StopAsync(token);
            Require(owner.LastError is null, "the SQLite owner stops cleanly with prepared starts installed");
            return 0;

            async ValueTask<bool> ProtectAsync(PendingRootStart start, CancellationToken ct)
            {
                Interlocked.Increment(ref calls);
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
