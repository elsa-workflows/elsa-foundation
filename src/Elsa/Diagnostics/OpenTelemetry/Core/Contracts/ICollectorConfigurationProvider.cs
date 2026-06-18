using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface ICollectorConfigurationProvider
{
    ValueTask<CollectorConfiguration> GetAsync(CancellationToken cancellationToken = default);
}
