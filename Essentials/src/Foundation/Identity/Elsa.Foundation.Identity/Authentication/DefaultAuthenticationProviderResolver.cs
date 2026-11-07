using Elsa.Foundation.Identity.Core.Authentication;

namespace Elsa.Foundation.Identity.Authentication;

public sealed class DefaultAuthenticationProviderResolver(IEnumerable<IAuthenticationProviderModule> modules,
    IEnumerable<IAuthenticationProviderSource>? sources = null) : IAuthenticationProviderResolver
{
    public async ValueTask<IReadOnlyList<AuthenticationProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<AuthenticationProviderDescriptor>();

        foreach (var module in modules)
            descriptors.Add(await module.DescribeAsync(cancellationToken));

        foreach (var source in sources ?? [])
        {
            var supplied = await source.ListAsync(cancellationToken);
            if (supplied.Count > 256) throw new InvalidOperationException("An authentication provider source exceeds its descriptor bound.");
            descriptors.AddRange(supplied);
        }

        if (descriptors.Count > 512 || descriptors.GroupBy(descriptor => (descriptor.Id.ToUpperInvariant(), descriptor.TenantId)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Authentication provider descriptors exceed their bound or contain duplicate identities.");

        return descriptors
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async ValueTask<AuthenticationProviderDescriptor?> FindAsync(string providerId, string? tenantId = null, bool allowGlobalFallback = false, CancellationToken cancellationToken = default)
    {
        var descriptors = await ListAsync(cancellationToken);
        var matches = descriptors
            .Where(x => string.Equals(x.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (tenantId is null)
            return matches.FirstOrDefault(x => x.TenantId is null) ?? matches.FirstOrDefault();

        var tenantMatch = matches.FirstOrDefault(x => string.Equals(x.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        if (tenantMatch is not null)
            return tenantMatch;

        return allowGlobalFallback ? matches.FirstOrDefault(x => x.TenantId is null) : null;
    }
}
