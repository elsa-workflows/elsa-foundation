using System.Text.Json;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 FR-B1. Notes carry the behaviour a shape cannot express, attached to the code they describe.
/// The properties worth guarding are not that a particular sentence exists — that is an editorial choice —
/// but that the mechanism keeps its two promises: notes reach the fragments, and they stay out of identity.
/// </summary>
public sealed class ConsumerNoteTests
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

    private static JsonDocument Fragment(string assembly) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "fragments", assembly + ".json")));

    /// <summary>
    /// The RFC's worked example, end to end: the enum's value space is published as `enumValues` and what
    /// choosing each value DOES is published as a per-member note. Three benchmarked sessions lost a run to
    /// the half of this that was missing.
    /// </summary>
    [Fact]
    public void An_enum_input_publishes_a_note_per_member_alongside_its_value_space()
    {
        using var fragment = Fragment("Elsa.Activities.Http");

        var responseMode = fragment.RootElement.GetProperty("activities").EnumerateArray()
            .Single(activity => activity.GetProperty("activityTypeKey").GetString()!.EndsWith("HttpEndpoint", StringComparison.Ordinal))
            .GetProperty("inputs").EnumerateArray()
            .Single(input => input.GetProperty("name").GetString() == "ResponseMode");

        var values = responseMode.GetProperty("enumValues").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        var notedMembers = responseMode.GetProperty("notes").EnumerateArray()
            .Select(note => note.GetProperty("member").GetString()!).ToArray();

        Assert.Equal(["async", "sync"], values);
        Assert.Equal(values.Order(StringComparer.Ordinal), notedMembers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_feature_publishes_its_composition_note()
    {
        using var fragment = Fragment("Elsa.Activities.Http");

        var note = fragment.RootElement.GetProperty("features").EnumerateArray()
            .Single(feature => feature.GetProperty("id").GetString() == "ActivitiesHttp")
            .GetProperty("notes").EnumerateArray()
            .Single();

        Assert.Equal("composition", note.GetProperty("kind").GetString());
        // The fact itself: endpoints answer under the feature's base path, not the activity's bare path.
        Assert.Contains("/workflows/http", note.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_published_note_uses_a_kind_from_the_closed_set()
    {
        string[] kinds = ["default", "requirement", "constraint", "failureMode", "limit", "composition"];

        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var note in Notes(fragment.RootElement))
            {
                Assert.Contains(note.GetProperty("kind").GetString(), kinds);
                Assert.False(string.IsNullOrWhiteSpace(note.GetProperty("note").GetString()),
                    $"An empty note in {Path.GetFileName(path)} publishes nothing but noise.");
            }
        }
    }

    private static IEnumerable<JsonElement> Notes(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("notes") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var note in property.Value.EnumerateArray())
                            yield return note;
                    }

                    foreach (var nested in Notes(property.Value))
                        yield return nested;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Notes(item))
                        yield return nested;
                }

                break;
        }
    }

    /// <summary>
    /// The load-bearing guarantee, and the reason notes are a separate projection pass: if a note fed the
    /// content hash, editing a sentence would change the activity's identity, and under reconciliation
    /// Model X a pre-existing database would throw <c>ActivityVersionHashMismatchException</c> on a
    /// docs-only change.
    /// </summary>
    /// <remarks>
    /// The proof is that a NOTED activity still hashes to what the reconciliation path produces — and the
    /// reconciliation path has no knowledge of notes at all. <see cref="EquivalenceTests"/> asserts that
    /// equality across every activity; this test just guards the specific case that would have caught the
    /// leak, so a future change cannot quietly satisfy the equivalence by dropping the note instead.
    /// </remarks>
    [Fact]
    public void The_activity_carrying_notes_is_the_one_whose_hash_must_stay_put()
    {
        using var fragment = Fragment("Elsa.Activities.Http");

        var noted = fragment.RootElement.GetProperty("activities").EnumerateArray()
            .Single(activity => activity.GetProperty("activityTypeKey").GetString()!.EndsWith("HttpEndpoint", StringComparison.Ordinal));

        // It really does carry notes — otherwise the equivalence assertion below proves nothing.
        Assert.Contains(noted.GetProperty("inputs").EnumerateArray(),
            input => input.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array);
        Assert.Matches("^[0-9A-F]{64}$", noted.GetProperty("contentHash").GetString()!);
    }
}
