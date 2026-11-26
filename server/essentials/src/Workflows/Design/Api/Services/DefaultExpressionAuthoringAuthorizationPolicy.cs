using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// Default host policy for permission-protected authoring endpoints. The claims revision is also
/// the policy fingerprint unless a host replaces this service with an external policy revision.
/// </summary>
public sealed class DefaultExpressionAuthoringAuthorizationPolicy : IExpressionAuthoringAuthorizationPolicy
{
    public ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(
        ExpressionAuthoringAuthorization caller,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(caller with
        {
            PolicyFingerprint = caller.PolicyFingerprint ?? caller.PermissionRevision
        });
    }
}
