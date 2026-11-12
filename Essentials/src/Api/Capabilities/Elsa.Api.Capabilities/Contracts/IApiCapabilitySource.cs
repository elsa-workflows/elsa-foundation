using Elsa.Api.Capabilities.Models;

namespace Elsa.Api.Capabilities.Contracts;

/// <summary>
/// Contributes operationally conditional client contracts. Sources are evaluated in the active
/// shell scope and must not infer declarations from feature or implementation type names.
/// </summary>
public interface IApiCapabilitySource
{
    ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);
}
