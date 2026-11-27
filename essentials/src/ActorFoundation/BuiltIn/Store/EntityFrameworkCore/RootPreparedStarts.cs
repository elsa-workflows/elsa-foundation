using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kernel.Durable;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>The exact root prepared before its executable can be pinned.</summary>
internal sealed record PreparedRootStart(string Tenant, string Reference, string Actor, int Partition,
    string Operation, string Fingerprint, long Deadline);

internal sealed class BoundProtectedRootStarts(string actorType,
    Func<string, StartSpec, CancellationToken, CancellationToken, Task<Contract.Admission?>> admit) : IProtectedRootStarts
{
    public async ValueTask<Contract.Admission> StartAsync(string operationId, Contract.StartSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.Type != actorType || spec.Ephemeral || !Enum.IsDefined(spec.History) ||
            !FoundationMetadataLimits.IsOutsideMessageId(operationId))
            return new(Contract.AdmissionOutcome.Refused);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(RootPreparedStarts.OneWayBudget);
        try
        {
            var admission = await admit(operationId, new(spec.Type, spec.TypeData, spec.Input, History: (History)spec.History),
                budget.Token, cancellationToken);
            if (admission is not null) return admission;
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable();
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Unavailable();
        }
        catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        catch (MetadataLimitExceeded) { return new(Contract.AdmissionOutcome.Refused); }
    }

    private static Contract.Admission Unavailable() =>
        new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy, RetryAfter: TimeSpan.FromSeconds(1));
}

internal static class RootPreparedStarts
{
    internal const string Table = "FoundationPreparedRootStarts";
    internal const string DeadlineIndex = "IX_FoundationPreparedRootStarts_Deadline";
    internal const string DeadlineColumns = """("Tenant", "Deadline", "Reference")""";

    internal const string Source = "/*prepared-start-source*/";
    private const string IssuancePrefix = PendingRootStart.IssuancePrefix;
    private const int MaximumIssuanceLength = 2_048;
    internal static readonly TimeSpan OneWayBudget = TimeSpan.FromSeconds(10);

    internal static long OneWayDeadline(long now) => checked(now + (long)OneWayBudget.TotalMilliseconds);

    // Ordinary ingress cannot submit this reserved id or win a protected operation's receipt.
    internal static string OneWayOperation(string operation) => "~prepared-start/1:" + FoundationRootCreator.OperationDigest(operation);
    private const string InlinePrefix = "~inline-start/1:";
    internal static string InlineOperation(string operation) => InlinePrefix + FoundationRootCreator.OperationDigest(operation);
    internal static bool IsInlineOperation(string operation) =>
        operation.StartsWith(InlinePrefix, StringComparison.Ordinal) && FoundationRootCreator.IsGeneratedReference(operation[InlinePrefix.Length..]);

