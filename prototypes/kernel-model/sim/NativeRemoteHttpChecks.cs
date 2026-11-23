using System.Net;
using System.Net.Http.Json;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class NativeHttpEdgeChecks
{
    private enum OwnerLoss { BeforeCommit, BeforeCommitPastDeadline, AfterCommit }

    internal static async Task<int> RunRemoteAsync(Flaw flaws)
    {
        foreach (var loss in Enum.GetValues<OwnerLoss>())
        {
            Console.WriteLine(loss);
            await CheckRemoteAsync(loss, flaws);
        }
        await CheckTenantRoutingAsync(flaws);
        return 0;
    }

    private static async Task CheckRemoteAsync(OwnerLoss loss, Flaw flaws)
    {
        const long initial = 1_703_400_000_000;
        const long requestTime = 20_000;
        const string receiver = "http-receiver";
        const string firstNode = "http-first-owner";
        const string nextNode = "http-next-owner";
        var lease = TimeSpan.FromSeconds(2);
        long timestamp = initial;
        long Now() => Interlocked.Read(ref timestamp);
        var store = new InMemoryStore(1, Now, flaws);
        var reads = new ActorList(store, 16);
        var scope = new Contract.BoundScope("http-remote");
        var requests = new Requests(receiver, flaws);
        var transport = new LocalTransport(receiver, requests);
        var originalLink = new DisconnectableReplyTransport(transport);
        var ingress = new InMemoryIngress(store, requests, receiver);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("approval", Wf.Sequence("main",
            Wf.HttpEndpoint("start", "begin"),
            Wf.Respond("start-response", "created", 201),
            Wf.HttpEndpoint("resume", "approve"),
            Wf.Respond("approval-response", Binding.Result("resume", "Body.name")))));
        var routes = new HttpRoutes(registry);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var entering = new TaskCompletionSource<CommitBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalTerminalCommits = 0;
        var original = new CountingStore(store, () => { }, beforeCommit: async batch =>
        {
            if (FinishesApproval(batch) && entering.TrySetResult(batch))
                await release.Task.WaitAsync(token);
        }, observeCommit: (batch, landed) =>
        {
            if (!landed || !FinishesApproval(batch)) return;
            Interlocked.Increment(ref originalTerminalCommits);
            if (loss != OwnerLoss.AfterCommit) return;
            originalLink.Disconnect();
            throw new StoreException(Failure.Unknown, "The terminal commit landed and its answer was lost.");
        });

        var owner = NewOwner(firstNode, original, lease, originalLink);
        await using var first = await new DurableHost([owner]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The initial remote owner could not activate.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        Task<IResult> HandleAsync(HttpContext context) => DispatchAsync(context, scope, routes, ingress, Now() + requestTime, token);
        app.MapPost("/{**route}", (Delegate)HandleAsync);
        await app.StartAsync(token);
        var pump = PumpOnceOwnedAsync(owner, token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            using (var response = await client.PostAsync("begin", null, token))
                Require(response.StatusCode == HttpStatusCode.Created && transport.Responses == 1,
                    "The edge receives the initial 201 from a remote logical owner.");

            var approval = (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items.Single();
            var born = await reads.TailAsync(scope, approval.Header.Ref, cancellationToken: token);
            Require(born.Next is not null && born.Items.Count > 0 && born.Items.All(entry => entry.Revision == 1),
                "The original approval has a committed wait and journal revision 1.");
            var originalEpoch = owner.Epoch;
            var answer = client.PostAsJsonAsync("approve", new { Name = "Ada" }, token);
            var pending = await entering.Task.WaitAsync(token);
            var request = requests.All.SingleOrDefault() ?? throw new InvalidOperationException("The receiver lost its registration before commit.");
            var turn = pending.Turns.Single(turn => turn.Row?.Status == InstanceStatus.Finished);
            var address = turn.Consumed.Concat(turn.Done).Select(key => store.Messages[key].Message.ReplyTo).Single(value => value is not null);
            Require(ReplyTo.Parse(address!) == (receiver, request.Registration, request.Deadline, false) &&
                    requests.IsOpen(request) && !answer.IsCompleted && transport.Responses == 1 &&
                    await reads.GetAsync(scope, approval.Header.Ref, token) is { Header.Status: Contract.ActorStatus.Running },
                "The durable input addresses the original receiver and the response stays held until commit.");

            if (loss == OwnerLoss.AfterCommit)
            {
                release.SetResult();
                await pump.WaitAsync(token);
                var status = (await reads.GetAsync(scope, approval.Header.Ref, token))?.Header.Status;
                var open = requests.IsOpen(request);
                Require(!owner.Owns && open && !answer.IsCompleted && transport.Responses == 1 && status == Contract.ActorStatus.Finished,
                    $"After the lost commit answer: owner active {owner.Owns}, actor {status}, receiving registration open {open}, HTTP complete {answer.IsCompleted}, responses {transport.Responses}, end {request.Error}.");
            }

            originalLink.Disconnect();
            var takenAt = Interlocked.Add(ref timestamp, (long)lease.TotalMilliseconds + 1);
            if (takenAt >= request.Deadline)
                throw new InvalidOperationException($"The ownership clock advanced past the edge deadline in {loss}.");
            var successor = NewOwner(nextNode, store, TimeSpan.FromMinutes(1), transport);
            await using var next = await new DurableHost([successor]).ActivateAsync(0, token)
                ?? throw new InvalidOperationException("The successor could not activate the partition.");
            Require(successor.Epoch > originalEpoch, "The successor takes the same partition at a higher epoch.");

            if (loss == OwnerLoss.BeforeCommitPastDeadline)
                ExpireAtEdge();
            while (await reads.GetAsync(scope, approval.Header.Ref, token) is { Header.Status: Contract.ActorStatus.Running })
            {
                if (!successor.Owns) throw new InvalidOperationException("The successor lost ownership before applying the approval.");
                await DriveAsync(successor, token);
                await Task.Delay(5, token);
            }
            await DriveAsync(successor, token);
            var terminal = await reads.TailAsync(scope, approval.Header.Ref, born.Next, token);
            Require(await reads.GetAsync(scope, approval.Header.Ref, token) is { Header.Status: Contract.ActorStatus.Finished } &&
                    terminal.Next is not null && terminal.Items.Count > 0 && terminal.Items.All(entry => entry.Revision == 2) &&
                    (await reads.TailAsync(scope, approval.Header.Ref, terminal.Next, token)).Items.Count == 0,
                "The original approval finishes with exactly one additional checkpoint revision.");

            if (loss == OwnerLoss.AfterCommit)
            {
                Require(requests.IsOpen(request) && !answer.IsCompleted, "The successor leaves the remote registration open until its deadline.");
                ExpireAtEdge();
            }
            using (var response = await answer)
            {
                if (loss == OwnerLoss.BeforeCommit)
                    Require(response.StatusCode == HttpStatusCode.OK && await response.Content.ReadAsStringAsync(token) == "Ada" &&
                            request is { Responses: 1, Ends: 0 } && transport.Responses == 2,
                        "The successor answers the original HTTP request once using its durable reply address.");
                else
                    Require(response.StatusCode == HttpStatusCode.GatewayTimeout && request is { Responses: 0, Ends: 1, Error: "timeout" } &&
                            transport.Responses == 1,
                        "The original connection returns one 504 and receives no replayed or late response.");
            }

            release.TrySetResult();
            await pump.WaitAsync(token);
            await DriveAsync(successor, token);
            var final = await reads.TailAsync(scope, approval.Header.Ref, born.Next, token);
            Require(!owner.Owns && terminal.Items.SequenceEqual(final.Items) && transport.WrongDestination is null &&
                    (await reads.TailAsync(scope, approval.Header.Ref, terminal.Next, token)).Items.Count == 0 &&
                    Volatile.Read(ref originalTerminalCommits) == (loss == OwnerLoss.AfterCommit ? 1 : 0) &&
                    transport.Responses == (loss == OwnerLoss.BeforeCommit ? 2 : 1),
                "The expired owner leaves the successor's journal and the edge's outcome intact.");

            void ExpireAtEdge()
            {
                Interlocked.Exchange(ref timestamp, request.Deadline);
                requests.Expire(Now());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new InvalidOperationException($"The remote HTTP check timed out in {loss}.");
        }
        finally
        {
            release.TrySetResult();
            deadline.Cancel();
            await pump;
            await app.StopAsync();
        }

        Worker NewOwner(string node, IKernelStore ownedStore, TimeSpan ownerLease, IReplyTransport replyTransport) =>
            new(node, 0, ownedStore, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)]),
                (new Requests(node, flaws), node)),
                new WorkerOptions { Lease = ownerLease, Monotonic = () => Now() - initial, Transport = replyTransport, Flaws = flaws });
    }

    // The receiver stays alive when the original owner disappears. The old Worker may still run
    // cleanup locally, which also lets the test observe its stale commit without delivering its replies.
    private sealed class DisconnectableReplyTransport(IReplyTransport receiver) : IReplyTransport
    {
        private volatile bool _disconnected;

        internal void Disconnect() => _disconnected = true;

        public void Send(string node, Reply reply)
        {
            if (!_disconnected) receiver.Send(node, reply);
        }
    }

    private static bool FinishesApproval(CommitBatch batch) => batch.Turns.Any(turn => turn.Row?.Status == InstanceStatus.Finished);

    private static async Task PumpOnceOwnedAsync(Worker owner, CancellationToken token)
    {
        try
        {
            while (owner.Owns && !token.IsCancellationRequested)
            {
                await DriveAsync(owner, token);
                await Task.Delay(5, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task DriveAsync(Worker owner, CancellationToken token)
    {
        await owner.WokenAsync(null, token);
        await owner.RunAsync(token);
    }
}
