using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class EfBookmarkStimulusEvidence
{
    internal static string LookupKey(string stimulusType, string stimulusHash) =>
        EfRelationalIdentity.Hash($"{stimulusType.Length}:{stimulusType}{stimulusHash}");

    internal static Task<bool> HasMatchingRowAsync(RuntimeDbContext context, string scope, string type, string stimulus,
        CancellationToken token)
    {
        var scopeKey = EfRelationalIdentity.Hash(scope);
        var lookup = LookupKey(type, stimulus);
        return context.Bookmarks.AsNoTracking().AnyAsync(row => row.ScopeKeyHash == scopeKey && row.StimulusLookupKey == lookup, token);
    }
}
