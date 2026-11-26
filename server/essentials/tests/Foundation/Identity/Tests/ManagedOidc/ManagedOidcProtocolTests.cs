using System.Net;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Oidc;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed class ManagedOidcProtocolTests
{
    [Theory]
    [InlineData("client_secret_basic")]
    [InlineData("client_secret_post")]
    [InlineData("none")]
    public async Task Real_HTTP_code_flow_uses_PKCE_client_authentication_and_verified_identity_without_retaining_tokens(string authentication)
    {
        await using var server = await ControlledOidcServer.Start();
        server.Authentication = authentication;
        await using var host = Create(server, authentication);
        Assert.Equal(ExternalProtocolFailure.None, (await host.Protocol.CheckAsync(host.Connection)).Failure);
        Assert.Equal(ExternalProtocolFailure.None, (await host.Protocol.TestAsync(host.Connection)).Failure);
        var (request, challenge) = await Start(host, server);
        var result = await host.Protocol.RedeemAsync(host.Connection, request);
        Assert.Equal(ExternalProtocolFailure.None, result.Failure);
        Assert.Equal("default", result.Identity!.TenantId);
        Assert.Equal("company", result.Identity.Provider);
        Assert.Equal(server.Issuer, result.Identity.Issuer);
        Assert.Equal("opaque-Subject", result.Identity.Subject);
        Assert.Contains(result.Claims, claim => claim.Type == "email_verified" && claim.Value == "true");
        Assert.DoesNotContain(result.Claims, claim => claim.Value.Contains("upstream-access", StringComparison.Ordinal) || claim.Value.Contains("upstream-refresh", StringComparison.Ordinal));
        Assert.DoesNotContain(ControlledOidcServer.ClientSecret, challenge.AuthorizationUrl!, StringComparison.Ordinal);
        Assert.DoesNotContain(request.CodeVerifier, challenge.AuthorizationUrl!, StringComparison.Ordinal);
        Assert.Equal(ExternalProtocolFailure.UpstreamRejected, (await host.Protocol.RedeemAsync(host.Connection, request)).Failure);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("missing-subject")]
    [InlineData("missing-nonce")]
    [InlineData("missing-iat")]
    [InlineData("future-iat")]
    [InlineData("string-iat")]
    [InlineData("string-exp")]
    [InlineData("token-type")]
    [InlineData("azp")]
    [InlineData("multiple-audiences")]
    [InlineData("access-hash")]
    [InlineData("duplicate-subject")]
    public async Task Signed_provider_responses_with_invalid_identity_evidence_never_return_a_principal(string scenario)
    {
        await using var server = await ControlledOidcServer.Start();
        server.TokenScenario = scenario;
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        var result = await host.Protocol.RedeemAsync(host.Connection, request);
        Assert.Equal(ExternalProtocolFailure.InvalidToken, result.Failure);
        Assert.Null(result.Identity);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task Secret_rotation_invalidates_in_flight_authentication_before_the_code_is_sent()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        host.Secret.Version++;
        var result = await host.Protocol.RedeemAsync(host.Connection, request);
        Assert.Equal(ExternalProtocolFailure.SecretChanged, result.Failure);
        Assert.Equal(0, server.TokenRequests);
        Assert.True(host.Secret.Reads >= 2);
    }

    [Fact]
    public async Task Configured_secret_value_changes_without_a_metadata_version_are_also_detected()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        host.Secret.Value = "changed configuration value";
        Assert.Equal(ExternalProtocolFailure.SecretChanged, (await host.Protocol.RedeemAsync(host.Connection, request)).Failure);
        Assert.Equal(0, server.TokenRequests);
    }

    [Theory]
    [InlineData("different")]
    [InlineData("missing")]
    public async Task Authorization_response_issuer_is_checked_before_code_exchange(string value)
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        var result = await host.Protocol.RedeemAsync(host.Connection, request with { ResponseIssuer = value == "missing" ? null : server.Issuer + "/" });
        Assert.Equal(ExternalProtocolFailure.ProviderChanged, result.Failure);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task A_wrong_verifier_is_refused_by_the_provider_and_new_signing_keys_are_used_without_restarting()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        Assert.Equal(ExternalProtocolFailure.UpstreamRejected, (await host.Protocol.RedeemAsync(host.Connection, request with { CodeVerifier = Random() })).Failure);
        server.RotateKey();
        Assert.Equal(ExternalProtocolFailure.None, (await host.Protocol.RedeemAsync(host.Connection, request)).Failure);
        Assert.True(server.KeyRequests >= 2);
    }

    [Fact]
    public async Task Rotation_between_key_fetch_and_token_response_refreshes_keys_once_without_redeeming_the_code_twice()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var (request, _) = await Start(host, server);
        server.RotateOnToken = true;
        Assert.Equal(ExternalProtocolFailure.None, (await host.Protocol.RedeemAsync(host.Connection, request)).Failure);
        Assert.Equal(1, server.TokenRequests);
        Assert.Equal(2, server.KeyRequests);
    }

    [Fact]
    public async Task Metadata_cannot_redirect_backchannel_calls_or_move_credentials_to_a_different_origin()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        server.RedirectDiscovery = true;
        Assert.Equal(ExternalProtocolFailure.InvalidDiscovery, (await host.Protocol.TestAsync(host.Connection)).Failure);
        server.RedirectDiscovery = false;
        server.ChangeMetadata = metadata => metadata["token_endpoint"] = "https://other.example/token";
        Assert.Equal(ExternalProtocolFailure.UntrustedEndpoint, (await host.Protocol.TestAsync(host.Connection)).Failure);
        Assert.Equal(0, server.TokenRequests);
        Assert.Equal(0, server.KeyRequests);
        server.ChangeMetadata = metadata => metadata["issuer"] = server.Issuer + "/";
        Assert.Equal(ExternalProtocolFailure.InvalidDiscovery, (await host.Protocol.TestAsync(host.Connection)).Failure);
    }

    [Fact]
    public async Task Metadata_must_advertise_the_selected_client_authentication_and_well_typed_issuer_support()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        server.ChangeMetadata = metadata => metadata["token_endpoint_auth_methods_supported"] = new[] { "none" };
        Assert.Equal(ExternalProtocolFailure.UnsupportedClientAuthentication, (await host.Protocol.TestAsync(host.Connection)).Failure);
        server.ChangeMetadata = metadata => metadata["authorization_response_iss_parameter_supported"] = "true";
        Assert.Equal(ExternalProtocolFailure.InvalidDiscovery, (await host.Protocol.TestAsync(host.Connection)).Failure);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task Discovery_is_bounded_in_size_and_duration_and_does_not_reach_the_key_or_token_endpoint()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        server.ChangeMetadata = metadata => metadata["oversized"] = new string('x', 140000);
        Assert.Equal(ExternalProtocolFailure.InvalidDiscovery, (await host.Protocol.TestAsync(host.Connection)).Failure);
        server.ChangeMetadata = null;
        server.DelayDiscovery = true;
        host.Provider.GetRequiredService<IOptions<ManagedOidcOptions>>().Value.BackchannelTimeout = TimeSpan.FromMilliseconds(200);
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(ExternalProtocolFailure.RemoteUnavailable, (await host.Protocol.TestAsync(host.Connection, caller.Token)).Failure);
        Assert.Equal(0, server.KeyRequests);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task Unsupported_algorithms_settings_and_unresolved_secrets_fail_local_checks()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        var symmetric = host.Connection with { Settings = host.Connection.Settings.Concat(new[] { new KeyValuePair<string, string>("signingAlgorithm", "HS256") }).ToDictionary() };
        Assert.Equal(ExternalProtocolFailure.InvalidConfiguration, (await host.Protocol.CheckAsync(symmetric)).Failure);
        var unknown = host.Connection with { Settings = host.Connection.Settings.Concat(new[] { new KeyValuePair<string, string>("skipNonce", "true") }).ToDictionary() };
        Assert.Equal(ExternalProtocolFailure.InvalidConfiguration, (await host.Protocol.CheckAsync(unknown)).Failure);
        host.Secret.Available = false;
        Assert.Equal(ExternalProtocolFailure.SecretUnavailable, (await host.Protocol.CheckAsync(host.Connection)).Failure);
        Assert.Equal(0, server.TokenRequests);
        Assert.Equal(0, server.KeyRequests);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("missing")]
    public async Task Loopback_HTTP_is_never_enabled_without_a_confirmed_Development_host(string environment)
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server, environment: environment);
        Assert.Equal(ExternalProtocolFailure.InvalidConfiguration, (await host.Protocol.CheckAsync(host.Connection)).Failure);
        var activation = host.Provider.GetServices<IHostedService>().Single(service => service.GetType().Name == "ManagedOidcActivation");
        await Assert.ThrowsAsync<InvalidOperationException>(() => activation.StartAsync(default));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_short_nonce_values_are_not_accepted()
    {
        await using var server = await ControlledOidcServer.Start();
        await using var host = Create(server);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Protocol.TestAsync(host.Connection, cancellation.Token).AsTask());
        Assert.Equal(ExternalProtocolFailure.InvalidResponse, (await host.Protocol.ChallengeAsync(host.Connection, new(ControlledOidcServer.Callback, Random(), "short", Random()))).Failure);
    }

    private static async Task<(ExternalProtocolCodeRequest Request, ExternalProtocolChallenge Challenge)> Start(ProtocolHost host, ControlledOidcServer server)
    {
        var state = Random(); var nonce = Random(); var verifier = Random();
        var challenge = await host.Protocol.ChallengeAsync(host.Connection, new(ControlledOidcServer.Callback, state, nonce, verifier));
        Assert.Equal(ExternalProtocolFailure.None, challenge.Failure);
        var parameters = QueryHelpers.ParseQuery(new Uri(challenge.AuthorizationUrl!).Query);
        Assert.Equal("S256", parameters["code_challenge_method"]);
        Assert.Equal(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), parameters["code_challenge"]);
        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await browser.GetAsync(challenge.AuthorizationUrl);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var returned = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal(state, returned["state"]);
        return (new(ControlledOidcServer.Callback, returned["code"]!, nonce, verifier, challenge.Issuer!, challenge.CredentialBinding!, returned["iss"]), challenge);
    }

    private static ProtocolHost Create(ControlledOidcServer server, string authentication = "client_secret_basic", string environment = "Development")
    {
        var secret = new SecretResolver();
        var services = new ServiceCollection();
        services.AddLogging();
        if (environment != "missing") services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
        services.AddSingleton<ISecretValueResolver>(secret);
        services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
        var connection = new ConnectionRecord
        {
            Id = "owned-provider", Key = "company", Settings = new Dictionary<string, string>
                { ["authority"] = server.Issuer, ["clientId"] = ControlledOidcServer.ClientId, ["clientAuthentication"] = authentication },
            Secrets = authentication == "none" ? new Dictionary<string, SecretReference>() :
                new Dictionary<string, SecretReference> { ["clientSecret"] = new("provider-client", SecretTypeNames.Text, "identity/external") }
        };
        return new(services.BuildServiceProvider(), connection, secret);
    }

    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    private sealed class ProtocolHost(ServiceProvider provider, ConnectionRecord connection, SecretResolver secret) : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;
        public ConnectionRecord Connection => connection;
        public SecretResolver Secret => secret;
        public IExternalAuthenticationProtocol Protocol => provider.GetServices<IExternalAuthenticationProtocol>().Single();
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
    private sealed class SecretResolver : ISecretValueResolver
    {
        public int Version = 1;
        public string Value = ControlledOidcServer.ClientSecret;
        public bool Available = true;
        public int Reads;
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Reads);
            Assert.Equal("default", tenantId);
            return ValueTask.FromResult(Available ? ResolvedSecret.Success(Value, new()
            {
                Id = "owned-client-secret", TenantId = tenantId, Name = reference.Name,
                Scope = reference.Scope, CurrentVersion = Version, Status = SecretStatus.Active
            }) : ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "sensitive resolver diagnostic"));
        }
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OwnedOidcFixture";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
