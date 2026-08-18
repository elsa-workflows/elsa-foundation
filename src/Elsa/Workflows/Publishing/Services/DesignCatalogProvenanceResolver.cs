using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Answers <see cref="IDesignProvenanceResolver"/> from the design catalog this engine actually has (FR-B-012).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives in the publishing surface.</b> The question belongs to Runtime's inspection views, but §E2.2
/// forbids Runtime from depending on Design, and Design deliberately references nothing of Runtime — the two are
/// fully decoupled, and adding an edge either way to answer a rendering question would be the wrong trade.
/// <c>Elsa.Workflows.Publishing</c> is the sanctioned bridge that already sees both (§2.24.2 #8), so it
/// answers here without a new dependency anywhere. It sits on the <b>engine</b> feature rather than the API one
/// because the capability is "this engine has a design catalog", not "this engine serves publishing HTTP".
/// </para>
/// <para>
/// <b>Composition carries the meaning.</b> A runtime-only engine does not compose this feature, so no resolver is
/// registered, and every design identifier renders flagged — which is correct, because such an engine has no
/// design catalog to resolve against. A combined engine composes it and gets a real per-id answer.
/// </para>
/// <para>
/// The store is optional for the same reason it is optional elsewhere in this module: publishing composes without
/// design persistence in some shapes. Absent a store nothing resolves, which is the same answer a runtime-only
/// engine gives and the safe one.
/// </para>
/// </remarks>
public sealed class DesignCatalogProvenanceResolver(IWorkflowDefinitionVersionStore? versionStore = null)
    : IDesignProvenanceResolver
{
    public async ValueTask<bool> ResolvesAsync(string definitionVersionId, CancellationToken cancellationToken = default)
    {
        if (versionStore is null || string.IsNullOrWhiteSpace(definitionVersionId))
            return false;

        return await versionStore.FindByIdAsync(definitionVersionId, cancellationToken) is not null;
    }
}
