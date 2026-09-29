using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// One of an upcaster's committed fixture pairs (spec 180, FR-022; #2144): one row of one table - every content column
/// the family declares for it - captured at the upcaster's source version, and the same row as the target version stores
/// it. It lives at <c>Fixtures/SchemaUpcasters/&lt;family&gt;/&lt;from&gt;-to-&lt;to&gt;/&lt;table&gt;.source.json</c> and
/// <c>.expected.json</c> in the owning module's test project, which copies it to its output; the table is named by the
/// type the family's <see cref="EfSchemaContentAttribute"/> declaration maps to it, <see cref="Entity"/>.
/// </summary>
public sealed record EfSchemaUpcasterFixture(string Family, string From, string To, Type Entity, EfSchemaRowContent Source, EfSchemaRowContent Expected);

/// <summary>
/// The store's half of FR-022's proofs for one family: the chain it reads through, how a fixture row becomes a stored row
/// read through the store's own read path, and how a value is written in an older version's format.
/// </summary>
public interface IEfSchemaUpcasterProofStore<TValue>
{
    /// <summary>The family's chain, whose declared content columns every fixture row holds.</summary>
    EfSchemaChain Chain { get; }

    /// <summary>
    /// Stores a row of <see cref="EfSchemaUpcasterFixture.Entity"/>'s table stamped <paramref name="stamp"/> whose content
    /// columns hold <paramref name="row"/>, and reads it back through the store's own read path, so every integrity clause
    /// and the chain run as they do in production.
    /// </summary>
    Task<TValue> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, EfSchemaRowContent row);

    /// <summary>
    /// The fixture's row for <paramref name="value"/> in <paramref name="version"/>'s format: every member and column a
    /// later version introduced left unset, as a writer at that version would have written it.
    /// </summary>
    EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture fixture, TValue value, string version);
}

/// <summary>
/// FR-022's three proofs for one upcaster, run over every fixture pair committed for its step: the upcast, the
/// old-format round trip that enforces expand-only content (FR-027), and the read through the store. A module's test
/// project proves each upcaster it ships by deriving a concrete class from this one directly, naming the upcaster, the
/// value its store reads, and the family:
/// <c>public sealed class OrdersAddLinesProof() : EfSchemaUpcasterProof&lt;AddLines, Order&gt;(OrdersEfModule.SchemaFamily, new OrdersProofStore());</c>
/// </summary>
/// <remarks>
/// The proofs live here rather than in each module's tests, so a module cannot ship one of them without the other two:
/// it supplies only the store's half. <c>EfSchemaFamilyFixtureProofGuardTests</c> fails the build when an upcaster has
/// no such class, or a committed fixture pair is proven by none, so the class must derive from this one directly and
/// name its family with a literal or a constant. The step's versions come from the upcaster's own
/// <see cref="EfSchemaUpcasterAttribute"/>. Every proof compares whole rows, so an upcaster that adds or drops a column,
/// or leaves one it should have changed, fails as surely as one that changes a document wrongly.
/// </remarks>
public abstract class EfSchemaUpcasterProof<TUpcaster, TValue>(string family, IEfSchemaUpcasterProofStore<TValue> store) : IAsyncDisposable
    where TUpcaster : IEfSchemaUpcaster, new()
{
    /// <summary>The first proof: the upcaster turns each source row into its expected row, column for column.</summary>
    [Fact]
    public void Upcasting_each_source_fixture_yields_its_expected_fixture()
    {
        foreach (var fixture in Fixtures())
            EfSchemaUpcasterFixtureSupport.AssertRowsEqual(
                fixture.Expected,
                new TUpcaster().Upcast(fixture.Source),
                $"Upcasting {fixture.Family}'s {fixture.Entity.Name} fixture from '{fixture.From}' to '{fixture.To}'");
    }

    /// <summary>
    /// The second proof, the old-format round trip: the expected row's value, written in the source version's format with
    /// every member and column the target version introduced left unset, reproduces the source row. A renamed or retyped
    /// member would not.
    /// </summary>
    [Fact]
    public async Task Writing_each_expected_value_at_the_source_version_reproduces_its_source_fixture()
    {
        foreach (var fixture in Fixtures())
            EfSchemaUpcasterFixtureSupport.AssertRowsEqual(
                fixture.Source,
                store.WriteAt(fixture, await store.ReadAsync(fixture, fixture.To, fixture.Expected), fixture.From),
                $"Writing {fixture.Family}'s {fixture.Entity.Name} '{fixture.To}' fixture at '{fixture.From}'");
    }

    /// <summary>
    /// The third proof: the source row, stored at its version and read through the store, is the same domain value as the
    /// expected row stored at its version.
    /// </summary>
    [Fact]
    public async Task Reading_each_source_fixture_through_the_store_equals_reading_its_expected_fixture()
    {
        foreach (var fixture in Fixtures())
            Assert.Equal(await store.ReadAsync(fixture, fixture.To, fixture.Expected), await store.ReadAsync(fixture, fixture.From, fixture.Source));
    }

    public ValueTask DisposeAsync() => store is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;

    private IReadOnlyList<EfSchemaUpcasterFixture> Fixtures()
    {
        Assert.Equal(family, store.Chain.Family);
        var step = typeof(TUpcaster).GetCustomAttribute<EfSchemaUpcasterAttribute>()
                   ?? throw new InvalidOperationException($"{typeof(TUpcaster).Name} carries no [EfSchemaUpcaster(from, to)].");
        var fixtures = EfSchemaUpcasterFixtureSupport.LoadAll(store.Chain, step.From, step.To);
        Assert.True(fixtures.Count > 0, $"{typeof(TUpcaster).Name} has no committed fixture pair for '{family}' from '{step.From}' to '{step.To}' (spec 180, FR-022).");
        return fixtures;
    }
}

