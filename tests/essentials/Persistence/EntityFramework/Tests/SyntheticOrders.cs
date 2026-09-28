using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The synthetic family spec 180's store tests run on, whose content has moved twice: version 2 added a currency, with a
/// projection column of its own, and version 3 added order lines. Its upcasters ship committed fixture pairs and are
/// proven as a module's would be (<see cref="AddCurrencyProof"/>, <see cref="AddLinesProof"/>).
/// </summary>
public static class SyntheticOrders
{
    public const string Family = "SyntheticOrders";
    public const string Table = "orders";

    internal static readonly EfSchemaChain Chain = Declare(Family, "3", Step<AddCurrency>(), Step<AddLines>());

    public sealed record Order(string Id, int Total, string Currency, string[] Lines)
    {
        public bool Equals(Order? other) =>
            other is not null && Id == other.Id && Total == other.Total && Currency == other.Currency && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => HashCode.Combine(Id, Total, Currency);
    }

    [EfSchemaUpcaster("1", "2")]
    public sealed class AddCurrency : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content)
        {
            UpcasterCalls.Currency();
            var order = JsonNode.Parse(content.Value)!.AsObject();
            order["Currency"] = "EUR";
            return order.ToJsonString();
        }
    }

    [EfSchemaUpcaster("2", "3")]
    public sealed class AddLines : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content)
        {
            UpcasterCalls.Lines();
            var order = JsonNode.Parse(content.Value)!.AsObject();
            order["Lines"] = new JsonArray();
            return order.ToJsonString();
        }
    }

    /// <summary>The order in <paramref name="version"/>'s format: every member a later version introduced left unset.</summary>
    internal static string FormatAt(Order order, string version)
    {
        var content = new JsonObject { ["Id"] = order.Id, ["Total"] = order.Total };
        if (Chain.IsAtOrAfter(version, "2"))
            content["Currency"] = order.Currency;
        if (Chain.IsAtOrAfter(version, "3"))
            content["Lines"] = new JsonArray([.. order.Lines.Select(line => JsonValue.Create(line))]);
        return content.ToJsonString();
    }

    /// <summary>How often each upcaster has run: a test-only observation; the upcasters stay pure functions of their input.</summary>
    internal static class UpcasterCalls
    {
        private static int currency;
        private static int lines;

        public static void Currency() => Interlocked.Increment(ref currency);

        public static void Lines() => Interlocked.Increment(ref lines);

        public static (int Currency, int Lines) Snapshot() => (Volatile.Read(ref currency), Volatile.Read(ref lines));
    }
}

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.AddCurrency"/>, through the synthetic store.</summary>
public sealed class AddCurrencyProof() : EfSchemaUpcasterProof<SyntheticOrders.AddCurrency, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.AddLines"/>, through the synthetic store.</summary>
public sealed class AddLinesProof() : EfSchemaUpcasterProof<SyntheticOrders.AddLines, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>The synthetic store's half of FR-022's proofs: each read puts the fixture's order row and reads it through the store.</summary>
internal sealed class SyntheticOrdersProofStore : IEfSchemaUpcasterProofStore<SyntheticOrders.Order>, IAsyncDisposable
{
    private readonly SyntheticOrdersDatabase orders = new();

    public async Task<SyntheticOrders.Order> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, string content)
    {
        var id = JsonNode.Parse(content)!["Id"]!.GetValue<string>();
        await orders.PutAsync(id, stamp, SyntheticOrders.Chain.IsAtOrAfter(stamp, "2") ? "EUR" : null, content);
        return await orders.Store().ReadAsync(id);
    }

    public string WriteAt(EfSchemaUpcasterFixture fixture, SyntheticOrders.Order value, string version) => SyntheticOrders.FormatAt(value, version);

    public ValueTask DisposeAsync() => orders.DisposeAsync();
}

