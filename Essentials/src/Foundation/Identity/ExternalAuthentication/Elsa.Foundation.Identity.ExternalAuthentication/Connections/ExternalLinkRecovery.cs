using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

/// <summary>Uses protected normal-session provenance as bounded credential history, independently of provider persistence.</summary>
internal sealed class ExternalLinkRecovery(IServiceScopeFactory scopes, IOptions<ExternalAuthenticationOptions> options,
    IConnectionRegistry registry, IConnectionReadiness readiness, ILocalLoginRecovery localRecovery, TimeProvider clock) : IExternalLinkRecovery
{
    public async ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureAsync(ClaimsPrincipal actor, ExternalLinkAuthorization authorization,
        ExternalIdentityKey excluded, CancellationToken cancellationToken = default)
    {
        var configured = options.Value; var tenant = configured.TenantId;
        if (authorization.User.TenantId != tenant || excluded.TenantId != tenant || configured.RecoveryScanLimit is < 1 or > 10000 ||
            configured.RecoveryEvidenceLifetime < TimeSpan.FromMinutes(1) || configured.RecoveryEvidenceLifetime > TimeSpan.FromDays(30)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); cancellationToken = deadline.Token;
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var authority = services.GetService<ICanonicalUserSignInService>();
        var schemes = services.GetService<IAuthenticationSchemeProvider>();
        if (authority?.SupportsDurableSessions != true || schemes is null || await schemes.GetSchemeAsync(authority.AuthenticationScheme) is null) return null;
        var views = (await registry.ReadAsync(cancellationToken)).Connections.Where(view => view.Effective && view.Record.Enabled && view.Record.ArchivedAt is null).ToArray();
        var observations = new Dictionary<string, ExternalProtocolCheck?>(StringComparer.Ordinal);
        var histories = new Dictionary<string, IReadOnlyList<AuthenticationSessionRecord>>(StringComparer.Ordinal);
        var userGuards = await Candidate(authorization.User.Id, false);
        if (userGuards is null) return null;
        var globalGuards = await localRecovery.CaptureRevisionGuardsAsync(actor, cancellationToken);
        if (globalGuards is null)
        {
            var attempted = new HashSet<string>(StringComparer.Ordinal);
            if (services.GetRequiredService<ITokenSubjectResolver>().TryResolve(actor, out var subject) && subject.TenantId == tenant && subject.CanonicalUserId is { } editor)
            { attempted.Add(editor); globalGuards = await Candidate(editor, true); }
            if (globalGuards is null && attempted.Add(authorization.User.Id)) globalGuards = await Candidate(authorization.User.Id, true);
            var users = services.GetService<IPagedUserStore>();
            for (var skip = 0; globalGuards is null && users is not null && skip < configured.RecoveryScanLimit;)
            {
                var take = Math.Min(IamPageRequest.MaximumTake, configured.RecoveryScanLimit - skip);
                var page = await users.ListPageAsync(tenant, new(skip, take), cancellationToken: cancellationToken);
                if (page.Items.Count > take || page.Items.Any(user => user.TenantId != tenant)) return null;
                foreach (var user in page.Items)
                    if (user.Status == UserStatus.Active && attempted.Add(user.Id) && await Candidate(user.Id, true) is { } found) { globalGuards = found; break; }
                skip += page.Items.Count;
                if (page.Items.Count == 0 || skip >= page.TotalCount) break;
            }
        }
        if (globalGuards is null || IdentitySnapshot.Merge(authorization.Guards, userGuards) is not { } first) return null;
        return IdentitySnapshot.Merge(first, globalGuards);

        async ValueTask<IReadOnlyList<IamRevisionGuard>?> Candidate(string userId, bool administrator)
        {
            var local = await IdentitySnapshot.CaptureAsync(services, tenant, userId, [], async () =>
                await Eligible(userId, true, administrator), cancellationToken);
            if (local is not null) return local;
            var store = services.GetService<IAuthenticationSessionStore>();
            if (store?.IsDurable != true) return null;
            if (!histories.TryGetValue(userId, out var sessions))
            {
                var page = await store.ListAsync(tenant, userId, new(0, Math.Min(100, configured.RecoveryScanLimit)), cancellationToken);
                if (page.Items.Count > Math.Min(100, configured.RecoveryScanLimit) || page.Items.Any(item => item.TenantId != tenant || item.UserId != userId)) return null;
                histories[userId] = sessions = page.Items;
            }
            var now = clock.GetUtcNow();
            foreach (var session in sessions)
            {
                if (session.CreatedAt > now || session.CreatedAt < now - configured.RecoveryEvidenceLifetime || session.ConnectionKey is null ||
                    session.Issuer is null || session.ExternalSubject is null || session.ExternalLinkRevision is null ||
                    session.ConnectionMaterialRevision is null || session.ConnectionCredentialBinding is null || session.ConnectionRedirectUri is null ||
                    string.Equals(session.ConnectionKey, excluded.Provider, StringComparison.OrdinalIgnoreCase) &&
                    session.Issuer == excluded.Issuer && session.ExternalSubject == excluded.Subject) continue;
                var view = views.SingleOrDefault(view => view.Record.Key == session.ConnectionKey && view.Record.Id == session.ConnectionRecordId &&
                    view.MaterialRevision == session.ConnectionMaterialRevision);
                if (view is null || services.GetRequiredService<IExternalAuthenticationFlowService>().GetCallbackUri(view.Record.Key, AuthenticationArtifactPurpose.SignIn) != session.ConnectionRedirectUri) continue;
                if (!observations.TryGetValue(view.Record.Id, out var tested))
                {
                    var protocols = services.GetServices<IExternalAuthenticationProtocol>().Where(item => item.Name == view.Record.Protocol).Take(2).ToArray();
                    tested = protocols.Length == 1 && await readiness.IsReadyAsync(view.Record, cancellationToken)
                        ? await protocols[0].TestAsync(view.Record, cancellationToken) : null;
                    observations[view.Record.Id] = tested;
                }
                if (tested is null || tested.Failure != ExternalProtocolFailure.None || tested.Issuer != session.Issuer || tested.CredentialBinding != session.ConnectionCredentialBinding) continue;
                var guards = await IdentitySnapshot.CaptureAsync(services, tenant, userId, view.Record.Admission.DefaultRoleIds, async () =>
                {
                    var checkedAt = clock.GetUtcNow();
                    if (session.CreatedAt > checkedAt || session.CreatedAt < checkedAt - configured.RecoveryEvidenceLifetime) return false;
                    // The original session remains ended/expired. Only its bounded historical credential observation is checked.
                    if (!await authority.ValidateSessionAsync(session with { RevokedAt = null, ExpiresAt = session.CreatedAt + configured.RecoveryEvidenceLifetime }, cancellationToken) ||
                        !await Eligible(userId, false, administrator)) return false;
                    var admission = await services.GetRequiredService<IExternalAdmissionService>().AssessAsync(new(view,
                        new(tenant, session.ConnectionKey, session.Issuer, session.ExternalSubject), [new("iss", session.Issuer), new("sub", session.ExternalSubject)]), cancellationToken);
                    return admission.Kind == ExternalAdmissionKind.ExistingUser && admission.User?.Id == userId;
                }, cancellationToken);
                if (guards is not null) return guards;
            }
            return null;
        }

        async ValueTask<bool> Eligible(string id, bool password, bool administrator) =>
            await authority.CanSignInAsync(new(tenant, id), password, cancellationToken) && (!administrator ||
                (await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(new(tenant, id,
                    DefaultIdentityPermissionKeys.IdentityProvidersManage), cancellationToken)).Status == CurrentSubjectPermissionStatus.Granted);
    }
}
