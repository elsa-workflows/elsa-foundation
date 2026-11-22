using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Models;

public sealed class ActivityAuthoringException(
    int statusCode,
    string errorCode,
    string title,
    string message,
    IReadOnlyList<ActivityDiagnostic>? diagnostics = null,
    Exception? innerException = null,
    ActivityRecoveryView? recovery = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;
    public IReadOnlyList<ActivityDiagnostic> Diagnostics { get; } = diagnostics ?? [];
    public ActivityRecoveryView? Recovery { get; } = recovery;
}
