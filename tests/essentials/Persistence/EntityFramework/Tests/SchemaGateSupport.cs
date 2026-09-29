using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
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

/// <summary>
/// One member of a <see cref="FakeFleetState"/>: what it reads of each family, for which database, and whether a reader
/// counts it. It is counted only once it has published, exactly as a real provider shows a member only after its
/// report is visible.
/// </summary>
internal sealed class FakeMember(string hostId)
{
    public string HostId { get; } = hostId;

    public string Incarnation { get; set; } = Guid.NewGuid().ToString("N");

    public Dictionary<string, (string[] Readable, string? Identity)> Reads { get; } = new(StringComparer.Ordinal);

    public bool Published { get; set; }

    public bool Live { get; set; } = true;

    public bool Lapsed { get; set; }

    public bool ReportUnknown { get; set; }

    public FakeMember Reading(string family, params string[] readable)
    {
        Reads[family] = (readable, null);
        return this;
    }

    public override string ToString() => $"{HostId} ({Incarnation[..6]})";
}

/// <summary>The one fleet every <see cref="FakeFleet"/> of a test reads, as a durable provider's store would be.</summary>
internal sealed class FakeFleetState
{
    private readonly object _lock = new();
    private readonly List<FakeMember> _members = [];
    private CancellationTokenSource _changes = new();

    public FakeMember Add(FakeMember member)
    {
        lock (_lock)
            _members.Add(member);
        return member;
    }

    /// <summary>Counts every published, live member whose report speaks for the database, as spec 183's FR-023 does.</summary>
    public EfSchemaFleetAnswer Count(string family, string version, string databaseIdentity)
    {
        lock (_lock)
        {
            var blockers = new List<string>();
            foreach (var member in _members.Where(member => member is { Published: true, Live: true }))
            {
                if (member.ReportUnknown)
                {
                    blockers.Add($"{member} (unknown)");
                    continue;
                }

                if (!member.Reads.TryGetValue(family, out var entry) || entry.Identity is not null && entry.Identity != databaseIdentity)
                    continue;
                if (!entry.Readable.Contains(version, StringComparer.Ordinal))
                    blockers.Add($"{member} reads [{string.Join(", ", entry.Readable)}]");
            }

            return new EfSchemaFleetAnswer(blockers.Count == 0, blockers);
        }
    }

    public void SignalChange()
    {
        CancellationTokenSource previous;
        lock (_lock)
            (previous, _changes) = (_changes, new CancellationTokenSource());
        previous.Cancel();
    }

    public IChangeToken ChangeToken()
    {
        lock (_lock)
            return new CancellationChangeToken(_changes.Token);
    }
}

/// <summary>
/// One member's view of a <see cref="FakeFleetState"/>. Every read is fresh. <see cref="BeforeCount"/> and
/// <see cref="BeforePublish"/> let a test run something at exactly the point the gate reads or publishes.
/// </summary>
internal sealed class FakeFleet(FakeFleetState state, FakeMember self) : IEfSchemaFleet
{
    public FakeMember Self { get; } = self;

    public Func<int, Task>? BeforeCount { get; set; }

    public Func<Task>? BeforePublish { get; set; }

    public Func<Task>? AfterCount { get; set; }

    public int Counts { get; private set; }

    public int Publishes { get; private set; }

    public bool FailPublish { get; set; }

    public EfSchemaFleetStanding GetLocalStanding() => new(new SchemaFinalizationMember(Self.HostId, Self.Incarnation), Self.Lapsed);

    public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
    {
        if (BeforePublish is { } before)
            await before();
        if (FailPublish)
            throw new InvalidOperationException($"{Self} has not joined.");
        Publishes++;
        Self.Published = true;
    }

    public async ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default)
    {
        var count = ++Counts;
        if (BeforeCount is { } before)
            await before(count);
        var answer = state.Count(family, version, databaseIdentity);
        if (AfterCount is { } after)
            await after();
        return answer;
    }

    public IChangeToken GetChangeToken() => state.ChangeToken();
}
