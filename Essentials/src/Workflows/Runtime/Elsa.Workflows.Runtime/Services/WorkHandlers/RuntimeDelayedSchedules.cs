using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.WorkHandlers;

/// <summary>
/// The runtime side of R-7, the delayed child schedule (spec 261). A structural evaluation stages a child schedule with a
/// due time; the work item that schedules it carries <see cref="RuntimeMetadataKeys.ScheduleNotBefore"/>, and its
/// post-commit intent carries the due time as <see cref="RuntimePostCommitIntent.NotBefore"/>, so its outbox item becomes
/// available then and the outbox sweep delivers it. Public so the Activities-assembly evaluations can use it, like
/// <see cref="SchedulerWorkHandlerHelpers"/>.
/// </summary>
public static class RuntimeDelayedSchedules
{
    /// <summary>
    /// Refuses the delayed schedules among <paramref name="requests"/> that this host cannot honour. A request whose due
    /// time is null or not after the runtime clock is an ordinary schedule and is not checked. Throws
    /// <see cref="RuntimeDelayedScheduleException"/>, which faults the evaluation with its code, when the host registers
    /// no <see cref="IRuntimeDelayedScheduleCapability"/> or a due time is more than
    /// <see cref="RuntimeDelayedScheduleOptions.MaxDelay"/> ahead.
    /// </summary>
    public static void Validate(
        IServiceProvider services,
        TimeProvider timeProvider,
        string executableNodeId,
        IReadOnlyCollection<RuntimeChildActivityScheduleRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(requests);

        DateTimeOffset? now = null;
        foreach (var request in requests)
        {
            if (request.NotBefore is not { } notBefore)
                continue;
            now ??= timeProvider.GetUtcNow();
            if (notBefore <= now)
                continue;

            if (services.GetService<IRuntimeDelayedScheduleCapability>() is null)
                throw new RuntimeDelayedScheduleException(
                    RuntimeDelayedScheduleFaultCodes.Unavailable,
                    $"Activity '{executableNodeId}' scheduled child '{request.ExecutableNodeId}' not before {Format(notBefore)}, but this host has no outbox sweep to deliver a delayed schedule.");

            var maxDelay = (services.GetService<RuntimeDelayedScheduleOptions>() ?? new RuntimeDelayedScheduleOptions()).MaxDelay;
            if (notBefore - now.Value > maxDelay)
                throw new RuntimeDelayedScheduleException(
                    RuntimeDelayedScheduleFaultCodes.TooFar,
                    $"Activity '{executableNodeId}' scheduled child '{request.ExecutableNodeId}' not before {Format(notBefore)}, more than {maxDelay} ahead of the runtime clock.");
        }
    }

    /// <summary>The due time a <c>ScheduleActivity</c> work item carries, or null for an ordinary schedule.</summary>
    public static DateTimeOffset? ReadNotBefore(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (workItem.CommandKind != WorkflowExecutionCommandKind.ScheduleActivity ||
            !workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.ScheduleNotBefore, out var value))
            return null;

        return DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var notBefore)
            ? notBefore
            : throw new InvalidOperationException($"Scheduler work item '{workItem.WorkItemId}' carries an invalid '{RuntimeMetadataKeys.ScheduleNotBefore}' value.");
    }

    /// <summary>Writes a due time as the work-item metadata value <see cref="ReadNotBefore"/> reads.</summary>
    public static string Format(DateTimeOffset notBefore) => notBefore.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