    internal static async Task<bool> ProtectAsync(ProtectRootStartAsync protect, PendingRootStart offer, CancellationToken token)
    {
        // A synchronous callback must not tie up an admission slot past its deadline.
        var work = Task.Run(async () => await protect(offer, token), token);
        try
        {
            var outcome = await work.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return outcome;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = work.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    internal static PendingRootStart Offer(PreparedRootStart stage, StartSpec spec, byte[] key)
    {
        if (!Valid(stage, key)) throw new InvalidOperationException("Held root preparation has an invalid identity.");
        var body = IssuancePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(DurableKeyJson.Write(stage)));
        var issuance = body + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(body)));
        if (issuance.Length > MaximumIssuanceLength)
            throw new InvalidOperationException("Held root preparation exceeds its issuance limit.");
        return new(stage.Tenant, new(stage.Reference), spec.Model, spec.Definition, issuance);
    }

    internal static PreparedRootStart? Verify(byte[] key, Contract.BoundScope scope, Contract.ActorRef actor, string issuance)
    {
        if (issuance is not { Length: <= MaximumIssuanceLength } ||
            !issuance.StartsWith(IssuancePrefix, StringComparison.Ordinal)) return null;
        var macAt = issuance.LastIndexOf('.');
        if (macAt <= IssuancePrefix.Length || !FoundationRootCreator.IsGeneratedReference(issuance[(macAt + 1)..])) return null;
        var mac = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(issuance[..macAt]));
        if (!CryptographicOperations.FixedTimeEquals(mac, Convert.FromHexString(issuance[(macAt + 1)..]))) return null;
        try
        {
            var stage = DurableKeyJson.Read<PreparedRootStart>(
                Encoding.UTF8.GetString(Convert.FromBase64String(issuance[IssuancePrefix.Length..macAt])));
            return stage is not null && stage.Tenant == scope.Tenant && stage.Reference == actor.Id && Valid(stage, key)
                ? stage : null;
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException) { return null; }
    }

    private static bool Valid(PreparedRootStart stage, byte[] key) =>
        stage.Tenant is { Length: > 0 and <= 64 } &&
        stage.Actor is { Length: > 0 and <= 256 } && stage.Partition >= 0 &&
        StoreBatchRules.AddressPartition(stage.Actor) == stage.Partition && stage.Deadline > 0 &&
        FoundationRootCreator.IsGeneratedReference(stage.Operation) &&
        stage.Fingerprint is { Length: 67 } fingerprint && fingerprint.StartsWith("v1:", StringComparison.Ordinal) &&
        FoundationRootCreator.IsGeneratedReference(fingerprint[3..]) &&
        FoundationRootCreator.ReferenceFor(key, stage.Tenant, new(stage.Actor, 1)) == stage.Reference;

    internal static Task<bool> RecordAsync(KernelDb db, PreparedRootStart stage, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null) =>
        ChangeAsync(db, $$"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            INSERT INTO "FoundationPreparedRootStarts" ("Tenant", "Reference", "Actor", "Partition", "Operation", "Fingerprint", "Deadline")
            SELECT {{stage.Tenant}}, {{stage.Reference}}, {{stage.Actor}}, {{stage.Partition}}, {{stage.Operation}}, {{stage.Fingerprint}}, {{stage.Deadline}}
            WHERE (SELECT "SourceReady" FROM source)
            """, token, bindSource);

    /// <summary>Consumes the exact preparation in the transaction that admits its birth.</summary>
    internal static Task<bool> ConsumeAsync(KernelDb db, PreparedRootStart stage, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null) =>
        ChangeAsync(db, $$"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            DELETE FROM /*prepared-only*/"FoundationPreparedRootStarts"
            WHERE "Tenant" = {{stage.Tenant}} AND "Reference" = {{stage.Reference}} AND "Actor" = {{stage.Actor}}
                AND "Partition" = {{stage.Partition}} AND "Operation" = {{stage.Operation}}
                AND "Fingerprint" = {{stage.Fingerprint}} AND "Deadline" = {{stage.Deadline}}
                AND (SELECT "SourceReady" FROM source)
            """, token, bindSource);

    internal static async Task<bool> AdmitInlineBirthAsync(KernelDb db, PreparedRootStart stage, long now, long until,
        CancellationToken token, Func<FormattableString, FormattableString>? bindSource)
    {
        if (now >= stage.Deadline || !await ConsumeAsync(db, stage, token, bindSource)) return false;
        if (!await ChangeAsync(db, $"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            INSERT INTO "FoundationRootReceipts" ("Tenant", "Operation", "Reference", "Until")
            SELECT {stage.Tenant}, {stage.Operation}, {stage.Reference}, {until} FROM source WHERE "SourceReady"
            ON CONFLICT ("Tenant", "Operation") DO UPDATE SET "Reference" = excluded."Reference", "Until" = excluded."Until"
                WHERE "FoundationRootReceipts"."Until" < {now}
            """, token, bindSource)) return false;
        return await ChangeAsync(db, $"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
            SELECT {stage.Tenant}, {stage.Reference}, {stage.Actor}, {stage.Partition}, 1, {"root:" + stage.Actor}, {stage.Fingerprint}
            FROM source WHERE "SourceReady"
            """, token, bindSource);
    }

    internal static async Task ExpireAsync(KernelDb db, PreparedRootStart stage, long now, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        if (now < stage.Deadline) return;
        await ConsumeAsync(db, stage, token, bindSource);
    }

    internal static Task<int> PruneAsync(KernelDb db, string tenant, long now, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null) =>
        db.Database.ExecuteSqlInterpolatedAsync(Bind(db, $"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            DELETE FROM /*prepared-only*/"FoundationPreparedRootStarts"
            WHERE "Reference" IN (
                SELECT "Reference" FROM /*prepared-only*/"FoundationPreparedRootStarts"
                WHERE "Tenant" = {tenant} AND "Deadline" <= {now} ORDER BY "Deadline", "Reference" LIMIT 32)
                AND (SELECT "SourceReady" FROM source)
            """, bindSource), token);

    internal static async Task<RootStartProof> ReadProofAsync(KernelDb db, PreparedRootStart stage, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        var creator = "root:" + stage.Actor;
        var query = Bind(db, $"""
            WITH source AS MATERIALIZED (/*prepared-start-source*/)
            SELECT CASE
                WHEN NOT source."SourceReady" THEN 0
                WHEN EXISTS (
                    SELECT 1 FROM /*prepared-only*/"FoundationRootBirths" b
                    JOIN /*prepared-only*/"Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                        AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
                    JOIN /*prepared-only*/"Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition"
                        AND j."Revision" = -1 AND j."Entries" = '["delete"]'
                    WHERE b."Tenant" = {stage.Tenant} AND b."Reference" = {stage.Reference} AND b."Actor" = {stage.Actor}
                        AND b."Partition" = {stage.Partition} AND b."Creation" = 1 AND b."Creator" = {creator}
                        AND b."Fingerprint" = {stage.Fingerprint} AND i."Retired" AND i."Status" <> {(int)InstanceStatus.Running}
                        AND i."FoundationTerminalRevision" > 0 AND i."FoundationTerminalAt" IS NOT NULL
                        AND i."FoundationWriteRevision" = i."Revision") THEN 1
                WHEN NOT EXISTS (SELECT 1 FROM /*prepared-only*/"FoundationPreparedRootStarts" WHERE "Reference" = {stage.Reference})
                    AND NOT EXISTS (SELECT 1 FROM /*prepared-only*/"FoundationRootBirths" WHERE "Reference" = {stage.Reference} OR "Actor" = {stage.Actor})
                    AND NOT EXISTS (SELECT 1 FROM /*prepared-only*/"Instances" WHERE "Partition" = {stage.Partition} AND "Id" = {stage.Actor})
                    AND NOT EXISTS (SELECT 1 FROM /*prepared-only*/"Messages" WHERE "InboxScope" = 0 AND "Id" = {creator}) THEN 2
                ELSE 0 END AS "Value" FROM source
            """, bindSource);
        return (RootStartProof)await db.Database.SqlQuery<int>(query).SingleAsync(token);
    }

    private static async Task<bool> ChangeAsync(KernelDb db, FormattableString query, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource) =>
        await db.Database.ExecuteSqlInterpolatedAsync(Bind(db, query, bindSource), token) == 1;

    internal static FormattableString Bind(KernelDb db, FormattableString query, Func<FormattableString, FormattableString>? bindSource)
    {
        if (!db.UsesRootPreparedStarts || db.Database.CurrentTransaction is null || !db.UsesSqliteShippedInstall && bindSource is null)
            throw new NotSupportedException("Held root preparation requires a bound provider transaction.");

        // Keep the source selection in the same statement as the query.
        var format = query.Format.Replace("/*prepared-only*/", db.UsesSqliteShippedInstall ? "" : "ONLY ", StringComparison.Ordinal);
        query = FormattableStringFactory.Create(format, query.GetArguments());
        return bindSource is not null ? bindSource(query) : FormattableStringFactory.Create(
            format.Replace(Source, """SELECT true AS "SourceReady" """, StringComparison.Ordinal), query.GetArguments());
    }

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT" : """text COLLATE "C" """;
        var deadline = sqlite ? "INTEGER" : "bigint";
        return $$"""
            "Reference" {{text}} NOT NULL PRIMARY KEY CHECK (length("Reference") = 64),
            "Tenant" {{text}} NOT NULL,
            "Actor" {{text}} NOT NULL,
            "Partition" integer NOT NULL REFERENCES "Partitions" ("Id"),
            "Operation" {{text}} NOT NULL CHECK (length("Operation") = 64),
            "Fingerprint" {{text}} NOT NULL CHECK (length("Fingerprint") = 67),
            "Deadline" {{deadline}} NOT NULL CHECK ("Deadline" > 0)
            """;
    }

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)}) USING heap;
            CREATE INDEX IF NOT EXISTS "{DeadlineIndex}" ON "{Table}" {DeadlineColumns};
            """, token);
    }
}
