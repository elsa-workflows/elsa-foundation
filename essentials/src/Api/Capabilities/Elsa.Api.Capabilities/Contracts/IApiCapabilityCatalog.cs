using Elsa.Api.Capabilities.Models;

namespace Elsa.Api.Capabilities.Contracts;

public interface IApiCapabilityCatalog
{
    Task<ApiCapabilitiesDocument> GetAsync(CancellationToken cancellationToken = default);
}
