using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The synthetic family spec 186's backfill is proven on, since every real family is still at 1.0.0: one family of
/// three tables at versions 1 and 2, and 3 where a test needs finalization to move past a completion. Orders gain a
/// currency at 2, in their content and in a projection column only version 2 and later fill, and a note at 3; order
/// lines, keyed by order and number, gain a unit at 2; receipts are content-addressed, so the backfill never rewrites
/// them. Everything here is provider-neutral and public, so the provider tests compile it in and run the
/// same family on every server engine.
/// </summary>
public static class BackfillFamily
{
    public const string Module = "Tests.Backfill";
    public const string Family = "BackfillOrders";
    public const string HistoryModule = "ElsaBackfillTests";
    public const string OrdersTable = "backfill_orders";
    public const string LinesTable = "backfill_lines";
    public const string ReceiptsTable = "backfill_receipts";
    public static readonly string[] Chain = ["1", "2"];

    /// <summary>The family as a build at <paramref name="current"/> declares it: "1", "2" with the upcaster from 1, or "3" with both.</summary>
    public static EfSchemaFamilyDescriptor Declaration(string current = "2", bool rewriter = true) =>
        new(Family, Module, current, typeof(BackfillFamily).Assembly)
        {
            Upcasters = current switch
            {
                "2" => [new EfSchemaUpcasterDescriptor(typeof(AddCurrencyAndUnit), "1", "2")],
                "3" => [new EfSchemaUpcasterDescriptor(typeof(AddCurrencyAndUnit), "1", "2"), new EfSchemaUpcasterDescriptor(typeof(AddNote), "2", "3")],
                _ => []
            },
            Entities = [typeof(BackfillOrderRow), typeof(BackfillLineRow), typeof(BackfillReceiptRow)],
            ContentAddressed = [typeof(BackfillReceiptRow)],
            Rewriter = rewriter ? typeof(BackfillRewriter) : null,
            ContentColumns =
            [
                new EfSchemaColumn(typeof(BackfillOrderRow), nameof(BackfillOrderRow.ContentJson)),
                new EfSchemaColumn(typeof(BackfillLineRow), nameof(BackfillLineRow.ContentJson)),
                new EfSchemaColumn(typeof(BackfillReceiptRow), nameof(BackfillReceiptRow.ContentJson))
            ]
        };

    public static EfSchemaModuleFamilies Families(string current = "2", bool rewriter = true) =>
        EfSchemaModuleFamilies.FromDeclarations(Module, [Declaration(current, rewriter)]);

    /// <summary>An order's content in <paramref name="version"/>'s format: version 1 has no currency, and only 3 has a note.</summary>
    public static string OrderContent(string id, int total, string? currency, string version)
    {
        var content = new JsonObject { ["Id"] = id, ["Total"] = total };
        if (version is "2" or "3")
            content["Currency"] = currency;
        if (version == "3")
            content["Note"] = "";
        return content.ToJsonString();
    }

    /// <summary>A line's content in <paramref name="version"/>'s format: version 1 has no unit.</summary>
    public static string LineContent(string sku, string? unit, string version)
    {
        var content = new JsonObject { ["Sku"] = sku };
        if (version is "2" or "3")
            content["Unit"] = unit;
        return content.ToJsonString();
    }

    /// <summary>Version 2: an order gains a currency and a line a unit; a receipt's content is its identity and never changes.</summary>
    public sealed class AddCurrencyAndUnit : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            var content = JsonNode.Parse(row[nameof(BackfillOrderRow.ContentJson)]!)!.AsObject();
            if (row.Entity == typeof(BackfillOrderRow))
                content["Currency"] = "EUR";
            else if (row.Entity == typeof(BackfillLineRow))
                content["Unit"] = "piece";
            else
                return row;
            return row.With(nameof(BackfillOrderRow.ContentJson), content.ToJsonString());
        }
    }

    /// <summary>Version 3: an order gains an empty note; lines and receipts are as they were.</summary>
    public sealed class AddNote : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            if (row.Entity != typeof(BackfillOrderRow))
                return row;
            var content = JsonNode.Parse(row[nameof(BackfillOrderRow.ContentJson)]!)!.AsObject();
            content["Note"] = "";
            return row.With(nameof(BackfillOrderRow.ContentJson), content.ToJsonString());
        }
    }

    public sealed record Order(string Id, int Total, string Currency);

    public sealed record Line(string OrderId, int Number, string Sku, string Unit);
}

