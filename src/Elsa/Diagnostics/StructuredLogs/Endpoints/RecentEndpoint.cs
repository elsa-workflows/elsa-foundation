using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Returns the most-recent captured entries (newest-aligned), optionally filtered. Thin adapter over the
/// store + filter binder + serializer.
/// </summary>
internal sealed class RecentEndpoint(
    IStructuredLogStore store,
    StructuredLogFilterBinder binder,
    StructuredLogEntrySerializer serializer,
    IOptions<StructuredLogsOptions> options) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        Get(options.Value.RecentPath);
        ConfigurePermissions(StructuredLogsPermissions.Policy);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var query = HttpContext.Request.Query;

        Core.Models.StructuredLogFilter filter;
        try
        {
            filter = binder.Bind(query["minLevel"], query["category"], query["source"], query["take"]);
        }
        catch (InvalidLogQueryException e)
        {
            await Send.StringAsync(e.Message, 400, cancellation: ct);
            return;
        }

        var entries = store.GetRecent(filter);
        await Send.StringAsync(serializer.SerializeMany(entries), 200, "application/json", ct);
    }
}
