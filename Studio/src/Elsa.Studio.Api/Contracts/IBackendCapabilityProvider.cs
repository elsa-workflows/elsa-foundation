namespace Elsa.Studio.Api.Contracts;

public interface IBackendCapabilityProvider
{
    Task<IReadOnlySet<string>> GetCapabilityIdsAsync(CancellationToken cancellationToken);
}
