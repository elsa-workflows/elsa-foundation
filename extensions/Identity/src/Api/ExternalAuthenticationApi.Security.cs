using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public static partial class ExternalAuthenticationApi
{
    private static async Task SecurityNotification(HttpContext context, string action, AuthEventOutcome outcome, string target, string result, string? sessionId = null,
        AuthEventCategory category = AuthEventCategory.CredentialLifecycle)
    {
        context.Items[typeof(AuthEvent)] = true;
        var actor = context.RequestServices.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var subject)
            ? subject.UserId ?? "external" : "anonymous";
        var detail = new Dictionary<string, string> { ["action"] = action, ["result"] = result };
        if (sessionId is not null) detail["sessionId"] = sessionId;
        var notice = new AuthEvent(Guid.NewGuid().ToString("n"), context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            category, actor, target, outcome, Tenant(context), detail);
        foreach (var sink in context.RequestServices.GetServices<IAuthEventSink>())
        {
            try { await sink.EmitAsync(notice, context.RequestAborted); }
            catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
            {
                // A notification fault cannot undo committed revocation or expose an upstream/sink exception.
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExternalAuthenticationApi))
                    .LogWarning("Authentication notification failed ({FailureType}).", exception.GetType().Name);
            }
        }
    }

    private static string Tenant(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.TenantId;
}
