using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>
/// Immutable authored provenance captured by a trusted publisher, separate from executable model content.
/// Runtime publication and recovery do not read the Design store.
/// </summary>
public sealed class BpmnModelPublicationProvenance
{
    public BpmnModelPublicationProvenance(string versionId, string version,
        IReadOnlyList<WorkflowExecutableLayoutRecord>? layout = null,
        IReadOnlyList<WorkflowExecutableActivityPresentationRecord>? activityPresentation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (layout?.Any(record => record is null) == true || activityPresentation?.Any(record => record is null) == true)
            throw new ArgumentException("Publication presentation cannot contain null records.");
        VersionId = versionId;
        Version = version;
        Layout = Array.AsReadOnly((layout ?? []).Select(record => record with
            { AdditionalProperties = record.AdditionalProperties?.Clone() }).ToArray());
        ActivityPresentation = Array.AsReadOnly((activityPresentation ?? []).ToArray());
    }

    public string VersionId { get; }
    public string Version { get; }
    public IReadOnlyList<WorkflowExecutableLayoutRecord> Layout { get; }
    public IReadOnlyList<WorkflowExecutableActivityPresentationRecord> ActivityPresentation { get; }
}
