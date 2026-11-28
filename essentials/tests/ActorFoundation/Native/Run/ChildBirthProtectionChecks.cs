using System.Collections.Concurrent;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal sealed class ChildBirthProtectionChecks(Func<KernelDb> open, Contract.BoundScope scope)
{
    private readonly ConcurrentDictionary<Contract.ActorRef, PendingChildBirth> _protected = new();
    private readonly ConcurrentDictionary<Contract.ActorRef, int> _attempts = new();
    private int _claimPartial;
    private PendingChildBirth[]? _partial;
    private int _retried;

    internal bool RetriedExactPartial => Volatile.Read(ref _retried) != 0;

    internal async ValueTask<ChildBirthProtectionOutcome> ProtectAsync(IReadOnlyList<PendingChildBirth> births, CancellationToken token)
    {
        if (births.Count == 0 || births.Any(birth => birth.Tenant != scope.Tenant ||
                birth.Type != WorkflowActor.WorkflowActor.ActorType || string.IsNullOrWhiteSpace(birth.TypeData)))
            throw new InvalidOperationException("The owner offered an invalid child protection batch.");
        foreach (var birth in births) _attempts.AddOrUpdate(birth.Actor, 1, (_, attempts) => attempts + 1);
        if (births.Count > 1 && Interlocked.CompareExchange(ref _claimPartial, 1, 0) == 0)
        {
            // Model a Runtime pin batch that writes one child durably, then loses its acknowledgement.
            await CheckBeforeWriteAsync(births, token);
            Remember(births[0]);
            Volatile.Write(ref _partial, births.ToArray());
            return ChildBirthProtectionOutcome.Retry;
        }
        if (Volatile.Read(ref _partial) is { } partial && births.Any(birth => birth.Actor == partial[0].Actor))
        {
            if (!births.SequenceEqual(partial))
                throw new InvalidOperationException("The retried child batch changed its immutable issuance.");
            if (!RetriedExactPartial) await CheckBeforeWriteAsync(births, token);
            Volatile.Write(ref _retried, 1);
        }
        foreach (var birth in births) Remember(birth);
        return ChildBirthProtectionOutcome.Protected;
    }

    internal Contract.ActorRef ForWorkflow(string workflow) => _protected.Values.Single(birth =>
    {
        using var header = JsonDocument.Parse(birth.TypeData);
        return header.RootElement.GetProperty("Workflow").GetString() == workflow;
    }).Actor;

    internal int Attempts(Contract.ActorRef child) => _attempts.GetValueOrDefault(child);

    internal async Task<bool> MatchesBirthAsync(Contract.ActorRef child, ChildBirthProofChecks.Ticket actual, CancellationToken token)
    {
        if (!_protected.TryGetValue(child, out var pending) || pending.Issuance != actual.Issuance) return false;
        return await EfStore.ConfirmRootChildBirthAsync(open, scope, pending.Actor, pending.Issuance, token) ==
            FoundationChildBirthProof.Unresolved;
    }

    private void Remember(PendingChildBirth birth) => _protected.AddOrUpdate(birth.Actor, birth, (_, prior) => prior == birth
        ? prior : throw new InvalidOperationException("An actor birth cannot change its protected selection or issuance."));

    private async Task CheckBeforeWriteAsync(IReadOnlyList<PendingChildBirth> births, CancellationToken token)
    {
        await using var db = open();
        if (db.UsesSqliteRootRuntime) db.Database.SetCommandTimeout(1);
        await using var write = await db.Database.BeginTransactionAsync(token);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1s'", token);
        // Ask the backend to take the same write lock the owner's checkpoint would need. A callback
        // already inside either writer cannot pass this independent write, even if the birth is invisible.
        var parent = await ParentReferenceAsync(births[0], db, token);
        var partition = await db.Database.SqlQuery<int>($"""
            SELECT b."Partition" AS "Value" FROM "FoundationRootBirths" b
            WHERE b."Reference" = {parent}
            """).SingleAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Partitions" SET "Position" = "Position" WHERE "Id" = {partition}
            """, token);
        foreach (var child in births)
        {
            if (await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Reference" = {child.Actor.Id}) AS "Value"
                """).SingleAsync(token))
                throw new InvalidOperationException("A child birth was already visible while it still awaited host protection.");
        }
    }

    private async Task<string> ParentReferenceAsync(PendingChildBirth child, KernelDb db, CancellationToken token)
    {
        var key = db.UsesSqliteRootRuntime ? await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token)
            : await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token);
        var issue = FoundationChildIssuance.Verify(key, scope.Tenant, child.Actor, child.Issuance)
            ?? throw new InvalidOperationException("The prepared child issuance cannot be authenticated in its physical store.");
        return Kernel.Durable.FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(issue.Parent, 1));
    }
}
