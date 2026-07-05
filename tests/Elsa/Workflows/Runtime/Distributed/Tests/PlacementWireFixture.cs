using System.Text.Json;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Builds the single canonical <see cref="ExecutionPlacementLease"/> whose serialized shape is frozen as the v1
/// wire format of the <c>executionPlacement</c> document kind (W27). The golden fixture drift test compares this
/// against the committed <c>Fixtures/v1</c> file, and the durable (Groundwork) placement store must persist exactly
/// this lease shape.
/// </summary>
internal static class PlacementWireFixture
{
    public const string Kind = "executionPlacement";

    private static readonly DateTimeOffset FixedTimestamp = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static ExecutionPlacementLease CanonicalLease() => new(
        workflowExecutionId: "wf-1",
        ownerId: "node-a",
        placementToken: 1,
        acquiredAt: FixedTimestamp,
        expiresAt: FixedTimestamp.AddSeconds(30));
}