/// <summary>
/// The mechanics of FR-022's proofs, for any module's test project to compile in, as it compiles in
/// <see cref="EfSchemaVersionSkewTestSupport"/>. A fixture is frozen once its version ships:
/// <c>EfSchemaFamilyFixtureProofGuardTests</c> fails the build when one is edited, deleted, or committed without being
/// recorded.
/// </summary>
/// <remarks>
/// A fixture file is one JSON object, one member per content column: a string is the text the column stores, verbatim,
/// so a column that holds no JSON document is written exactly; any other JSON value is a document the column stores as
/// that value's serialization; <c>null</c> is a null column. A column the family declares for the table that the file
/// does not name is null, as it is on every row written before the column existed, so a fixture frozen before a later
/// version added a column still loads. A member the family does not declare for the table fails the load.
/// </remarks>
internal static class EfSchemaUpcasterFixtureSupport
{
    private const string SourceSuffix = ".source.json";

    /// <summary>Every fixture pair committed for <paramref name="chain"/>'s family's step from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static IReadOnlyList<EfSchemaUpcasterFixture> LoadAll(EfSchemaChain chain, string from, string to)
    {
        var directory = FixtureDirectory(chain.Family, from, to);
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*" + SourceSuffix)
            .Select(Path.GetFileName)
            .Select(name => name![..^SourceSuffix.Length])
            .Order(StringComparer.Ordinal)
            .Select(table => Load(chain, from, to, table))
            .ToArray();
    }

    public static EfSchemaUpcasterFixture Load(EfSchemaChain chain, string from, string to, string table)
    {
        var tables = chain.ContentColumns.GroupBy(column => column.Entity).Where(declared => declared.Key.Name == table).ToArray();
        Assert.True(tables.Length == 1,
            $"The upcaster fixture '{table}' of '{chain.Family}' from '{from}' to '{to}' names {(tables.Length == 0 ? "no table" : "more than one table")} " +
            "the family declares content columns for; a fixture is named by the type its [EfSchemaContent] declaration maps to the table.");
        var entity = tables[0].Key;
        var columns = tables[0].Select(column => column.Name).ToArray();
        var directory = FixtureDirectory(chain.Family, from, to);
        return new EfSchemaUpcasterFixture(chain.Family, from, to, entity, Read("source"), Read("expected"));

        EfSchemaRowContent Read(string role)
        {
            var path = Path.Join(directory, $"{table}.{role}.json");
            Assert.True(File.Exists(path), $"Missing the committed upcaster fixture '{path}' (spec 180, FR-022).");
            var document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                           ?? throw new InvalidDataException($"The upcaster fixture '{path}' is not a JSON object of content columns.");
            var undeclared = document.Select(member => member.Key).Except(columns, StringComparer.Ordinal).ToArray();
            Assert.True(undeclared.Length == 0,
                $"The upcaster fixture '{path}' holds {string.Join(", ", undeclared.Select(column => $"'{column}'"))}, which '{chain.Family}' does not " +
                $"declare as content of {entity.Name}; it declares {string.Join(", ", columns.Select(column => $"'{column}'"))}.");
            return new EfSchemaRowContent(entity, [.. columns.Select(column => (column, Text(document[column])))]);
        }
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is <paramref name="expected"/>: the same table, the same columns, and each
    /// column equal, by semantic JSON equality as the golden fixtures compare where both hold JSON, and ordinally where
    /// either does not.
    /// </summary>
    public static void AssertRowsEqual(EfSchemaRowContent expected, EfSchemaRowContent? actual, string what)
    {
        if (actual is not null && actual.Entity == expected.Entity &&
            actual.Columns.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Columns.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            expected.Columns.All(column => SameColumn(column.Value, actual[column.Key])))
            return;

        Assert.Fail($"{what} does not match its committed fixture. A shipped fixture is frozen and never regenerated.\n\n" +
                    $"Expected (committed):\n{Describe(expected)}\n\nActual:\n{(actual is null ? "no row" : Describe(actual))}");
    }

    /// <summary>The row as a fixture file writes it, so a failure shows both rows in the committed form.</summary>
    public static string Describe(EfSchemaRowContent row)
    {
        var document = new JsonObject();
        foreach (var (column, value) in row.Columns)
            document[column] = value is null ? null : Parse(value) is { } json && json is not JsonValue ? json : JsonValue.Create(value);
        return $"{row.Entity.Name} {document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static bool SameColumn(string? expected, string? actual) =>
        expected is null || actual is null
            ? expected is null && actual is null
            : Parse(expected) is { } expectedJson && Parse(actual) is { } actualJson
                ? JsonNode.DeepEquals(expectedJson, actualJson)
                : StringComparer.Ordinal.Equals(expected, actual);

    /// <summary>The text a fixture member says its column stores: a string verbatim, any other JSON value serialized.</summary>
    private static string? Text(JsonNode? member) =>
        member switch
        {
            null => null,
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
            _ => member.ToJsonString()
        };

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FixtureDirectory(string family, string from, string to) =>
        Path.Join(AppContext.BaseDirectory, "Fixtures", "SchemaUpcasters", family, $"{from}-to-{to}");
}
