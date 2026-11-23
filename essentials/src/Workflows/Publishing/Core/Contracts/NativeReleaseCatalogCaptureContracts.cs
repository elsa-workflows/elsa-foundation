using Elsa.Workflows.Publishing.Releases.Runtime.Core;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Required host export policy for exact original Activity Definition publication fragments.</summary>
/// <remarks>Separate from workflow-root admission. Nullable original catalog ownership is inert provenance,
/// while the selected source context identifies the acquisition operation and grants no access.</remarks>
public interface INativeReleaseCatalogCaptureAuthorization
{
    ValueTask<bool> AuthorizeActivityVersionAsync(ReleaseRuntimeContentScope context, string activityVersionId, CancellationToken cancellationToken = default);
}
