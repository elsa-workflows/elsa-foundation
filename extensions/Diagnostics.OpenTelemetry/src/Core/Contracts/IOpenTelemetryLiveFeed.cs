using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Core.Contracts;

public interface IOpenTelemetryLiveFeed
{
    ValueTask PublishAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OpenTelemetryStreamItem> SubscribeAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default);
}
