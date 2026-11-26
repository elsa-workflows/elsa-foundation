using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunWorkflowHistoryAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory("sqlite-root-workflow-history");
        try
        {
            var file = Path.Combine(directory.FullName, "history.db");
            await InstallFoundationAsync(file, token);
            var tenant = EmbeddedTenant.Bind(ActorReadsKind);
            var other = ContractScopes.Bind("history-other");
            var definitions = new SqliteAdmissionDefinitions();
            var reader = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, ActorReadsKind,
                (_, _, kind) => kind == Contract.Kinds.Delete, cancellationToken: token);
            var reads = SqliteEmbeddedHosting.CreateFoundationReads(file);
            var first = await AdmitAsync(tenant, "first", "nul");
            var escaped = await AdmitAsync(tenant, "escaped", "escaped");
            var third = await AdmitAsync(tenant, "third", "plain");
            var hidden = await AdmitAsync(other, "hidden", "nul");
            Require(await HistoryAsync() is { Items.Count: 0, AsOf: > 0 }, "history is empty before the first committed checkpoint");
            await using var host = SqliteEmbeddedHostedService.ForFoundation(file, ActorReadsKind, new ActorReadsProbe(),
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
            await host.StartAsync(token);
            foreach (var actor in new[] { first, escaped, third })
                await WaitForRootAsync(reader, tenant.Tenant, actor, 1, token);
            await WaitForRootAsync(reader, other.Tenant, hidden, 1, token);
            var baseline = await HistoryAsync();
            Require(baseline.Items.Count == 3 && baseline.Items.All(row => row.UpdatedAt > 0 && row.UpdatedAt <= baseline.AsOf),
                "SQLite history observes committed actor checkpoints and the same snapshot clock");
            Require((await HistoryAsync(definition: "a\0b")).Items is [{ Reference: var nul, Definition: "a\0b" }] && nul == first.Id &&
                (await HistoryAsync(definition: "a\\u0000b")).Items is [{ Reference: var slash }] && slash == escaped.Id &&
                (await HistoryAsync(definition: "A\0b")).Items.Count == 0,
                "history filters compare the encoded definition exactly, including NUL, literal backslash and case");
            Require((await HistoryAsync(type: ActorReadsKind.ToUpperInvariant())).Items.Count == 0 &&
                (await HistoryAsync(scope: other)).Items is [{ Reference: var only }] && only == hidden.Id,
                "workflow type and tenant both constrain history");

            var paged = new List<EfStore.RootWorkflowHistoryItem>();
            EfStore.RootWorkflowHistoryItem? after = null;
            while ((await HistoryAsync(after: after, limit: 1)).Items is [var item])
            {
                paged.Add(item);
                if (paged.Count > baseline.Items.Count) throw new InvalidOperationException("History repeated a continuation.");
                after = item;
            }
            Require(paged.SequenceEqual(baseline.Items) && paged.SequenceEqual(paged.OrderByDescending(row => row.UpdatedAt)
                .ThenBy(row => row.Reference, StringComparer.Ordinal)), "bounded history pages follow time and ordinal reference without duplicates");

            await TurnAsync(first, "silent", 2);
            await using (var db = OpenFoundationDb(file, definitions))
                Require(await db.Database.SqlQuery<int>($"""
                    SELECT "Revision" AS "Value" FROM "FoundationWorkflowHistory" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {first.Id}
                    """).SingleAsync(token) == 2, "history advances at a checkpoint even when the actor records no entries");
            await TurnAsync(first, "finish", 3);
            var committed = (await reader.ReadFirstRootAsync(tenant.Tenant, first.Id, token))!;
            Require(committed is { Status: InstanceStatus.Finished, TerminalAt: > 0 } &&
                (await HistoryAsync(status: InstanceStatus.Finished, from: committed.TerminalAt, to: committed.TerminalAt)).Items is
                    [{ Reference: var ended, UpdatedAt: var time }] && ended == first.Id && time == committed.TerminalAt &&
                (await HistoryAsync(status: InstanceStatus.Finished, to: committed.TerminalAt - 1)).Items.Count == 0,
                "terminal history time equals the committed mark and the time range is inclusive");

            var finished = (await reads.GetAsync(tenant, first, token))!;
            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(first), "delete", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(finished.Selection!, ExpectedTarget: first)), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the held selection admits retirement of the terminal workflow");
            while (await reads.GetAsync(tenant, first, token) is not null) await Task.Delay(25, token);
            await using (var db = OpenFoundationDb(file, definitions))
                Require(!await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationWorkflowHistory" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {first.Id}) AS "Value"
                    """).SingleAsync(token) && (await HistoryAsync()).Items.Count == 2 && (await HistoryAsync(scope: other)).Items.Count == 1,
                    "the retirement transaction removes exactly its own history row from the durable projection");
            await host.StopAsync(token);
            Require(host.LastError is null, "the SQLite workflow history owner stops cleanly");

            await using (var db = OpenFoundationDb(file, definitions))
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_project_root_workflow_history", token);
            await ExpectFoundationFailureAsync<StoreFormatException>(() => HistoryAsync(), "workflow history refuses a drifted shipped projector");
            return 0;

            Task<EfStore.RootWorkflowHistoryPage> HistoryAsync(string? definition = null, InstanceStatus? status = null,
                long? from = null, long? to = null, string type = ActorReadsKind, Contract.BoundScope? scope = null,
                EfStore.RootWorkflowHistoryItem? after = null, int limit = 20) => reader.ReadRootWorkflowHistoryAsync(
                (scope ?? tenant).Tenant, type, definition, (int?)status, from, to, after?.UpdatedAt, after?.Reference, limit, token);

            async Task<Contract.ActorRef> AdmitAsync(Contract.BoundScope scope, string id, string definition) => (await ingress.SendAsync(scope,
                new Contract.Target.Start(new(ActorReadsKind, definition)), id, Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;

            async Task TurnAsync(Contract.ActorRef actor, string kind, long revision)
            {
                Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), kind, kind, cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, $"the history fixture admits the {kind} turn");
                await WaitForRootAsync(reader, tenant.Tenant, actor, revision, token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
