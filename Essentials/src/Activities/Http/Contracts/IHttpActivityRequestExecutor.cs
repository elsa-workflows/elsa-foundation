using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Http.Contracts;

/// <summary>
/// Owns one HTTP activity exchange, including request construction, connection application and transport.
/// The returned response is mapped to workflow outcomes by the activity.
/// </summary>
public interface IHttpActivityRequestExecutor
{
    ValueTask<SendHttpRequestResult> ExecuteAsync(HttpActivityRequest request, ActivityExecutionContext context,
        CancellationToken cancellationToken = default);
}
