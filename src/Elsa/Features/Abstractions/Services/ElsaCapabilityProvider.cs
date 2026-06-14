using Elsa.Features.Abstractions;

namespace Elsa.Features.Abstractions.Services;

public sealed class ElsaCapabilityProvider(IEnumerable<ElsaCapability> capabilities) : IElsaCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<ElsaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = capabilities
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(y => y.SourceFeature, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyCollection<ElsaCapability>>(result);
    }
}
