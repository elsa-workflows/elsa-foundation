using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>Immutable choice of owner mode and auxiliary actor type for a shared shipped store.</summary>
internal static class RootScheduleOwnerMode
{
    internal const string Table = "FoundationScheduleOwnerMode";

    internal static bool Applies(KernelDb db) => db.RequiredFormat == StoreFormat.RootShipped && db.UsesRootScheduleControls;

    internal static string Columns(bool sqlite)
    {
        var integer = sqlite ? "INTEGER" : "integer";
        var text = sqlite ? "TEXT COLLATE BINARY" : """text COLLATE "C" """;
        return $"""
            "Singleton" {integer} PRIMARY KEY CHECK ("Singleton" = 1),
            "ScheduleActor" {text} NOT NULL
            """;
    }

    internal static Task InstallPostgresAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)})
            """, token);

    internal static async Task BindAsync(KernelDb db, string? auxiliaryActor, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        // The empty type denotes a standalone owner. A contender never overwrites the winner's choice.
        var expected = auxiliaryActor ?? "";
        await db.Database.ExecuteSqlInterpolatedAsync(RootScheduleInventory.Bind($"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            INSERT INTO "FoundationScheduleOwnerMode" ("Singleton", "ScheduleActor")
            SELECT 1, {expected} FROM source WHERE "SourceReady"
                AND NOT EXISTS (SELECT 1 FROM "FoundationScheduleRoots")
                AND NOT EXISTS (SELECT 1 FROM "FoundationScheduleControls")
            ON CONFLICT ("Singleton") DO NOTHING
            """, bindSource), token);
        await RequireAsync(db, expected, token, bindSource, binding: true);
    }

    internal static async Task RequireAsync(KernelDb db, string expected, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null, bool binding = false)
    {
        var saved = await db.Database.SqlQuery<OwnerMode>(RootScheduleInventory.Bind($"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", mode."ScheduleActor"
            FROM source LEFT JOIN "FoundationScheduleOwnerMode" mode ON source."SourceReady" AND mode."Singleton" = 1
            """, bindSource)).SingleAsync(token);
        if (!saved.SourceReady)
            throw new StoreFormatException("The schedule owner mode differs from the selected physical root store.");
        if (saved.ScheduleActor is null)
        {
            if (binding) throw new StoreFormatException("The schedule owner mode is missing from a store with durable schedule work.");
            throw new NotSupportedException("Start the managed schedule owner before admitting schedule work.");
        }
        if (!StringComparer.Ordinal.Equals(saved.ScheduleActor, expected))
            throw new NotSupportedException("This root store is bound to a different schedule owner mode or actor type.");
    }

    private sealed record OwnerMode(bool SourceReady, string? ScheduleActor);
}
