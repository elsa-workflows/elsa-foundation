using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Returns the log sources known to this host (v1: the single local source) for a UI source selector.
/// </summary>
internal sealed class SourcesEndpoint(
    IStructuredLogSourceProvider sourceProvider,
    IOptions<StructuredLogsOptions> options) : ElsaEndpointWithoutRequest<IReadOnlyList<LogSource>>
{
    public override void Configure()
    {
        Get(options.Value.SourcesPath);
        ConfigurePermissions(StructuredLogsPermissions.Policy);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(sourceProvider.GetKnownSources(), ct);
}
