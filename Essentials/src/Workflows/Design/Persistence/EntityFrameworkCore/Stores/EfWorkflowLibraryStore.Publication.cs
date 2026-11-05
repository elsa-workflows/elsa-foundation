using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    /// <summary>
    /// Refreshes the query projection from serving authority while holding the same tenant fence
    /// as organization writes. Reading inside the fence prevents delayed notifications from
    /// overwriting a newer authority observation. This never changes authored content or manual tags.
    /// </summary>
    internal Task<bool> RefreshPublicationAsync(string definitionId, IWorkflowActivationAuthority authority,
        CancellationToken cancellationToken) =>
        WriteAsync(Guid.NewGuid().ToString("N"), "library-publication-refresh", new { definitionId }, async token =>
        {
            var hash = DefinitionHash(definitionId);
            var definition = await DefinitionRows.SingleOrDefaultAsync(row => row.IdLookupHash == hash, token);
            // Imported runtime artifacts may have no local authoring definition.
            if (definition is null) return false;
            EfDesignSupport.EnsureDefinitionIdentity(definitionId, definition.Id, "library publication projection");
            var slots = await authority.ListByDefinitionAsync(definition.Id, token);
            var published = slots.Any(slot => slot.ActiveActivationId is not null);
            var organization = await OrganizationRows.SingleOrDefaultAsync(row => row.DefinitionHash == hash, token);
            if (organization is null)
            {
                organization = NewOrganization(definition);
                // Persisting serving membership does not constitute a manual organization edit.
                // Preserve the same initial revision a reader saw while this row was virtual.
                organization.Revision = "0";
            }
            organization.Published = published;
            return published;
        }, cancellationToken);
}
