using System.Diagnostics;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class GeneratedRootChecks
{
    private const string ChildConnection = "ELSA_TEST_ROOT_ADMISSION_POSTGRES";

    internal static async Task<int> WriteFromChildAsync()
    {
        var connection = Environment.GetEnvironmentVariable(ChildConnection) ?? throw new InvalidOperationException("Missing test database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var writer = new PostgresRootAdmission(() => new(options), TimeSpan.FromMinutes(1));
        var result = await writer.AdmitAsync("demo", "child-process", new("workflow", "definition", "child input"));
        Console.WriteLine(result.Reference);
        return result.Duplicate ? 1 : 0;
    }

    internal static async Task Run(Func<KernelDb> create, IKernelStore firstClient, IKernelStore nextClient)
    {
        const string tenant = "demo";
        const string operation = "start/one";
        var spec = new StartSpec("workflow", "definition", "approval-data");
        var admission = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        var next = new PostgresRootAdmission(create, TimeSpan.FromMinutes(1));
        await Task.WhenAll(admission.InstallAsync(), next.InstallAsync());

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admission.AdmitAsync(tenant, operation, spec)));
        var reference = results.Single(r => !r.Duplicate).Reference;
        Check(results.All(r => r.Reference == reference), "eight racing starts returned one durable reference");
        var replay = await next.AdmitAsync(tenant, operation, spec with { Input = "later" });
        Check(replay.Duplicate && replay.Reference == reference, "a fresh client replays the first receipt");
        var birth = (await next.ResolveAsync(tenant, reference))!;
        var actor = new InstanceRef(birth.Id);
        var creator = (await next.ReadPendingCreatorAsync(tenant, reference))!;
        Check(creator.Id == operation && creator.InboxKey == new InboxKey("root:" + actor.Id) &&
            creator.Target == actor && birth.Creation == 1 && creator.Kind == Kinds.Start && creator.TargetCreation == birth.Creation &&
            creator.Start?.Creation is { Generation: 1, Previous: 0 } && creator.Payload == spec.Input &&
            creator.Start.Input == spec.Input, "cold inbox read rebuilds the operation id, exact generation, input and independent storage key");
        await using var db = create();
        var physical = await db.Messages.SingleAsync();
        Check(physical.Payload == spec.Input && !physical.Start!.Contains(spec.Input!),
            "the creator and one copy of its start input are stored once");
        try
        {
            await nextClient.PollAsync(actor.Partition, 1, TimeSpan.Zero, 32);
            throw new InvalidOperationException("legacy owner accepted a public creator");
        }
        catch (StoreException error) when (error.Failure == Failure.Definite) { }
        Check(await next.ReadPendingCreatorAsync(tenant, reference) == creator, "the legacy poll preserves the pending exact creator");

        var other = await next.AdmitAsync("other", operation, spec);
        Check(!other.Duplicate && other.Reference != reference && await next.ResolveAsync("other", reference) is null &&
            await next.ResolveAsync(tenant, other.Reference) is null && await next.ReadPendingCreatorAsync("other", reference) is null, "equal operations in two tenants retain separate exact bindings");

        // A provider error after the receipt and birth inserts must undo all three effects.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Messages" ADD CONSTRAINT "fixture_reject_start" CHECK ("Kind" <> 'start') NOT VALID
            """);
        try
        {
            await admission.AdmitAsync(tenant, "rollback", spec);
            throw new InvalidOperationException("expected the fixture's inbox constraint to reject the creator");
        }
        catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { ConstraintName: "fixture_reject_start" }) { }
        Check(await db.Messages.CountAsync() == 2 && await Count("births") == 2 && await Count("receipts") == 2,
            "failed inbox insert rolls back its root receipt and binding");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Messages" DROP CONSTRAINT "fixture_reject_start" """);
        var retried = await next.AdmitAsync(tenant, "rollback", spec);
        Check(!retried.Duplicate && await admission.ResolveAsync(tenant, retried.Reference) is not null,
            "retry after rollback admits a complete new birth");

        // Advance only the test receipt's stored deadline. Cover replacement with and without a sweep.
        await Expire(reference);
        var replacements = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => next.AdmitAsync(tenant, operation, spec)));
        var fresh = replacements.Single(r => !r.Duplicate);
        Check(replacements.All(r => r.Reference == fresh.Reference) && fresh.Reference != reference && await admission.ResolveAsync(tenant, reference) == birth,
            "unswept expired receipt admits a new life and preserves the first exact reference");
        await Expire(fresh.Reference);
        Check(await next.SweepReceiptsAsync() == 1 && await next.ResolveAsync(tenant, fresh.Reference) is not null,
            "sweeping expired receipts preserves their birth bindings");
        var afterSweep = await admission.AdmitAsync(tenant, operation, spec);
        Check(!afterSweep.Duplicate && afterSweep.Reference != fresh.Reference && !firstClient.CreationIdentityAvailable,
            "admission after sweep returns a new life, with the Worker capability still gated");

        foreach (var input in new string?[] { null, "", "one\0two", "one\ud800two", new string('"', 1024 * 1024) })
        {
            var receipt = await next.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), spec with { Input = input });
            var reopened = await admission.ReadPendingCreatorAsync(tenant, receipt.Reference);
            Check(reopened is not null && reopened.Payload == input && reopened.Start!.Input == input,
                $"raw or escaped input round-trips ({input?.Length.ToString() ?? "absent"} code units)");
        }

        foreach (var due in new long?[] { null, 0, 2_000_000_000_000 })
        {
            var receipt = await admission.AdmitAsync(tenant, Guid.NewGuid().ToString("N"), spec, due: due);
            var reopened = await next.ReadPendingCreatorAsync(tenant, receipt.Reference);
            Check(reopened is not null && reopened.Due == due,
                $"cold read preserves due time {due?.ToString() ?? "absent"}");
        }

        var messageCount = await db.Messages.CountAsync();
        var receiptCount = await Count("receipts");
        try
        {
            await next.AdmitAsync(tenant, "oversize-encoded", spec with { Input = new string('\0', 3 * 1024 * 1024) });
            throw new InvalidOperationException("expected the encoded creator to exceed its row budget");
        }
        catch (ArgumentException e) when (e.ParamName == "spec") { }
        Check(await db.Messages.CountAsync() == messageCount && await Count("births") == messageCount && await Count("receipts") == receiptCount,
            "an oversized physical representation leaves no creator, binding or receipt");

        var command = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
        command.ArgumentList.Add(typeof(GeneratedRootChecks).Assembly.Location);
        command.ArgumentList.Add("root-admission-writer");
        command.Environment[ChildConnection] = db.Database.GetConnectionString();
        using (var child = Process.Start(command)!)
        {
            var issued = (await child.StandardOutput.ReadToEndAsync()).Trim();
            await child.WaitForExitAsync();
            var recovered = await next.AdmitAsync(tenant, "child-process", spec);
            Check(child.ExitCode == 0 && recovered.Duplicate && recovered.Reference == issued &&
                (await next.ReadPendingCreatorAsync(tenant, issued))?.Payload == "child input",
                "a restarted writer sees the exited process's reference and original pending input");
        }

        db.Partitions.Add(new PartitionEntity { Id = 2 });
        await db.SaveChangesAsync();
        try
        {
            await next.InstallAsync();
            throw new InvalidOperationException("reopening accepted changed virtual partition placement");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("no longer matches")) { }
        Check(await next.ResolveAsync(tenant, reference) == birth, "reopening with a changed partition count preserves existing placement");

        Task<int> Expire(string r) => db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "FoundationRootReceipts" SET "Until" = 0 WHERE "Reference" = {r}""");

        Task<long> Count(string table) => db.Database.SqlQueryRaw<long>(table == "births"
            ? """SELECT count(*) AS "Value" FROM "FoundationRootBirths" """
            : """SELECT count(*) AS "Value" FROM "FoundationRootReceipts" """).SingleAsync();
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
