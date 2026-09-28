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
internal sealed record EfSchemaUpcasterFixture(string Family, string From, string To, string Table, string Column, string Source, string Expected);

/// <summary>
/// The mechanics of FR-022's proofs, for any module's test project to compile in, as it compiles in
/// <see cref="EfSchemaVersionSkewTestSupport"/>. The second and third proofs - the old-format round trip and the read
/// through the store - need the store, so a module's own test drives them with <see cref="AssertSemanticallyEqual"/>.
/// A fixture is frozen once its version ships: <c>EfSchemaFamilyDeclarationGuardTests</c> fails the build when one is
/// edited, deleted, or committed without being recorded.
/// </summary>
internal static class EfSchemaUpcasterFixtureSupport
{
    public static EfSchemaUpcasterFixture Load(string family, string from, string to, string table, string column)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SchemaUpcasters", family, $"{from}-to-{to}");
        return new EfSchemaUpcasterFixture(family, from, to, table, column, Read("source"), Read("expected"));

        string Read(string role)
        {
            var path = Path.Combine(directory, $"{table}.{column}.{role}.json");
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

    private static string Canonical(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
}
