using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>Immutable choice of owner mode and auxiliary actor type for a shared shipped store.</summary>
internal static class RootScheduleOwnerMode
{
    internal const string Table = "FoundationScheduleOwnerMode";

    internal static bool Applies(KernelDb db) => db.UsesRootScheduleControls &&
        (db.RequiredFormat == StoreFormat.RootShipped || db.UsesRootAuthoredEvents || db.UsesRootDirectTestRuns);

    // Seed only as part of creating the store. A missing row on an existing store is damage, not an unbound owner.
    internal static void ConfigureModel(ModelBuilder model, bool sqlite)
    {
        model.Entity<OwnerModeRow>(entity =>
        {
            entity.ToTable(Table, table => table.HasCheckConstraint("CK_FoundationScheduleOwnerMode_Singleton", """ "Singleton" = 1 """));
            entity.HasKey(row => row.Singleton);
            entity.Property(row => row.Singleton).ValueGeneratedNever();
            entity.Property(row => row.ScheduleActor).UseCollation(sqlite ? "BINARY" : "C");
            entity.HasData(new OwnerModeRow { Singleton = 1 });
        });
    }

    internal static Task SeedSqliteAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""INSERT INTO "FoundationScheduleOwnerMode" ("Singleton") VALUES (1)""", token);

    internal static async Task BindAsync(KernelDb db, string? auxiliaryActor, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        // Null is unbound; an empty actor type is a bound standalone owner. A contender never overwrites the winner.
        var expected = auxiliaryActor ?? "";
        await db.Database.ExecuteSqlInterpolatedAsync(BindSource(db, $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            UPDATE /*direct*/"FoundationScheduleOwnerMode" SET "ScheduleActor" = {expected}
            WHERE "Singleton" = 1 AND "ScheduleActor" IS NULL AND EXISTS (SELECT 1 FROM source WHERE "SourceReady")
            """, bindSource), token);
        await RequireAsync(db, expected, token, bindSource);
    }

    internal static async Task RequireAsync(KernelDb db, string expected, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        var saved = await db.Database.SqlQuery<OwnerMode>(BindSource(db, $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", mode."Singleton", mode."ScheduleActor"
            FROM source LEFT JOIN /*direct*/"FoundationScheduleOwnerMode" mode ON source."SourceReady" AND mode."Singleton" = 1
            """, bindSource)).SingleAsync(token);
        if (!saved.SourceReady)
            throw new StoreFormatException("The schedule owner mode differs from the selected physical root store.");
        if (saved.Singleton is null)
            throw new StoreFormatException("The schedule owner mode is missing from the installed root store.");
        if (saved.ScheduleActor is null)
            throw new NotSupportedException("Start the managed schedule owner before admitting schedule work.");
        if (!StringComparer.Ordinal.Equals(saved.ScheduleActor, expected))
            throw new NotSupportedException("This root store is bound to a different schedule owner mode or actor type.");
    }

    private static FormattableString BindSource(KernelDb db, FormattableString query,
        Func<FormattableString, FormattableString>? bindSource)
    {
        var direct = db.Database.IsNpgsql() ? "ONLY " : "";
        var format = query.Format.Replace("/*direct*/", direct, StringComparison.Ordinal);
        return RootScheduleInventory.Bind(FormattableStringFactory.Create(format, query.GetArguments()), bindSource);
    }

    private sealed class OwnerModeRow
    {
        public int Singleton { get; init; }
        public string? ScheduleActor { get; init; }
    }

    private sealed record OwnerMode(bool SourceReady, int? Singleton, string? ScheduleActor);
}
