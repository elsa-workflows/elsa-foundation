using System.Net;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class NativeHttpEdgeChecks
{
    /// <summary>
    /// Design card 2's acceptance case: a SendHttpRequest job re-run after an owner loss sends the same Idempotency-Key.
    /// Owner A starts the workflow and its job's request blocks at the partner; A stops without releasing its partition;
    /// owner B takes it when A's lease expires, re-runs the job from the committed ticket, and the partner answers.
    /// </summary>
    private static async Task CheckOwnerLossRerunAsync(string mode, Action<bool, string> check, CancellationToken token)
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, _) =>
        {
            if (first.TrySetResult()) await release.Task;     // the first attempt hangs while its owner is lost
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = new NativeHttpOptions { Client = new HttpClient(handler) };
        var registry = new WorkflowRegistry().Add(Wf.Workflow("rerun", Wf.Sequence("main",
            Wf.Http("send", "https://partner.example/rerun", timeout: TimeSpan.FromMinutes(5)).Capture("response"))));
        var store = new InMemoryStore(1, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var scope = ContractScopes.Bind("http-rerun");
        var lease = TimeSpan.FromMilliseconds(500);
        Worker Owner(string node)
        {
            var requests = new Requests(node);
            return new Worker(node, 0, store, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry, new NativeExecutionOptions { Http = http })]), (requests, node)),
                new WorkerOptions { Lease = lease, Transport = new LocalTransport(node, requests) });
        }

        var a = Owner("rerun-a");
        using var stopA = CancellationTokenSource.CreateLinkedTokenSource(token);
        var activationA = await new DurableHost([a]).ActivateAsync(0, token) ?? throw new InvalidOperationException("Owner A could not activate.");
        var pumpA = RunOwnerAsync(a, stopA.Token);
        await new InMemoryIngress(store, new Requests("rerun-a"), "rerun-a").SendAsync(scope,
            new Contract.Target.Start(PublicWorkflow.Start(NativeExecutionModel.Start("rerun", mode))), $"rerun-{mode}", Contract.Kinds.Start, cancellationToken: token);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        stopA.Cancel();     // owner A is lost: it stops taking turns and does not release its partition
        await pumpA;

        var b = Owner("rerun-b");
        using var stopB = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pumpB = RunOwnerAsync(b, stopB.Token);
        var reads = new ActorList(store, 16);
        Contract.ActorView? finished = null;
        for (var waited = 0; finished is null && waited < 200; waited++)
        {
            finished = (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Finished), cancellationToken: token)).Items.SingleOrDefault();
            if (finished is null) await Task.Delay(50, token);
        }
        release.TrySetResult();     // the lost attempt answers late; its owner is fenced
        stopB.Cancel();
        await pumpB;
        await activationA.DisposeAsync();
        var keys = handler.Sent.Select(sent => sent.Headers.GetValueOrDefault(SendHttpRequest.IdempotencyKeyHeader)).ToArray();
        check(finished is not null && keys is [{ } key, var again] && key == again && key.EndsWith($"/send/{EffectKey.RootPath}/0", StringComparison.Ordinal),
            $"{mode}: a job re-run after its owner was lost sends the same Idempotency-Key, and the workflow finishes on the new owner ({keys.Length} sends, finished={finished is not null})");
    }
}
