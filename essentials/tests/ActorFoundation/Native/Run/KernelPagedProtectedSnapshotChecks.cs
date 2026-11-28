using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunProtectedSnapshotAsync(string provider, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("paged-protected-snapshot");
        var model = new ProtectedSnapshotActor();
        const string slot = "protected-order";
        EventStartRegistration Register(string version) => new("start", ProtectedSnapshotActor.Key,
            "activation-" + version, "source-" + version, "artifact-" + version, "selection-" + version, new(model.Type, version));
        var v1 = Register("v1");
        var v2 = Register("v2");
        var directory = Directory.CreateTempSubdirectory("paged-protected-snapshot");
        try
        {
            var path = Path.Combine(directory.FullName, "store.db");
            var (open, ingress, reads, owner, port, restart) = await OpenAsync(model, provider, path, inBatch, token, eventStarts: true,
                pagedPublicEvents: true);
            await using var initial = owner;
            var events = port ?? throw new InvalidOperationException("The protected Event start port is missing.");
            Check(await events.Apply(scope, slot, 1, [v1], token) == EventRegistrationResult.Applied,
                "the first exact protected authority is installed before publishing");
            await initial.DisposeAsync();

            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "publisher")),
                "publisher", Contract.Kinds.Start, cancellationToken: token);
            if (admitted is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } publisher })
                throw new InvalidOperationException("The protected snapshot author was refused.");

            var produced = await InterruptPagedEventOwnerAtAsync(provider, inBatch, path, afterProducer: true, async () =>
            {
                await using var db = open();
                var message = await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == RootPublicEventTicket.Scope &&
                    row.Kind == Kinds.Publish, token);
                var ticket = RootPublicEventTicket.Decode(message);
                Check(!message.Done && ticket.Initial && ticket.Author?.Tenant == scope.Tenant &&
                    RootAdmissionWire.DecodePayload(ticket.Payload.Text, ticket.Payload.Escaped) == ProtectedSnapshotActor.Body,
                    "the producer committed its original body and initial authored ticket");
                var materialized = await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventPublications" WHERE "Occurrence" = {ticket.Occurrence}) AS "Value"
                    """).SingleAsync(token);
                Check(!materialized, "the first owner stopped before materializing the publication");
                return ticket;
            }, token);

            var producer = await reads.GetAsync(scope, publisher, token);
            Check(producer is { Header.Status: Contract.ActorStatus.Finished, Park: null } &&
                producer.Header.TypeData == produced.EventId && (await events.List(scope, null, token)).Count == 0,
                "the committed publisher identifies the ticket and no selected target is visible");
            Check(await events.Apply(scope, slot, 2, [v2], token) == EventRegistrationResult.Applied,
                "the second exact authority replaces the first while the original publication is unmaterialized");
            var drainedBefore = await events.Drained(scope, slot, 1, token);

            await using var replacement = await restart(token);
            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventDispatch" d
                        JOIN "FoundationPublicEventPublications" p ON p."Occurrence" = d."Occurrence"
                        WHERE d."Occurrence" = {produced.Occurrence} AND p."Tenant" = {scope.Tenant}
                            AND p."EventId" = {DurableKeyJson.Write(produced.EventId)}
                            AND p."Incarnation" = {produced.Incarnation.ToString("N")}
                            AND d."Ticket" = {produced.Id} AND d."Status" = {(int)Contract.PublicationStatus.Matched}) AS "Value"
                    """).SingleAsync(token);
            }, replacement);
            var targets = await events.List(scope, null, token);
            Check(!drainedBefore && targets is [var captured] && captured.Tenant == scope.Tenant && captured.Slot == slot &&
                captured.Revision == 1 && captured.Registration == v1 && captured.EventId == produced.EventId &&
                captured.Key == ProtectedSnapshotActor.Key && captured.Payload == ProtectedSnapshotActor.Body,
                $"the owner used only the pinned v1 authority and exact body (drained before processing: {drainedBefore}, " +
                $"captured revisions: {string.Join(", ", targets.Select(target => target.Revision))})");

            Check(!await events.Drained(scope, slot, 1, token), "v1 remains pinned until its selected target transfers to the born actor");
            var pinCalls = 0;
            var result = await events.Start(scope, targets.Single().Id, target => target.Registration.Template with { Input = target.Payload },
                (offer, _) =>
                {
                    Check(offer.Tenant == scope.Tenant && offer.Type == model.Type && offer.TypeData == v1.Template.TypeData,
                        "the protected relay prepared the captured v1 executable and tenant");
                    pinCalls++;
                    return ValueTask.FromResult(true);
                }, token);
            if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } consumer })
                throw new InvalidOperationException($"The protected v1 target was refused ({result.Outcome}, {result.Reason}).");
            await UntilAsync(async () =>
                (await reads.GetAsync(scope, consumer, token)) is { Header.Status: Contract.ActorStatus.Finished, Header.TypeData: "v1", Park: null },
                replacement);
            Check(pinCalls == 1 && await events.Drained(scope, slot, 1, token) && (await events.List(scope, null, token)).Count == 0,
                "the selected v1 actor consumed the original body and its settled target released the obsolete authority");
            return 0;
        }
        finally { directory.Delete(recursive: true); }

        async Task UntilAsync(Func<Task<bool>> atBoundary, IAsyncDisposable owner)
        {
            while (!await atBoundary())
            {
                if (owner is SqliteEmbeddedHostedService { LastError: { } fault })
                    throw new InvalidOperationException("The protected snapshot owner faulted.", fault);
                await Task.Delay(20, token);
            }
        }
    }

    private sealed class ProtectedSnapshotActor : Contract.IActorType
    {
        internal const string Key = "event:protected-order";
        internal const string Body = "published before replacing protected authority";
        public string Type => "paged-protected-snapshot";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var message = turn.Inbox.Single();
            if (message.Kind != Contract.Kinds.Start) throw new InvalidOperationException("Expected a protected snapshot creator.");
            if (turn.Header.TypeData == "publisher")
                turn.SetHeader(turn.Publish(Key, Body, TimeSpan.FromMinutes(5)), turn.Header.Labels);
            else if (message.Payload != Body)
                throw new InvalidOperationException("The protected start did not receive the selected body.");
            turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
