using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kernel.Durable;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>The exact root prepared for a held start before its executable can be pinned.</summary>
internal sealed record PreparedRootStart(string Tenant, string Reference, string Actor, int Partition,
    string Operation, string Fingerprint, long Deadline);

internal static class RootPreparedStarts
{
    internal const string Table = "FoundationPreparedRootStarts";
    internal const string DeadlineIndex = "IX_FoundationPreparedRootStarts_Deadline";
    internal const string DeadlineColumns = """("Deadline", "Reference")""";

    internal const string Source = "/*prepared-start-source*/";
    private const string IssuancePrefix = "held-root/1.";
    private const int MaximumIssuanceLength = 2_048;

    internal static PendingRootStart Offer(PreparedRootStart stage, StartSpec spec, byte[] key)
    {
        if (!Valid(stage, key)) throw new InvalidOperationException("Held root preparation has an invalid identity.");
        var body = IssuancePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(DurableKeyJson.Write(stage)));
        var issuance = body + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(body)));
        if (issuance.Length > MaximumIssuanceLength)
            throw new InvalidOperationException("Held root preparation exceeds its issuance limit.");
        return new(stage.Tenant, new(stage.Reference), spec.Type, spec.TypeData, issuance);
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

    private static async Task<bool> ChangeAsync(KernelDb db, FormattableString query, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource)
    {
        if (!db.UsesRootPreparedStarts || db.Database.CurrentTransaction is null || !db.UsesSqliteRootRuntime && bindSource is null)
            throw new NotSupportedException("Held root preparation requires a bound provider write transaction.");

        // Keep the source selection in the same statement as the mutation.
        var format = query.Format.Replace("/*prepared-only*/", db.UsesSqliteRootRuntime ? "" : "ONLY ", StringComparison.Ordinal);
        query = FormattableStringFactory.Create(format, query.GetArguments());
        var bound = bindSource is not null ? bindSource(query) : FormattableStringFactory.Create(
            format.Replace(Source, """SELECT true AS "SourceReady" """, StringComparison.Ordinal), query.GetArguments());
        return await db.Database.ExecuteSqlInterpolatedAsync(bound, token) == 1;
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