public sealed class BackfillOrderRow
{
    public string Id { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public long Revision { get; set; }
    public string ContentJson { get; set; } = "";

    /// <summary>A projection version 2 introduced: null on every row an older writer left, the order's currency from 2 on.</summary>
    public string? Currency { get; set; }
}

public sealed class BackfillLineRow
{
    public string OrderId { get; set; } = "";
    public int Number { get; set; }
    public string SchemaVersion { get; set; } = "";
    public long Revision { get; set; }
    public string ContentJson { get; set; } = "";
}

/// <summary>A content-addressed row: its key is the hash of its content, so rewriting it would forge its identity.</summary>
public sealed class BackfillReceiptRow
{
    public string Hash { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string ContentJson { get; set; } = "";
}

public sealed class BackfillContext(DbContextOptions<BackfillContext> options) : DbContext(options)
{
    public DbSet<BackfillOrderRow> Orders => Set<BackfillOrderRow>();

    public DbSet<BackfillLineRow> Lines => Set<BackfillLineRow>();

    public DbSet<BackfillReceiptRow> Receipts => Set<BackfillReceiptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BackfillOrderRow>(row =>
        {
            row.ToTable(BackfillFamily.OrdersTable);
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired(false);
            row.Property(entity => entity.Revision).IsConcurrencyToken();
            row.Property(entity => entity.Currency).HasMaxLength(8);
        });
        modelBuilder.Entity<BackfillLineRow>(row =>
        {
            row.ToTable(BackfillFamily.LinesTable);
            row.HasKey(entity => new { entity.OrderId, entity.Number });
            row.Property(entity => entity.OrderId).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired(false);
            row.Property(entity => entity.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<BackfillReceiptRow>(row =>
        {
            row.ToTable(BackfillFamily.ReceiptsTable);
            row.HasKey(entity => entity.Hash);
            row.Property(entity => entity.Hash).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired(false);
        });
        modelBuilder.MapSchemaFinalization(BackfillFamily.HistoryModule).IndexSchemaVersionStamps();
    }
}

/// <summary>
/// The family's read and write paths as spec 180 prescribes them, and its rewriter over them (spec 186, FR-004): the
/// version check first, the projection's integrity clause for the stamped version, every content column upcast
/// together, then a write at the host's write version that fills the projection, writes every content column and
/// restamps the row, by compare-and-set on its revision.
/// </summary>
public sealed class BackfillRewriter(BackfillContext context, EfSchemaFinalizationGates gates, BackfillProbe probe) : IEfSchemaRowRewriter
{
    public async ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default)
    {
        probe.Asked(row);
        var gate = gates.FindForContext(typeof(BackfillContext)) ?? throw new InvalidOperationException("The module has not been admitted.");
        var chain = gate.Families.Chains.Single();
        var writeVersion = gate.StateOf(BackfillFamily.Family)!.WriteVersion;
        if (row.Entity == typeof(BackfillOrderRow))
        {
            var id = (string)row.Key[0]!;
            var stored = await context.Orders.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
            if (stored is null)
                return EfSchemaRewriteOutcome.Missing;
            var order = BackfillStore.Read(chain, stored);
            if (chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
                return EfSchemaRewriteOutcome.AlreadyCurrent;
            await probe.BeforeWriteAsync(row);
            BackfillStore.Write(stored, order, writeVersion);
        }
        else if (row.Entity == typeof(BackfillLineRow))
        {
            var (orderId, number) = ((string)row.Key[0]!, (int)row.Key[1]!);
            var stored = await context.Lines.SingleOrDefaultAsync(line => line.OrderId == orderId && line.Number == number, cancellationToken);
            if (stored is null)
                return EfSchemaRewriteOutcome.Missing;
            var line = BackfillStore.Read(chain, stored);
            if (chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
                return EfSchemaRewriteOutcome.AlreadyCurrent;
            await probe.BeforeWriteAsync(row);
            BackfillStore.Write(stored, line, writeVersion);
        }
        else
            throw new InvalidOperationException($"The backfill asked to rewrite a content-addressed {row.Entity.Name}, which it never may.");

        await context.SaveChangesAsync(cancellationToken);
        probe.Written(row);
        await probe.AfterWriteAsync(row);
        return EfSchemaRewriteOutcome.Rewritten;
    }
}

public static class BackfillStore
{
    public static BackfillFamily.Order Read(EfSchemaChain chain, BackfillOrderRow row)
    {
        EfSchemaVersion.EnsureReadable(chain, row.SchemaVersion);
        if (chain.IsAtOrAfter(row.SchemaVersion, "2") != row.Currency is not null)
            throw new InvalidDataException($"Order '{row.Id}' has a currency projection that does not match its stamp '{row.SchemaVersion}'.");
        var content = chain.Upcast<BackfillOrderRow>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson));
        var order = Parse<BackfillFamily.Order>(content[nameof(row.ContentJson)]);
        if (order.Id != row.Id || order.Currency is null || row.Currency is not null && row.Currency != order.Currency)
            throw new InvalidDataException($"Order '{row.Id}' does not match its projections.");
        return order;
    }

    public static BackfillFamily.Line Read(EfSchemaChain chain, BackfillLineRow row)
    {
        EfSchemaVersion.EnsureReadable(chain, row.SchemaVersion);
        var content = chain.Upcast<BackfillLineRow>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson));
        var node = JsonNode.Parse(content[nameof(row.ContentJson)] ?? throw new InvalidDataException("A line has no content."))!;
        return new BackfillFamily.Line(row.OrderId, row.Number, node["Sku"]!.GetValue<string>(), node["Unit"]!.GetValue<string>());
    }

