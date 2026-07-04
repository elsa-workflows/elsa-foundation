using CShells.FastEndpoints.Contracts;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Configurators;

/// <summary>
/// Turns on FastEndpoints' RFC 7807 ProblemDetails error responses (MS-14) for every Elsa endpoint. Registered once at
/// the shared <see cref="FastEndpointsFeatureBase"/> level so the whole API surface — including endpoints contributed by
/// other feature packages — returns a single, consistent machine-readable error shape instead of the default plain
/// error DTO. The endpoint base classes map their known failures onto the right status code (e.g. a not-found lookup to
/// 404, an invalid argument to 400); this configurator only fixes the wire <em>shape</em> those statuses are rendered in.
/// </summary>
internal sealed class ProblemDetailsFastEndpointConfigurator : IFastEndpointsConfigurator
{
    /// <inheritdoc />
    public void Configure(Config config)
    {
        config.Errors.UseProblemDetails();
    }
}
