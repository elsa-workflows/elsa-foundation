using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Read-side provider causes only; never classifies uncertain writes or callback effects.</summary>
internal static class RuntimeHistoryReadFailures
{
    internal static bool IsMalformed(Exception exception) =>
        exception is InvalidDataException or JsonException or RuntimeHistoryIntegrityException ||
        exception.InnerException is { } cause && IsMalformed(cause);

    internal static bool IsUnavailable(Exception exception)
    {
        if (IsMalformed(exception) || RuntimeCheckpointCommitValidationException.IsCauseOf(exception)) return false;
        var unavailable = false;
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is ArgumentException) return false;
            unavailable |= cause is IOException or DbException or TimeoutException or NotSupportedException or
                OperationCanceledException or RuntimeHistoryUnavailableException or RuntimeSchedulerWorkUnavailableException;
        }
        return unavailable;
    }
}
