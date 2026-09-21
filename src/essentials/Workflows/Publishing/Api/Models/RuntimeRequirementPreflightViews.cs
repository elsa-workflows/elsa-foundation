using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record RuntimeRequirementPreflightView(
    int CheckedArtifactCount,
    bool IsReady,
    IReadOnlyList<RuntimeRequirementPreflightItemView> Requirements,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record RuntimeRequirementPreflightItemView(
    string ConsumerKey,
    string SchemaVersion,
    string Status,
    int AffectedArtifactCount);