/// <summary>
/// A SQLite database of <see cref="SyntheticOrders"/> rows, and the store that reads and writes them the way every
/// first-party store now does.
/// </summary>
internal sealed class SyntheticOrdersDatabase : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase database = new("schema-chain");
    private readonly List<OrdersContext> contexts = [];

    public SyntheticOrdersDatabase()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public OrderStore Store(EfSchemaChain? chain = null)
    {
        var context = NewContext();
        lock (contexts)
            contexts.Add(context);
        return new OrderStore(context, chain ?? SyntheticOrders.Chain);
    }

    /// <summary>Stores the row as given, past the store, replacing any row with its id.</summary>
    public async Task PutAsync(string id, string stamp, string? currency, string content)
    {
        await using var context = NewContext();
        var row = await context.Orders.SingleOrDefaultAsync(order => order.Id == id);
        if (row is null)
            context.Orders.Add(row = new OrderRow { Id = id });
        row.SchemaVersion = stamp;
        row.Currency = currency;
        row.ContentJson = content;
        await context.SaveChangesAsync();
    }

    public async Task<(string Stamp, string? Currency, string Content)> RawAsync(string id)
    {
        await using var context = NewContext();
        var row = await context.Orders.AsNoTracking().SingleAsync(order => order.Id == id);
        return (row.SchemaVersion, row.Currency, row.ContentJson);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    private OrdersContext NewContext() => new(new DbContextOptionsBuilder<OrdersContext>().UseSqlite(database.ConnectionString).Options);

    /// <summary>
    /// The read and write paths of spec 180 in their prescribed order: version check, integrity clauses for the stamped
    /// version, upcast, deserialize, current validation; and a write that checks the stored row, then stamps the current
    /// version on the current format.
    /// </summary>
    public sealed class OrderStore(OrdersContext context, EfSchemaChain chain)
    {
        public async Task<SyntheticOrders.Order> ReadAsync(string id) => Read(await context.Orders.AsNoTracking().SingleAsync(order => order.Id == id), id);

        public async Task SaveAsync(SyntheticOrders.Order order)
        {
            var row = await context.Orders.SingleOrDefaultAsync(candidate => candidate.Id == order.Id);
            if (row is null)
                context.Orders.Add(row = new OrderRow { Id = order.Id });
            else
                _ = Read(row, order.Id);
            row.ContentJson = JsonSerializer.Serialize(order);
            row.Currency = order.Currency;
            row.SchemaVersion = chain.CurrentVersion;
            await context.SaveChangesAsync();
        }

        private SyntheticOrders.Order Read(OrderRow row, string id)
        {
            if (EfSchemaVersion.NotReadable(chain, row.SchemaVersion) || row.Id != id)
                throw new InvalidDataException("The order row's identity is corrupt.");
            var hasCurrency = chain.IsAtOrAfter(row.SchemaVersion, "2");
            if (hasCurrency != row.Currency is not null)
                throw new InvalidDataException("The order row's currency projection does not match its stamped version.");

            SyntheticOrders.Order order;
            try
            {
                order = JsonSerializer.Deserialize<SyntheticOrders.Order>(chain.Upcast(row.SchemaVersion, SyntheticOrders.Table, nameof(row.ContentJson), row.ContentJson))
                        ?? throw new InvalidDataException("The order content is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The order content is not valid.", exception);
            }

            if (order.Id != row.Id || order.Currency is null || order.Lines is null || hasCurrency && order.Currency != row.Currency)
                throw new InvalidDataException("The order content does not match its projections.");
            return order;
        }
    }

    public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
    {
        public DbSet<OrderRow> Orders => Set<OrderRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var order = modelBuilder.Entity<OrderRow>();
            order.ToTable(SyntheticOrders.Table);
            order.HasKey(row => row.Id);
            order.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        }
    }

    public sealed class OrderRow
    {
        public string Id { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string? Currency { get; set; }
        public string ContentJson { get; set; } = "";
    }
}
