using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>Exercises the guarded OIDC event transition through direct events and the real JwtBearer handler.</summary>
public sealed class OidcBearerNormalizationTests
{
    private const string Scheme = "test-oidc-jwt";
    private const string Issuer = "https://issuer.example.test";
    private const string SecondIssuer = "https://second-issuer.example.test";
    private const string Audience = "worker-api";
    private const string Provider = "provider-a";
    private const string Tenant = "tenant-a";
    private const string Canary = "private-canary-value-7ac19";

    public enum CallbackStage
    {
        MessageReceived,
        TokenValidated,
        AuthenticationFailed
    }

    public enum CallbackDisposition
    {
        Fail,
        NoResult,
        Exception
    }

    public enum CallbackRegistrationFailure
    {
        MissingCapture,
        Disabled,
        WrongScheme
    }

    [Fact]
    public async Task A_real_rsa_signed_bearer_is_validated_then_published_as_the_guarded_identity()
    {
        await using var host = await NormalizationTestHost.StartAsync();
        var token = host.CreateToken(new Claim("external-canary", Canary));

        using var response = await host.SendBearerAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OidcBearerNormalizationEvents.NormalizedAuthenticationType, Header(response, "X-Authentication-Type"));
        Assert.Equal("1", Header(response, "X-Identity-Count"));
        Assert.Equal("1", Header(response, "X-Normalized-Count"));
        Assert.Equal(Tenant, Header(response, "X-Tenant"));
        Assert.Equal(Provider, Header(response, "X-Provider"));
        Assert.Equal("1", Header(response, "X-External-Canary-Count"));
        Assert.Equal(1, host.Mappings.Calls);
        Assert.Equal(Tenant, host.Mappings.LastTenant);
        Assert.Equal(Provider, host.Mappings.LastProvider);
    }

    [Fact]
    public async Task A_second_issuer_cannot_be_normalized_after_discovery_address_changes_on_options_reload()
    {
        const string firstMetadataAddress = "https://metadata.example.test/issuer-a/.well-known/openid-configuration";
        const string secondMetadataAddress = "https://metadata.example.test/issuer-b/.well-known/openid-configuration";
        const string secondKeyId = "second-test-key";
        using var secondRsa = RSA.Create(2048);
        var secondSigningKey = new RsaSecurityKey(secondRsa) { KeyId = secondKeyId };
        var mappings = new TestClaimMappingStore();
        var endpointCalls = 0;
        var metadataAddressVersion = 0;
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            onEndpointReached: _ => Interlocked.Increment(ref endpointCalls),
            backchannelHandlerFactory: signingRsa => new LocalDiscoveryHandler(
                new DiscoveryIssuer(Issuer, firstMetadataAddress, "test-key", signingRsa),
                new DiscoveryIssuer(SecondIssuer, secondMetadataAddress, secondKeyId, secondRsa)),
            configureBearerOptions: options =>
            {
                options.MetadataAddress = Volatile.Read(ref metadataAddressVersion) == 0 ? firstMetadataAddress : secondMetadataAddress;
                options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                    issuer is Issuer or SecondIssuer ? issuer : throw new SecurityTokenInvalidIssuerException();
            });

        using var firstIssuerResponse = await host.SendBearerAsync(host.CreateToken());
        Assert.Equal(HttpStatusCode.OK, firstIssuerResponse.StatusCode);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(1, endpointCalls);

        var secondIssuerToken = host.CreateToken(SecondIssuer, secondSigningKey);
        using var refusedBeforeReload = await host.SendBearerAsync(secondIssuerToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refusedBeforeReload.StatusCode);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(1, endpointCalls);

        Volatile.Write(ref metadataAddressVersion, 1);
        Assert.True(host.RemoveBearerOptionsFromCache());
        HttpStatusCode? statusAfterReload = null;
        var reloadException = await Record.ExceptionAsync(async () =>
        {
            using var response = await host.SendBearerAsync(secondIssuerToken);
            statusAfterReload = response.StatusCode;
        });

        Assert.True(reloadException is OptionsValidationException,
            $"Expected options-validation refusal; observed status {(int?)statusAfterReload}, mapping calls {mappings.Calls}, endpoint calls {endpointCalls}.");
        var exception = Assert.IsType<OptionsValidationException>(reloadException);
        Assert.Contains(OidcBearerOptionsValidator.ConfigurationInvalid, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(1, endpointCalls);
    }

    [Fact]
    public async Task Message_received_can_extract_a_valid_token_without_authorization_header_and_reject_a_tampered_token()
    {
        var mappings = new TestClaimMappingStore();
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnMessageReceived = context =>
            {
                context.Token = context.Request.Query["token"];
                return Task.CompletedTask;
            });
        var token = host.CreateToken();

        using var response = await host.SendQueryTokenAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, mappings.Calls);
        using var tampered = await host.SendQueryTokenAsync(Tamper(token));

        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Equal(1, mappings.Calls);
        AssertBareChallenge(tampered);
    }

    [Fact]
    public async Task A_success_result_from_message_received_cannot_bypass_real_token_validation()
    {
        var mappings = new TestClaimMappingStore();
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnMessageReceived = context =>
            {
                context.Principal = Ticket(context.Scheme.Name, new Claim("external-canary", Canary)).Principal;
                context.Success();
                return Task.CompletedTask;
            });

        using var response = await host.SendAsync("/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, mappings.Calls);
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task A_success_result_from_token_validated_cannot_short_circuit_normalization()
    {
        var mappings = new TestClaimMappingStore();
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnTokenValidated = context =>
            {
                context.Success();
                return Task.CompletedTask;
            });

        using var response = await host.SendBearerAsync(host.CreateToken());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, mappings.Calls);
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task A_success_result_from_authentication_failed_cannot_turn_a_bad_signature_into_a_ticket()
    {
        var mappings = new TestClaimMappingStore();
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnAuthenticationFailed = context =>
            {
                context.Principal = Ticket(context.Scheme.Name, new Claim("external-canary", Canary)).Principal;
                context.Success();
                return Task.CompletedTask;
            });

        using var response = await host.SendBearerAsync(Tamper(host.CreateToken()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, mappings.Calls);
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task Prior_token_validated_claim_changes_are_filtered_and_legitimate_rules_are_applied()
    {
        var rule = Rule("external-group", "mapped-worker", permissions: Set("worker.execute"));
        var mappings = new TestClaimMappingStore([rule]);
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnTokenValidated = context =>
            {
                ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim("external-group", "mapped-worker"));
                return Task.CompletedTask;
            });
        var token = host.CreateToken(
            new Claim(IdentityClaimTypes.Normalized.ToUpperInvariant(), "forged"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-forged"),
            new Claim(IdentityClaimTypes.Provider, "provider-forged"),
            new Claim(IdentityClaimTypes.Role, "admin"),
            new Claim(IdentityClaimTypes.Permission, "worker.admin"));

        using var response = await host.SendBearerAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Tenant, Header(response, "X-Tenant"));
        Assert.Equal(Provider, Header(response, "X-Provider"));
        Assert.Equal("1", Header(response, "X-Normalized-Count"));
        Assert.Equal("worker.execute", Header(response, "X-Permissions"));
        Assert.Equal("0", Header(response, "X-Roles-Count"));
        Assert.Equal(Tenant, mappings.LastTenant);
        Assert.Equal(Provider, mappings.LastProvider);
    }

    [Theory]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.Exception)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.Exception)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.Exception)]
    public async Task Prior_callback_refusals_never_publish_a_ticket_or_expose_their_values(
        CallbackStage stage, CallbackDisposition disposition)
    {
        var mappings = new TestClaimMappingStore();
        var callbacks = CallbacksFor(stage, disposition);
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events =>
            {
                events.OnMessageReceived = callbacks.MessageReceived;
                events.OnTokenValidated = callbacks.TokenValidated;
                events.OnAuthenticationFailed = callbacks.AuthenticationFailed;
            });
        var token = host.CreateToken();
        if (stage == CallbackStage.AuthenticationFailed)
            token = Tamper(token);

        using var response = await host.SendBearerAsync(token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, mappings.Calls);
        Assert.False(body.Contains(Canary, StringComparison.Ordinal));
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task A_mapping_failure_does_not_publish_a_ticket_or_expose_exception_text()
    {
        var mappings = new TestClaimMappingStore
        {
            OnListAsync = (_, _, _) => ValueTask.FromException<IReadOnlyList<ClaimMappingRule>>(
                new InvalidOperationException(Canary))
        };
        await using var host = await NormalizationTestHost.StartAsync(mappings: mappings);

        using var response = await host.SendBearerAsync(host.CreateToken());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, mappings.Calls);
        Assert.False(body.Contains(Canary, StringComparison.Ordinal));
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task A_normalizer_failure_does_not_publish_a_ticket_or_expose_exception_text()
    {
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new InvalidOperationException(Canary));
        await using var host = await NormalizationTestHost.StartAsync(normalizer: normalizer);

        using var response = await host.SendBearerAsync(host.CreateToken());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.Contains(Canary, StringComparison.Ordinal));
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task A_malformed_normalizer_result_does_not_publish_a_ticket()
    {
        var normalizer = TestClaimsNormalizer.Sync(_ => Result(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(IdentityClaimTypes.Normalized, Canary)], "wrong-authentication-type"))));
        await using var host = await NormalizationTestHost.StartAsync(normalizer: normalizer);

        using var response = await host.SendBearerAsync(host.CreateToken());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.Contains(Canary, StringComparison.Ordinal));
        AssertBareChallenge(response);
    }

    [Fact]
    public async Task Request_abort_after_rule_lookup_prevents_normalization_and_ticket_publication()
    {
        using var requestCancellation = new CancellationTokenSource();
        var lookupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource<IReadOnlyList<ClaimMappingRule>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mappings = new TestClaimMappingStore
        {
            OnListAsync = async (_, _, _) =>
            {
                lookupEntered.TrySetResult();
                return await releaseLookup.Task;
            }
        };
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run after lookup cancellation."));
        await using var host = await NormalizationTestHost.StartAsync(mappings: mappings, normalizer: normalizer);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateToken());

        var responseTask = host.Client.SendAsync(request, requestCancellation.Token);
        await lookupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        requestCancellation.Cancel();
        releaseLookup.TrySetResult([]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.True(mappings.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(0, normalizer.Calls);
    }

    [Fact]
    public async Task Request_abort_after_message_received_prevents_rule_lookup()
    {
        using var requestCancellation = new CancellationTokenSource();
        var mappings = new TestClaimMappingStore();
        await using var host = await NormalizationTestHost.StartAsync(
            mappings: mappings,
            configureEvents: events => events.OnMessageReceived = _ =>
            {
                requestCancellation.Cancel();
                return Task.CompletedTask;
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateToken());

        var responseTask = host.Client.SendAsync(request, requestCancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.Equal(0, mappings.Calls);
    }

    [Fact]
    public async Task Request_abort_during_a_noncooperative_normalizer_prevents_ticket_publication()
    {
        using var requestCancellation = new CancellationTokenSource();
        var normalizationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNormalizer = new TaskCompletionSource<ClaimsNormalizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizer = TestClaimsNormalizer.Async(async (_, _) =>
        {
            normalizationEntered.TrySetResult();
            return await releaseNormalizer.Task;
        });
        await using var host = await NormalizationTestHost.StartAsync(normalizer: normalizer);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateToken());

        var responseTask = host.Client.SendAsync(request, requestCancellation.Token);
        await normalizationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        requestCancellation.Cancel();
        releaseNormalizer.TrySetResult(Result(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "worker-1")], "raw"))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.Equal(1, normalizer.Calls);
    }

    [Fact]
    public async Task Request_abort_during_output_admission_keeps_the_original_principal_unpublished()
    {
        using var requestAborted = new CancellationTokenSource();
        var outputAdmissionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalizedPrincipal = new ClaimsPrincipal(
            new AdmissionCancellationIdentity(
                RequiredNormalizedClaims(),
                OidcBearerNormalizationEvents.NormalizedAuthenticationType,
                () =>
                {
                    outputAdmissionReached.TrySetResult();
                    requestAborted.Cancel();
                }));
        var normalizer = TestClaimsNormalizer.Sync(_ => Result(normalizedPrincipal));
        var unit = CreateDirectEventUnit(normalizer);
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original, requestAborted.Token);

        Assert.False(outputAdmissionReached.Task.IsCompleted);
        var exception = await Record.ExceptionAsync(() => unit.Events.TokenValidated(context));

        Assert.True(outputAdmissionReached.Task.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Same(original, context.Principal);
        Assert.Equal(1, normalizer.Calls);
    }

    [Fact]
    public async Task Actual_bearer_handler_does_not_reach_the_endpoint_when_abort_occurs_during_output_admission()
    {
        using var requestCancellation = new CancellationTokenSource();
        var outputAdmissionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpointCalls = 0;
        HttpContext? requestContext = null;
        var normalizedPrincipal = new ClaimsPrincipal(
            new AdmissionCancellationIdentity(
                RequiredNormalizedClaims(),
                OidcBearerNormalizationEvents.NormalizedAuthenticationType,
                () =>
                {
                    outputAdmissionReached.TrySetResult();
                    requestCancellation.Cancel();
                    requestContext!.Abort();
                }));
        var normalizer = TestClaimsNormalizer.Sync(_ => Result(normalizedPrincipal));
        await using var host = await NormalizationTestHost.StartAsync(
            normalizer: normalizer,
            configureEvents: events => events.OnTokenValidated = context =>
            {
                requestContext = context.HttpContext;
                return Task.CompletedTask;
            },
            onEndpointReached: _ => Interlocked.Increment(ref endpointCalls));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateToken());

        var responseTask = host.Client.SendAsync(request, requestCancellation.Token);
        await outputAdmissionReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.Equal(1, normalizer.Calls);
        Assert.Equal(0, Volatile.Read(ref endpointCalls));
    }

    [Fact]
    public async Task Direct_event_filters_forged_internal_claims_before_normalization()
    {
        ClaimsPrincipal? received = null;
        var normalizer = TestClaimsNormalizer.Sync(context =>
        {
            received = context.Principal;
            return Result(new ClaimsPrincipal(
                new ClaimsIdentity(RequiredNormalizedClaims(), OidcBearerNormalizationEvents.NormalizedAuthenticationType)));
        });
        var unit = CreateDirectEventUnit(normalizer);
        var original = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "worker-1"),
            new Claim("outside", "kept"),
            new Claim(IdentityClaimTypes.Normalized.ToUpperInvariant(), "forged"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-forged"),
            new Claim(IdentityClaimTypes.Provider, "provider-forged"),
            new Claim(IdentityClaimTypes.Role.ToUpperInvariant(), "admin"),
            new Claim(IdentityClaimTypes.Permission, "worker.admin")
        ], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original);

        await unit.Events.TokenValidated(context);

        Assert.Null(context.Result);
        Assert.NotNull(received);
        Assert.Contains(received!.Claims, claim => claim.Type == "outside" && claim.Value == "kept");
        Assert.DoesNotContain(received.Claims, IsInternalClaim);
        Assert.Equal(OidcBearerNormalizationEvents.NormalizedAuthenticationType, context.Principal!.Identity!.AuthenticationType);
    }

    [Fact]
    public async Task Direct_event_and_default_normalizer_ignore_rules_from_other_provider_or_tenant()
    {
        var mappings = new TestClaimMappingStore(
        [
            Rule("groups", "allow", provider: "provider-other", permissions: Set("worker.execute")),
            Rule("groups", "allow", tenant: "tenant-other", permissions: Set("worker.manage"))
        ]);
        var normalizer = new DefaultClaimsNormalizer(new ClaimMappingRuleEvaluator());
        var unit = CreateDirectEventUnit(normalizer, mappings);
        var context = CreateTokenValidatedContext(unit.Options, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("groups", "allow")], "validated-token")));

        await unit.Events.TokenValidated(context);

        Assert.Null(context.Result);
        var identity = Assert.Single(context.Principal!.Identities);
        Assert.Empty(identity.FindAll(IdentityClaimTypes.Permission));
        Assert.Empty(identity.FindAll(IdentityClaimTypes.Role));
        Assert.Equal(Tenant, mappings.LastTenant);
        Assert.Equal(Provider, mappings.LastProvider);
    }

    [Fact]
    public async Task Direct_event_admits_only_the_exact_normalized_identity_marker_and_namespace()
    {
        var normalizer = TestClaimsNormalizer.Sync(_ => Result(new ClaimsPrincipal(
            new ClaimsIdentity(RequiredNormalizedClaims(), OidcBearerNormalizationEvents.NormalizedAuthenticationType))));
        var unit = CreateDirectEventUnit(normalizer);
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original);

        await unit.Events.TokenValidated(context);

        Assert.Null(context.Result);
        var identity = Assert.Single(context.Principal!.Identities);
        Assert.Equal(OidcBearerNormalizationEvents.NormalizedAuthenticationType, identity.AuthenticationType);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.Equal(Tenant, Assert.Single(identity.FindAll(IdentityClaimTypes.TenantId)).Value);
        Assert.Equal(Provider, Assert.Single(identity.FindAll(IdentityClaimTypes.Provider)).Value);
    }

    [Fact]
    public async Task Direct_event_refuses_wrong_type_marker_namespace_and_multiple_identities()
    {
        (string Name, ClaimsPrincipal Principal)[] invalidResults =
        [
            ("wrong authentication type", new ClaimsPrincipal(new ClaimsIdentity(RequiredNormalizedClaims(), "raw-token"))),
            ("unauthenticated output", new ClaimsPrincipal(new ClaimsIdentity(RequiredNormalizedClaims()))),
            ("missing marker", new ClaimsPrincipal(new ClaimsIdentity(
                RequiredNormalizedClaims().Where(claim => claim.Type != IdentityClaimTypes.Normalized),
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("wrong marker value", new ClaimsPrincipal(new ClaimsIdentity(
                [.. RequiredNormalizedClaims().Where(claim => claim.Type != IdentityClaimTypes.Normalized), new Claim(IdentityClaimTypes.Normalized, "v2")],
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("duplicate marker", new ClaimsPrincipal(new ClaimsIdentity(
                [.. RequiredNormalizedClaims(), new Claim(IdentityClaimTypes.Normalized, "v1")],
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("noncanonical marker type", new ClaimsPrincipal(new ClaimsIdentity(
                [.. RequiredNormalizedClaims().Where(claim => claim.Type != IdentityClaimTypes.Normalized), new Claim(IdentityClaimTypes.Normalized.ToUpperInvariant(), "v1")],
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("wrong tenant", new ClaimsPrincipal(new ClaimsIdentity(
                [.. RequiredNormalizedClaims().Where(claim => claim.Type != IdentityClaimTypes.TenantId), new Claim(IdentityClaimTypes.TenantId, "tenant-other")],
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("wrong provider", new ClaimsPrincipal(new ClaimsIdentity(
                [.. RequiredNormalizedClaims().Where(claim => claim.Type != IdentityClaimTypes.Provider), new Claim(IdentityClaimTypes.Provider, "provider-other")],
                OidcBearerNormalizationEvents.NormalizedAuthenticationType))),
            ("multiple identities", new ClaimsPrincipal(
            [
                new ClaimsIdentity(RequiredNormalizedClaims(), OidcBearerNormalizationEvents.NormalizedAuthenticationType),
                new ClaimsIdentity([new Claim("sub", "second")], "second")
            ])),
            ("throwing identity enumeration", new ThrowingIdentityEnumerationPrincipal()),
            ("throwing claim enumeration", new ClaimsPrincipal(new ThrowingClaimsEnumerationIdentity()))
        ];

        foreach (var (name, invalidPrincipal) in invalidResults)
        {
            var normalizer = TestClaimsNormalizer.Sync(_ => Result(invalidPrincipal));
            var unit = CreateDirectEventUnit(normalizer);
            var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
            var context = CreateTokenValidatedContext(unit.Options, original);

            await unit.Events.TokenValidated(context);

            Assert.Equal("oidc-normalization-result-invalid", context.Result?.Failure?.Message);
            Assert.False(context.Result?.Succeeded);
            Assert.Same(original, context.Principal);
            Assert.NotEqual(OidcBearerNormalizationEvents.NormalizedAuthenticationType, context.Principal?.Identity?.AuthenticationType);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public async Task Direct_event_refuses_a_null_normalizer_result_without_replacing_the_validated_principal()
    {
        var unit = CreateDirectEventUnit(TestClaimsNormalizer.Sync(_ => null!));
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original);

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-result-invalid", context.Result?.Failure?.Message);
        Assert.Same(original, context.Principal);
    }

    [Fact]
    public async Task Direct_event_replaces_a_value_bearing_callback_exception_with_a_fixed_failure()
    {
        var unit = CreateDirectEventUnit(TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("should not normalize")));
        unit.Registration.CapturedCallbacks.Remove(unit.Options);
        unit.Registration.CapturedCallbacks.Add(unit.Options, new OidcBearerCallbacks(
            _ => Task.CompletedTask,
            _ => throw new InvalidOperationException(Canary),
            _ => Task.CompletedTask));
        var context = CreateTokenValidatedContext(unit.Options, new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker")], "validated-token")));

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-events-failed", context.Result?.Failure?.Message);
        Assert.False((context.Result?.Failure?.Message ?? string.Empty).Contains(Canary, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.MessageReceived, CallbackDisposition.Exception)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.TokenValidated, CallbackDisposition.Exception)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.Fail)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.NoResult)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackDisposition.Exception)]
    public async Task Direct_event_preserves_callback_refusals_and_fixes_failures_at_every_stage(
        CallbackStage stage,
        CallbackDisposition disposition)
    {
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run after prior refusal."));
        var unit = CreateDirectEventUnit(normalizer, mappings);
        unit.Registration.CapturedCallbacks.Remove(unit.Options);
        unit.Registration.CapturedCallbacks.Add(unit.Options, CallbacksFor(stage, disposition));

        var result = await InvokeDirectCallbackAsync(stage, unit.Events, unit.Options);

        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        if (disposition == CallbackDisposition.NoResult)
        {
            Assert.True(result.None);
            Assert.Null(result.Failure);
        }
        else
        {
            Assert.False(result.None);
            Assert.Equal("oidc-normalization-events-failed", result.Failure?.Message);
            Assert.False((result.Failure?.Message ?? string.Empty).Contains(Canary, StringComparison.Ordinal));
        }
        Assert.Equal(0, mappings.Calls);
        Assert.Equal(0, normalizer.Calls);
    }

    [Theory]
    [InlineData(CallbackStage.MessageReceived, CallbackRegistrationFailure.MissingCapture)]
    [InlineData(CallbackStage.MessageReceived, CallbackRegistrationFailure.Disabled)]
    [InlineData(CallbackStage.MessageReceived, CallbackRegistrationFailure.WrongScheme)]
    [InlineData(CallbackStage.TokenValidated, CallbackRegistrationFailure.MissingCapture)]
    [InlineData(CallbackStage.TokenValidated, CallbackRegistrationFailure.Disabled)]
    [InlineData(CallbackStage.TokenValidated, CallbackRegistrationFailure.WrongScheme)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackRegistrationFailure.MissingCapture)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackRegistrationFailure.Disabled)]
    [InlineData(CallbackStage.AuthenticationFailed, CallbackRegistrationFailure.WrongScheme)]
    public async Task Direct_events_reject_missing_or_incompatible_captured_callbacks(
        CallbackStage stage,
        CallbackRegistrationFailure failure)
    {
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run."));
        var unit = CreateDirectEventUnit(
            normalizer,
            mappings,
            registrationNormalize: failure != CallbackRegistrationFailure.Disabled,
            registrationScheme: failure == CallbackRegistrationFailure.WrongScheme ? "other-scheme" : Scheme,
            captureCallbacks: failure != CallbackRegistrationFailure.MissingCapture);

        var result = await InvokeDirectCallbackAsync(stage, unit.Events, unit.Options);

        Assert.Equal("oidc-normalization-configuration-invalid", result?.Failure?.Message);
        Assert.False(result?.Succeeded);
        Assert.Equal(0, mappings.Calls);
        Assert.Equal(0, normalizer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_token_validation_rejects_a_missing_validated_principal_or_security_token(bool principalMissing)
    {
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run."));
        var unit = CreateDirectEventUnit(normalizer, mappings);
        var context = CreateTokenValidatedContext(
            unit.Options,
            principalMissing ? null : new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token")),
            includeSecurityToken: principalMissing);

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-result-invalid", context.Result?.Failure?.Message);
        Assert.Equal(0, mappings.Calls);
        Assert.Equal(0, normalizer.Calls);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("provider")]
    [InlineData("tenant")]
    [InlineData("throws")]
    public async Task Direct_token_validation_uses_the_frozen_trust_values_from_current_options(string change)
    {
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run."));
        var current = new OidcAuthenticationOptions
        {
            NormalizeBearerClaims = change != "disabled",
            JwtBearerScheme = Scheme,
            Authority = Issuer,
            Audience = Audience,
            ProviderId = change == "provider" ? "provider-other" : Provider,
            TenantId = change == "tenant" ? "tenant-other" : Tenant,
            RequireHttpsMetadata = false
        };
        IOptionsMonitor<OidcAuthenticationOptions> monitor = change == "throws"
            ? new ThrowingOptionsMonitor<OidcAuthenticationOptions>()
            : new TestOptionsMonitor<OidcAuthenticationOptions>(current);
        var unit = CreateDirectEventUnit(normalizer, mappings, optionsMonitor: monitor);
        var context = CreateTokenValidatedContext(unit.Options,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token")));

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-configuration-invalid", context.Result?.Failure?.Message);
        Assert.Equal(0, mappings.Calls);
        Assert.Equal(0, normalizer.Calls);
    }

    [Fact]
    public async Task Direct_token_validation_requires_the_exact_unprivileged_tenant_scope_before_mapping()
    {
        var purposes = new PersistenceAccessPurpose("oidc-normalization-test");
        (string Name, IPersistenceAccessContextAccessor Access)[] accessors =
        [
            ("mismatched tenant", new TestPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-other")))),
            ("missing context", new TestPersistenceAccessContextAccessor(null)),
            ("unavailable accessor", new ThrowingPersistenceAccessContextAccessor()),
            ("global", new TestPersistenceAccessContextAccessor(PersistenceAccessContext.Global)),
            ("privileged tenant", new TestPersistenceAccessContextAccessor(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope(Tenant), purposes))),
            ("privileged across scopes", new TestPersistenceAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(purposes)))
        ];

        foreach (var (name, access) in accessors)
        {
            var mappings = new TestClaimMappingStore();
            var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run without an admitted scope."));
            var unit = CreateDirectEventUnit(normalizer, mappings, access: access);
            var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
            var context = CreateTokenValidatedContext(unit.Options, original);

            await unit.Events.TokenValidated(context);

            Assert.Equal("oidc-normalization-persistence-scope-invalid", context.Result?.Failure?.Message);
            Assert.Same(original, context.Principal);
            Assert.Equal(0, mappings.Calls);
            Assert.Equal(0, normalizer.Calls);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public async Task Direct_mapping_exceptions_are_value_free_and_do_not_reach_the_normalizer()
    {
        var mappings = new TestClaimMappingStore
        {
            OnListAsync = (_, _, _) => ValueTask.FromException<IReadOnlyList<ClaimMappingRule>>(
                new InvalidOperationException(Canary))
        };
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Normalization must not run after mapping failure."));
        var unit = CreateDirectEventUnit(normalizer, mappings);
        var context = CreateTokenValidatedContext(unit.Options,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token")));

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-mapping-unavailable", context.Result?.Failure?.Message);
        Assert.DoesNotContain(Canary, context.Result?.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(0, normalizer.Calls);
    }

    [Fact]
    public async Task Direct_normalizer_exceptions_are_value_free_after_successful_mapping()
    {
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new InvalidOperationException(Canary));
        var unit = CreateDirectEventUnit(normalizer, mappings);
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original);

        await unit.Events.TokenValidated(context);

        Assert.Equal("oidc-normalization-failed", context.Result?.Failure?.Message);
        Assert.DoesNotContain(Canary, context.Result?.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Same(original, context.Principal);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(1, normalizer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_mapping_and_normalizer_exception_catch_boundaries_rethrow_when_request_is_aborted(bool mappingStage)
    {
        using var requestAborted = new CancellationTokenSource();
        var mappings = new TestClaimMappingStore();
        var normalizer = TestClaimsNormalizer.Sync(_ => throw new InvalidOperationException(Canary));
        if (mappingStage)
            mappings.OnListAsync = (_, _, _) =>
            {
                requestAborted.Cancel();
                throw new InvalidOperationException(Canary);
            };
        else
            normalizer = TestClaimsNormalizer.Sync(_ =>
            {
                requestAborted.Cancel();
                throw new InvalidOperationException(Canary);
            });
        var unit = CreateDirectEventUnit(normalizer, mappings);
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token"));
        var context = CreateTokenValidatedContext(unit.Options, original, requestAborted.Token);

        var exception = await Record.ExceptionAsync(() => unit.Events.TokenValidated(context));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Null(context.Result);
        Assert.Same(original, context.Principal);
        Assert.Equal(1, mappings.Calls);
        Assert.Equal(mappingStage ? 0 : 1, normalizer.Calls);
    }

    [Fact]
    public async Task Direct_challenge_and_forbidden_events_set_only_their_protocol_responses()
    {
        var unit = CreateDirectEventUnit(TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Not used.")));
        var scheme = new AuthenticationScheme(Scheme, Scheme, typeof(JwtBearerHandler));
        var challengeContext = new JwtBearerChallengeContext(new DefaultHttpContext(), scheme, unit.Options, new AuthenticationProperties());
        var forbiddenContext = new ForbiddenContext(new DefaultHttpContext(), scheme, unit.Options);

        await unit.Events.Challenge(challengeContext);
        await unit.Events.Forbidden(forbiddenContext);

        Assert.True(challengeContext.Handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, challengeContext.Response.StatusCode);
        Assert.Equal("Bearer", challengeContext.Response.Headers.WWWAuthenticate);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenContext.Response.StatusCode);
        Assert.Empty(forbiddenContext.Response.Headers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_challenge_and_forbidden_events_observe_request_abort(bool challenge)
    {
        using var requestAborted = new CancellationTokenSource();
        requestAborted.Cancel();
        var unit = CreateDirectEventUnit(TestClaimsNormalizer.Sync(_ => throw new Xunit.Sdk.XunitException("Not used.")));
        var scheme = new AuthenticationScheme(Scheme, Scheme, typeof(JwtBearerHandler));
        var httpContext = new DefaultHttpContext { RequestAborted = requestAborted.Token };
        var statusBefore = httpContext.Response.StatusCode;

        var exception = challenge
            ? await Record.ExceptionAsync(() => unit.Events.Challenge(new JwtBearerChallengeContext(httpContext, scheme, unit.Options, new AuthenticationProperties())))
            : await Record.ExceptionAsync(() => unit.Events.Forbidden(new ForbiddenContext(httpContext, scheme, unit.Options)));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(statusBefore, httpContext.Response.StatusCode);
        Assert.Empty(httpContext.Response.Headers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oidc_opt_in_composes_with_first_party_openiddict_in_both_registration_orders(bool oidcFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceCore(Tenant);
        ReplaceClaimMappingStore(services, new TestClaimMappingStore());
        // The host has already elected the first-party selector. Both registrations must leave that choice authoritative.
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictIdentityDefaults.SelectorScheme;
            options.DefaultChallengeScheme = OpenIddictIdentityDefaults.SelectorScheme;
        });

        if (!oidcFirst)
            AddOpenIddict(services);
        services.AddFoundationIdentityOidc(ConfigureNormalizationOptions);
        if (oidcFirst)
            AddOpenIddict(services);

        using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Scheme);

        Assert.Equal(OpenIddictIdentityDefaults.SelectorScheme, authentication.DefaultAuthenticateScheme);
        Assert.Equal(OpenIddictIdentityDefaults.SelectorScheme, authentication.DefaultChallengeScheme);
        Assert.Contains(schemes, scheme => scheme.Name == Scheme && scheme.HandlerType == typeof(JwtBearerHandler));
        Assert.Contains(schemes, scheme => scheme.Name == OpenIddictIdentityDefaults.SelectorScheme);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), bearer.EventsType);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents));

        static void AddOpenIddict(IServiceCollection target)
        {
            target.AddOpenIddictVendorForTests(builder =>
                builder.UseInMemoryDatabase($"oidc-normalization-{Guid.NewGuid():N}"));
            target.AddFoundationIdentityOpenIddict(options => options.IsDevelopmentOrDemo = true);
        }
    }

    [Fact]
    public void Repeated_opt_in_registration_is_refused_without_adding_a_second_events_adapter()
    {
        var services = CreateOptInServices();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddFoundationIdentityOidc(ConfigureNormalizationOptions));

        Assert.Equal("oidc-normalization-configuration-invalid", exception.Message);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents));
    }

    [Fact]
    public void Late_opt_in_without_a_registered_bridge_is_refused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOidc();
        services.Configure<OidcAuthenticationOptions>(ConfigureNormalizationOptions);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<OidcAuthenticationOptions>>().Get(Options.DefaultName));

        Assert.Contains("oidc-normalization-configuration-invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Late_scheme_rename_is_refused_by_the_frozen_registration()
    {
        var services = CreateOptInServices();
        services.Configure<OidcAuthenticationOptions>(options => options.JwtBearerScheme = "renamed-after-registration");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<OidcAuthenticationOptions>>().Get(Options.DefaultName));

        Assert.Contains("oidc-normalization-configuration-invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raw_bearer_authentication_type_cannot_be_enrolled_as_normalized()
    {
        var services = CreateOptInServices();
        services.AddNormalizedAuthenticationType("AuthenticationTypes.Federation");
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<OidcBearerActivationGuard>().InitializeAsync(CancellationToken.None));

        Assert.Equal("oidc-normalization-configuration-invalid", exception.Message);
    }

    [Fact]
    public async Task A_replaced_actual_bearer_handler_is_refused_during_activation()
    {
        var services = CreateOptInServices();
        services.Configure<AuthenticationOptions>(options =>
            options.Schemes.Single(scheme => scheme.Name == Scheme).HandlerType = typeof(ForeignJwtBearerHandler));
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<OidcBearerActivationGuard>().InitializeAsync(CancellationToken.None));

        Assert.Equal("oidc-normalization-configuration-invalid", exception.Message);
    }

    [Fact]
    public void Foreign_events_type_subclass_and_challenge_callbacks_are_refused()
    {
        Action<JwtBearerOptions>[] incompatibleOptions =
        [
            options => options.EventsType = typeof(ForeignJwtBearerEvents),
            options => options.Events = new ForeignJwtBearerEvents(),
            options => options.Events.OnChallenge = _ => Task.CompletedTask,
            options => options.Events.OnForbidden = _ => Task.CompletedTask
        ];

        foreach (var configureBearer in incompatibleOptions)
        {
            var services = CreateOptInServices();
            services.Configure<JwtBearerOptions>(Scheme, configureBearer);
            using var provider = services.BuildServiceProvider();

            var exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Scheme));

            Assert.Contains("oidc-normalization-events-incompatible", exception.Message, StringComparison.Ordinal);
        }
    }

    private static ServiceCollection CreateOptInServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceCore(Tenant);
        ReplaceClaimMappingStore(services, new TestClaimMappingStore());
        services.AddFoundationIdentityOidc(ConfigureNormalizationOptions);
        return services;
    }

    private static void ConfigureNormalizationOptions(OidcAuthenticationOptions options)
    {
        options.NormalizeBearerClaims = true;
        options.JwtBearerScheme = Scheme;
        options.Authority = Issuer;
        options.Audience = Audience;
        options.ProviderId = Provider;
        options.TenantId = Tenant;
        options.RequireHttpsMetadata = false;
    }

    private static void ReplaceClaimMappingStore(IServiceCollection services, IClaimMappingStore store)
    {
        services.RemoveAll<IClaimMappingStore>();
        services.AddSingleton<IClaimMappingStore>(store);
    }

    private sealed class ForeignJwtBearerEvents : JwtBearerEvents
    {
    }

    private sealed class ForeignJwtBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static string Header(HttpResponseMessage response, string name) => response.Headers.GetValues(name).Single();

    private static void AssertBareChallenge(HttpResponseMessage response)
    {
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer" && value.Parameter is null);
        Assert.False(response.Headers.WwwAuthenticate.ToString().Contains(Canary, StringComparison.Ordinal));
    }

    private static bool IsInternalClaim(Claim claim) =>
        new[]
        {
            IdentityClaimTypes.Normalized,
            IdentityClaimTypes.TenantId,
            IdentityClaimTypes.Provider,
            IdentityClaimTypes.Role,
            IdentityClaimTypes.Permission
        }.Contains(claim.Type, StringComparer.OrdinalIgnoreCase);

    private static string Tamper(string token)
    {
        var pieces = token.Split('.');
        pieces[2] = (pieces[2][0] == 'A' ? "B" : "A") + pieces[2][1..];
        return string.Join('.', pieces);
    }

    private static AuthenticationTicket Ticket(string scheme, params Claim[] claims) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(claims, "bypass")), new AuthenticationProperties(), scheme);

    private static ClaimMappingRule Rule(
        string matchClaimType,
        string matchValue,
        string? provider = Provider,
        string? tenant = Tenant,
        IReadOnlySet<string>? roles = null,
        IReadOnlySet<string>? permissions = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            tenant!,
            provider!,
            matchClaimType,
            matchValue,
            roles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            permissions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            1,
            false);

    private static Claim[] RequiredNormalizedClaims() =>
    [
        new(IdentityClaimTypes.Normalized, "v1"),
        new(IdentityClaimTypes.TenantId, Tenant),
        new(IdentityClaimTypes.Provider, Provider)
    ];

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);

    private static ClaimsNormalizationResult Result(ClaimsPrincipal principal) =>
        new(principal, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static OidcBearerCallbacks CallbacksFor(CallbackStage stage, CallbackDisposition disposition) => new(
        context => ApplyCallback(stage, CallbackStage.MessageReceived, disposition, context.Fail, context.NoResult),
        context => ApplyCallback(stage, CallbackStage.TokenValidated, disposition, context.Fail, context.NoResult),
        context => ApplyCallback(stage, CallbackStage.AuthenticationFailed, disposition, context.Fail, context.NoResult));

    private static Task ApplyCallback(
        CallbackStage selectedStage,
        CallbackStage callbackStage,
        CallbackDisposition disposition,
        Action<string> fail,
        Action noResult)
    {
        if (selectedStage != callbackStage)
            return Task.CompletedTask;

        switch (disposition)
        {
            case CallbackDisposition.Fail:
                fail(Canary);
                break;
            case CallbackDisposition.NoResult:
                noResult();
                break;
            case CallbackDisposition.Exception:
                throw new FormatException(Canary);
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition), disposition, null);
        }
        return Task.CompletedTask;
    }

    private static async Task<AuthenticateResult?> InvokeDirectCallbackAsync(
        CallbackStage stage,
        OidcBearerNormalizationEvents events,
        JwtBearerOptions options,
        string contextScheme = Scheme)
    {
        var scheme = new AuthenticationScheme(contextScheme, contextScheme, typeof(JwtBearerHandler));
        switch (stage)
        {
            case CallbackStage.MessageReceived:
            {
                var context = new MessageReceivedContext(new DefaultHttpContext(), scheme, options);
                await events.MessageReceived(context);
                return context.Result;
            }
            case CallbackStage.TokenValidated:
            {
                var context = CreateTokenValidatedContext(options,
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "worker-1")], "validated-token")));
                await events.TokenValidated(context);
                return context.Result;
            }
            case CallbackStage.AuthenticationFailed:
            {
                var context = new AuthenticationFailedContext(
                    new DefaultHttpContext(), scheme, options) { Exception = new InvalidOperationException("invalid token") };
                await events.AuthenticationFailed(context);
                return context.Result;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }
    }

    private static (OidcBearerNormalizationEvents Events, OidcBearerRegistration Registration, JwtBearerOptions Options) CreateDirectEventUnit(
        IClaimsNormalizer normalizer,
        TestClaimMappingStore? mappings = null,
        IOptionsMonitor<OidcAuthenticationOptions>? optionsMonitor = null,
        IPersistenceAccessContextAccessor? access = null,
        bool registrationNormalize = true,
        string? registrationScheme = null,
        bool captureCallbacks = true)
    {
        mappings ??= new TestClaimMappingStore();
        var services = new ServiceCollection();
        services.AddSingleton<IClaimsNormalizer>(normalizer);
        services.AddSingleton<IClaimMappingStore>(mappings);
        var registration = new OidcBearerRegistration(
            registrationNormalize, registrationScheme ?? Scheme, true, Issuer, Audience, Provider, Tenant, false, services);
        var options = new JwtBearerOptions();
        if (captureCallbacks)
            registration.CapturedCallbacks.Add(options, new OidcBearerCallbacks(
                _ => Task.CompletedTask,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask));
        var events = new OidcBearerNormalizationEvents(
            registration,
            optionsMonitor ?? new TestOptionsMonitor<OidcAuthenticationOptions>(new OidcAuthenticationOptions
            {
                NormalizeBearerClaims = true,
                JwtBearerScheme = Scheme,
                Authority = Issuer,
                Audience = Audience,
                ProviderId = Provider,
                TenantId = Tenant,
                RequireHttpsMetadata = false
            }),
            mappings,
            normalizer,
            access ?? new TestPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(Tenant))));
        return (events, registration, options);
    }

    private static TokenValidatedContext CreateTokenValidatedContext(
        JwtBearerOptions options,
        ClaimsPrincipal? principal,
        CancellationToken requestAborted = default,
        bool includeSecurityToken = true)
    {
        var scheme = new AuthenticationScheme(Scheme, Scheme, typeof(JwtBearerHandler));
        var httpContext = new DefaultHttpContext { RequestAborted = requestAborted };
        var context = new TokenValidatedContext(httpContext, scheme, options);
        if (includeSecurityToken)
            context.SecurityToken = new JwtSecurityToken(issuer: Issuer, audience: Audience);
        context.Principal = principal!;
        return context;
    }

    private sealed class AdmissionCancellationIdentity : ClaimsIdentity
    {
        private readonly Action _onAuthenticationCheck;
        private int _checked;

        public AdmissionCancellationIdentity(IEnumerable<Claim> claims, string authenticationType, Action onAuthenticationCheck)
            : base(claims, authenticationType) => _onAuthenticationCheck = onAuthenticationCheck;

        public override bool IsAuthenticated
        {
            get
            {
                if (Interlocked.Exchange(ref _checked, 1) == 0)
                    _onAuthenticationCheck();
                return base.IsAuthenticated;
            }
        }
    }

    private sealed class ThrowingIdentityEnumerationPrincipal : ClaimsPrincipal
    {
        public override IEnumerable<ClaimsIdentity> Identities => Enumerate();

        private static IEnumerable<ClaimsIdentity> Enumerate()
        {
            yield return new ClaimsIdentity(RequiredNormalizedClaims(), OidcBearerNormalizationEvents.NormalizedAuthenticationType);
            throw new InvalidOperationException(Canary);
        }
    }

    private sealed class ThrowingClaimsEnumerationIdentity : ClaimsIdentity
    {
        public ThrowingClaimsEnumerationIdentity()
            : base(RequiredNormalizedClaims(), OidcBearerNormalizationEvents.NormalizedAuthenticationType)
        {
        }

        public override IEnumerable<Claim> Claims => Enumerate();

        private static IEnumerable<Claim> Enumerate()
        {
            yield return new Claim(IdentityClaimTypes.Normalized, "v1");
            throw new InvalidOperationException(Canary);
        }
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class TestPersistenceAccessContextAccessor(PersistenceAccessContext? current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => current!;
    }

    private sealed class ThrowingPersistenceAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => throw new InvalidOperationException("No request scope is available.");
    }

    private sealed class ThrowingOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => throw new InvalidOperationException("Options reload is unavailable.");

        public TOptions Get(string? name) => throw new InvalidOperationException("Options reload is unavailable.");

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class TestClaimsNormalizer(
        Func<ClaimsNormalizationContext, CancellationToken, ValueTask<ClaimsNormalizationResult>> normalize) : IClaimsNormalizer
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<ClaimsNormalizationResult> NormalizeAsync(ClaimsNormalizationContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return normalize(context, cancellationToken);
        }

        public static TestClaimsNormalizer Sync(Func<ClaimsNormalizationContext, ClaimsNormalizationResult> normalize) =>
            new((context, _) => ValueTask.FromResult(normalize(context)));

        public static TestClaimsNormalizer Async(
            Func<ClaimsNormalizationContext, CancellationToken, Task<ClaimsNormalizationResult>> normalize) =>
            new((context, cancellationToken) => new ValueTask<ClaimsNormalizationResult>(normalize(context, cancellationToken)));
    }

    private sealed class TestClaimMappingStore(IReadOnlyList<ClaimMappingRule>? rules = null) : IClaimMappingStore
    {
        private int _calls;

        public Func<string, string, CancellationToken, ValueTask<IReadOnlyList<ClaimMappingRule>>>? OnListAsync { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public string? LastTenant { get; private set; }

        public string? LastProvider { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenantId, string provider, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            LastTenant = tenantId;
            LastProvider = provider;
            LastCancellationToken = cancellationToken;
            return OnListAsync?.Invoke(tenantId, provider, cancellationToken) ??
                   ValueTask.FromResult(rules ?? Array.Empty<ClaimMappingRule>());
        }

        public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed record DiscoveryIssuer(string Issuer, string MetadataAddress, string KeyId, RSA SigningRsa)
    {
        public string JwksAddress => new Uri(new Uri(MetadataAddress), "../jwks").AbsoluteUri;
    }

    private sealed class LocalDiscoveryHandler(params DiscoveryIssuer[] issuers) : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, DiscoveryIssuer> _metadata = issuers.ToDictionary(
            issuer => new Uri(issuer.MetadataAddress).AbsolutePath, StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, DiscoveryIssuer> _jwks = issuers.ToDictionary(
            issuer => new Uri(issuer.JwksAddress).AbsolutePath, StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath;
            if (path is not null && _metadata.TryGetValue(path, out var metadataIssuer))
                return Task.FromResult(JsonResponse(new Dictionary<string, string>
                {
                    ["issuer"] = metadataIssuer.Issuer,
                    ["jwks_uri"] = metadataIssuer.JwksAddress
                }));

            if (path is not null && _jwks.TryGetValue(path, out var signingIssuer))
            {
                var parameters = signingIssuer.SigningRsa.ExportParameters(false);
                return Task.FromResult(JsonResponse(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = signingIssuer.KeyId,
                            alg = SecurityAlgorithms.RsaSha256,
                            n = Base64UrlEncoder.Encode(parameters.Modulus!),
                            e = Base64UrlEncoder.Encode(parameters.Exponent!)
                        }
                    }
                }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class NormalizationTestHost : IAsyncDisposable
    {
        private const string KeyId = "test-key";
        private readonly RSA _rsa;
        private readonly RsaSecurityKey _signingKey;
        private readonly IHost _host;
        private readonly HttpClient? _backchannel;

        private NormalizationTestHost(IHost host, RSA rsa, RsaSecurityKey signingKey, TestClaimMappingStore mappings, HttpClient? backchannel)
        {
            _host = host;
            _rsa = rsa;
            _signingKey = signingKey;
            Mappings = mappings;
            _backchannel = backchannel;
            Client = host.GetTestClient();
        }

        public HttpClient Client { get; }

        public TestClaimMappingStore Mappings { get; }

        public static async Task<NormalizationTestHost> StartAsync(
            TestClaimMappingStore? mappings = null,
            Action<JwtBearerEvents>? configureEvents = null,
            TestClaimsNormalizer? normalizer = null,
            Action<HttpContext>? onEndpointReached = null,
            Func<RSA, HttpMessageHandler>? backchannelHandlerFactory = null,
            Action<JwtBearerOptions>? configureBearerOptions = null)
        {
            mappings ??= new TestClaimMappingStore();
            var rsa = RSA.Create(2048);
            var signingKey = new RsaSecurityKey(rsa) { KeyId = KeyId };
            var backchannel = backchannelHandlerFactory is null ? null : new HttpClient(backchannelHandlerFactory(rsa));
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthorization();
                        services.AddPersistenceCore(Tenant);
                        ReplaceClaimMappingStore(services, mappings);
                        if (normalizer is not null)
                            services.AddScoped<IClaimsNormalizer>(_ => normalizer);
                        services.AddFoundationIdentityOidc(options =>
                        {
                            options.NormalizeBearerClaims = true;
                            options.JwtBearerScheme = Scheme;
                            options.Authority = Issuer;
                            options.Audience = Audience;
                            options.ProviderId = Provider;
                            options.TenantId = Tenant;
                            options.RequireHttpsMetadata = false;
                        });
                        if (configureEvents is not null)
                            services.Configure<JwtBearerOptions>(Scheme, options => configureEvents(options.Events));
                        if (backchannel is not null || configureBearerOptions is not null)
                            services.Configure<JwtBearerOptions>(Scheme, options =>
                            {
                                if (backchannel is not null)
                                    options.Backchannel = backchannel;
                                configureBearerOptions?.Invoke(options);
                            });
                        services.PostConfigure<JwtBearerOptions>(Scheme, options =>
                        {
                            if (backchannel is not null)
                                return;
                            options.ConfigurationManager = null;
                            options.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidIssuer = Issuer,
                                ValidateAudience = true,
                                ValidAudience = Audience,
                                ValidateLifetime = true,
                                RequireExpirationTime = true,
                                RequireSignedTokens = true,
                                ValidateIssuerSigningKey = true,
                                IssuerSigningKey = signingKey,
                                ClockSkew = TimeSpan.Zero
                            };
                        });
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapGet("/whoami", async context =>
                        {
                            onEndpointReached?.Invoke(context);
                            await WritePrincipalHeaders(context);
                        }).RequireAuthorization());
                    });
                })
                .Build();

            try
            {
                await host.StartAsync();
                return new NormalizationTestHost(host, rsa, signingKey, mappings, backchannel);
            }
            catch
            {
                try
                {
                    await host.StopAsync();
                }
                finally
                {
                    host.Dispose();
                    backchannel?.Dispose();
                    rsa.Dispose();
                }
                throw;
            }
        }

        public string CreateToken(params Claim[] claims) => CreateToken(Issuer, _signingKey, claims);

        public string CreateToken(string issuer, RsaSecurityKey signingKey, params Claim[] claims)
        {
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer,
                Audience,
                [new Claim("sub", "worker-1"), .. claims],
                now.AddMinutes(-1),
                now.AddMinutes(5),
                new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public Task<HttpResponseMessage> SendBearerAsync(string token) => SendAsync("/whoami", token);

        public Task<HttpResponseMessage> SendQueryTokenAsync(string token) =>
            SendAsync($"/whoami?token={Uri.EscapeDataString(token)}");

        public bool RemoveBearerOptionsFromCache() =>
            _host.Services.GetRequiredService<IOptionsMonitorCache<JwtBearerOptions>>().TryRemove(Scheme);

        public async Task<HttpResponseMessage> SendAsync(string path, string? token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                _host.Dispose();
                _backchannel?.Dispose();
                _rsa.Dispose();
            }
        }

        private static Task WritePrincipalHeaders(HttpContext context)
        {
            var principal = context.User;
            var identity = principal.Identities.Single();
            context.Response.Headers["X-Authentication-Type"] = identity.AuthenticationType ?? string.Empty;
            context.Response.Headers["X-Identity-Count"] = principal.Identities.Count().ToString();
            context.Response.Headers["X-Normalized-Count"] = identity.FindAll(IdentityClaimTypes.Normalized).Count().ToString();
            context.Response.Headers["X-Tenant"] = identity.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty;
            context.Response.Headers["X-Provider"] = identity.FindFirst(IdentityClaimTypes.Provider)?.Value ?? string.Empty;
            context.Response.Headers["X-Permissions"] = string.Join(",", identity.FindAll(IdentityClaimTypes.Permission).Select(claim => claim.Value));
            context.Response.Headers["X-Roles-Count"] = identity.FindAll(IdentityClaimTypes.Role).Count().ToString();
            context.Response.Headers["X-External-Canary-Count"] = identity.FindAll("external-canary").Count().ToString();
            return context.Response.WriteAsync("ok");
        }

    }
}