    /// <summary>Writes <paramref name="order"/> in <paramref name="version"/>'s format: every content column, the projection, the stamp, and the next revision.</summary>
    public static void Write(BackfillOrderRow row, BackfillFamily.Order order, string version)
    {
        row.ContentJson = BackfillFamily.OrderContent(order.Id, order.Total, order.Currency, version);
        row.Currency = version is "2" or "3" ? order.Currency : null;
        row.SchemaVersion = version;
        row.Revision++;
    }

    public static void Write(BackfillLineRow row, BackfillFamily.Line line, string version)
    {
        row.ContentJson = BackfillFamily.LineContent(line.Sku, line.Unit, version);
        row.SchemaVersion = version;
        row.Revision++;
    }

    private static T Parse<T>(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json ?? throw new InvalidDataException("The row has no content."))
                   ?? throw new InvalidDataException("The row's content is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The row's content is not valid.", exception);
        }
    }
}

/// <summary>
/// What a test sees of, and does to, a host's rewriter: every row it was asked about and every row it wrote, and hooks
/// that run just before and just after a row's write, where a test races a live write or crashes the host.
/// </summary>
public sealed class BackfillProbe
{
    private readonly ConcurrentQueue<string> _asked = new();
    private readonly ConcurrentQueue<string> _written = new();

    public Func<EfSchemaRowToRewrite, Task>? BeforeWrite { get; set; }

    public Func<EfSchemaRowToRewrite, Task>? AfterWrite { get; set; }

    public IReadOnlyList<string> AskedRows => _asked.ToArray();

    public IReadOnlyList<string> WrittenRows => _written.ToArray();

    public void Asked(EfSchemaRowToRewrite row) => _asked.Enqueue(Key(row));

    public void Written(EfSchemaRowToRewrite row) => _written.Enqueue(Key(row));

    public Task BeforeWriteAsync(EfSchemaRowToRewrite row) => BeforeWrite?.Invoke(row) ?? Task.CompletedTask;

    public Task AfterWriteAsync(EfSchemaRowToRewrite row) => AfterWrite?.Invoke(row) ?? Task.CompletedTask;

