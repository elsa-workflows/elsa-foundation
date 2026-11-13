using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

/// <summary>Analyzes the existing Activity Definition state envelope without saving it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityDataSelectionContextRequest(
    [property: JsonIgnore] string DraftId,
    [property: JsonRequired] string ContractVersion,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] long ExpectedBaseRevision,
    [property: JsonRequired] string DocumentRevision,
    ActivityDataSelectionTarget Target,
    ActivityDataSelectionQuery? Query = null,
    JsonElement? CurrentSnapshot = null,
    string? SnapshotHash = null);

public sealed record ActivityDataSelectionContextResponse(
    string ContractVersion,
    string State,
    string? Code,
    string ResourceKind,
    string ResourceId,
    string? BaseRevision,
    string DocumentRevision,
    string? SnapshotHash,
    string? ContextRevision,
    ActivityDataSelectionAnalysis? Analysis);

internal sealed record ActivityDataSelectionContextIdentity(
    string ResourceId, long BaseRevision, string DocumentRevision, string SnapshotHash,
    ActivityDataSelectionTarget Target, ActivityDataSelectionQuery? Query, string Actor, string? TenantId,
    string Authorization, ActivityAuthoringCatalogResult Catalog, string Profile, ActivityDataSelectionAnalysis Analysis)
{
    public ActivityDefinitionDraftStatus DraftStatus { get; init; }
}
