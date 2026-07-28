namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>What a single reference GC sweep removed (ADR 0040).</summary>
public sealed record WorkflowExecutableReferenceSweepResult(
    int DeletedReferenceCount,
    int DeletedArtifactCount,
    int DeletedActivityTemplateCount = 0)
{
    /// <summary>True when the sweep removed at least one reference or artifact.</summary>
    public bool DidWork => DeletedReferenceCount > 0 || DeletedArtifactCount > 0 || DeletedActivityTemplateCount > 0;
}
