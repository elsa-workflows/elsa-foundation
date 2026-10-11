using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.Schema;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The synthetic family spec 180's store tests run on, whose rows have moved four times: version 2 added a currency, with
/// a projection column of its own; version 3 added order lines to the order document; version 4 moved the lines into
/// a content column of their own, in one step over the whole row (#2144); and version 5 completes that move by dropping
/// the order document's own copy of the lines, the removal half of FR-027's expand-only split. Its upcasters ship
/// committed fixture pairs and are proven as a module's would be (<see cref="AddCurrencyProof"/>,
/// <see cref="AddLinesProof"/>, <see cref="MoveLinesProof"/>, <see cref="RemoveLinesProof"/>).
/// </summary>
public static class SyntheticOrders
{
    public const string Family = "SyntheticOrders";
    public const string Table = "orders";
    public const string ContentColumn = nameof(SyntheticOrdersDatabase.OrderRow.ContentJson);
    public const string LinesColumn = nameof(SyntheticOrdersDatabase.OrderRow.LinesJson);

    internal static readonly EfSchemaChain Chain = Declare("5", Step<AddCurrency>(), Step<AddLines>(), Step<MoveLines>(), Step<RemoveLines>());

    /// <summary>A chain of this family's table at <paramref name="current"/>, over <paramref name="upcasters"/>, oldest first.</summary>
    internal static EfSchemaChain Declare(string current, params EfSchemaUpcasterDescriptor[] upcasters) =>
        EfSchemaChain.For(Descriptor(Family, current, upcasters) with
        {
            ContentColumns = [new EfSchemaColumn(typeof(SyntheticOrdersDatabase.OrderRow), ContentColumn), new EfSchemaColumn(typeof(SyntheticOrdersDatabase.OrderRow), LinesColumn)]
        });

    public sealed record Order(string Id, int Total, string Currency, string[] Lines)
    {
        public bool Equals(Order? other) =>
            other is not null && Id == other.Id && Total == other.Total && Currency == other.Currency && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => HashCode.Combine(Id, Total, Currency);
    }

    [EfSchemaUpcaster("1", "2")]
    public sealed class AddCurrency : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            UpcasterCalls.Currency();
            var order = JsonNode.Parse(row[ContentColumn]!)!.AsObject();
            order["Currency"] = "EUR";
            return row.With(ContentColumn, order.ToJsonString());
        }
    }

    [EfSchemaUpcaster("2", "3")]
    public sealed class AddLines : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            UpcasterCalls.Lines();
            var order = JsonNode.Parse(row[ContentColumn]!)!.AsObject();
            order["Lines"] = new JsonArray();
            return row.With(ContentColumn, order.ToJsonString());
        }
    }

    /// <summary>
    /// Version 4 moves the order's lines into a column of their own, in one step over the whole row: it reads them from
    /// the order document and writes them to the lines column, which every read from version 4 on takes them from. The
    /// document keeps its copy: FR-027 adds the new member beside the old one and removes the old one only at a later
    /// version, once no version the family can still finalize reads it, so version 3's format is still written by leaving
    /// the lines column unset, never by a reverse transform.
    /// </summary>
    [EfSchemaUpcaster("3", "4")]
    public sealed class MoveLines : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            UpcasterCalls.Move();
            var lines = JsonNode.Parse(row[ContentColumn]!)!["Lines"]!.AsArray();
            return row.With(LinesColumn, lines.ToJsonString());
        }
    }

    /// <summary>
    /// Version 5 completes the move version 4 began: the order document's own copy of the lines, kept beside the new
    /// column so a version that still read the document directly was never broken, is dropped now that every read takes
    /// the lines from their own column alone. This is FR-027's removal half of the expand-only split - the member is
    /// removed only once no version the family can still finalize reads it from the document - and FR-022's old-format
    /// round trip still holds for it: writing the domain value back at version 4's format restores the document's copy,
    /// since a removal step introduces no member of its own for that proof to leave unset, and <see cref="FormatAt"/>
    /// reconstructs every historical shape from the version alone, not by reversing this step (2026-09-29 note).
    /// </summary>
    [EfSchemaUpcaster("4", "5")]
    public sealed class RemoveLines : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            UpcasterCalls.Remove();
            var order = JsonNode.Parse(row[ContentColumn]!)!.AsObject();
            order.Remove("Lines");
            return row.With(ContentColumn, order.ToJsonString());
        }
    }

    /// <summary>The order in <paramref name="version"/>'s format: every member and column a later version introduced left unset.</summary>
    internal static EfSchemaRowContent FormatAt(Order order, string version)
    {
        var content = new JsonObject { ["Id"] = order.Id, ["Total"] = order.Total };
        if (Chain.IsAtOrAfter(version, "2"))
            content["Currency"] = order.Currency;
        // The document's own copy of the lines lives only from version 3, when it was their sole source, through
        // version 4, when the column introduced that version still kept it beside them; version 5 removes it (#2144).
        if (Chain.IsAtOrAfter(version, "3") && !Chain.IsAtOrAfter(version, "5"))
            content["Lines"] = LinesOf(order);
        return new EfSchemaRowContent(
            typeof(SyntheticOrdersDatabase.OrderRow),
            (ContentColumn, content.ToJsonString()),
            (LinesColumn, Chain.IsAtOrAfter(version, "4") ? LinesOf(order).ToJsonString() : null));
    }

    private static JsonArray LinesOf(Order order) => new([.. order.Lines.Select(line => JsonValue.Create(line))]);

    /// <summary>How often each upcaster has run: a test-only observation; the upcasters stay pure functions of their input.</summary>
    internal static class UpcasterCalls
    {
        private static readonly AsyncLocal<Scope?> Current = new();

        public static Scope Begin() => new();

        public static void Currency() => Current.Value?.RecordCurrency();

        public static void Lines() => Current.Value?.RecordLines();

        public static void Move() => Current.Value?.RecordMove();

        public static void Remove() => Current.Value?.RecordRemove();

        internal sealed class Scope : IDisposable
        {
            private readonly Scope? previous;
            private int currency;
            private int lines;
            private int move;
            private int remove;
            private bool disposed;

            internal Scope()
            {
                previous = Current.Value;
                Current.Value = this;
            }

            internal void RecordCurrency() => Interlocked.Increment(ref currency);

            internal void RecordLines() => Interlocked.Increment(ref lines);

            internal void RecordMove() => Interlocked.Increment(ref move);

            internal void RecordRemove() => Interlocked.Increment(ref remove);

            public (int Currency, int Lines, int Move, int Remove) Snapshot() =>
                (Volatile.Read(ref currency), Volatile.Read(ref lines), Volatile.Read(ref move), Volatile.Read(ref remove));

            public void Dispose()
            {
                if (disposed)
                    return;

                Current.Value = previous;
                disposed = true;
            }
        }
    }
}

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.AddCurrency"/>, through the synthetic store.</summary>
public sealed class AddCurrencyProof() : EfSchemaUpcasterProof<SyntheticOrders.AddCurrency, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.AddLines"/>, through the synthetic store.</summary>
public sealed class AddLinesProof() : EfSchemaUpcasterProof<SyntheticOrders.AddLines, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.MoveLines"/>, the step that moves data between two content columns.</summary>
public sealed class MoveLinesProof() : EfSchemaUpcasterProof<SyntheticOrders.MoveLines, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>FR-022's three proofs for <see cref="SyntheticOrders.RemoveLines"/>, the removal half of the move between two content columns.</summary>
public sealed class RemoveLinesProof() : EfSchemaUpcasterProof<SyntheticOrders.RemoveLines, SyntheticOrders.Order>(SyntheticOrders.Family, new SyntheticOrdersProofStore());

