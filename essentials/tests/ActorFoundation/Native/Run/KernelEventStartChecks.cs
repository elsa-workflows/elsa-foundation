using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private sealed record EventStartPort(
        Func<Contract.BoundScope, string, long, IReadOnlyList<EventStartRegistration>, CancellationToken, Task<EventRegistrationResult>> Apply,
        Func<Contract.BoundScope, string?, CancellationToken, Task<IReadOnlyList<EventStartTarget>>> List,
        Func<Contract.BoundScope, string, Func<EventStartTarget, Contract.StartSpec>, ProtectRootStartAsync, CancellationToken, Task<Contract.Admission>> Start,
        Func<Contract.BoundScope, string, long, CancellationToken, Task<bool>> Drained,
        Func<Contract.BoundScope, Contract.ActorRef, string, CancellationToken, Task<RootStartProof>> Proof);

    internal static async Task<int> RunStartsAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = stop.Token;
        var scope = ContractScopes.Bind("authored-events");
        var other = ContractScopes.Bind("other");
        const string slot = "published-orders";
        const string eventKey = "event:Order";
        var registry = new WorkflowRegistry().Add(Producer("first-publisher", "Order", "old payload"))
            .Add(Producer("next-publisher", "Order", "new payload"))
            .Add(Wf.Workflow("null-publisher", Wf.Publish("publish", "Order")));
        var chart = NativeExecutableMapper.Map(ExecutableOf("chart-start", "published-orders", Node("chart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "start",
                connections = new[] { new { source = new { nodeId = "start", port = "Done" }, target = new { nodeId = "seen" } } }
            }), slots: [Slot("Flowchart.Activities", EventNode(), SetOutput("seen", "seen", Result("value", "start", "Payload")))])));
        var native = NativeExecutableMapper.Map(ExecutableOf("native-start", "published-orders", Sequence("root", EventNode(),
            SetOutput("seen", "seen", Result("value", "start", "Payload")))));
        registry.Add(chart).Add(native);
        var old = Register(chart, "old-activation");
        var next = Register(native, "new-activation");
        var model = new PublicWorkflow([new NativeExecutionModel(registry)]);
        var directory = Directory.CreateTempSubdirectory("event-starts");
        try
        {
            var (open, ingress, reads, owner, eventPort) = await OpenAsync(model, provider, Path.Combine(directory.FullName, "store.db"),
                inBatch, token, eventStarts: true);
            await using var owned = owner;
            var events = eventPort ?? throw new InvalidOperationException("The event start port is missing.");
            Check(await events.Apply(scope, slot, 5, [old], token) == EventRegistrationResult.Applied &&
                  await events.Apply(other, slot, 5, [next], token) == EventRegistrationResult.Applied,
                "the same event and slot register independently for two bound tenants");
            await PublishAsync("first-publisher");
            var selectedOld = (await events.List(scope, null, token)).Single();
            Check(selectedOld is { Revision: 5, Key: eventKey, Registration.ActivationId: "old-activation" } &&
                selectedOld.Payload == JsonSerializer.Serialize("old payload") && (await events.List(other, null, token)).Count == 0,
                "an authored publication captures its exact active Flowchart source, trigger and payload in the producer checkpoint");

            Check(await events.Apply(scope, slot, 6, [next], token) == EventRegistrationResult.Applied &&
                  await events.Apply(scope, slot, 5, [], token) == EventRegistrationResult.Stale &&
                  await events.Apply(scope, slot, 6, [], token) == EventRegistrationResult.Conflict &&
                  await events.Apply(scope, slot, 6, [next], token) == EventRegistrationResult.Duplicate &&
                  !await events.Drained(scope, slot, 5, token),
                "replacement linearizes at the provider revision and keeps accepted predecessor work pinned");
            await PublishAsync("next-publisher");
            var pending = await events.List(scope, null, token);
            Check(pending.Count == 2 && pending.Any(item => item == selectedOld),
                "the durable queue keeps the old start while selecting the new Native activation");
            var selectedNew = pending.Single(item => item.Revision == 6);
            Check(selectedNew.Registration == next && selectedNew.Payload == JsonSerializer.Serialize("new payload"),
                "a subsequent publish sees only the replacement registration");

            static Contract.StartSpec Unexpected(EventStartTarget _) => throw new InvalidOperationException("No start may be composed.");
            static ValueTask<bool> UnexpectedProtect(PendingRootStart _, CancellationToken __) =>
                throw new InvalidOperationException("No executable pin may be requested.");
            Check((await events.Start(other, selectedOld.Id, Unexpected, UnexpectedProtect, token)).Reason == Contract.RefusalReason.TargetMismatch,
                "another tenant cannot read or admit a captured target by its opaque ID");

            Check((await events.Start(scope, selectedOld.Id, target => target.Registration.Template with
                    { TypeData = next.Template.TypeData }, UnexpectedProtect, token)).Reason == Contract.RefusalReason.TargetMismatch,
                "the provider rejects a different executable header before asking for protection");

            PendingRootStart? abandoned = null;
            ValueTask<bool> RefuseProtection(PendingRootStart request, CancellationToken _)
            {
                abandoned = request;
                return ValueTask.FromResult(false);
            }
            var refused = await events.Start(scope, selectedOld.Id, Compose, RefuseProtection, token);
            Check(refused.Reason == Contract.RefusalReason.TargetMismatch && abandoned is not null &&
                  await events.Proof(scope, abandoned.Actor, abandoned.Issuance, token) == RootStartProof.PreparationDisarmed &&
                  (await events.List(scope, null, token)).Any(item => item == selectedOld),
                "a process can refuse one prepared pin and prove fenced absence while the exact accepted target survives");

            var prepared = new System.Collections.Concurrent.ConcurrentDictionary<Contract.ActorRef, PendingRootStart>();
            var bothPrepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<bool> ProtectBoth(PendingRootStart request, CancellationToken ct)
            {
                Check(request.Tenant == scope.Tenant && request.TypeData == old.Template.TypeData,
                    "the prepared pin carries the captured source material in the bound tenant");
                if (!prepared.TryAdd(request.Actor, request)) throw new InvalidOperationException("Preparations must be distinct.");
                if (prepared.Count == 2) bothPrepared.TrySetResult();
                await bothPrepared.Task.WaitAsync(ct);
                return true;
            }
            var concurrent = await Task.WhenAll(events.Start(scope, selectedOld.Id, Compose, ProtectBoth, token),
                events.Start(scope, selectedOld.Id, Compose, ProtectBoth, token));
            var accepted = concurrent.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted);
            var actor = accepted.Actor!.Value;
            Check(concurrent.Single(item => item.Outcome == Contract.AdmissionOutcome.Duplicate).Actor == actor &&
                await events.Proof(scope, prepared.Values.Single(item => item.Actor != actor).Actor,
                    prepared.Values.Single(item => item.Actor != actor).Issuance, token) == RootStartProof.PreparationDisarmed,
                "racing protected attempts settle one birth and disarm the unused preparation");
            Check(await events.Drained(scope, slot, 5, token),
                "the replaced registration can release its source pin after the final target transfers to its root pin");
            var ended = await OutputAsync(actor, "old payload");

            var newer = await events.Start(scope, selectedNew.Id, Compose, (_, _) => ValueTask.FromResult(true), token);
            Check(newer is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the replacement's independent target is admitted");
            await OutputAsync(newer.Actor!.Value, "new payload");

            await PublishAsync("null-publisher");
            var emptyPayload = (await events.List(scope, null, token)).Single();
            Check(emptyPayload.Payload is null && emptyPayload.Registration == next, "an authored empty payload remains distinct from an absent target");
            var nullable = await events.Start(scope, emptyPayload.Id, Compose, (_, _) => ValueTask.FromResult(true), token);
            Check(nullable is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the no-payload event starts its selected Native entry");
            await OutputAsync(nullable.Actor!.Value, null);

            var command = JsonSerializer.Serialize(new Contract.OperatorCommand(ended.Selection!, ExpectedTarget: actor));
            Check((await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "delete-selected", Contract.Kinds.Delete,
                command, cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the exact Flowchart root is selected for retirement");
            await UntilAsync(async () => await reads.GetAsync(scope, actor, token) is null);
            await using (var db = open())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "FoundationRootReceipts" WHERE "Tenant" = {scope.Tenant} AND "Reference" = {actor.Id}
                    """, token);
                var scrubbed = await db.Database.SqlQuery<bool>($"""
                    SELECT "Authority" IS NULL AND "Payload" IS NULL AS "Value" FROM "FoundationEventStartTargets"
                    WHERE "Tenant" = {scope.Tenant} AND "Id" = {selectedOld.Id}
                    """).SingleAsync(token);
                Check(scrubbed, "settlement scrubs the selected target body while retaining immutable birth identity");
            }
            var priorPin = prepared[actor];
            Check(await events.Proof(scope, actor, priorPin.Issuance, token) == RootStartProof.BirthRetired &&
                (await events.Start(scope, selectedOld.Id, Unexpected, UnexpectedProtect, token)) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } duplicate } && duplicate == actor,
                "retirement and ordinary receipt expiry preserve the exact target receipt without re-composing or pinning a new birth");
            Check(await events.Apply(scope, slot, 7, [], token) == EventRegistrationResult.Applied,
                "unpublish installs a newer tombstone");
            await PublishAsync("first-publisher");
            Check((await events.List(scope, null, token)).Count == 0 && await events.Drained(scope, slot, 6, token),
                "a publication after tombstone cannot select the retired slot");
            await stop.CancelAsync();
            return 0;

            async Task PublishAsync(string workflow)
            {
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(Spec(workflow)), Guid.NewGuid().ToString("N"),
                    Contract.Kinds.Start, cancellationToken: token);
                var reference = result.Actor ?? throw new InvalidOperationException($"Publisher refused: {result}");
                await UntilAsync(async () => (await reads.GetAsync(scope, reference, token))?.Header.Status == Contract.ActorStatus.Finished);
            }

            Contract.StartSpec Compose(EventStartTarget target)
            {
                var registration = target.Registration;
                var workflow = registry.Get(registration.ArtifactId);
                Check(target.Tenant == scope.Tenant && target.Key == eventKey && registration.ExecutableSelection == workflow.ExecutableSelection &&
                      NativeStartTriggers.StartEvents(workflow).Single().Id == registration.Node,
                    "the relay binds the exact selected artifact, trigger and canonical material");
                return registration.Template with
                {
                    Input = NativeExecutionModel.Start(workflow.Id, definition: workflow.Definition, triggerNode: registration.Node,
                        trigger: target.Payload is null ? null : JsonNode.Parse(target.Payload)).Input
                };
            }

            async Task<Contract.ActorView> OutputAsync(Contract.ActorRef actorReference, string? expected)
            {
                Contract.ActorView? view = null;
                await UntilAsync(async () => (view = await reads.GetAsync(scope, actorReference, token))?.Header.Status == Contract.ActorStatus.Finished);
                var output = await ((Contract.IActorTerminalOutputReads)reads)
                    .ReadTerminalOutputAsync(scope, actorReference, view!.Header.EndedAt!.Value, true, token);
                Check(output?.Values is { } values && JsonNode.Parse(values) is JsonObject results &&
                    results.ContainsKey("seen") && results["seen"]?.GetValue<string>() == expected,
                    "the exact authored start consumes the captured payload at its selected Event node");
                return view;
            }

            async Task UntilAsync(Func<Task<bool>> check)
            {
                while (!await check()) await Task.Delay(20, token);
            }
        }
        finally { directory.Delete(recursive: true); }

        static Elsa.Workflows.Runtime.Core.Models.ExecutableNode EventNode() =>
            Node("start", Types.Event, [Literal("EventName", "Order", "String"), Literal("CanStartWorkflow", true, "Boolean")]);
        static EventStartRegistration Register(Workflow workflow, string activation) =>
            new("start", eventKey, activation, "source-" + activation, workflow.Id,
                workflow.ExecutableSelection!, PublicWorkflow.Start(NativeExecutionModel.Start(workflow)) with { Input = null });
    }
}
