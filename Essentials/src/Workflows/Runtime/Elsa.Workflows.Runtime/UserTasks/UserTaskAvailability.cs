using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal static class UserTaskAvailability
{
    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is JsonException or InvalidDataException or ArgumentException or NotSupportedException) return false;
        if (exception is OperationCanceledException) return !cancellationToken.IsCancellationRequested;
        if (exception is TimeoutException or IOException or System.Data.Common.DbException || exception is UserTaskException { Code: "unavailable" }) return true;
        return exception is InvalidOperationException && exception.InnerException is { } inner &&
            (IsUnavailable(inner, cancellationToken) || inner is InvalidOperationException { InnerException: null });
    }
}
