using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class StimulusDeliveryFailures
{
    internal static bool IsUnavailable(Exception exception)
    {
        if (RuntimeCheckpointCommitValidationException.IsCauseOf(exception)) return false;
        if (exception is StimulusDeliveryBoundaryException boundary) return boundary.IsUnavailable;
        if (exception is InvalidDataException or JsonException or ArgumentException or NotSupportedException or
            WorkflowExecutableReferenceRejectedException or WorkflowExecutableStartRejectedException) return false;
        if (exception is IOException or DbException or TimeoutException or OperationCanceledException or RuntimeSchedulerWorkUnavailableException or
            WorkflowExecutableRootWriteLeaseUnavailableException or WorkflowExecutableRootWriteLeaseLostException) return true;
        if (exception.InnerException is { } cause) return IsUnavailable(cause);
        return exception is not InvalidOperationException;
    }
}
