using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MinimalEndpoints;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public static partial class ExternalAuthenticationApi
{
    private const string PublicCategory = "identity-protocol";
    private const string PublicReason = "External authentication protocol validates one-time protected browser state and verified provider evidence.";

    public static void MapExternalAuthenticationApi(IEndpointRouteBuilder endpoints)
    {
        var owner = typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name!;
        var api = endpoints.MapEndpointGroup(owner, ExternalAuthenticationApiJsonContext.Default, tag: "Identity");
        var path = endpoints.ServiceProvider.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.CallbackPath;
        if (!Local(path) || path.Contains('?') || path.Contains('#') || path.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("External authentication requires a local callback path.");
        path = path.TrimEnd('/');
        api.MapUnboundOperation("GET", path + "/antiforgery", "ExternalAuthenticationAntiforgery", typeof(ExternalAntiforgeryResponse), 200, null,
                context => Run(context, Antiforgery), name: "ExternalAuthenticationAntiforgery", containFailures: false, documentAuthResponses: false)
            .AllowPublic(PublicCategory, "Supplies a same-origin antiforgery request token; the paired cookie is HttpOnly.");
        api.MapUnboundOperation("GET", path + "/signin/{key}", "ExternalAuthenticationChooser", typeof(void), 302, null,
                context => Run(context, Chooser), name: "ExternalAuthenticationChooser", containFailures: false, documentAuthResponses: false)
            .AllowPublic(PublicCategory, "Opens the backend-owned chooser without initiating authentication or bypassing provider choice.");
        api.MapUnboundOperation("POST", path + "/signin/{key}", "ExternalAuthenticationStart", typeof(void), 302, null,
                context => Run(context, context => Begin(context, AuthenticationArtifactPurpose.SignIn)), name: "ExternalAuthenticationStart", containFailures: false, documentAuthResponses: false)
            .WithMetadata(new AcceptsMetadata(["application/x-www-form-urlencoded"], typeof(ExternalAuthenticationStartRequest), false))
            .AllowPublic(PublicCategory, PublicReason);
        api.MapUnboundOperation("GET", path + "/signin/{key}/callback", "ExternalAuthenticationCallback", typeof(void), 302, null,
                context => Run(context, context => Complete(context, AuthenticationArtifactPurpose.SignIn)), name: "ExternalAuthenticationCallback", containFailures: false, documentAuthResponses: false)
            .AllowPublic(PublicCategory, PublicReason);
        api.MapUnboundOperation("POST", path + "/preview/{key}", "ExternalAuthenticationPreviewStart", typeof(void), 302, null,
                context => Run(context, context => Begin(context, AuthenticationArtifactPurpose.Preview)), name: "ExternalAuthenticationPreviewStart", containFailures: false)
            .WithMetadata(new AcceptsMetadata(["application/x-www-form-urlencoded"], typeof(ExternalAuthenticationStartRequest), false))
            .RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
        api.MapUnboundOperation("GET", path + "/preview/{key}/callback", "ExternalAuthenticationPreviewCallback", typeof(void), 302, null,
                context => Run(context, context => Complete(context, AuthenticationArtifactPurpose.Preview)), name: "ExternalAuthenticationPreviewCallback", containFailures: false, documentAuthResponses: false)
            .AllowPublic(PublicCategory, "Interactive preview callback validates the initiating administrator's existing cookie and bound one-time state; it never signs in.");
        api.MapUnboundOperation("GET", path + "/preview/{key}/results/{handle}", "ExternalAuthenticationPreviewResult", typeof(ExternalAuthenticationPreview), 200, null,
                context => Run(context, Preview), name: "ExternalAuthenticationPreviewResult", containFailures: false)
            .RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
        MapManagement(endpoints);
        MapIdentityAdministration(endpoints);
    }

    private static async Task Run(HttpContext context, Func<HttpContext, Task> handler, int maximumBody = 16384)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                if (context.Request.ContentLength > maximumBody) { await Failure(context, StatusCodes.Status413PayloadTooLarge); return; }
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = Math.Min(limit.MaxRequestBodySize ?? maximumBody, maximumBody);
            }
            await handler(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (BadHttpRequestException exception) { await Failure(context, exception.StatusCode); }
        catch (InvalidDataException) { await Failure(context, StatusCodes.Status400BadRequest); }
        catch (JsonException) { await Failure(context, StatusCodes.Status400BadRequest); }
        catch (Exception)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            await Failure(context, StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            if (!context.RequestAborted.IsCancellationRequested && context.Response.StatusCode >= 400 &&
                !context.Items.ContainsKey(typeof(AuthEvent)))
            {
                var operation = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
                var action = operation switch
                {
                    "ExternalAuthenticationStart" => "signin.start",
                    "ExternalAuthenticationCallback" => "signin.complete",
                    "ExternalAuthenticationPreviewStart" => "preview.start",
                    "ExternalAuthenticationPreviewCallback" => "preview.complete",
                    "ExternalConnectionsChange" => "connection.change",
                    "ExternalIdentityAttachLink" => "link.attach",
                    "ExternalIdentityRemoveLink" => "link.remove",
                    "ExternalIdentityRevokeSession" => "session.revoke",
                    _ => null
                };
                if (action is not null)
                    await SecurityNotification(context, action,
                        context.Response.StatusCode >= 500 ? AuthEventOutcome.Failure : AuthEventOutcome.Denied,
                        "external-authentication", "request-refused",
                        category: action.StartsWith("signin.", StringComparison.Ordinal) || action.StartsWith("preview.", StringComparison.Ordinal)
                            ? AuthEventCategory.Authentication : action.StartsWith("session.", StringComparison.Ordinal)
                                ? AuthEventCategory.CredentialLifecycle : AuthEventCategory.IdentityChange);
            }
        }
    }

    private static async Task Antiforgery(HttpContext context)
    {
        var purpose = context.Request.Query["purpose"];
        if (purpose.Count > 1 || purpose.Count == 1 && purpose[0] != "preview") { await Failure(context, 400); return; }
        var principal = context.User;
        if (purpose.Count == 1)
        {
            var administrator = await Administrator(context);
            if (administrator is null) { await Failure(context, 401); return; }
            context.User = administrator;
        }
        AntiforgeryTokenSet tokens;
        try { tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context); }
        finally { context.User = principal; }
        if (tokens.RequestToken is null) { await Failure(context, 503); return; }
        await Results.Json(new ExternalAntiforgeryResponse(tokens.RequestToken, tokens.FormFieldName, tokens.HeaderName),
            ExternalAuthenticationApiJsonContext.Default.ExternalAntiforgeryResponse).ExecuteAsync(context);
    }

    private static async Task Chooser(HttpContext context)
    {
        var configured = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        var login = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationApiOptions>>().Value.LoginPath;
        if (!Local(login) || login.Contains('?') || login.Contains('#') || !Uri.TryCreate(configured.PublicBaseUrl, UriKind.Absolute, out var root) ||
            context.RequestServices.GetRequiredService<IExternalAuthenticationFlowService>().GetCallbackUri(Route(context, "key"), AuthenticationArtifactPurpose.SignIn) is null ||
            context.Request.Query["returnUrl"].Count > 1 || context.Request.Query["returnUrl"].ToString().Length > 4096) { await Failure(context, 400); return; }
        await Results.Redirect(QueryHelpers.AddQueryString(root.AbsoluteUri.TrimEnd('/') + login, "returnUrl", context.Request.Query["returnUrl"].FirstOrDefault() ?? "/")).ExecuteAsync(context);
    }

    private static async Task Begin(HttpContext context, AuthenticationArtifactPurpose purpose)
    {
        if (!context.Request.HasFormContentType) { await Failure(context, 415); return; }
        if (!await SessionAuthorityAvailable(context)) { await Failure(context, 503); return; }
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Failure(context, 400); return; }
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form["returnUrl"].Count > 1 || form["connectionRecordId"].Count > 1) { await Failure(context, 400); return; }
        var actor = purpose == AuthenticationArtifactPurpose.Preview ? await Administrator(context) : null;
        if (purpose == AuthenticationArtifactPurpose.Preview && actor is null) { await Failure(context, 401); return; }
        var key = Route(context, "key");
        var browser = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var start = await context.RequestServices.GetRequiredService<IExternalAuthenticationFlowService>().BeginAsync(new(key, purpose, browser,
            form["returnUrl"].FirstOrDefault() ?? "/", purpose == AuthenticationArtifactPurpose.Preview ? form["connectionRecordId"].FirstOrDefault() : null), actor, context.RequestAborted);
        if (start.Failure != ExternalFlowFailure.None || start.State is null || start.AuthorizationUrl is null)
        {
            await SecurityNotification(context, purpose == AuthenticationArtifactPurpose.SignIn ? "signin.start" : "preview.start", AuthEventOutcome.Denied,
                "external-authentication", start.Failure.ToString(), category: AuthEventCategory.Authentication);
            await Failure(context, 400); return;
        }
        SetCorrelation(context, key, purpose, start.State, browser);
        await Results.Redirect(start.AuthorizationUrl).ExecuteAsync(context);
    }

    private static async Task Complete(HttpContext context, AuthenticationArtifactPurpose purpose)
    {
        if (!await SessionAuthorityAvailable(context)) { await Failure(context, 503); return; }
        var query = context.Request.Query;
        if (query["state"].Count != 1 || query["code"].Count > 1 || query["iss"].Count > 1 || query["error"].Count > 1 ||
            (query["code"].Count == 1) == (query["error"].Count == 1)) { await Failure(context, 400); return; }
        var key = Route(context, "key");
        var handle = query["state"].ToString();
        var browser = ReadCorrelation(context, key, purpose, handle);
        if (browser is null) { await Failure(context, 400); return; }
        var actor = purpose == AuthenticationArtifactPurpose.Preview ? await Administrator(context) : null;
        if (purpose == AuthenticationArtifactPurpose.Preview && actor is null) { await Failure(context, 401); return; }
        var flow = await context.RequestServices.GetRequiredService<IExternalAuthenticationFlowService>().CompleteAsync(
            new(key, purpose, handle, browser, query["code"].FirstOrDefault(), query["iss"].FirstOrDefault(), query["error"].Count == 1), actor, context.RequestAborted);
        if (flow.Failure is not (ExternalFlowFailure.ExpiredOrUsed or ExternalFlowFailure.Denied)) DeleteCorrelation(context, key, purpose, handle);
        if (flow.Failure != ExternalFlowFailure.None)
        {
            await SecurityNotification(context, purpose == AuthenticationArtifactPurpose.SignIn ? "signin.complete" : "preview.complete", AuthEventOutcome.Denied,
                "external-authentication", flow.Failure.ToString(), category: AuthEventCategory.Authentication);
            if (purpose == AuthenticationArtifactPurpose.Preview && flow.ReturnUrl is not null)
                await Results.Redirect(QueryHelpers.AddQueryString(flow.ReturnUrl, new Dictionary<string, string?>
                    { ["externalPreviewFailure"] = flow.Failure.ToString(), ["externalPreviewConnection"] = key })).ExecuteAsync(context);
            else await Failure(context, 400);
            return;
        }
        if (purpose == AuthenticationArtifactPurpose.SignIn)
        {
            if (flow.UserId is null || flow.ReturnUrl is null || flow.Provenance?.ConnectionKey != key) { await Failure(context, 400); return; }
            await SecurityNotification(context, "signin.admit", AuthEventOutcome.Success, flow.UserId, "admitted", category: AuthEventCategory.Authentication);
            var issuer = context.RequestServices.GetRequiredService<ICanonicalUserSignInService>();
            var tenant = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.TenantId;
            var signedIn = await issuer.SignInAsync(new(tenant, flow.UserId, ManagedConnectionProviderSource.ProviderId(key))
                {
                    ExternalIdentity = new(tenant, key, flow.Provenance.Issuer, flow.Provenance.Subject),
                    ConnectionRecordId = flow.Provenance.ConnectionRecordId,
                    ConnectionMaterialRevision = flow.Provenance.MaterialRevision,
                    ConnectionCredentialBinding = flow.Provenance.CredentialBinding,
                    ConnectionRedirectUri = flow.Provenance.RedirectUri
                }, context.RequestAborted);
            if (signedIn is null)
            {
                await SecurityNotification(context, "signin.complete", AuthEventOutcome.Denied, flow.UserId, "user-unavailable", category: AuthEventCategory.Authentication);
                await Failure(context, 401); return;
            }
            try
            {
                await context.RequestServices.GetRequiredService<IConnectionSignInEvidence>().RecordAsync(flow.UserId,
                    flow.Provenance with { AuthenticationSessionId = signedIn.AuthenticationSessionId }, context.RequestAborted);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
            {
                // Optional recovery observations must not turn a successful sign-in into an outage.
                // Missing evidence can never permit a destructive management change.
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExternalAuthenticationApi))
                    .LogWarning("External recovery evidence was not recorded ({FailureType}).", exception.GetType().Name);
            }
            await SecurityNotification(context, "signin.complete", AuthEventOutcome.Success, flow.UserId, "signed-in", category: AuthEventCategory.Authentication);
            await Results.Redirect(flow.ReturnUrl).ExecuteAsync(context);
            return;
        }
        if (flow.PreviewResultHandle is null || flow.ReturnUrl is null) { await Failure(context, 400); return; }
        await SecurityNotification(context, "preview.complete", AuthEventOutcome.Success, "external-authentication", "previewed", category: AuthEventCategory.Authentication);
        SetCorrelation(context, key, AuthenticationArtifactPurpose.PreviewResult, flow.PreviewResultHandle, browser);
        await Results.Redirect(QueryHelpers.AddQueryString(flow.ReturnUrl, new Dictionary<string, string?>
            { ["externalPreviewHandle"] = flow.PreviewResultHandle, ["externalPreviewConnection"] = key })).ExecuteAsync(context);
    }

    private static async Task Preview(HttpContext context)
    {
        var key = Route(context, "key"); var handle = Route(context, "handle");
        var actor = await Administrator(context);
        if (actor is null) { await Failure(context, 401); return; }
        var browser = ReadCorrelation(context, key, AuthenticationArtifactPurpose.PreviewResult, handle);
        if (browser is null) { await Failure(context, 400); return; }
        var result = await context.RequestServices.GetRequiredService<IExternalAuthenticationFlowService>().ReadPreviewAsync(key, handle, browser, actor, context.RequestAborted);
        if (result.Failure is not (ExternalFlowFailure.ExpiredOrUsed or ExternalFlowFailure.Denied)) DeleteCorrelation(context, key, AuthenticationArtifactPurpose.PreviewResult, handle);
        if (result.Failure != ExternalFlowFailure.None || result.Preview is null) { await Failure(context, 400); return; }
        await Results.Json(result.Preview, ExternalAuthenticationApiJsonContext.Default.ExternalAuthenticationPreview).ExecuteAsync(context);
    }

    private static async Task<ClaimsPrincipal?> Administrator(HttpContext context)
    {
        var issuer = context.RequestServices.GetService<ICanonicalUserSignInService>();
        if (issuer is null) return null;
        var authenticated = await context.AuthenticateAsync(issuer.AuthenticationScheme);
        if (!authenticated.Succeeded || authenticated.Principal is null) return null;
        var subjects = context.RequestServices.GetRequiredService<ITokenSubjectResolver>();
        if (!subjects.TryResolve(authenticated.Principal, out var cookie) || cookie.CanonicalUserId is null) return null;
        // A Studio bearer transport and the browser's cookie may name different users. Never silently
        // run a preview as that other cookie identity, even if both accounts can manage providers.
        if (context.User.Identity?.IsAuthenticated == true &&
            (!subjects.TryResolve(context.User, out var current) || current != cookie)) return null;
        return authenticated.Principal;
    }

    private static async Task<bool> SessionAuthorityAvailable(HttpContext context)
    {
        var issuer = context.RequestServices.GetService<ICanonicalUserSignInService>();
        return issuer?.SupportsDurableSessions == true && await context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(issuer.AuthenticationScheme) is not null;
    }

    private static void SetCorrelation(HttpContext context, string key, AuthenticationArtifactPurpose purpose, string handle, string browser)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        var expiry = clock.GetUtcNow() + (purpose == AuthenticationArtifactPurpose.PreviewResult ? options.PreviewResultLifetime : options.FlowLifetime);
        var value = Protector(context, key, purpose, handle).Protect(JsonSerializer.Serialize(new Correlation(browser, expiry)));
        context.Response.Cookies.Append(CookieName(key, purpose, handle), value, CookieOptions(context, expiry));
    }

    private static string? ReadCorrelation(HttpContext context, string key, AuthenticationArtifactPurpose purpose, string handle)
    {
        if (handle.Length is < 43 or > 128 || !handle.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return null;
        var raw = context.Request.Cookies[CookieName(key, purpose, handle)];
        if (raw is null || raw.Length > 4096) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Correlation>(Protector(context, key, purpose, handle).Unprotect(raw));
            return value is not null && value.ExpiresAt > context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() ? value.Browser : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException) { return null; }
    }

    private static IDataProtector Protector(HttpContext context, string key, AuthenticationArtifactPurpose purpose, string handle) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("Elsa.ExternalAuthentication.Correlation.v1",
            context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.TenantId, key, purpose.ToString(), handle);
    private static string CookieName(string key, AuthenticationArtifactPurpose purpose, string handle) =>
        "Elsa.External." + purpose + "." + key + "." + Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(handle)))[..24];
    private static CookieOptions CookieOptions(HttpContext context, DateTimeOffset? expires = null)
    {
        var configured = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        var root = new Uri(configured.PublicBaseUrl!, UriKind.Absolute);
        var path = root.AbsolutePath.TrimEnd('/') + configured.CallbackPath.TrimEnd('/');
        return new()
        {
            HttpOnly = true, Secure = root.Scheme == Uri.UriSchemeHttps, SameSite = SameSiteMode.Lax, IsEssential = true,
            Path = path.Length == 0 ? "/" : path, Expires = expires
        };
    }
    private static void DeleteCorrelation(HttpContext context, string key, AuthenticationArtifactPurpose purpose, string handle) =>
        context.Response.Cookies.Delete(CookieName(key, purpose, handle), CookieOptions(context));
    private static bool Local(string value) => value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\') &&
        !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));
    private static string Route(HttpContext context, string key) => context.Request.RouteValues[key]?.ToString() ?? "";
    private static Task Failure(HttpContext context, int status) => Results.Json(new ExternalAuthenticationFailureResponse("external-authentication-failed"),
        ExternalAuthenticationApiJsonContext.Default.ExternalAuthenticationFailureResponse, statusCode: status).ExecuteAsync(context);
    private sealed record Correlation(string Browser, DateTimeOffset ExpiresAt);
}
