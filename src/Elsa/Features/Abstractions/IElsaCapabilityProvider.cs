namespace Elsa.Features.Abstractions;

public interface IElsaCapabilityProvider
{
    ValueTask<IReadOnlyCollection<ElsaCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
