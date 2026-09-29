using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// What the finalization gate's tests share: a module of one or two synthetic families whose chains reach versions
/// 1 to 3, a context that maps their table and the finalization record, and a fleet whose members, reports and liveness
/// a test sets directly.
/// </summary>
internal static class SchemaGate
{
    public const string Module = "Tests.Gate";
    public const string Family = "GateProbe";
    public const string OtherFamily = "GateOther";

    /// <summary>The module as a build whose <see cref="Family"/> is at <paramref name="current"/> declares it: "1", "2" or "3".</summary>
    public static EfSchemaModuleFamilies Families(string current, string otherCurrent = "1") =>
        EfSchemaModuleFamilies.FromDeclarations(Module,
        [
            Declaration(Family, current) with { Entities = [typeof(GateRow)] },
            Declaration(OtherFamily, otherCurrent) with { Entities = [typeof(OtherRow)] }
        ]);

    public static EfSchemaFamilyDescriptor Declaration(string family, string current) => current switch
    {
        "1" => Descriptor(family, "1"),
        "2" => Descriptor(family, "2", Step<SyntheticOrders.AddCurrency>()),
        "3" => Descriptor(family, "3", Step<SyntheticOrders.AddCurrency>(), Step<SyntheticOrders.AddLines>()),
        // A build that has retired 1: its chain starts at 2.
        "2-3" => Descriptor(family, "3", Step<SyntheticOrders.AddLines>()),
        _ => throw new ArgumentOutOfRangeException(nameof(current), current, "The synthetic chains reach 1 to 3.")
    };

    public static EfSchemaFinalizationOptions Options() => new()
    {
        IntentWaitBound = TimeSpan.FromSeconds(2),
        IntentPollInterval = TimeSpan.FromMilliseconds(20),
        EvaluationInterval = TimeSpan.FromMilliseconds(100),
        RefreshInterval = TimeSpan.FromMilliseconds(50)
    };

    public static EfSchemaModuleGate Gate(EfSchemaModuleFamilies families, IEfSchemaFleet? fleet, EfSchemaFinalizationObservations? observations = null) =>
        new(families, fleet, observations ?? new EfSchemaFinalizationObservations(), Options());

    /// <summary>A context on <paramref name="connectionString"/> carrying the write check, whose container holds <paramref name="gates"/>.</summary>
    public static GateContext Context(string connectionString, EfSchemaFinalizationGates? gates = null, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection().AddSingleton(gates ?? new EfSchemaFinalizationGates()).BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<GateContext>()
            .UseSqlite(connectionString)
            .UseApplicationServiceProvider(services);
        EfSchemaWriteGateInterceptor.EnsureAdded(builder);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new GateContext(builder.Options);
    }
}

internal sealed class GateContext(DbContextOptions<GateContext> options) : DbContext(options)
{
    public DbSet<GateRow> Rows => Set<GateRow>();

    public DbSet<OtherRow> Others => Set<OtherRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GateRow>(row =>
        {
            row.ToTable("gate_rows");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        });
        modelBuilder.Entity<OtherRow>(row =>
        {
            row.ToTable("gate_others");
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        });
        modelBuilder.MapSchemaFinalization("ElsaGateTests").IndexSchemaVersionStamps();
    }
}

internal sealed class GateRow
{
    public string Id { get; set; } = "";

    public string SchemaVersion { get; set; } = "";

    public string Content { get; set; } = "";
}

internal sealed class OtherRow
{
    public string Id { get; set; } = "";

    public string SchemaVersion { get; set; } = "";
}
