using Elsa.Features.Abstractions;

namespace Elsa.Features.Abstractions.Services;

public sealed class ElsaCapabilityProvider(IEnumerable<ElsaCapability> capabilities) : IElsaCapabilityProvider
{
    private readonly IReadOnlyCollection<ElsaCapability> _capabilities = capabilities
        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.OrderBy(y => y.SourceFeature, StringComparer.OrdinalIgnoreCase).First())
        .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public ValueTask<IReadOnlyCollection<ElsaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_capabilities);
    }
}
