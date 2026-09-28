using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 180's read and write paths end to end, through a store written the way every first-party store now is, over a
/// synthetic family whose content has moved twice: version 2 added a currency, with a projection column of its own, and
/// version 3 added order lines. A row stamped 1 is read through the chain, left untouched by the read, and upgraded when
/// it is next written; rows the chain cannot place are skew, and a readable row whose content is damaged is corruption.
/// </summary>
public sealed class EfSchemaChainStoreTests : IAsyncDisposable
{
    private const string Table = "orders";
    private static readonly EfSchemaChain Orders = Declare("SyntheticOrders", "3", Step<AddCurrency>(), Step<AddLines>());
    private static readonly Order Expected = new("order-1", 42, "EUR", []);

    private readonly TemporarySqliteDatabase database = new("schema-chain");
    private readonly List<OrdersContext> contexts = [];

    public EfSchemaChainStoreTests()
    {
        using var context = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseSqlite(database.ConnectionString).Options);
        context.Database.EnsureCreated();
    }

    /// <summary>US1, scenario 1: a version-1 row reads as the domain value the version-3 row of the same order does.</summary>
    [Fact]
    public async Task A_row_two_versions_behind_reads_as_the_current_version_would()
    {
        await InsertAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");
        await InsertAsync("order-3", "3", "EUR", """{"Id":"order-3","Total":42,"Currency":"EUR","Lines":[]}""");

        Assert.Equal(Expected, await Store().ReadAsync("order-1"));
        Assert.Equal(Expected with { Id = "order-3" }, await Store().ReadAsync("order-3"));
    }

    /// <summary>US1, scenario 2: a read never rewrites a row, so its stamp and its stored content stay exactly as they were.</summary>
    [Fact]
    public async Task Reading_an_older_row_leaves_its_stamp_and_bytes_unchanged()
    {
        const string stored = """{"Id":"order-1","Total":42}""";
        await InsertAsync("order-1", "1", null, stored);

        _ = await Store().ReadAsync("order-1");

        Assert.Equal(("1", (string?)null, stored), await RawAsync("order-1"));
    }

    /// <summary>
    /// FR-013 and FR-014: the next write stamps the current version and writes the current format, which is how a row
    /// moves forward; the row then reads without any upcaster running.
    /// </summary>
    [Fact]
    public async Task A_row_read_two_versions_behind_is_upgraded_when_it_is_next_written()
    {
        await InsertAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");

        var read = await Store().ReadAsync("order-1");
        await Store().SaveAsync(read with { Total = 43 });

        var (stamp, currency, content) = await RawAsync("order-1");
        Assert.Equal(Orders.CurrentVersion, stamp);
        Assert.Equal("EUR", currency);
        Assert.Equal(Expected with { Total = 43 }, JsonSerializer.Deserialize<Order>(content));
        var calls = UpcasterCalls.Snapshot();
        Assert.Equal(Expected with { Total = 43 }, await Store().ReadAsync("order-1"));
        Assert.Equal(calls, UpcasterCalls.Snapshot());
    }

    /// <summary>US1, scenario 3 and FR-021: a row at the current version is read without running any upcaster.</summary>
    [Fact]
    public async Task A_row_at_the_current_version_runs_no_upcaster()
    {
        await InsertAsync("order-3", "3", "EUR", """{"Id":"order-3","Total":42,"Currency":"EUR","Lines":[]}""");
        var calls = UpcasterCalls.Snapshot();

        _ = await Store().ReadAsync("order-3");

        Assert.Equal(calls, UpcasterCalls.Snapshot());
    }

    /// <summary>US2, scenarios 1 and 3: above the chain, below it and unstamped are skew, and never reported as corruption.</summary>
    [Theory]
    [InlineData("4")]
    [InlineData("0")]
    [InlineData("")]
    public async Task A_row_the_chain_cannot_place_is_skew_and_never_corruption(string stamp)
    {
        await InsertAsync("order-x", stamp, "EUR", "not-json");

        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().ReadAsync("order-x"));

        Assert.Equal("SyntheticOrders", skew.Module);
        Assert.Equal(stamp, skew.Found);
        Assert.Equal(["1", "2", "3"], skew.ReadableVersions);
    }

    /// <summary>US2, scenario 2 at read time: below a gap is skew, whatever upcasters the chain declares further down.</summary>
    [Fact]
    public async Task A_row_below_a_gap_is_skew_while_one_above_it_still_reads()
    {
        var gapped = Declare("SyntheticOrders", "3", Step<CurrencyFromNothing>(), Step<AddLines>());
        await InsertAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");
        await InsertAsync("order-2", "2", "EUR", """{"Id":"order-2","Total":42,"Currency":"EUR"}""");

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store(gapped).ReadAsync("order-1"));
        Assert.Equal(Expected with { Id = "order-2" }, await Store(gapped).ReadAsync("order-2"));
    }

    /// <summary>US2, scenario 4: a readable version whose content is not that version's shape is a damaged row.</summary>
    [Fact]
    public async Task A_readable_row_whose_content_is_damaged_is_corruption()
    {
        await InsertAsync("order-1", "1", null, "not-json");

        await Assert.ThrowsAsync<InvalidDataException>(() => Store().ReadAsync("order-1"));
    }

    /// <summary>
    /// FR-008: the currency projection exists from version 2 on. A version-1 row carries none and passes; a version-1 row
    /// that carries one, or a version-2 row that lacks it, fails the integrity clause for its own stamped version.
    /// </summary>
    [Theory]
    [InlineData("1", "EUR", """{"Id":"order-1","Total":42}""")]
    [InlineData("2", null, """{"Id":"order-1","Total":42,"Currency":"EUR"}""")]
    public async Task A_projection_is_checked_against_the_definition_of_the_rows_stamped_version(string stamp, string? currency, string content)
    {
        await InsertAsync("order-1", stamp, currency, content);

        await Assert.ThrowsAsync<InvalidDataException>(() => Store().ReadAsync("order-1"));
    }

    /// <summary>FR-018: a write never overwrites a row whose stamp it cannot read, so a newer row's content is never lost.</summary>
    [Fact]
    public async Task A_write_refuses_to_replace_a_row_it_cannot_read()
    {
        const string newer = """{"Id":"order-1","Total":42,"Currency":"EUR","Lines":[],"Gift":true}""";
        await InsertAsync("order-1", "4", "EUR", newer);

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => Store().SaveAsync(Expected));

        Assert.Equal(("4", "EUR", newer), await RawAsync("order-1"));
    }

    [Fact]
    public async Task Concurrent_readers_of_an_older_row_all_see_the_current_value_and_leave_the_row_as_it_was()
    {
        const string stored = """{"Id":"order-1","Total":42}""";
        await InsertAsync("order-1", "1", null, stored);

        var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Store().ReadAsync("order-1"))));

        Assert.All(reads, read => Assert.Equal(Expected, read));
        Assert.Equal(("1", (string?)null, stored), await RawAsync("order-1"));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    private OrderStore Store(EfSchemaChain? chain = null)
    {
        var context = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseSqlite(database.ConnectionString).Options);
        lock (contexts)
            contexts.Add(context);
        return new OrderStore(context, chain ?? Orders);
    }

    private async Task InsertAsync(string id, string stamp, string? currency, string content)
    {
        await using var context = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseSqlite(database.ConnectionString).Options);
        context.Orders.Add(new OrderRow { Id = id, SchemaVersion = stamp, Currency = currency, ContentJson = content });
        await context.SaveChangesAsync();
    }

    private async Task<(string Stamp, string? Currency, string Content)> RawAsync(string id)
    {
        await using var context = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseSqlite(database.ConnectionString).Options);
        var row = await context.Orders.AsNoTracking().SingleAsync(order => order.Id == id);
        return (row.SchemaVersion, row.Currency, row.ContentJson);
    }

    /// <summary>
    /// The read and write paths of spec 180 in their prescribed order: version check, integrity clauses for the stamped
    /// version, upcast, deserialize, current validation; and a write that checks the stored row, then stamps the current
    /// version on the current format.
    /// </summary>
    private sealed class OrderStore(OrdersContext context, EfSchemaChain chain)
    {
        public async Task<Order> ReadAsync(string id) => Read(await context.Orders.AsNoTracking().SingleAsync(order => order.Id == id), id);

        public async Task SaveAsync(Order order)
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

        private Order Read(OrderRow row, string id)
        {
            if (EfSchemaVersion.NotReadable(chain, row.SchemaVersion) || row.Id != id)
                throw new InvalidDataException("The order row's identity is corrupt.");
            var hasCurrency = chain.IsAtOrAfter(row.SchemaVersion, "2");
            if (hasCurrency != row.Currency is not null)
                throw new InvalidDataException("The order row's currency projection does not match its stamped version.");

            Order order;
            try
            {
                order = JsonSerializer.Deserialize<Order>(chain.Upcast(row.SchemaVersion, Table, nameof(row.ContentJson), row.ContentJson))
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

    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
    {
        public DbSet<OrderRow> Orders => Set<OrderRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var order = modelBuilder.Entity<OrderRow>();
            order.ToTable(Table);
            order.HasKey(row => row.Id);
            order.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        }
    }

    private sealed class OrderRow
    {
        public string Id { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string? Currency { get; set; }
        public string ContentJson { get; set; } = "";
    }

    private sealed record Order(string Id, int Total, string Currency, string[] Lines)
    {
        public bool Equals(Order? other) =>
            other is not null && Id == other.Id && Total == other.Total && Currency == other.Currency && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => HashCode.Combine(Id, Total, Currency);
    }

    /// <summary>How often each upcaster has run: a test-only observation; the upcasters stay pure functions of their input.</summary>
    private static class UpcasterCalls
    {
        private static int currency;
        private static int lines;

        public static void Currency() => Interlocked.Increment(ref currency);

        public static void Lines() => Interlocked.Increment(ref lines);

        public static (int Currency, int Lines) Snapshot() => (Volatile.Read(ref currency), Volatile.Read(ref lines));
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class AddCurrency : IEfSchemaUpcaster
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
    private sealed class AddLines : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content)
        {
            UpcasterCalls.Lines();
            var order = JsonNode.Parse(content.Value)!.AsObject();
            order["Lines"] = new JsonArray();
            return order.ToJsonString();
        }
    }

    /// <summary>An upcaster into a version below the gap, so the chain declares something under it that must stay unused.</summary>
    [EfSchemaUpcaster("0", "1")]
    private sealed class CurrencyFromNothing : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }
}