/// <summary>The synthetic store's half of FR-022's proofs: each read puts the fixture's order row and reads it through the store.</summary>
internal sealed class SyntheticOrdersProofStore : IEfSchemaUpcasterProofStore<SyntheticOrders.Order>, IAsyncDisposable
{
    private readonly SyntheticOrdersDatabase orders = new();

    public EfSchemaChain Chain => SyntheticOrders.Chain;

    public async Task<SyntheticOrders.Order> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, EfSchemaRowContent row)
    {
        var id = JsonNode.Parse(row[SyntheticOrders.ContentColumn]!)!["Id"]!.GetValue<string>();
        await orders.PutAsync(id, stamp, Chain.IsAtOrAfter(stamp, "2") ? "EUR" : null, row[SyntheticOrders.ContentColumn]!, row[SyntheticOrders.LinesColumn]);
        return await orders.Store().ReadAsync(id);
    }

    public EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture fixture, SyntheticOrders.Order value, string version) => SyntheticOrders.FormatAt(value, version);

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
    public async Task PutAsync(string id, string stamp, string? currency, string content, string? lines = null)
    {
        await using var context = NewContext();
        var row = await context.Orders.SingleOrDefaultAsync(order => order.Id == id);
        if (row is null)
            context.Orders.Add(row = new OrderRow { Id = id });
        row.SchemaVersion = stamp;
        row.Currency = currency;
        row.ContentJson = content;
        row.LinesJson = lines;
        await context.SaveChangesAsync();
    }

    public async Task<(string Stamp, string? Currency, string Content, string? Lines)> RawAsync(string id)
    {
        await using var context = NewContext();
        var row = await context.Orders.AsNoTracking().SingleAsync(order => order.Id == id);
        return (row.SchemaVersion, row.Currency, row.ContentJson, row.LinesJson);
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
    /// version, the row's content columns upcast together, deserialize, current validation; and a write that checks the
    /// stored row, then stamps the current version on the current format of every content column.
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
            // The current format's own shape, not a flat serialization of the domain value: version 5 no longer
            // restates the lines in the order document, so a write must not put them back there (#2144).
            var content = SyntheticOrders.FormatAt(order, chain.CurrentVersion);
            row.ContentJson = content[nameof(row.ContentJson)]!;
            row.LinesJson = content[nameof(row.LinesJson)];
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

            var content = chain.Upcast<OrderRow>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson), (nameof(row.LinesJson), row.LinesJson));
            SyntheticOrders.Order order;
            string[] lines;
            try
            {
                order = JsonSerializer.Deserialize<SyntheticOrders.Order>(content[nameof(row.ContentJson)]!)
                        ?? throw new InvalidDataException("The order content is empty.");
                lines = JsonSerializer.Deserialize<string[]>(content[nameof(row.LinesJson)] ?? throw new InvalidDataException("The order row has no lines."))
                        ?? throw new InvalidDataException("The order lines are empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The order content is not valid.", exception);
            }

            // The lines column is where a read takes the lines from, always: the upcast content is the current
            // format's, which from version 5 on no longer restates them in the order document to compare against
            // (#2144), so the domain value's own lines - whatever the document held, current format or not - are
            // replaced with the column's below rather than checked against it.
            if (order.Id != row.Id || order.Currency is null || hasCurrency && order.Currency != row.Currency)
                throw new InvalidDataException("The order content does not match its projections.");
            return order with { Lines = lines };
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
        public string? LinesJson { get; set; }
    }
}
