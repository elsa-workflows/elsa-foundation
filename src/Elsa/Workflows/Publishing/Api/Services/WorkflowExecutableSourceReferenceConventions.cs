using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Canonical SourceKind names carried on <see cref="WorkflowExecutableSourceReference"/> records.</summary>
public static class WorkflowExecutableSourceKinds
{
    public const string WorkflowDefinitionVersion = "WorkflowDefinitionVersion";
    public const string WorkflowDraftSnapshot = "WorkflowDraftSnapshot";
}

/// <summary>
/// Verbatim copy of a definition version's layout into the reference sidecar (ADR 0039), shared by the publish and
/// test-run flows. The design records map 1:1 into the runtime-owned layout type; AdditionalProperties travels
/// opaquely (ADR 0035).
/// </summary>
public static class WorkflowExecutableLayoutSidecar
{
    public static IReadOnlyList<WorkflowExecutableLayoutRecord> CopyFrom(WorkflowDefinitionVersionLayout? layout) =>
        layout is null
            ? []
            : layout.Records
                .Select(record => new WorkflowExecutableLayoutRecord(
                    record.NodeId,
                    record.X,
                    record.Y,
                    record.Width,
                    record.Height,
                    record.AdditionalProperties))
                .ToArray();
}
