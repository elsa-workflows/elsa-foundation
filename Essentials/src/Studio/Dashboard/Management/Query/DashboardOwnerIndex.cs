using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Management.Query;

internal static class DashboardOwnerIndex
{
    public static IReadOnlyDictionary<string, T> Build<T>(IEnumerable<T> extensions, Func<T, IReadOnlyCollection<DashboardDatasetOwner>> ownership)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            var owners = ownership(extension);
            if (owners is null || owners.Count > 100)
                throw Invalid();
            foreach (var owner in owners)
            {
                if (owner is null || string.IsNullOrWhiteSpace(owner.DatasetId) || owner.DatasetId.Length > 128 ||
                    string.IsNullOrWhiteSpace(owner.OwnerModuleId) || owner.OwnerModuleId.Length > 128 ||
                    !result.TryAdd(owner.DatasetId, extension))
                    throw Invalid();
            }
        }
        return result;
    }

    public static T Resolve<T>(IEnumerable<T> extensions, Func<T, IReadOnlyCollection<DashboardDatasetOwner>> ownership,
        DashboardDatasetDeclaration dataset)
    {
        var index = Build(extensions, ownership);
        if (!index.TryGetValue(dataset.Id, out var extension) ||
            !ownership(extension).Any(owner => owner.DatasetId == dataset.Id && owner.OwnerModuleId == dataset.OwnerModuleId))
            throw new DashboardQueryException("DATASET_UNAVAILABLE", "The dataset's owning query operation is unavailable.");
        return extension;
    }

    private static DashboardQueryException Invalid() => new("DATASET_OWNER_CONFLICT", "Dataset ownership configuration is invalid or ambiguous.");
}
