using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Opt-in handler for a true source-neutral durable outbox source, using the existing contribution registry.</summary>
public interface IRuntimeDeliverySourceHandler
{
    ValueTask HandleAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default);
}
