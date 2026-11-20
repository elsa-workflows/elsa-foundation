using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>Provider-owned projection of its exact manifest into the common canonical read-analysis view.</summary>
/// <remarks>
/// Invoked by the scoped coordinator after resource/provider authorization. The projection performs
/// no store read and creates no synthetic workflow draft. Null means the contribution does not own
/// this provider/schema; an owned malformed manifest must fail rather than disappear as unsupported.
/// Singleton providers must not capture the scoped analyzer to implement this port.
/// </remarks>
public interface IActivityDataSelectionDocumentProjector
{
    ValueTask<ActivityDataSelectionDocument?> TryProjectAsync(
        ActivityProviderManifest manifest,
        ActivityContract contract,
        string resourceId,
        string contractRevision,
        CancellationToken cancellationToken = default);
}
