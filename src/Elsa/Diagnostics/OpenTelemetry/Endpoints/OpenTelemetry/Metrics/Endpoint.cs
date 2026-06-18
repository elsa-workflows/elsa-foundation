using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetry.Metrics;

[PublicAPI]
internal class Endpoint(IOpenTelemetryProvider provider) : ElsaEndpoint<OpenTelemetryMetricFilter, OpenTelemetryMetricResult>
{
    public override void Configure()
    {
        Post("/diagnostics/opentelemetry/metrics/search");
        ConfigurePermissions(OpenTelemetryPermissions.Policy);
    }

    public override async Task<OpenTelemetryMetricResult> ExecuteAsync(OpenTelemetryMetricFilter request, CancellationToken cancellationToken)
    {
        return await provider.GetMetricsAsync(request, cancellationToken);
    }
}
