using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// One of an upcaster's committed fixture pairs (spec 180, FR-022): a content column captured at the upcaster's source
/// version, and the same column as the target version stores it. It lives at
/// <c>Fixtures/SchemaUpcasters/&lt;family&gt;/&lt;from&gt;-to-&lt;to&gt;/&lt;table&gt;.&lt;column&gt;.source.json</c> and
/// <c>.expected.json</c> in the owning module's test project, which copies it to its output.
/// </summary>
public sealed record EfSchemaUpcasterFixture(string Family, string From, string To, string Table, string Column, string Source, string Expected);

/// <summary>
/// The store's half of FR-022's proofs for one family: how a fixture column becomes a stored row read through the
/// store's own read path, and how a value is written in an older version's format.
/// </summary>
public interface IEfSchemaUpcasterProofStore<TValue>
{
    /// <summary>
    /// Stores a row stamped <paramref name="stamp"/> whose <see cref="EfSchemaUpcasterFixture.Column"/> of
    /// <see cref="EfSchemaUpcasterFixture.Table"/> holds <paramref name="content"/>, and reads it back through the store's
    /// own read path, so every integrity clause and the chain run as they do in production.
    /// </summary>
    Task<TValue> ReadAsync(EfSchemaUpcasterFixture fixture, string stamp, string content);

    /// <summary>
    /// The fixture's column for <paramref name="value"/> in <paramref name="version"/>'s format: every member a later
    /// version introduced left unset, as a writer at that version would have written it.
    /// </summary>
    string WriteAt(EfSchemaUpcasterFixture fixture, TValue value, string version);
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
/// it supplies only the store's half. <c>EfSchemaFamilyDeclarationGuardTests</c> fails the build when an upcaster has no
/// such class, or a committed fixture pair is proven by none, so the class must derive from this one directly and name
/// its family with a literal or a constant. The step's versions come from the upcaster's own
/// <see cref="EfSchemaUpcasterAttribute"/>.
/// </remarks>
public abstract class EfSchemaUpcasterProof<TUpcaster, TValue>(string family, IEfSchemaUpcasterProofStore<TValue> store) : IAsyncDisposable
    where TUpcaster : IEfSchemaUpcaster, new()
{
    /// <summary>The first proof: the upcaster turns each source fixture into its expected fixture.</summary>
    [Fact]
    public void Upcasting_each_source_fixture_yields_its_expected_fixture()
    {
        foreach (var fixture in Fixtures())
            EfSchemaUpcasterFixtureSupport.AssertUpcasts(new TUpcaster(), fixture);
    }

    /// <summary>
    /// The second proof, the old-format round trip: the expected fixture's value, written in the source version's format
    /// with every member the target version introduced left unset, reproduces the source fixture. A renamed or retyped
    /// member would not.
    /// </summary>
    [Fact]
    public async Task Writing_each_expected_value_at_the_source_version_reproduces_its_source_fixture()
    {
        foreach (var fixture in Fixtures())
            EfSchemaUpcasterFixtureSupport.AssertSemanticallyEqual(
                fixture.Source,
                store.WriteAt(fixture, await store.ReadAsync(fixture, fixture.To, fixture.Expected), fixture.From),
                $"Writing {fixture.Family}'s {fixture.Table}.{fixture.Column} '{fixture.To}' fixture at '{fixture.From}'");
    }

    /// <summary>
    /// The third proof: the source fixture, stored as a row at its version and read through the store, is the same
    /// domain value as the expected fixture stored at its version.
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
        var step = typeof(TUpcaster).GetCustomAttribute<EfSchemaUpcasterAttribute>()
                   ?? throw new InvalidOperationException($"{typeof(TUpcaster).Name} carries no [EfSchemaUpcaster(from, to)].");
        var fixtures = EfSchemaUpcasterFixtureSupport.LoadAll(family, step.From, step.To);
        Assert.True(fixtures.Count > 0, $"{typeof(TUpcaster).Name} has no committed fixture pair for '{family}' from '{step.From}' to '{step.To}' (spec 180, FR-022).");
        return fixtures;
    }
}

/// <summary>
/// The mechanics of FR-022's proofs, for any module's test project to compile in, as it compiles in
/// <see cref="EfSchemaVersionSkewTestSupport"/>. A fixture is frozen once its version ships:
/// <c>EfSchemaFamilyDeclarationGuardTests</c> fails the build when one is edited, deleted, or committed without being
/// recorded.
/// </summary>
internal static class EfSchemaUpcasterFixtureSupport
{
    private const string SourceSuffix = ".source.json";

    /// <summary>Every fixture pair committed for <paramref name="family"/>'s step from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static IReadOnlyList<EfSchemaUpcasterFixture> LoadAll(string family, string from, string to)
    {
        var directory = FixtureDirectory(family, from, to);
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*" + SourceSuffix)
            .Select(Path.GetFileName)
            .Select(name => name![..^SourceSuffix.Length])
            .Order(StringComparer.Ordinal)
            .Select(name => Load(family, from, to, name[..name.LastIndexOf('.')], name[(name.LastIndexOf('.') + 1)..]))
            .ToArray();
    }

    public static EfSchemaUpcasterFixture Load(string family, string from, string to, string table, string column)
    {
        var directory = FixtureDirectory(family, from, to);
        return new EfSchemaUpcasterFixture(family, from, to, table, column, Read("source"), Read("expected"));

        string Read(string role)
        {
            var path = Path.Join(directory, $"{table}.{column}.{role}.json");
            Assert.True(File.Exists(path), $"Missing the committed upcaster fixture '{path}' (spec 180, FR-022).");
            return File.ReadAllText(path);
        }
    }

    /// <summary>The first proof: <paramref name="upcaster"/> turns the source fixture into the expected fixture.</summary>
    public static void AssertUpcasts(IEfSchemaUpcaster upcaster, EfSchemaUpcasterFixture fixture) =>
        AssertSemanticallyEqual(
            fixture.Expected,
            upcaster.Upcast(new EfSchemaContent(fixture.Table, fixture.Column, fixture.Source)),
            $"Upcasting {fixture.Family}'s {fixture.Table}.{fixture.Column} fixture from '{fixture.From}' to '{fixture.To}'");

    /// <summary>Semantic JSON equality, as the golden fixtures compare: member order and whitespace do not matter.</summary>
    public static void AssertSemanticallyEqual(string expected, string actual, string what)
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);
        if (!JsonNode.DeepEquals(expectedNode, actualNode))
            Assert.Fail($"{what} does not match its committed fixture. A shipped fixture is frozen and never regenerated.\n\n" +
                        $"Expected (committed):\n{Canonical(expectedNode)}\n\nActual:\n{Canonical(actualNode)}");
    }

    private static string FixtureDirectory(string family, string from, string to) =>
        Path.Join(AppContext.BaseDirectory, "Fixtures", "SchemaUpcasters", family, $"{from}-to-{to}");

    private static string Canonical(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
}
