using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.Native;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private sealed record CapturedPublicStart(string Wait, string Registration, string Start, string Payload);

    internal static async Task<int> RunPublicStartsAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = stop.Token;
        var scope = ContractScopes.Bind("public-events");
        var other = ContractScopes.Bind("public-other");
        var registry = new WorkflowRegistry().Add(Producer("publish", "Orders", "payload"));
        var model = new PublicWorkflow([new NativeExecutionModel(registry, new() { AuthoredEventStarts = true })]);
        const string key = "event:Orders";
        var directory = Directory.CreateTempSubdirectory("public-event-starts");
        try
        {
            var (open, ingress, reads, owner, _) = await OpenAsync(model, provider, Path.Combine(directory.FullName, "store.db"),
                inBatch, token, publicStarts: true);
            await using var owned = owner;
            var initial = Spec("first") with { Input = "first\0template" };
            var replacement = Spec("second");
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, "event:orders", "starter", replacement, token);
            await ingress.SetStartAsync(other, key, "starter", replacement, token);
            await PublishAsync("one");
            var first = (await CapturedAsync()).Single();
            Check(first.Wait == "starter" && DurableKeyJson.Read<Contract.StartSpec>(first.Start) == initial &&
                DurableKeyJson.Read<string>(first.Payload) == "\"payload\"",
                "both engines pin exactly one start for equal registration, ignoring another tenant and another key case");

            await ingress.SetStartAsync(scope, key, "starter", replacement, token);
            await PublishAsync("two");
            var second = (await CapturedAsync()).Single(item => item.Registration != first.Registration);
            Check(DurableKeyJson.Read<Contract.StartSpec>(second.Start) == replacement && (await CapturedAsync()).Contains(first),
                "replacing a registered start affects later publications and preserves the original captured template");

            await ingress.SetStartAsync(scope, key, "starter", null, token);
            await ingress.SetStartAsync(scope, key, "starter", null, token);
            await PublishAsync("three");
            var captured = await CapturedAsync();
            Check(captured.Count == 2 && captured.Contains(first) && captured.Contains(second),
                "idempotent unpublish preserves accepted work and excludes the removed registration");
            return 0;

            async Task PublishAsync(string operation)
            {
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(Spec("publish")), operation, Contract.Kinds.Start,
                    cancellationToken: token);
                if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor })
                    throw new InvalidOperationException($"The publication's actor start was refused: {result}");
                while (true)
                {
                    var view = await reads.GetAsync(scope, actor, token);
                    if (view?.Header.Status == Contract.ActorStatus.Finished) return;
                    if (view?.Park is not null) throw new InvalidOperationException(view.Header.Fault);
                    await Task.Delay(20, token);
                }
            }

            async Task<List<CapturedPublicStart>> CapturedAsync()
            {
                await using var db = open();
                return await db.Database.SqlQuery<CapturedPublicStart>($"""
                    SELECT "Wait", "Registration", "Start", "Payload"
                    FROM "FoundationPublicEventTargets" WHERE "Tenant" = {scope.Tenant} OR "Tenant" = {other.Tenant}
                    """).ToListAsync(token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
