using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

internal static class BackgroundActivityEligibility
{
    public const string WorkIdMetadataKey = "runtime.backgroundActivityWorkId";
    public const string ResultReason = "BackgroundActivityResult";

    public static void Validate(ExecutableNode node)
    {
        if (node.ExecutionPlacement != ActivityExecutionPlacement.Background ||
            node.ActivityContract?.SupportsBackgroundExecution != true ||
            node.IntrinsicKind is not null || node.Structure is not null || node.ChildSlots.Count > 0 ||
            !node.ActivityContract.Result.Policy.IsPersistable || node.ActivityContract.Result.Policy.HidesValue() ||
            node.ActivityContract.Result.Projections.Values.Any(projection => !projection.Policy.IsPersistable || projection.Policy.HidesValue()))
            throw new InvalidOperationException("VF-BG-001: The pinned activity is not eligible for background execution.");
    }

    public static void Validate(IActivity activity)
    {
        if (activity.GetType().GetCustomAttribute<BackgroundExecutionSafeAttribute>() is null ||
            activity is IRuntimeStructuralActivity or IRuntimeActivityCheckpointParticipant or IStatefulActivity or
                IRuntimeActivityChildCompletionHandler or IRuntimeActivityChildFaultHandler or IRuntimeActivityChildNotificationHandler)
            throw new InvalidOperationException("VF-BG-001: The activated activity does not implement the pinned background-safe leaf contract.");
    }
}
