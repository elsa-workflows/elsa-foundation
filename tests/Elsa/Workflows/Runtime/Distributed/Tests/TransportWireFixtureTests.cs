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
    [Fact]
    public void Serialized_Shape_Matches_The_Committed_Golden_Fixture()
    {
        var item = TransportWireFixture.CanonicalItem();
        var actualJson = JsonSerializer.Serialize(item, TransportWireFixture.SerializerOptions);

        if (GoldenFixtureTestSupport.Regenerate)
        {
            GoldenFixtureTestSupport.WriteFixtureToSource(
                SourceDirectory(), TransportWireFixture.Kind, GoldenFixtureTestSupport.Canonicalize(JsonNode.Parse(actualJson)));
            return;
        }

        var expectedJson = GoldenFixtureTestSupport.ReadCommittedFixture(TransportWireFixture.Kind);
        GoldenFixtureTestSupport.AssertJsonSemanticallyEqual(
            expectedJson,
            actualJson,
            "The serialized shape of the execution command transport item no longer matches its committed golden " +
            "fixture (Fixtures/v1/executionCommandTransport.json). This is the frozen v1 wire format shared with the " +
            "durable transport follow-up.");
    }

    [Fact]
    public void Committed_Fixture_Round_Trips_Into_An_Equivalent_Item()
    {
        if (GoldenFixtureTestSupport.Regenerate)
            return;

        var fixtureJson = GoldenFixtureTestSupport.ReadCommittedFixture(TransportWireFixture.Kind);
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

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) => Path.GetDirectoryName(callerFilePath)!;
}
