using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Golden-fixture drift and round-trip tests that freeze the v1 wire shape of the per-execution placement lease
/// (document kind <c>executionPlacement</c>, W27). The committed <c>Fixtures/v1/executionPlacement.json</c> file IS
/// the frozen v1 wire format: the drift test fails if the serialized shape changes without a version bump, and the
/// round-trip test proves the committed fixture still deserializes into an equivalent lease.
/// </summary>
public sealed class PlacementWireFixtureTests
{
    [Fact]
    public void Serialized_Shape_Matches_The_Committed_Golden_Fixture()
    {
        var lease = PlacementWireFixture.CanonicalLease();
        var actualJson = JsonSerializer.Serialize(lease, PlacementWireFixture.SerializerOptions);

        if (GoldenFixtureTestSupport.Regenerate)
        {
            GoldenFixtureTestSupport.WriteFixtureToSource(
                SourceDirectory(), PlacementWireFixture.Kind, GoldenFixtureTestSupport.Canonicalize(JsonNode.Parse(actualJson)));
            return;
        }

        var expectedJson = GoldenFixtureTestSupport.ReadCommittedFixture(PlacementWireFixture.Kind);
        GoldenFixtureTestSupport.AssertJsonSemanticallyEqual(
            expectedJson,
            actualJson,
            "The serialized shape of the execution placement lease no longer matches its committed golden fixture " +
            "(Fixtures/v1/executionPlacement.json). This is the frozen v1 wire format shared with the durable " +
            "placement store.");
    }

    [Fact]
    public void Committed_Fixture_Round_Trips_Into_An_Equivalent_Lease()
    {
        if (GoldenFixtureTestSupport.Regenerate)
            return;

        var fixtureJson = GoldenFixtureTestSupport.ReadCommittedFixture(PlacementWireFixture.Kind);
        var deserialized = JsonSerializer.Deserialize<ExecutionPlacementLease>(fixtureJson, PlacementWireFixture.SerializerOptions);

        Assert.NotNull(deserialized);
        var expected = PlacementWireFixture.CanonicalLease();
        Assert.Equal(expected.WorkflowExecutionId, deserialized!.WorkflowExecutionId);
        Assert.Equal(expected.OwnerId, deserialized.OwnerId);
        Assert.Equal(expected.PlacementToken, deserialized.PlacementToken);
        Assert.Equal(expected.AcquiredAt, deserialized.AcquiredAt);
        Assert.Equal(expected.ExpiresAt, deserialized.ExpiresAt);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) => Path.GetDirectoryName(callerFilePath)!;
}
