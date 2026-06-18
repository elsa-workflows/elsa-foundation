using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetry.Traces;

[PublicAPI]
internal class Endpoint(IOpenTelemetryProvider provider) : ElsaEndpoint<OpenTelemetryTraceFilter, OpenTelemetryTraceResult>
{
    public override void Configure()
    {
        Post("/diagnostics/opentelemetry/traces/search");
        ConfigurePermissions(OpenTelemetryPermissions.Policy);
    }

    public override async Task<OpenTelemetryTraceResult> ExecuteAsync(OpenTelemetryTraceFilter request, CancellationToken cancellationToken)
    {
        return await provider.GetTracesAsync(request, cancellationToken);
    }
}
