using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetry.Resources;

[PublicAPI]
internal class Endpoint(IOpenTelemetryProvider provider) : ElsaEndpoint<OpenTelemetryResourceFilter, OpenTelemetryResourceResult>
{
    public override void Configure()
    {
        Post("/diagnostics/opentelemetry/resources/search");
        ConfigurePermissions(OpenTelemetryPermissions.Policy);
    }

    public override async Task<OpenTelemetryResourceResult> ExecuteAsync(OpenTelemetryResourceFilter request, CancellationToken cancellationToken)
    {
        return await provider.GetResourcesAsync(request, cancellationToken);
    }
}
