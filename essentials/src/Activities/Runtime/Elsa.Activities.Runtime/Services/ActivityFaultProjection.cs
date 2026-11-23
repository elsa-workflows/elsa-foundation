using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Projects an activity-authored <see cref="ActivityFault"/> onto the durable
/// <see cref="NormalizedActivityFault"/> record. Every scheduler work handler that persists a returned
/// fault goes through here so the projection — in particular the author-supplied classification metadata —
/// stays identical across the invoke, resume, notify-parent and parent-completion paths.
/// </summary>
public static class ActivityFaultProjection
{
    public static NormalizedActivityFault ToNormalized(this ActivityFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        var classification = fault.ClassificationMetadata;
        return new NormalizedActivityFault(
            fault.Code,
            typeof(ActivityFault).FullName!,
            fault.Message,
            sanitizedStackTrace: null,
            fault.IsRetryable,
            // Keep an unclassified fault's record byte-identical to what it was before classification
            // existed: pass null rather than an empty map.
            metadata: classification.Count == 0 ? null : classification);
    }
}