    public static string Key(EfSchemaRowToRewrite row) => $"{row.Entity.Name}:{string.Join("/", row.Key)}";
}

/// <summary>
/// One database the family lives in, on whichever engine <paramref name="configure"/> selects: contexts for the hosts,
/// and an unchecked one standing for a writer this test's gates do not govern, such as the release before the version.
/// </summary>
public sealed class BackfillDatabase(Func<DbContextOptionsBuilder, DbContextOptionsBuilder> configure)
{
    /// <summary>A context whose container is <paramref name="services"/>, so the write check finds that host's gates.</summary>
    public BackfillContext Context(IServiceProvider? services = null)
    {
        var builder = configure(new DbContextOptionsBuilder<BackfillContext>());
        if (services is not null)
        {
            builder.UseApplicationServiceProvider(services);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder);
        }

        return new BackfillContext((DbContextOptions<BackfillContext>)builder.Options);
    }

    /// <summary>Creates the tables, and the family's record as the version-1 release left it: every row is at 1.</summary>
    public async Task CreateAsync(TimeProvider time)
    {
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        await new EfSchemaFinalizationStore(context, time).GetOrCreateAsync(BackfillFamily.Family, "1", BackfillFamily.Chain, SchemaFinalizationActor.OfOperator("release-1"));
    }

    /// <summary>Writes rows past every gate, as a writer this host does not govern would have.</summary>
    public async Task SeedAsync(params object[] rows)
    {
        await using var context = Context();
        foreach (var row in rows)
            context.Add(row);
        await context.SaveChangesAsync();
    }

    public static BackfillOrderRow Order(string id, int total, string version = "1") =>
        new() { Id = id, SchemaVersion = version, Revision = 1, ContentJson = BackfillFamily.OrderContent(id, total, "EUR", version), Currency = version is "2" or "3" ? "EUR" : null };

    public static BackfillLineRow Line(string orderId, int number, string version = "1") =>
        new() { OrderId = orderId, Number = number, SchemaVersion = version, Revision = 1, ContentJson = BackfillFamily.LineContent($"sku-{orderId}-{number}", "piece", version) };

    public static BackfillReceiptRow Receipt(string hash, string version = "1") =>
        new() { Hash = hash, SchemaVersion = version, ContentJson = new JsonObject { ["Hash"] = hash }.ToJsonString() };

    /// <summary>Every row of every table, as stored, in key order: what a test compares across runs.</summary>
    public async Task<IReadOnlyList<string>> SnapshotAsync()
    {
        await using var context = Context();
        var orders = (await context.Orders.AsNoTracking().ToListAsync()).OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => $"order {row.Id} {row.SchemaVersion} r{row.Revision} {row.Currency ?? "-"} {row.ContentJson}");
        var lines = (await context.Lines.AsNoTracking().ToListAsync()).OrderBy(row => row.OrderId, StringComparer.Ordinal).ThenBy(row => row.Number)
            .Select(row => $"line {row.OrderId}/{row.Number} {row.SchemaVersion} r{row.Revision} {row.ContentJson}");
        var receipts = (await context.Receipts.AsNoTracking().ToListAsync()).OrderBy(row => row.Hash, StringComparer.Ordinal)
            .Select(row => $"receipt {row.Hash} {row.SchemaVersion} {row.ContentJson}");
        return [.. orders, .. lines, .. receipts];
    }

    public async Task<SchemaFinalizationRecord> RecordAsync()
    {
        await using var context = Context();
        return (await new EfSchemaFinalizationStore(context).FindAsync(BackfillFamily.Family))!;
    }
}

/// <summary>
/// One host of a backfill test: its container, with its own gates for the write check, its rewriter's probe and its
/// contexts; the module's finalization gate; and its backfill worker. Rounds are run directly, so a test decides exactly
/// when each happens.
/// </summary>
public sealed class BackfillHost : IAsyncDisposable
{
    private readonly BackfillDatabase _database;
    private readonly ServiceProvider _services;
    private readonly EfSchemaFinalizationGates _gates = new();

