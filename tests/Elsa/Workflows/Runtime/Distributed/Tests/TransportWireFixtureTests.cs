using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Golden-fixture drift and round-trip tests that freeze the v1 wire shape of the cross-node command transport item.
/// </summary>
/// <remarks>
/// This unit ships the in-memory transport only; the durable (Groundwork) transport is a named follow-up. The committed
/// <c>Fixtures/v1/executionCommandTransport.json</c> file IS the frozen v1 wire format, so the follow-up cannot reshape
/// it: the drift test fails if the serialized shape changes without a version bump, and the round-trip test proves the
/// committed fixture still deserializes into an equivalent item.
/// </remarks>
public sealed class TransportWireFixtureTests
{
    // Set ELSA_DISTRIBUTED_FIXTURE_REGEN=1 to (re)write the committed fixture after an intentional, versioned change.
    private static readonly bool Regenerate = Environment.GetEnvironmentVariable("ELSA_DISTRIBUTED_FIXTURE_REGEN") == "1";

    [Fact]
    public void Serialized_Shape_Matches_The_Committed_Golden_Fixture()
    {
        var item = TransportWireFixture.CanonicalItem();
        var actualJson = JsonSerializer.Serialize(item, TransportWireFixture.SerializerOptions);

        if (Regenerate)
        {
            WriteFixtureToSource(Canonicalize(JsonNode.Parse(actualJson)));
            return;
        }

        var expectedJson = ReadCommittedFixture();
        AssertJsonSemanticallyEqual(expectedJson, actualJson);
    }

    [Fact]
    public void Committed_Fixture_Round_Trips_Into_An_Equivalent_Item()
    {
        if (Regenerate)
            return;

        var fixtureJson = ReadCommittedFixture();
        var deserialized = JsonSerializer.Deserialize<ExecutionCommandTransportItem>(fixtureJson, TransportWireFixture.SerializerOptions);

        Assert.NotNull(deserialized);
        var expected = TransportWireFixture.CanonicalItem();
        Assert.Equal(expected.TransportItemId, deserialized!.TransportItemId);
        Assert.Equal(expected.WorkflowExecutionId, deserialized.WorkflowExecutionId);
        Assert.Equal(expected.Sequence, deserialized.Sequence);
        Assert.Equal(expected.DeliveryAttemptCount, deserialized.DeliveryAttemptCount);
        Assert.Equal(expected.LeasedByOwnerId, deserialized.LeasedByOwnerId);
        Assert.Equal(expected.LeaseExpiresAt, deserialized.LeaseExpiresAt);
        Assert.Equal(expected.Envelope.EnvelopeId, deserialized.Envelope.EnvelopeId);
        Assert.Equal(expected.Envelope.IdempotencyKey, deserialized.Envelope.IdempotencyKey);
        Assert.Equal(expected.Envelope.Command.Kind, deserialized.Envelope.Command.Kind);
        Assert.Equal(expected.Envelope.Command.CommandId, deserialized.Envelope.Command.CommandId);
    }

    private static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        if (JsonNode.DeepEquals(expected, actual))
            return;

        Assert.Fail(
            "The serialized shape of the execution command transport item no longer matches its committed golden " +
            "fixture (Fixtures/v1/executionCommandTransport.json). This is the frozen v1 wire format shared with the " +
            "durable transport follow-up.\n\n" +
            "To evolve it you must bump the schema version, add an upcaster, add a new versioned fixture, and keep the " +
            "old one. Run with ELSA_DISTRIBUTED_FIXTURE_REGEN=1 only after a deliberate, versioned change.\n\n" +
            $"Expected (committed):\n{Canonicalize(expected)}\n\nActual (serialized today):\n{Canonicalize(actual)}");
    }

    private static string Canonicalize(JsonNode? node) =>
        SortRecursively(node)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    private static JsonNode? SortRecursively(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    result[property.Key] = SortRecursively(property.Value?.DeepClone());
                return result;
            case JsonArray array:
                var newArray = new JsonArray();
                foreach (var element in array)
                    newArray.Add(SortRecursively(element?.DeepClone()));
                return newArray;
            default:
                return node?.DeepClone();
        }
    }

    private static string ReadCommittedFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1", TransportWireFixture.Kind + ".json");
        Assert.True(File.Exists(path), $"Missing committed golden fixture at '{path}'. Run with ELSA_DISTRIBUTED_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static void WriteFixtureToSource(string canonicalJson)
    {
        var directory = Path.Combine(SourceDirectory(), "Fixtures", "v1");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, TransportWireFixture.Kind + ".json"), canonicalJson);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) => Path.GetDirectoryName(callerFilePath)!;
}
