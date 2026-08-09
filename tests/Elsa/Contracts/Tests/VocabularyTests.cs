using System.Text.Json;
using Elsa.Contracts.Generator;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 FR-C1/C5/C6. Every value space here was a required field typed as a bare string, enumerated
/// nowhere: a benchmarked session probed four endpoints for the variable type-alias vocabulary, got 404
/// from all of them, and guessed. Publishing the list is only worth anything if it is the SAME list the
/// server enforces, so each is compared against the product type that owns it.
/// </summary>
public sealed class VocabularyTests
{
    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string DocumentPath() => Path.Combine(ContractsRoot(), "vocabularies.json");

    private static JsonDocument Raw() => JsonDocument.Parse(File.ReadAllBytes(DocumentPath()));

    private static string[] Strings(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static string[] Values(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetProperty("value").GetString()!).ToArray();

    [Fact]
    public void The_reserved_bare_type_aliases_are_the_ones_the_registry_enforces()
    {
        using var raw = Raw();

        // A bare alias outside this set is refused at registration, so a consumer that invents one gets a
        // failure it cannot diagnose from the contracts alone.
        Assert.Equal(
            TypeAliasConvention.ReservedBareAliases.Order(StringComparer.Ordinal),
            Strings(raw.RootElement.GetProperty("typeAliases").GetProperty("reservedBare")));
    }

    /// <summary>
    /// The WIRE spelling, not the CLR name: publishing `Single` while the submit schema enumerates `single`
    /// is a value a consumer cannot use, which is exactly what happened.
    /// </summary>
    [Fact]
    public void The_collection_kinds_are_the_enum_the_binder_reads_in_wire_spelling()
    {
        using var raw = Raw();

        Assert.Equal(
            Enum.GetValues<CollectionKind>().Select(WireSpelling.Of).Order(StringComparer.Ordinal),
            Strings(raw.RootElement.GetProperty("collectionKinds")));
    }

    [Fact]
    public void The_intrinsic_kinds_are_the_enum_the_compiler_switches_on()
    {
        using var raw = Raw();

        Assert.Equal(
            Enum.GetNames<AuthoredWorkflowIntrinsicKind>()
                .Select(name => char.ToLowerInvariant(name[0]) + name[1..])
                .Order(StringComparer.Ordinal),
            Strings(raw.RootElement.GetProperty("intrinsicKinds")));
    }

    [Fact]
    public void The_authored_via_vocabulary_is_the_closed_set_the_fragments_use()
    {
        using var raw = Raw();
        var published = Values(raw.RootElement.GetProperty("authoredVia"));

        Assert.Equal([InputAuthoringSites.Argument, InputAuthoringSites.IntrinsicBlock], published);

        // And nothing in the fragments uses a term outside it — the field is load-bearing, so an
        // unpublished value would send a consumer straight into a rejected submission.
        var used = Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json")
            .SelectMany(path => AuthoringSites(JsonDocument.Parse(File.ReadAllBytes(path)).RootElement))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(used, term => Assert.Contains(term, published));
    }

    private static IEnumerable<string> AuthoringSites(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("authoredVia") && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;
                    foreach (var nested in AuthoringSites(property.Value))
                        yield return nested;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in AuthoringSites(item))
                        yield return nested;
                }

                break;
        }
    }

    /// <summary>
    /// The point of publishing the alias rule rather than only the reserved list: every type a consumer
    /// meets in the fragments must be explainable by one of the two published rules, or the rule is wrong.
    /// </summary>
    [Fact]
    public void Every_type_in_the_fragments_is_explained_by_the_published_alias_rules()
    {
        using var raw = Raw();
        var reserved = Strings(raw.RootElement.GetProperty("typeAliases").GetProperty("reservedBare"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var type in TypeNames(fragment.RootElement))
            {
                var bare = type.TrimEnd('?');
                Assert.True(
                    reserved.Contains(bare) || bare.Contains('.') || bare is "Any",
                    $"'{type}' in {Path.GetFileName(path)} is neither a reserved bare alias nor a dotted CLR " +
                    "full name, so the published alias rule does not explain it.");
            }
        }
    }

    /// <summary>
    /// Walks the input/output <c>type</c> fields structurally rather than by name. A blind recursive search
    /// also picks up the <c>type</c> KEYWORD inside embedded JSON Schemas (<c>"type": "integer"</c>), which
    /// is a different vocabulary entirely and would make this assert something it does not mean.
    /// </summary>
    private static IEnumerable<string> TypeNames(JsonElement fragment)
    {
        foreach (var collection in new[] { "activities", "intrinsics" })
        {
            if (!fragment.TryGetProperty(collection, out var contracts))
                continue;

            foreach (var contract in contracts.EnumerateArray())
            {
                foreach (var arguments in new[] { "inputs", "outputs" })
                {
                    if (!contract.TryGetProperty(arguments, out var entries))
                        continue;

                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                            yield return type.GetString()!;
                    }
                }
            }
        }
    }

    [Fact]
    public void The_document_is_fingerprinted_in_the_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "manifest.json")));

        Assert.Equal(
            manifest.RootElement.GetProperty("vocabularies").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(DocumentPath())));
    }
}
