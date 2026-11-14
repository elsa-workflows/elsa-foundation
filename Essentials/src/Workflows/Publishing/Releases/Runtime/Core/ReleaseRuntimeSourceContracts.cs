using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Core;

/// <summary>Required host-selected acquisition namespace. No default is supplied and no field grants export permission.</summary>
public interface IReleaseRuntimeSourceContextAccessor
{
    ReleaseRuntimeContentScope Current { get; }
}

/// <summary>Required source-side export authorization, evaluated before exact material/template payload reads.</summary>
/// <remarks>A correct namespace, hash or declared dependency is not read authority. A host may explicitly authorize
/// a retained root's closure; the generic contributor never invents that permission. Target policy is independent.</remarks>
public interface IReleaseRuntimeSourceAuthorization
{
    ValueTask<bool> AuthorizeMaterialAsync(ReleaseRuntimeContentScope context, string artifactId, string artifactHash,
        WorkflowExecutableIdentity? fullPin, CancellationToken cancellationToken = default);
    ValueTask<bool> AuthorizeTemplateAsync(ReleaseRuntimeContentScope context, string templateId, string templateHash,
        CancellationToken cancellationToken = default);
}
