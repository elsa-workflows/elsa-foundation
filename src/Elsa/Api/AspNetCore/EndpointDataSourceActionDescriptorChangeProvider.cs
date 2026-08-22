using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// Projects dynamic endpoint changes into API Explorer's action-descriptor invalidation seam.
/// </summary>
public sealed class EndpointDataSourceActionDescriptorChangeProvider : IActionDescriptorChangeProvider
{
    private readonly EndpointDataSource _endpointDataSource;

    /// <summary>Creates the API Explorer invalidation bridge for the effective endpoint source.</summary>
    public EndpointDataSourceActionDescriptorChangeProvider(EndpointDataSource endpointDataSource)
    {
        ArgumentNullException.ThrowIfNull(endpointDataSource);
        _endpointDataSource = endpointDataSource;
    }

    /// <inheritdoc />
    public IChangeToken GetChangeToken() => _endpointDataSource.GetChangeToken();
}
