using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal sealed record RootPublicEventHost(string ActorType, bool ProtectsStarts)
{
    internal const string Table = "FoundationPublicEventHost";

    internal static string Columns(bool sqlite) => sqlite
        ? """
          "Singleton" INTEGER PRIMARY KEY CHECK ("Singleton" = 1),
          "ActorType" TEXT COLLATE BINARY NOT NULL,
          "ProtectsStarts" INTEGER NOT NULL CHECK ("ProtectsStarts" IN (0, 1)),
          FOREIGN KEY ("Singleton") REFERENCES "FoundationRootIssuer" ("Singleton")
          """
        : """
          "Singleton" boolean PRIMARY KEY CHECK ("Singleton"),
          "ActorType" text COLLATE "C" NOT NULL,
          "ProtectsStarts" boolean NOT NULL,
          FOREIGN KEY ("Singleton") REFERENCES "FoundationRootIssuer" ("Singleton")
          """;

    private sealed record BoundHost(bool SourceReady, string? ActorType, bool? ProtectsStarts);

    internal static async Task InstallAsync(KernelDb db, RootPublicEventHost? expected, bool create, CancellationToken token)
    {
        if (expected is null) throw new StoreFormatException("This Event profile requires an installed host authority.");
        if (create)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationPublicEventHost" ("Singleton", "ActorType", "ProtectsStarts")
                VALUES (true, {expected.ActorType}, {expected.ProtectsStarts})
                """, token);
        await RequireAsync(db, expected, bind: null, token);
    }

    internal static async Task RequireAsync(KernelDb db, RootPublicEventHost expected, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        if (db.UsesRootPublicEventHost && await ReadAsync(db, bind, token) != expected)
            throw new StoreFormatException("The public Event host authority differs from the configured actor type or root start protection.");
    }

    internal static async Task<RootPublicEventHost?> ReadAsync(KernelDb db, Func<FormattableString, FormattableString>? bind,
        CancellationToken token)
    {
        if (!db.UsesRootPublicEventHost) return null;
        var row = await db.Database.SqlQuery<BoundHost>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            SELECT source."SourceReady", host."ActorType", host."ProtectsStarts"
            FROM source LEFT JOIN "FoundationPublicEventHost" host ON source."SourceReady" AND host."Singleton" = true
            """, bind)).SingleAsync(token);
        if (!row.SourceReady || row.ActorType is null || row.ProtectsStarts is null)
            throw new StoreFormatException("The installed public Event host authority is unavailable.");
        var installed = new RootPublicEventHost(row.ActorType, row.ProtectsStarts.Value);
        if (db.PublicEventHost is { } expected && installed != expected)
            throw new StoreFormatException("The public Event host authority differs from the configured actor type or root start protection.");
        return installed;
    }
}
