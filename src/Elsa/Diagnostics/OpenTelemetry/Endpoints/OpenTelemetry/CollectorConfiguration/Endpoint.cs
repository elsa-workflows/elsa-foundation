using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetry.CollectorConfiguration;

[PublicAPI]
internal class Endpoint(IOpenTelemetryProvider provider) : ElsaEndpointWithoutRequest<Elsa.Diagnostics.OpenTelemetry.Core.Models.CollectorConfiguration>
{
    public override void Configure()
    {
        Get("/diagnostics/opentelemetry/collector-configuration");
        ConfigurePermissions(OpenTelemetryPermissions.Policy);
    }

    public override async Task<Elsa.Diagnostics.OpenTelemetry.Core.Models.CollectorConfiguration> ExecuteAsync(CancellationToken cancellationToken)
    {
        return await provider.GetCollectorConfigurationAsync(cancellationToken);
    }
}