    public BackfillHost(
        BackfillDatabase database,
        IEfSchemaFleet? fleet,
        EfSchemaBackfillOptions options,
        TimeProvider time,
        EfSchemaModuleFamilies? families = null,
        EfSchemaFinalizationObservations? observations = null)
    {
        _database = database;
        Probe = new BackfillProbe();
        Observations = observations ?? new EfSchemaFinalizationObservations();
        _services = new ServiceCollection()
            .AddSingleton(_gates)
            .AddSingleton(Probe)
            .AddScoped(provider => database.Context(provider))
            .BuildServiceProvider();
        Gate = new EfSchemaModuleGate(
            families ?? BackfillFamily.Families(),
            fleet,
            Observations,
            new EfSchemaFinalizationOptions { IntentWaitBound = TimeSpan.FromSeconds(2), IntentPollInterval = TimeSpan.FromMilliseconds(20) },
            time);
        Backfill = new EfSchemaBackfill(Gate, fleet, options, time);
        Gate.UseBackfill(Backfill);
    }

    public EfSchemaModuleGate Gate { get; }

    public EfSchemaBackfill Backfill { get; }

    public BackfillProbe Probe { get; }

    public EfSchemaFinalizationObservations Observations { get; }

    public EfSchemaBackfillStatus Status => Backfill.StatusOf(BackfillFamily.Family);

    /// <summary>What the module's migrator does once the schema is current: admit the module and register its gate.</summary>
    public async Task ActivateAsync()
    {
        await using (var context = _database.Context(_services))
            await Gate.ActivateAsync(context);
        _gates.Register(typeof(BackfillContext), Gate);
    }

    public async Task RefreshAsync()
    {
        await using var context = _database.Context(_services);
        await Gate.RefreshAsync(context);
    }

    public async Task EvaluateAsync()
    {
        await using var context = _database.Context(_services);
        await Gate.EvaluateAsync(context);
        await Gate.RefreshAsync(context);
    }

    public Task RunOnceAsync(CancellationToken cancellationToken = default) => Backfill.RunOnceAsync(WithScopeAsync, cancellationToken);

    /// <summary>A context of this host, carrying its write check.</summary>
    public BackfillContext Context() => _database.Context(_services);

    /// <summary>A live write of <paramref name="order"/> through the family's write path, at this host's write version.</summary>
    public async Task SaveOrderAsync(BackfillFamily.Order order)
    {
        await using var context = Context();
        var stored = await context.Orders.SingleAsync(row => row.Id == order.Id);
        var chain = Gate.Families.Chains.Single();
        _ = BackfillStore.Read(chain, stored);
        BackfillStore.Write(stored, order, Gate.StateOf(BackfillFamily.Family)!.WriteVersion);
        await context.SaveChangesAsync();
    }

    public async Task WithScopeAsync(Func<EfSchemaBackfillScope, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        await action(new EfSchemaBackfillScope(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<BackfillContext>()));
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

/// <summary>
/// A host alone in its fleet, as the in-process membership makes it: it reads everything its build reads, and the
/// settle condition holds once its own gate has observed the version.
/// </summary>
public sealed class SoloBackfillFleet(EfSchemaFinalizationObservations observations, string hostId = "solo") : IEfSchemaFleet
{
    private readonly SchemaFinalizationMember _member = new(hostId, Guid.NewGuid().ToString("N"));

    public TimeSpan SettleMargin { get; set; } = TimeSpan.Zero;

    public EfSchemaFleetStanding GetLocalStanding() => new(_member, false);

    public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));

    public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default)
    {
        var observed = observations.Find(family).ObservedFinalizedVersion;
        return ValueTask.FromResult(observed is not null && versions.Contains(observed, StringComparer.Ordinal)
            ? new EfSchemaFleetAnswer(true, [])
            : new EfSchemaFleetAnswer(false, [$"{hostId} has observed [{observed ?? "nothing"}]"]));
    }

    public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
}

/// <summary>A clock a test moves by hand, for claims, the settle margin and the audit interval.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
