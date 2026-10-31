using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    public async Task<IReadOnlyDictionary<string, string>> GetVersionLabelsAsync(IReadOnlyList<string> versionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        if (versionIds.Count > 100 || versionIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Select at most 100 nonblank workflow version identities.");
        if (versionIds.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        var distinct = versionIds.Distinct(StringComparer.Ordinal).ToArray();
        var hashes = distinct.Select(EfDesignSupport.LookupHash).ToArray();
        var rows = await Scoped(db.Versions).AsNoTracking().Where(row => hashes.Contains(row.IdLookupHash))
            .Select(row => new { row.Id, row.Version }).ToArrayAsync(cancellationToken);
        if (rows.Any(row => !distinct.Contains(row.Id, StringComparer.Ordinal)))
            throw new InvalidOperationException("A workflow version-label lookup returned a different physical identity.");
        return rows.ToDictionary(row => row.Id, row => row.Version, StringComparer.Ordinal);
    }
}
