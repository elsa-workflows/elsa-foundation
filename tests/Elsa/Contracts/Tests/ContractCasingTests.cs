using System.Text.Json;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Guards the whole class of defect where a fragment publishes a CLR spelling and the wire expects another.
/// </summary>
/// <remarks>
/// This has now happened three times: intrinsic kind (<c>Set</c> vs <c>set</c>), then <c>collectionKind</c>
/// (<c>Single</c> vs <c>single</c>) and <c>editingMode</c> at the same time. Each was found by a consumer
/// copying a value out of a fragment into a submission and having the body rejected. Fixing the instance is
/// not enough — every one came from calling <c>.ToString()</c> on an enum, so the guard has to be against
/// the published artifact rather than against any particular field.
/// </remarks>
public sealed class ContractCasingTests
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

    /// <summary>
    /// The submit schema is generated from the wire contract, so its enumerations ARE the accepted spellings.
    /// Every `collectionKind` a fragment publishes must be one of them.
    /// </summary>
    [Fact]
    public void Published_collection_kinds_are_spellings_the_submit_schema_accepts()
    {
        var accepted = SubmitSchemaEnum("collectionKind");
        Assert.NotEmpty(accepted);

        foreach (var (file, value) in PublishedValues("collectionKind"))
        {
            Assert.True(accepted.Contains(value),
                $"{file} publishes collectionKind '{value}', which the submit schema does not accept " +
                $"({string.Join(", ", accepted)}). A consumer copying it into a variable declaration gets a " +
                "rejected body.");
        }
    }

    /// <summary>
    /// `editingMode` is served through a converter that writes camelCase, so a PascalCase fragment value
    /// contradicts what the same consumer sees from the live expression-descriptor endpoint.
    /// </summary>
    [Fact]
    public void Published_editing_modes_use_the_served_spelling()
    {
        var published = PublishedValues("editingMode").Select(item => item.Value).Distinct(StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(published);
        Assert.All(published, value =>
            Assert.True(value.Length > 0 && char.IsLower(value[0]),
                $"editingMode '{value}' is published in CLR casing; the served value is camelCase."));
    }

    /// <summary>
    /// The backstop, and the one that would have caught all three: no published enum-ish value may be
    /// PascalCase unless the served view exposes that field as a plain string (in which case the CLR name IS
    /// the wire name). Those exceptions are named, so adding a fourth is a deliberate act.
    /// </summary>
    [Fact]
    public void No_published_enum_field_carries_clr_casing_unless_it_is_a_declared_exception()
    {
        // Fields the served view types expose as `string`, delivered verbatim rather than through an enum
        // converter — verified against the view definitions, not assumed.
        string[] servedAsPlainStrings = ["executionType"];

        foreach (var field in new[] { "collectionKind", "editingMode", "kind" })
        {
            if (servedAsPlainStrings.Contains(field))
                continue;

            foreach (var (file, value) in PublishedValues(field))
            {
                // Structure/intrinsic `kind` values are domain identifiers (`sequence`, `set`), not CLR names.
                Assert.True(value.Length == 0 || !char.IsUpper(value[0]),
                    $"{file} publishes {field} '{value}' in CLR casing. Publish the wire spelling " +
                    "(WireSpelling.Of) or add the field to the declared exceptions with the reason.");
            }
        }
    }

    private static HashSet<string> SubmitSchemaEnum(string property)
    {
        using var schema = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "submit-schema.json")));
        var values = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema.RootElement, property, values);
        return values;
    }

    private static void Collect(JsonElement element, string property, HashSet<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    if (member.NameEquals(property) &&
                        member.Value.ValueKind == JsonValueKind.Object &&
                        member.Value.TryGetProperty("enum", out var enumeration) &&
                        enumeration.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in enumeration.EnumerateArray())
                        {
                            if (value.ValueKind == JsonValueKind.String)
                                values.Add(value.GetString()!);
                        }
                    }

                    Collect(member.Value, property, values);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, property, values);
                break;
        }
    }

    private static IEnumerable<(string File, string Value)> PublishedValues(string property)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var value in Values(fragment.RootElement, property))
                yield return (Path.GetFileName(path), value);
        }
    }

    private static IEnumerable<string> Values(JsonElement element, string property)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    // Only direct string values: an embedded JSON Schema's own `kind`/`enum` blocks are a
                    // different vocabulary and must not be swept in.
                    if (member.NameEquals(property) && member.Value.ValueKind == JsonValueKind.String)
                        yield return member.Value.GetString()!;

                    if (!member.NameEquals("payloadSchema") && !member.NameEquals("uiSpecifications") &&
                        !member.NameEquals("requestSchema") && !member.NameEquals("responseSchema"))
                    {
                        foreach (var nested in Values(member.Value, property))
                            yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Values(item, property))
                        yield return nested;
                }

                break;
        }
    }
}
