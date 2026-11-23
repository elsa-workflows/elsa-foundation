using System.Diagnostics;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresMultiRootClient
{
    internal const string Primary = "preview-a", Secondary = "preview-b";

    internal static Func<KernelDb> Database(bool durable = false, bool operators = false)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a dedicated PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        if (operators) return () => KernelDb.ForPostgresRootOperators(options);
        return () => durable ? KernelDb.ForPostgresRootSelf(options) : new KernelDb(options);
    }

    internal static async Task<Contract.ActorRef> Start(PostgresPreviewFoundation foundation, Contract.BoundScope scope, string id, CancellationToken token,
        Contract.AdmissionOutcome expected = Contract.AdmissionOutcome.Accepted)
    {
        var workflow = new WorkflowRuntime(foundation, foundation);
        var accepted = await workflow.StartAsync(scope, id, NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: token);
        return accepted.Outcome == expected && accepted.Actor is { } actor ? actor
            : throw new InvalidOperationException("The generated root was not accepted.");
    }

    internal static async Task Send(PostgresPreviewFoundation foundation, Contract.BoundScope scope, Contract.ActorRef actor,
        string id, string payload, Contract.AdmissionOutcome expected, CancellationToken token)
    {
        var receipt = await foundation.SendAsync(scope, new Contract.Target.Ref(actor), id, CompositeSignal.Kind, payload, cancellationToken: token);
        if (receipt.Outcome != expected || receipt.Actor != actor) throw new InvalidOperationException("Unexpected exact input receipt.");
    }

    internal static async Task CheckReads(PostgresPreviewFoundation foundation, Contract.BoundScope scope, Contract.ActorRef own,
        Contract.ActorRef foreign, CancellationToken token)
    {
        var journal = await foundation.TailAsync(scope, own, cancellationToken: token);
        var ownMarker = Json.Write("approved:" + scope.Tenant + "-two");
        var foreignMarker = Json.Write("approved:" + (scope.Tenant == Primary ? Secondary : Primary) + "-two");
        var inaccessible = await foundation.TailAsync(scope, foreign, cancellationToken: token);
        if (!journal.Items.Select(e => e.Revision).Distinct().SequenceEqual([1L, 2L, 3L]) ||
            journal.Items.Count(e => e.Revision == 3 && e.Entry == ownMarker) != 1 || journal.Items.Any(e => e.Entry == foreignMarker) ||
            journal.Items.Count(e => e.Entry == Json.Write("end")) != 1 || journal.Items[^1].Entry != Json.Write("end") ||
            await foundation.GetAsync(scope, foreign, token) is not null || inaccessible.Items.Count != 0 || inaccessible.Next is not null)
            throw new InvalidOperationException("The public journal or tenant read did not match the two-root fixture.");
    }

    // Each process has exactly one configured public ingress identity.
    internal static async Task<int> SecondaryAsync(string[] args)
    {
        var scope = FixedTenantScope.Bind(Secondary);
        var open = Database(durable: args is ["missing-durable", _], operators: args is ["operator-start"] or ["operator-resume", _] or ["operator-list", _, _]);
        var store = new EfStore(open, postgres: true);
        var ingress = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = limit.Token;
        var foundation = new PostgresPreviewFoundation(ingress, store, new(store, await ingress.OpenJournalCursorAsync(token), 64), WorkflowActor.WorkflowActor.ActorType);
        switch (args)
        {
            case ["start"]:
                Console.WriteLine((await Start(foundation, scope, "start", token)).Id);
                break;
            case ["operator-start"]:
                Console.WriteLine((await Start(foundation, scope, "charlie", token)).Id);
                break;
            case ["operator-list", var neighbor, var saved]:
                var query = PostgresRootListChecks.ApprovalQuery();
                var list = await foundation.ListAsync(scope, query, cancellationToken: token);
                if (list.Items is not [var row] || row.Header.Ref.Id != neighbor || row.Header.Tenant != scope.Tenant || list.Next is not null)
                    throw new InvalidOperationException("The second tenant did not see exactly its own projected actor.");
                try { await foundation.ListAsync(scope, query, new(saved), token); }
                catch (ArgumentException) { break; }
                throw new InvalidOperationException("The second tenant used another tenant's list token.");
            case ["operator-resume", var actorRef]:
                var operatorApi = new PostgresPreviewFoundation(ingress, store,
                    new(store, await ingress.OpenJournalCursorAsync(token), 64), WorkflowActor.WorkflowActor.ActorType,
                    (request, kind) => ReferenceEquals(request, scope) && kind == Contract.Kinds.Resume);
                var reply = await operatorApi.SendAsync(scope, new Contract.Target.Ref(new(actorRef)),
                    PostgresRootOperatorChecks.SuspendId, Contract.Kinds.Resume, cancellationToken: token);
                if (reply.Outcome != Contract.AdmissionOutcome.Accepted || reply.Actor?.Id != actorRef)
                    throw new InvalidOperationException("The second tenant's Resume was not admitted against its actor.");
                break;
            case ["start-receipt", var operation, var result] when Enum.TryParse<Contract.AdmissionOutcome>(result, out var required):
                Console.WriteLine((await Start(foundation, scope, operation, token, required)).Id);
                break;
            case ["missing" or "missing-durable", var hidden]:
                var unavailable = new Contract.ActorRef(hidden);
                var journal = await foundation.TailAsync(scope, unavailable, cancellationToken: token);
                if (await foundation.GetAsync(scope, unavailable, token) is not null || journal.Items.Count != 0 || journal.Next is not null)
                    throw new InvalidOperationException("The second tenant can see an unexpected actor or journal.");
                break;
            case ["send", var reference, var id, var payload, var expected] when Enum.TryParse<Contract.AdmissionOutcome>(expected, out var outcome):
                await Send(foundation, scope, new(reference), id, payload, outcome, token);
                break;
            case ["reads", var own, var foreign]:
                await CheckReads(foundation, scope, new(own), new(foreign), token);
                break;
            default: return 2;
        }
        return 0;
    }

    internal static async Task<string> CallSecondaryAsync(CancellationToken token, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add(typeof(PostgresMultiRootClient).Assembly.Location);
        start.ArgumentList.Add("pg-multi-root-secondary");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the second tenant client.");
        try
        {
            var output = await child.StandardOutput.ReadToEndAsync(token);
            await child.WaitForExitAsync(token);
            return child.ExitCode == 0 ? output.Trim() : throw new InvalidOperationException("The second tenant client failed.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }
}
