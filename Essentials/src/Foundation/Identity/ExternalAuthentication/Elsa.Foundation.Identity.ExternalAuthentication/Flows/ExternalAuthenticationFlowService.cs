using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Flows;

internal sealed class ExternalAuthenticationFlowService(IServiceScopeFactory scopes, IConnectionRegistry registry,
    IOptions<ExternalAuthenticationOptions> options, IDataProtectionProvider protection, ITokenSubjectResolver subjects,
    IConnectionWriteAuthorizer authorizer, TimeProvider clock, IHostEnvironment? environment = null) : IExternalAuthenticationFlowService
{
    public string? GetCallbackUri(string connectionKey, AuthenticationArtifactPurpose purpose)
    {
        if (!Key(connectionKey) || purpose is not (AuthenticationArtifactPurpose.SignIn or AuthenticationArtifactPurpose.Preview) ||
            !Uri.TryCreate(options.Value.PublicBaseUrl, UriKind.Absolute, out var root) || !TrustedUri(root) ||
            root.Query.Length != 0 || root.Fragment.Length != 0 || root.UserInfo.Length != 0 || !Local(options.Value.CallbackPath) ||
            options.Value.CallbackPath.Contains('?') || options.Value.CallbackPath.Contains('#') ||
            options.Value.CallbackPath.Split('/').Any(segment => segment is "." or "..")) return null;
        return root.AbsoluteUri.TrimEnd('/') + options.Value.CallbackPath.TrimEnd('/') + "/" +
            (purpose == AuthenticationArtifactPurpose.SignIn ? "signin" : "preview") + "/" + connectionKey + "/callback";
    }

    public async ValueTask<ExternalFlowStarted> BeginAsync(ExternalFlowStart request, ClaimsPrincipal? administrator = null, CancellationToken cancellationToken = default)
    {
        if (!Opaque(request.BrowserBinding) || request.Purpose is not (AuthenticationArtifactPurpose.SignIn or AuthenticationArtifactPurpose.Preview))
            return new(null, ExternalFlowFailure.InvalidState);
        var admin = await Administrator(request.Purpose, administrator, cancellationToken);
        if (request.Purpose == AuthenticationArtifactPurpose.Preview && admin is null) return new(null, ExternalFlowFailure.Denied);
        if (!ReturnUrl(request.ReturnUrl)) return new(null, ExternalFlowFailure.InvalidReturnUrl);
        var callback = GetCallbackUri(request.ConnectionKey, request.Purpose);
        if (callback is null) return new(null, ExternalFlowFailure.InvalidCallback);
        if (options.Value.FlowLifetime < TimeSpan.FromSeconds(1) || options.Value.FlowLifetime > TimeSpan.FromMinutes(15)) return new(null, ExternalFlowFailure.Unavailable);
        var selected = await Select(request.ConnectionKey, request.ConnectionRecordId, request.Purpose, cancellationToken);
        if (selected is null) return new(null, ExternalFlowFailure.ConnectionUnavailable);
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetService<IAuthenticationArtifactStore>();
        if (!Usable(store)) return new(null, ExternalFlowFailure.Unavailable);
        var protocol = Protocol(scope.ServiceProvider, selected.Record.Protocol);
        if (protocol is null) return new(null, ExternalFlowFailure.Unavailable);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var handle = Random(); var nonce = Random(); var verifier = Random();
            var challenge = await protocol.ChallengeAsync(selected.Record, new(callback, handle, nonce, verifier), cancellationToken);
            if (challenge.Failure != ExternalProtocolFailure.None || challenge.AuthorizationUrl is null || challenge.Issuer is null || challenge.CredentialBinding is null)
                return new(null, ExternalFlowFailure.ProviderFailed, challenge.Failure);
            var now = Now();
            var state = new FlowState(1, selected.Record.Key, selected.Record.Id, selected.MaterialRevision, selected.Record.Protocol,
                callback, request.ReturnUrl, nonce, verifier, challenge.Issuer, challenge.CredentialBinding, now, Milliseconds(now + options.Value.FlowLifetime));
            var artifact = Protect(handle, request.Purpose, request.ConnectionKey, request.BrowserBinding, admin, now, state.ExpiresAt, state);
            if (await store!.TryCreateAsync(artifact, cancellationToken)) return new(challenge.AuthorizationUrl) { State = handle };
        }
        return new(null, ExternalFlowFailure.Conflict);
    }

    public async ValueTask<ExternalFlowCompleted> CompleteAsync(ExternalFlowCallback request, ClaimsPrincipal? administrator = null, CancellationToken cancellationToken = default)
    {
        if (!Opaque(request.State) || !Opaque(request.BrowserBinding) || !Key(request.ConnectionKey) ||
            request.Purpose is not (AuthenticationArtifactPurpose.SignIn or AuthenticationArtifactPurpose.Preview))
            return new(Failure: ExternalFlowFailure.InvalidState);
        var admin = await Administrator(request.Purpose, administrator, cancellationToken);
        if (request.Purpose == AuthenticationArtifactPurpose.Preview && admin is null) return new(Failure: ExternalFlowFailure.Denied);
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetService<IAuthenticationArtifactStore>();
        if (!Usable(store)) return new(Failure: ExternalFlowFailure.Unavailable);
        var artifact = await Consume(store!, request.State, request.Purpose, request.ConnectionKey, request.BrowserBinding, admin, cancellationToken);
        if (artifact is null) return new(Failure: ExternalFlowFailure.ExpiredOrUsed);
        var state = Unprotect<FlowState>(artifact);
        if (state is null || state.Format != 1 || state.ConnectionKey != request.ConnectionKey || !Times(artifact, state.CreatedAt, state.ExpiresAt))
            return new(Failure: ExternalFlowFailure.InvalidState);
        if (state.RedirectUri != GetCallbackUri(request.ConnectionKey, request.Purpose)) return new(Failure: ExternalFlowFailure.StaleConnection);
        if (!ReturnUrl(state.ReturnUrl)) return new(Failure: ExternalFlowFailure.InvalidReturnUrl);
        // Only a consumed, protected, browser/administrator-bound preview can return a failure to its
        // currently allowed destination. Invalid or unbound state must never supply a redirect.
        ExternalFlowCompleted Refuse(ExternalFlowFailure failure, ExternalProtocolFailure protocolFailure = ExternalProtocolFailure.None) =>
            new(ReturnUrl: request.Purpose == AuthenticationArtifactPurpose.Preview ? state.ReturnUrl : null,
                Failure: failure, ProtocolFailure: protocolFailure);
        var selected = await Select(state.ConnectionKey, request.Purpose == AuthenticationArtifactPurpose.Preview ? state.ConnectionRecordId : null, request.Purpose, cancellationToken);
        if (selected is null || selected.Record.Id != state.ConnectionRecordId || selected.MaterialRevision != state.MaterialRevision)
            return Refuse(ExternalFlowFailure.StaleConnection);
        if (request.ProviderError) return Refuse(ExternalFlowFailure.ProviderFailed);
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 8192 || request.ResponseIssuer?.Length > 400)
            return Refuse(ExternalFlowFailure.InvalidState);
        var protocol = Protocol(scope.ServiceProvider, state.Protocol);
        if (protocol is null) return Refuse(ExternalFlowFailure.Unavailable);
        var verified = await protocol.RedeemAsync(selected.Record, new(state.RedirectUri, request.Code, state.Nonce, state.CodeVerifier,
            state.Issuer, state.CredentialBinding, request.ResponseIssuer), cancellationToken);
        if (verified.Failure != ExternalProtocolFailure.None || verified.Identity is null)
            return Refuse(ExternalFlowFailure.ProviderFailed, verified.Failure);
        if (verified.Identity.TenantId != options.Value.TenantId || verified.Identity.Provider != state.ConnectionKey || verified.Identity.Issuer != state.Issuer)
            return Refuse(ExternalFlowFailure.InvalidState);
        var admission = scope.ServiceProvider.GetRequiredService<IExternalAdmissionService>();
        var context = new ExternalAdmissionContext(selected, verified.Identity, verified.Claims);
        if (request.Purpose == AuthenticationArtifactPurpose.SignIn)
        {
            var result = await admission.AdmitAsync(context, cancellationToken);
            return result.UserId is null
                ? new(Failure: ExternalFlowFailure.AdmissionRejected, AdmissionFailure: result.Failure)
                : new(result.UserId, state.ReturnUrl, Provenance: new(state.ConnectionKey, state.ConnectionRecordId, state.Issuer, verified.Identity.Subject)
                    { MaterialRevision = state.MaterialRevision, CredentialBinding = state.CredentialBinding, RedirectUri = state.RedirectUri });
        }
        var prospective = await admission.AssessAsync(context, cancellationToken);
        if (prospective.RoleIds.Count > 128 || prospective.Permissions.Count > 1024 || options.Value.PreviewResultLifetime < TimeSpan.FromSeconds(1) ||
            options.Value.PreviewResultLifetime > TimeSpan.FromMinutes(10)) return Refuse(ExternalFlowFailure.InvalidResult);
        var observed = Now();
        var preview = new ExternalAuthenticationPreview(state.ConnectionKey, state.ConnectionRecordId, state.MaterialRevision,
            verified.Identity.Issuer, verified.Identity.Subject, prospective.Kind, prospective.Kind == ExternalAdmissionKind.ExistingUser ? prospective.User?.Id : null,
            prospective.User?.UserName, prospective.User?.DisplayName, prospective.User?.Email,
            prospective.RoleIds.Order(StringComparer.Ordinal).ToArray(), prospective.Permissions.Order(StringComparer.Ordinal).ToArray(), prospective.Failure, observed);
        var resultHandle = Random();
        var payload = new PreviewState(1, preview, observed, Milliseconds(observed + options.Value.PreviewResultLifetime));
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload)) > 131072) return Refuse(ExternalFlowFailure.InvalidResult);
        var saved = await store!.TryCreateAsync(Protect(resultHandle, AuthenticationArtifactPurpose.PreviewResult, state.ConnectionKey,
            request.BrowserBinding, admin, observed, payload.ExpiresAt, payload), cancellationToken);
        return saved ? new(ReturnUrl: state.ReturnUrl, PreviewResultHandle: resultHandle) : Refuse(ExternalFlowFailure.Conflict);
    }

    public async ValueTask<ExternalPreviewResult> ReadPreviewAsync(string connectionKey, string handle, string browserBinding, ClaimsPrincipal administrator,
        CancellationToken cancellationToken = default)
    {
        if (!Key(connectionKey) || !Opaque(handle) || !Opaque(browserBinding)) return new(null, ExternalFlowFailure.InvalidState);
        var admin = await Administrator(AuthenticationArtifactPurpose.Preview, administrator, cancellationToken);
        if (admin is null) return new(null, ExternalFlowFailure.Denied);
        await using var scope = CreateScope();
        var store = scope.ServiceProvider.GetService<IAuthenticationArtifactStore>();
        if (!Usable(store)) return new(null, ExternalFlowFailure.Unavailable);
        var artifact = await Consume(store!, handle, AuthenticationArtifactPurpose.PreviewResult, connectionKey, browserBinding, admin, cancellationToken);
        if (artifact is null) return new(null, ExternalFlowFailure.ExpiredOrUsed);
        var result = Unprotect<PreviewState>(artifact);
        if (result is null || result.Format != 1 || result.Preview.ConnectionKey != connectionKey || !Times(artifact, result.CreatedAt, result.ExpiresAt))
            return new(null, ExternalFlowFailure.InvalidResult);
        var current = await Select(connectionKey, result.Preview.ConnectionRecordId, AuthenticationArtifactPurpose.Preview, cancellationToken);
        return new(result.Preview with { Stale = current is null || current.MaterialRevision != result.Preview.MaterialRevision });
    }

    private async ValueTask<string?> Administrator(AuthenticationArtifactPurpose purpose, ClaimsPrincipal? actor, CancellationToken token)
    {
        if (purpose == AuthenticationArtifactPurpose.SignIn) return null;
        if (actor is null || !subjects.TryResolve(actor, out var subject) || subject.TenantId != options.Value.TenantId ||
            subject.CanonicalUserId is null || !await authorizer.AuthorizeAsync(actor, token)) return null;
        return subject.CanonicalUserId;
    }

    private async Task<ConnectionView?> Select(string key, string? recordId, AuthenticationArtifactPurpose purpose, CancellationToken token)
    {
        var views = (await registry.ReadAsync(token)).Connections.Where(view => view.Record.Key == key && view.Record.ArchivedAt is null);
        if (purpose == AuthenticationArtifactPurpose.SignIn)
        {
            if (recordId is not null) return null;
            return views.SingleOrDefault(view => view.Effective && view.Record.Enabled);
        }
        return recordId is null ? null : views.SingleOrDefault(view => view.Record.Id == recordId);
    }

    private AuthenticationArtifact Protect<T>(string handle, AuthenticationArtifactPurpose purpose, string key, string browser, string? administrator,
        DateTimeOffset created, DateTimeOffset expires, T value)
    {
        var id = Hash("artifact/v1", options.Value.TenantId, purpose.ToString(), handle);
        var browserHash = Hash("browser/v1", key, browser);
        var adminHash = administrator is null ? null : Hash("administrator/v1", options.Value.TenantId, administrator);
        var envelope = new AuthenticationArtifact(id, options.Value.TenantId, purpose, browserHash, adminHash, created, expires, "");
        return envelope with { ProtectedPayload = Protector(envelope).Protect(JsonSerializer.Serialize(value)) };
    }

    private async ValueTask<AuthenticationArtifact?> Consume(IAuthenticationArtifactStore store, string handle, AuthenticationArtifactPurpose purpose,
        string key, string browser, string? administrator, CancellationToken token) =>
        await store.TryConsumeAsync(options.Value.TenantId, Hash("artifact/v1", options.Value.TenantId, purpose.ToString(), handle), purpose,
            Hash("browser/v1", key, browser), administrator is null ? null : Hash("administrator/v1", options.Value.TenantId, administrator), Now(), token);

    private T? Unprotect<T>(AuthenticationArtifact artifact) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(Protector(artifact).Unprotect(artifact.ProtectedPayload)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException) { return null; }
    }
    private IDataProtector Protector(AuthenticationArtifact artifact) => protection.CreateProtector("Elsa.ExternalAuthentication.Artifact.v1",
        artifact.TenantId, artifact.Id, artifact.Purpose.ToString(), artifact.BrowserBinding, artifact.AdministratorBinding ?? "");
    private bool Times(AuthenticationArtifact artifact, DateTimeOffset created, DateTimeOffset expires) =>
        created == artifact.CreatedAt && expires == artifact.ExpiresAt && created <= Now() && expires > Now();
    private bool Usable(IAuthenticationArtifactStore? store) => store is not null && (store.IsDurable || environment?.IsDevelopment() == true);
    private static IExternalAuthenticationProtocol? Protocol(IServiceProvider provider, string name)
    {
        var matches = provider.GetServices<IExternalAuthenticationProtocol>().Where(protocol => protocol.Name == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private AsyncServiceScope CreateScope()
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(options.Value.TenantId)));
        return scope;
    }
    private bool ReturnUrl(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096 || value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (Local(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !TrustedUri(uri) || uri.UserInfo.Length != 0) return false;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return (Uri.TryCreate(options.Value.PublicBaseUrl, UriKind.Absolute, out var own) && origin == own.GetLeftPart(UriPartial.Authority)) ||
            options.Value.AllowedReturnUrlOrigins.Any(allowed => Uri.TryCreate(allowed, UriKind.Absolute, out var candidate) && candidate.UserInfo.Length == 0 &&
                candidate.Query.Length == 0 && candidate.Fragment.Length == 0 && candidate.AbsolutePath == "/" && candidate.GetLeftPart(UriPartial.Authority) == origin);
    }
    private bool TrustedUri(Uri uri) => uri.IsWellFormedOriginalString() &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && environment?.IsDevelopment() == true);
    private static bool Local(string? value) => value is { Length: > 0 } && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal) &&
        !value.Contains('\\') && !value.Any(char.IsControl);
    private static bool Key(string? value) => value is { Length: > 0 and <= 80 } && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool Opaque(string? value) => value is { Length: >= 43 and <= 128 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static DateTimeOffset Milliseconds(DateTimeOffset value) => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());
    private static string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)]; Span<byte> unit = stackalloc byte[sizeof(char)];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length);
            foreach (var character in value) { BinaryPrimitives.WriteUInt16BigEndian(unit, character); hash.AppendData(unit); }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed record FlowState(int Format, string ConnectionKey, string ConnectionRecordId, string MaterialRevision, string Protocol,
        string RedirectUri, string ReturnUrl, string Nonce, string CodeVerifier, string Issuer, string CredentialBinding, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
    private sealed record PreviewState(int Format, ExternalAuthenticationPreview Preview, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
}
