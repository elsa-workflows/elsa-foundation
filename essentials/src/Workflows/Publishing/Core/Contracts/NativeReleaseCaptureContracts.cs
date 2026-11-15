using Elsa.Workflows.Publishing.Releases.Runtime.Core;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Required trusted acquisition context; physical partition and nullable logical tenant remain independent.</summary>
public interface INativeReleaseCaptureContextAccessor
{
    ReleaseRuntimeContentScope Current { get; }
}

/// <summary>Required source export policy; selectors, scopes and publication associations grant no permission.</summary>
public interface INativeReleaseCaptureAuthorization
{
    /// <summary>Authorizes original authored bytes and bounded publication provenance for this exact version.</summary>
    ValueTask<bool> AuthorizeWorkflowVersionAsync(ReleaseRuntimeContentScope context, string definitionVersionId, CancellationToken cancellationToken = default);
    /// <summary>Authorizes the selected root body before its full stored producer pin is known.</summary>
    ValueTask<bool> AuthorizeSelectedMaterialAsync(ReleaseRuntimeContentScope context, string definitionVersionId,
        string sourceReferenceId, string artifactId, CancellationToken cancellationToken = default);
}

public static class NativeReleaseCaptureSelections
{
    public const string WorkflowDefinitionVersion = "elsa.workflow-definition-version";
}
