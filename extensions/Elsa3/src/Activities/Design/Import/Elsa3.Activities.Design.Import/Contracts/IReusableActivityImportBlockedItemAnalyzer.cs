using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;

namespace Elsa3.Activities.Design.Import.Contracts;

/// <summary>
/// Design-side mapping seam implemented by Elsa3.Mapping, next to <see cref="IReusableActivityImportMaterializer"/>.
/// It converts plan items an apply left out because a reusable workflow they use was refused, for their own conversion
/// diagnostics only, so one migration inventory lists every finding (#6242). It writes nothing and the items stay out.
/// </summary>
public interface IReusableActivityImportBlockedItemAnalyzer
{
    /// <summary>
    /// The conversion diagnostics of each of <paramref name="blockedSourceVersionIds"/>, each naming its item by
    /// <c>SourceVersionId</c> metadata. A reference to a reusable workflow that converts is read against its converted
    /// contract; one that does not is read against what its Elsa 3 definition declares (inputs, outputs, outcomes), and the
    /// findings that only its converted contract could settle, routing on its outcomes, are left out.
    /// </summary>
    /// <param name="tenantId">As for <see cref="IReusableActivityImportMaterializer.MaterializeAsync"/>.</param>
    ValueTask<IReadOnlyList<Elsa3MigrationDiagnostic>> AnalyzeBlockedAsync(
        ReusableActivityImportCollection collection,
        ReusableActivityImportPlan plan,
        IReadOnlyCollection<string> blockedSourceVersionIds,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
