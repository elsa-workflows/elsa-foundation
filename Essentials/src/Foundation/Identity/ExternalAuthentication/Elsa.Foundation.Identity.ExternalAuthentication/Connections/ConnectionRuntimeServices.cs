using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

/// <summary>Default installed-protocol/IAM/cookie checks and evidence-backed administrative recovery.</summary>
internal sealed class ConnectionRuntimeServices(IServiceScopeFactory scopes, IOptions<ExternalAuthenticationOptions> options,
    ConnectionProofStore proofs, TimeProvider clock, IHostEnvironment? environment = null)
    : IConnectionReadiness, IConnectionRecovery, ILocalLoginRecovery, IConnectionSignInEvidence
{
    public async ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
        (await AssessAsync(connection, cancellationToken)).Ready;

    public async ValueTask<ConnectionReadinessResult> AssessAsync(ConnectionRecord connection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { ConnectionSelection.Validate([connection]); }
        catch (InvalidOperationException) { return new(ConnectionReadinessFailure.InvalidConfiguration); }
        await using var scope = CreateScope();
        var services = scope.ServiceProvider;
        var session = services.GetService<ICanonicalUserSignInService>();
        var schemes = services.GetService<IAuthenticationSchemeProvider>();
        if (session is null || !session.SupportsDurableSessions || schemes is null || await schemes.GetSchemeAsync(session.AuthenticationScheme) is null)
            return new(ConnectionReadinessFailure.SessionUnavailable);
        var artifact = services.GetService<IAuthenticationArtifactStore>();
        if (artifact is null || !artifact.IsDurable && environment?.IsDevelopment() != true) return new(ConnectionReadinessFailure.ArtifactStoreUnavailable);
        var flow = services.GetService<IExternalAuthenticationFlowService>();
        if (flow?.GetCallbackUri(connection.Key, AuthenticationArtifactPurpose.SignIn) is null ||
            flow.GetCallbackUri(connection.Key, AuthenticationArtifactPurpose.Preview) is null) return new(ConnectionReadinessFailure.CallbackUnavailable);
        var protocol = Protocol(services, connection.Protocol);
        if (protocol is null) return new(ConnectionReadinessFailure.ProtocolUnavailable);
        var check = await protocol.CheckAsync(connection, cancellationToken);
        if (check.Failure != ExternalProtocolFailure.None) return new(check.Failure == ExternalProtocolFailure.SecretUnavailable
            ? ConnectionReadinessFailure.SecretUnavailable : ConnectionReadinessFailure.ProtocolConfiguration, check.Failure);
        var users = services.GetService<IUserStore>(); var roles = services.GetService<IRoleStore>();
        var links = services.GetService<IExternalIdentityStore>(); var membership = services.GetService<ITenantMembershipStore>();
        if (users is null || roles is null || links is null || !links.SupportsQualifiedKeys || membership is null)
            return new(ConnectionReadinessFailure.IdentityUnavailable);
        if (connection.Admission.Type != "reject")
        {
            if (services.GetServices<IExternalProvisioningPolicy>().Count(policy => policy.Name == connection.Admission.Type) != 1)
                return new(ConnectionReadinessFailure.PolicyUnavailable);
            if (connection.Admission.Type == "jit" && connection.Admission.Matcher is not null ||
                connection.Admission.Type == "match" && connection.Admission.Matcher != "verified-email") return new(ConnectionReadinessFailure.PolicyInvalid);
            var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(options.Value.TenantId, cancellationToken);
            if (services.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
                    services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities).CanManageUsers && !links.SupportsAtomicAdmission)
                return new(ConnectionReadinessFailure.IdentityUnavailable);
        }
        foreach (var id in connection.Admission.DefaultRoleIds.Distinct(StringComparer.Ordinal))
        {
            if (options.Value.AllowedRoleIds.Count > 0 && !options.Value.AllowedRoleIds.Contains(id, StringComparer.Ordinal)) return new(ConnectionReadinessFailure.RoleForbidden);
            if (await roles.FindAsync(options.Value.TenantId, id, cancellationToken) is null) return new(ConnectionReadinessFailure.RoleUnavailable);
        }
        return new();
    }

    public async ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
        await External(connection, cancellationToken) is not null;
    public ValueTask<bool> IsUsableForAsync(ConnectionRecord connection, ClaimsPrincipal actor, CancellationToken cancellationToken = default) => IsUsableAsync(connection, cancellationToken);
    public async ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default) => await Local(null, cancellationToken) is not null;
    public async ValueTask<bool> IsUsableForAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) => await Local(actor, cancellationToken) is not null;
    public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ConnectionRecord connection, ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) => External(connection, cancellationToken);
    public ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) => Local(actor, cancellationToken);

    public async ValueTask RecordAsync(string userId, ExternalAuthenticationProvenance provenance, CancellationToken cancellationToken = default)
    {
        if (!options.Value.AllowDatabaseManagement || !WindowValid() || string.IsNullOrWhiteSpace(userId) ||
            provenance.MaterialRevision is null || provenance.CredentialBinding is null || provenance.RedirectUri is null || provenance.AuthenticationSessionId is null) return;
        await using var scope = CreateScope();
        var services = scope.ServiceProvider;
        var session = services.GetService<ICanonicalUserSignInService>() is { } authority
            ? await authority.ReadSessionAsync(new(options.Value.TenantId, userId), provenance.AuthenticationSessionId, cancellationToken) : null;
        if (session is null || session.ConnectionKey != provenance.ConnectionKey || session.ConnectionRecordId != provenance.ConnectionRecordId ||
            session.Issuer != provenance.Issuer || session.ExternalSubject != provenance.Subject) return;
        var view = (await services.GetRequiredService<IConnectionRegistry>().ReadAsync(cancellationToken)).Connections
            .SingleOrDefault(view => view.Effective && view.Record.Enabled && view.Record.ArchivedAt is null && view.Record.Key == provenance.ConnectionKey);
        if (view is null || view.Record.Id != provenance.ConnectionRecordId || view.MaterialRevision != provenance.MaterialRevision ||
            !await Eligible(services, userId, false, cancellationToken)) return;
        if (services.GetRequiredService<IExternalAuthenticationFlowService>().GetCallbackUri(view.Record.Key, AuthenticationArtifactPurpose.SignIn) != provenance.RedirectUri) return;
        var link = await services.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(
            new(options.Value.TenantId, provenance.ConnectionKey, provenance.Issuer, provenance.Subject), cancellationToken);
        if (link?.UserId != userId) return;
        var protocol = Protocol(services, view.Record.Protocol);
        if (protocol is null) return;
        var current = await protocol.CheckAsync(view.Record, cancellationToken);
        if (current.Failure != ExternalProtocolFailure.None || current.Issuer != provenance.Issuer || current.CredentialBinding != provenance.CredentialBinding) return;
        var now = clock.GetUtcNow();
        await proofs.RecordAsync(view.Record.Key, new(view.Record.Id, view.MaterialRevision, provenance.RedirectUri, provenance.Issuer, provenance.Subject,
            userId, provenance.CredentialBinding, now)
            { AuthenticationSessionId = session.Id, SecurityVersion = session.SecurityVersion, SessionProvider = session.Provider,
                ExternalLinkRevision = session.ExternalLinkRevision },
            now - options.Value.RecoveryEvidenceLifetime, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<IamRevisionGuard>?> External(ConnectionRecord connection, CancellationToken token)
    {
        if (!options.Value.AllowDatabaseManagement || !WindowValid() || !connection.Enabled || connection.ArchivedAt is not null || !await IsReadyAsync(connection, token)) return null;
        var material = ConnectionSelection.MaterialRevision(ConnectionSelection.Snapshot(connection));
        var now = clock.GetUtcNow();
        var candidates = (await proofs.ReadAsync(connection.Key, token)).Where(proof => proof.RecordId == connection.Id && proof.MaterialRevision == material &&
            proof.ObservedAt <= now && proof.ObservedAt >= now - options.Value.RecoveryEvidenceLifetime).ToArray();
        if (candidates.Length == 0) return null;
        await using var scope = CreateScope(); var services = scope.ServiceProvider;
        var protocol = Protocol(services, connection.Protocol);
        if (protocol is null) return null;
        var callback = services.GetRequiredService<IExternalAuthenticationFlowService>().GetCallbackUri(connection.Key, AuthenticationArtifactPurpose.SignIn);
        // Metadata/JWKS readiness is bounded by the protocol. A configured but unreachable provider is not recovery.
        var tested = await protocol.TestAsync(connection, token);
        if (tested.Failure != ExternalProtocolFailure.None || tested.Issuer is null || tested.CredentialBinding is null) return null;
        foreach (var proof in candidates.Where(proof => proof.Issuer == tested.Issuer && proof.CredentialBinding == tested.CredentialBinding && proof.RedirectUri == callback))
        {
            var guards = await IdentitySnapshot.CaptureAsync(services, options.Value.TenantId, proof.UserId, connection.Admission.DefaultRoleIds, async () =>
            {
                // Historical evidence has its own bounded window, with current account and exact link binding.
                if (proof.AuthenticationSessionId is null || proof.SecurityVersion is null || proof.SessionProvider is null || proof.ExternalLinkRevision is null ||
                    services.GetService<ICanonicalUserSignInService>() is not { } authority ||
                    !await authority.ValidateSessionAsync(new(proof.AuthenticationSessionId, options.Value.TenantId, proof.UserId,
                        proof.SessionProvider, proof.SecurityVersion, proof.ObservedAt,
                        proof.ObservedAt + options.Value.RecoveryEvidenceLifetime)
                        { ConnectionKey = connection.Key, ConnectionRecordId = connection.Id, Issuer = proof.Issuer, ExternalSubject = proof.Subject,
                            ExternalLinkRevision = proof.ExternalLinkRevision }, token)) return false;
                if (!await Eligible(services, proof.UserId, false, token)) return false;
                var assessment = await services.GetRequiredService<IExternalAdmissionService>().AssessAsync(new(
                    new(connection, ConnectionSource.Database, true, null, false, material),
                    new(options.Value.TenantId, connection.Key, proof.Issuer, proof.Subject), [new("iss", proof.Issuer), new("sub", proof.Subject)]), token);
                return assessment.Kind == ExternalAdmissionKind.ExistingUser && assessment.User?.Id == proof.UserId;
            }, token);
            if (guards is not null) return guards;
        }
        return null;
    }

    private async ValueTask<IReadOnlyList<IamRevisionGuard>?> Local(ClaimsPrincipal? actor, CancellationToken token)
    {
        if (options.Value.RecoveryScanLimit is < 1 or > 10000) return null;
        await using var scope = CreateScope(); var services = scope.ServiceProvider;
        var issuer = services.GetService<ICanonicalUserSignInService>(); var schemes = services.GetService<IAuthenticationSchemeProvider>();
        if (issuer is null || schemes is null || await schemes.GetSchemeAsync(issuer.AuthenticationScheme) is null) return null;
        string? actingId = null;
        if (actor is not null && services.GetRequiredService<ITokenSubjectResolver>().TryResolve(actor, out var subject) && subject.TenantId == options.Value.TenantId)
            actingId = subject.CanonicalUserId;
        if (actingId is not null && await Candidate(actingId) is { } ownGuards) return ownGuards;
        var users = services.GetService<IPagedUserStore>();
        if (users is null) return null;
        for (var skip = 0; skip < options.Value.RecoveryScanLimit;)
        {
            var page = await users.ListPageAsync(options.Value.TenantId, new(skip, Math.Min(IamPageRequest.MaximumTake, options.Value.RecoveryScanLimit - skip)), cancellationToken: token);
            foreach (var user in page.Items)
                if (user.Id != actingId && user.Status == UserStatus.Active && await Candidate(user.Id) is { } guards) return guards;
            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.TotalCount) break;
        }
        return null; // No unexamined account is optimistically treated as usable.

        ValueTask<IReadOnlyList<IamRevisionGuard>?> Candidate(string id) => IdentitySnapshot.CaptureAsync(services,
            options.Value.TenantId, id, [], async () => await Eligible(services, id, true, token), token);
    }

    private async Task<bool> Eligible(IServiceProvider services, string userId, bool password, CancellationToken token)
    {
        var issuer = services.GetService<ICanonicalUserSignInService>();
        if (issuer is null || !await issuer.CanSignInAsync(new(options.Value.TenantId, userId), password, token)) return false;
        return (await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(
            new(options.Value.TenantId, userId, DefaultIdentityPermissionKeys.IdentityProvidersManage), token)).Status == CurrentSubjectPermissionStatus.Granted;
    }
    private bool WindowValid() => options.Value.RecoveryEvidenceLifetime >= TimeSpan.FromMinutes(1) && options.Value.RecoveryEvidenceLifetime <= TimeSpan.FromDays(30);
    private AsyncServiceScope CreateScope()
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(options.Value.TenantId)));
        return scope;
    }
    private static IExternalAuthenticationProtocol? Protocol(IServiceProvider services, string name)
    {
        var matches = services.GetServices<IExternalAuthenticationProtocol>().Where(protocol => protocol.Name == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
