using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Failing-first registration and result-handler contract tests for the Foundation-owned
/// authorization bridge. These tests intentionally describe the lifecycle contract rather than
/// selecting a winner from an ambiguous service collection.
/// </summary>
public sealed class ReplacementContractRegistrationTests
{
    [Fact]
    public void Defaults_register_the_three_replacement_contracts()
    {
        var services = new ServiceCollection();

        services.AddFoundationIdentityAbstractions();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ClaimsPermissionEvaluator>(provider.GetRequiredService<IPermissionEvaluator>());
        Assert.IsType<PermissionPolicyNameFormatter>(provider.GetRequiredService<IPermissionPolicyNameFormatter>());
        Assert.IsType<CompositePermissionCatalog>(provider.GetRequiredService<IPermissionCatalog>());
    }

    [Fact]
    public void Explicit_replacements_are_symmetric_before_and_after_foundation_registration()
    {
        AssertExplicitEvaluatorReplacement(beforeFoundation: true);
        AssertExplicitEvaluatorReplacement(beforeFoundation: false);
        AssertExplicitFormatterReplacement(beforeFoundation: true);
        AssertExplicitFormatterReplacement(beforeFoundation: false);
        AssertExplicitCatalogReplacement(beforeFoundation: true);
        AssertExplicitCatalogReplacement(beforeFoundation: false);
    }

    [Fact]
    public void Untagged_direct_replacements_before_foundation_are_rejected_immediately()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var services = new ServiceCollection();
            services.AddScoped<IPermissionEvaluator, ReplacementEvaluator>();
            services.AddFoundationIdentityAbstractions();
        });

        Assert.Throws<InvalidOperationException>(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPermissionPolicyNameFormatter, ReplacementFormatter>();
            services.AddFoundationIdentityAbstractions();
        });

        Assert.Throws<InvalidOperationException>(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPermissionCatalog, ReplacementCatalog>();
            services.AddFoundationIdentityAbstractions();
        });
    }

    [Fact]
    public void Direct_untagged_replacements_after_foundation_fail_startup_validation()
    {
        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.AddScoped<IPermissionEvaluator, ReplacementEvaluator>();
        }, typeof(ClaimsPermissionEvaluator), typeof(ReplacementEvaluator));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.AddSingleton<IPermissionPolicyNameFormatter, ReplacementFormatter>();
        }, typeof(PermissionPolicyNameFormatter), typeof(ReplacementFormatter));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.AddSingleton<IPermissionCatalog, ReplacementCatalog>();
        }, typeof(CompositePermissionCatalog), typeof(ReplacementCatalog));
    }

    [Fact]
    public void Removing_the_foundation_registration_and_adding_directly_is_not_a_supported_replacement()
    {
        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionEvaluator>();
            services.AddScoped<IPermissionEvaluator, ReplacementEvaluator>();
        }, typeof(ReplacementEvaluator));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionPolicyNameFormatter>();
            services.AddSingleton<IPermissionPolicyNameFormatter, ReplacementFormatter>();
        }, typeof(ReplacementFormatter));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionCatalog>();
            services.AddSingleton<IPermissionCatalog, ReplacementCatalog>();
        }, typeof(ReplacementCatalog));
    }

    [Fact]
    public void Zero_and_multiple_replacement_descriptors_fail_with_named_diagnostics()
    {
        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionEvaluator>();
        }, typeof(IPermissionEvaluator));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionPolicyNameFormatter>();
        }, typeof(IPermissionPolicyNameFormatter));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionCatalog>();
        }, typeof(IPermissionCatalog));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionEvaluator>();
            services.AddScoped<IPermissionEvaluator, ReplacementEvaluator>();
            services.AddScoped<IPermissionEvaluator, SecondEvaluator>();
        }, typeof(ReplacementEvaluator), typeof(SecondEvaluator));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionPolicyNameFormatter>();
            services.AddSingleton<IPermissionPolicyNameFormatter, ReplacementFormatter>();
            services.AddSingleton<IPermissionPolicyNameFormatter, SecondFormatter>();
        }, typeof(ReplacementFormatter), typeof(SecondFormatter));

        AssertStartupValidationFails(services =>
        {
            services.AddFoundationIdentityAbstractions();
            services.RemoveAll<IPermissionCatalog>();
            services.AddSingleton<IPermissionCatalog, ReplacementCatalog>();
            services.AddSingleton<IPermissionCatalog, SecondCatalog>();
        }, typeof(ReplacementCatalog), typeof(SecondCatalog));
    }

    [Fact]
    public void Replacement_marker_mismatch_names_the_selected_contract_and_descriptors()
    {
        AssertMarkerMismatch(
            services => services.ReplacePermissionEvaluator<ReplacementEvaluator>(),
            services =>
            {
                services.RemoveAll<IPermissionEvaluator>();
                services.AddScoped<IPermissionEvaluator, SecondEvaluator>();
            },
            typeof(ReplacementEvaluator), typeof(SecondEvaluator));

        AssertMarkerMismatch(
            services => services.ReplacePermissionPolicyNameFormatter<ReplacementFormatter>(),
            services =>
            {
                services.RemoveAll<IPermissionPolicyNameFormatter>();
                services.AddSingleton<IPermissionPolicyNameFormatter, SecondFormatter>();
            },
            typeof(ReplacementFormatter), typeof(SecondFormatter));

        AssertMarkerMismatch(
            services => services.ReplacePermissionCatalog<ReplacementCatalog>(),
            services =>
            {
                services.RemoveAll<IPermissionCatalog>();
                services.AddSingleton<IPermissionCatalog, SecondCatalog>();
            },
            typeof(ReplacementCatalog), typeof(SecondCatalog));
    }

    [Fact]
    public void Repeated_foundation_registration_and_repeated_same_replacement_are_idempotent()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        services.AddFoundationIdentityAbstractions();

        using (var provider = services.BuildServiceProvider())
        {
            Assert.IsType<ClaimsPermissionEvaluator>(provider.GetRequiredService<IPermissionEvaluator>());
            Assert.IsType<PermissionPolicyNameFormatter>(provider.GetRequiredService<IPermissionPolicyNameFormatter>());
            Assert.IsType<CompositePermissionCatalog>(provider.GetRequiredService<IPermissionCatalog>());
        }

        Assert.Single(services, x => x.ServiceType == typeof(IPermissionEvaluator));
        Assert.Single(services, x => x.ServiceType == typeof(IPermissionPolicyNameFormatter));
        Assert.Single(services, x => x.ServiceType == typeof(IPermissionCatalog));

        var replaced = new ServiceCollection();
        replaced.ReplacePermissionEvaluator<ReplacementEvaluator>();
        replaced.ReplacePermissionEvaluator<ReplacementEvaluator>();
        replaced.ReplacePermissionPolicyNameFormatter<ReplacementFormatter>();
        replaced.ReplacePermissionPolicyNameFormatter<ReplacementFormatter>();
        replaced.ReplacePermissionCatalog<ReplacementCatalog>();
        replaced.ReplacePermissionCatalog<ReplacementCatalog>();
        replaced.AddFoundationIdentityAbstractions();

        using var replacedProvider = replaced.BuildServiceProvider();
        Assert.IsType<ReplacementEvaluator>(replacedProvider.GetRequiredService<IPermissionEvaluator>());
        Assert.IsType<ReplacementFormatter>(replacedProvider.GetRequiredService<IPermissionPolicyNameFormatter>());
        Assert.IsType<ReplacementCatalog>(replacedProvider.GetRequiredService<IPermissionCatalog>());
    }

    [Fact]
    public async Task Result_handler_registration_uses_default_when_the_host_has_no_prior_descriptor()
    {
        var authentication = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(authentication);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        Assert.Single(services, x => x.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        var permissionPolicy = await GetPermissionPolicyAsync(provider);
        var context = new DefaultHttpContext { RequestServices = provider, User = TrustedPrincipal() };
        await handler.HandleAsync(_ => Task.CompletedTask, context, permissionPolicy, PolicyAuthorizationResult.Challenge());
        await handler.HandleAsync(_ => Task.CompletedTask, context, permissionPolicy, PermissionForbid(permissionPolicy));

        Assert.Equal(1, authentication.ChallengeCalls);
        Assert.Equal(1, authentication.ForbidCalls);
    }

    [Fact]
    public async Task Result_handler_captures_one_prior_implementation_type_factory_or_instance()
    {
        await AssertCapturedResultHandler((services, state) =>
        {
            services.AddSingleton(state);
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, RecordingResultHandler>();
        });
        await AssertCapturedResultHandler((services, state) =>
        {
            services.AddSingleton(state);
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(_ => new RecordingResultHandler(state));
        });
        await AssertCapturedResultHandler((services, state) =>
        {
            services.AddSingleton(state);
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(new RecordingResultHandler(state));
        });
    }

    [Fact]
    public void Multiple_prior_result_handlers_fail_immediately_and_name_every_descriptor()
    {
        AssertMultiplePriorResultHandlers(services =>
        {
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, FirstResultHandler>();
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, SecondResultHandler>();
        }, typeof(FirstResultHandler), typeof(SecondResultHandler));

        AssertMultiplePriorResultHandlers(services =>
        {
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(_ => new FirstResultHandler());
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(_ => new SecondResultHandler());
        });

        AssertMultiplePriorResultHandlers(services =>
        {
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(new FirstResultHandler());
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(new SecondResultHandler());
        }, typeof(FirstResultHandler), typeof(SecondResultHandler));
    }

    [Fact]
    public void Repeated_foundation_registration_installs_one_result_handler_and_host_after_is_a_startup_conflict()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        services.AddFoundationIdentityAbstractions();

        Assert.Single(services, x => x.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, FirstResultHandler>();
        AssertStartupValidationFails(services, typeof(FirstResultHandler), typeof(IAuthorizationMiddlewareResultHandler));
    }

    [Fact]
    public void Repeated_foundation_registration_installs_one_policy_provider_and_host_after_is_a_startup_conflict()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        services.AddFoundationIdentityAbstractions();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IAuthorizationPolicyProvider));

        services.AddSingleton<IAuthorizationPolicyProvider, LatePolicyProvider>();
        AssertStartupValidationFails(services, typeof(LatePolicyProvider), typeof(IAuthorizationPolicyProvider));
    }

    [Fact]
    public void Missing_policy_provider_marker_fails_validation_and_cannot_be_silently_rewrapped()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        var marker = Assert.Single(services, descriptor =>
            descriptor.ServiceType.Name == "FoundationIdentityPolicyProviderRegistration");
        services.Remove(marker);

        using (var provider = services.BuildServiceProvider())
        {
            var exception = Assert.Throws<OptionsValidationException>(
                provider.GetRequiredService<IStartupValidator>().Validate);
            Assert.Contains("Markers: 0", exception.Message, StringComparison.Ordinal);
        }

        var registrationException = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationIdentityAbstractions());
        Assert.Contains("without its Foundation registration marker", registrationException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_result_handler_marker_fails_validation_and_cannot_be_silently_rewrapped()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions();
        var marker = Assert.Single(services, descriptor =>
            descriptor.ServiceType.Name == "FoundationIdentityResultHandlerRegistration");
        services.Remove(marker);

        using (var provider = services.BuildServiceProvider())
        {
            var exception = Assert.Throws<OptionsValidationException>(
                provider.GetRequiredService<IStartupValidator>().Validate);
            Assert.Contains("Markers: 0", exception.Message, StringComparison.Ordinal);
        }

        var registrationException = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationIdentityAbstractions());
        Assert.Contains("without its Foundation registration marker", registrationException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" trusted")]
    [InlineData("trusted ")]
    public void Invalid_normalized_authentication_types_fail_startup_validation(string authenticationType)
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { authenticationType });

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);
        Assert.Contains("Normalized authentication types", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Captured_result_handler_preserves_challenge_forbid_success_and_unrelated_policy_delegation()
    {
        var captured = new RecordingResultHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(captured);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var permissionPolicy = await GetPermissionPolicyAsync(provider);
        var unrelatedPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await InvokeAsync(handler, provider, UntrustedPrincipal(), permissionPolicy, PolicyAuthorizationResult.Challenge());
        await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PermissionForbid(permissionPolicy));
        await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PolicyAuthorizationResult.Success());
        await InvokeAsync(handler, provider, UntrustedPrincipal(), unrelatedPolicy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(4, captured.Results.Count);
        Assert.True(captured.Results[0].Challenged);
        Assert.True(captured.Results[1].Forbidden);
        Assert.True(captured.Results[2].Succeeded);
        Assert.True(captured.Results[3].Forbidden);
        Assert.Equal(1, captured.NextCalls);
    }

    [Fact]
    public async Task Permission_result_handler_rewrites_only_authenticated_untrusted_failures_to_challenge()
    {
        var captured = new RecordingResultHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(captured);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var permissionPolicy = await GetPermissionPolicyAsync(provider);

        await InvokeAsync(handler, provider, new ClaimsPrincipal(new ClaimsIdentity()), permissionPolicy, PolicyAuthorizationResult.Challenge());
        Assert.True(captured.Results[^1].Challenged);
        await InvokeAsync(handler, provider, new ClaimsPrincipal(new ClaimsIdentity()), permissionPolicy, PermissionForbid(permissionPolicy));
        Assert.True(captured.Results[^1].Forbidden);

        var authenticatedUntrustedPrincipals = new[]
        {
            UntrustedPrincipal(),
            UnmarkedAuthenticatedPrincipal(),
            new ClaimsPrincipal(new ClaimsIdentity[]
            {
                TrustedIdentity(),
                TrustedIdentity()
            })
        };

        foreach (var principal in authenticatedUntrustedPrincipals)
        {
            await InvokeAsync(handler, provider, principal, permissionPolicy, PermissionForbid(permissionPolicy));
            Assert.True(captured.Results[^1].Challenged);
        }

        await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PermissionForbid(permissionPolicy));
        Assert.True(captured.Results[^1].Forbidden);

        await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PolicyAuthorizationResult.Success());
        Assert.True(captured.Results[^1].Succeeded);
    }

    [Fact]
    public async Task Untrusted_permission_failures_are_independent_of_identity_order_and_endpoint_routing()
    {
        var captured = new RecordingResultHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(captured);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var permissionPolicy = await GetPermissionPolicyAsync(provider);

        var anonymous = new ClaimsIdentity();
        var untrusted = UntrustedIdentity();
        var firstAnonymous = new ClaimsPrincipal(new[] { anonymous, untrusted });
        var firstUntrusted = new ClaimsPrincipal(new[] { untrusted, new ClaimsIdentity() });

        await InvokeAsync(handler, provider, firstAnonymous, permissionPolicy, PermissionForbid(permissionPolicy), clearEndpoint: true);
        await InvokeAsync(handler, provider, firstUntrusted, permissionPolicy, PermissionForbid(permissionPolicy), clearEndpoint: true);

        Assert.All(captured.Results, result => Assert.True(result.Challenged));
    }

    [Fact]
    public async Task Default_result_handler_maps_permission_outcomes_to_challenge_forbid_success_and_unrelated_forbid()
    {
        var authentication = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(authentication);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var permissionPolicy = await GetPermissionPolicyAsync(provider);
        var unrelatedPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        await InvokeAsync(handler, provider, UntrustedPrincipal(), permissionPolicy, PermissionForbid(permissionPolicy));
        await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PermissionForbid(permissionPolicy));
        var success = await InvokeAsync(handler, provider, TrustedPrincipal(), permissionPolicy, PolicyAuthorizationResult.Success());
        await InvokeAsync(handler, provider, UntrustedPrincipal(), unrelatedPolicy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(1, authentication.ChallengeCalls);
        Assert.Equal(2, authentication.ForbidCalls);
        Assert.Equal(1, success.NextCalls);
    }

    private static void AssertExplicitEvaluatorReplacement(bool beforeFoundation)
    {
        var services = new ServiceCollection();
        if (beforeFoundation)
            services.ReplacePermissionEvaluator<ReplacementEvaluator>();

        services.AddFoundationIdentityAbstractions();

        if (!beforeFoundation)
            services.ReplacePermissionEvaluator<ReplacementEvaluator>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ReplacementEvaluator>(provider.GetRequiredService<IPermissionEvaluator>());
    }

    private static void AssertExplicitFormatterReplacement(bool beforeFoundation)
    {
        var services = new ServiceCollection();
        if (beforeFoundation)
            services.ReplacePermissionPolicyNameFormatter<ReplacementFormatter>();

        services.AddFoundationIdentityAbstractions();

        if (!beforeFoundation)
            services.ReplacePermissionPolicyNameFormatter<ReplacementFormatter>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ReplacementFormatter>(provider.GetRequiredService<IPermissionPolicyNameFormatter>());
    }

    private static void AssertExplicitCatalogReplacement(bool beforeFoundation)
    {
        var services = new ServiceCollection();
        if (beforeFoundation)
            services.ReplacePermissionCatalog<ReplacementCatalog>();

        services.AddFoundationIdentityAbstractions();

        if (!beforeFoundation)
            services.ReplacePermissionCatalog<ReplacementCatalog>();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ReplacementCatalog>(provider.GetRequiredService<IPermissionCatalog>());
    }

    private static void AssertStartupValidationFails(Action<IServiceCollection> configure, params Type[] expectedTypes)
    {
        var services = new ServiceCollection();
        configure(services);
        AssertStartupValidationFails(services, expectedTypes);
    }

    private static void AssertStartupValidationFails(IServiceCollection services, params Type[] expectedTypes)
    {
        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        foreach (var expectedType in expectedTypes)
            Assert.Contains(expectedType.FullName!, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertMultiplePriorResultHandlers(Action<IServiceCollection> configure, params Type[] expectedTypes)
    {
        var services = new ServiceCollection();
        configure(services);

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddFoundationIdentityAbstractions());

        Assert.Contains(typeof(IAuthorizationMiddlewareResultHandler).FullName!, exception.Message, StringComparison.Ordinal);
        foreach (var expectedType in expectedTypes)
            Assert.Contains(expectedType.FullName!, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertMarkerMismatch(
        Action<IServiceCollection> registerReplacement,
        Action<IServiceCollection> replaceDescriptor,
        params Type[] expectedTypes)
    {
        var services = new ServiceCollection();
        registerReplacement(services);
        services.AddFoundationIdentityAbstractions();
        replaceDescriptor(services);
        AssertStartupValidationFails(services, expectedTypes);
    }

    private static async Task AssertCapturedResultHandler(
        Action<IServiceCollection, ResultHandlerState> registerHostHandler)
    {
        var state = new ResultHandlerState();
        var services = new ServiceCollection();
        registerHostHandler(services, state);
        services.AddFoundationIdentityAbstractions(ConfigureTrustedAuthenticationType);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        Assert.NotSame(state.Handler, handler);

        var policy = await GetPermissionPolicyAsync(provider);
        await InvokeAsync(handler, provider, TrustedPrincipal(), policy, PolicyAuthorizationResult.Challenge());
        await InvokeAsync(handler, provider, TrustedPrincipal(), policy, PermissionForbid(policy));
        await InvokeAsync(handler, provider, TrustedPrincipal(), policy, PolicyAuthorizationResult.Success());

        Assert.NotNull(state.Handler);
        var actualHostHandler = state.Handler;
        Assert.Equal(3, actualHostHandler.Results.Count);
        Assert.True(actualHostHandler.Results[0].Challenged);
        Assert.True(actualHostHandler.Results[1].Forbidden);
        Assert.True(actualHostHandler.Results[2].Succeeded);
        Assert.Equal(1, actualHostHandler.NextCalls);
    }

    private static async Task<AuthorizationPolicy> GetPermissionPolicyAsync(IServiceProvider provider)
    {
        var codec = provider.GetRequiredService<IPermissionPolicyCodec>();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(codec.Format(PermissionPolicyDescriptor.Single("shell-management.read")));
        Assert.NotNull(policy);
        return policy!;
    }

    private static async Task<Invocation> InvokeAsync(
        IAuthorizationMiddlewareResultHandler handler,
        IServiceProvider provider,
        ClaimsPrincipal principal,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult result,
        bool clearEndpoint = false)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            User = principal
        };

        if (clearEndpoint)
            context.SetEndpoint(null);

        var nextCalls = 0;
        await handler.HandleAsync(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        }, context, policy, result);

        return new Invocation(context.Response.StatusCode, nextCalls);
    }

    private static PolicyAuthorizationResult PermissionForbid(AuthorizationPolicy policy) =>
        PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(policy.Requirements));

    private static void ConfigureTrustedAuthenticationType(FoundationIdentityOptions options) =>
        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { TrustedAuthenticationType };

    private static ClaimsPrincipal TrustedPrincipal() => new(TrustedIdentity());

    private static ClaimsIdentity TrustedIdentity() => new ClaimsIdentity(
        new[] { new Claim(IdentityClaimTypes.Normalized, "v1") },
        TrustedAuthenticationType);

    private static ClaimsPrincipal UntrustedPrincipal() => new(UntrustedIdentity());

    private static ClaimsIdentity UntrustedIdentity() => new ClaimsIdentity(
        new[] { new Claim(IdentityClaimTypes.Normalized, "v1") },
        "untrusted-provider");

    private static ClaimsPrincipal UnmarkedAuthenticatedPrincipal() => new(new ClaimsIdentity(
        [new Claim(IdentityClaimTypes.Permission, "shell-management.read")],
        "unmarked-provider"));

    private const string TrustedAuthenticationType = "trusted-provider";

    private sealed record Invocation(int StatusCode, int NextCalls);

    private sealed class ReplacementEvaluator : IPermissionEvaluator
    {
        public ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PermissionEvaluationResult.Denied());
    }

    private sealed class SecondEvaluator : IPermissionEvaluator
    {
        public ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PermissionEvaluationResult.Denied());
    }

    private sealed class ReplacementFormatter : IPermissionPolicyNameFormatter
    {
        public string Format(string permission) => permission;

        public bool TryParse(string policyName, out string permission)
        {
            permission = policyName;
            return true;
        }
    }

    private sealed class SecondFormatter : IPermissionPolicyNameFormatter
    {
        public string Format(string permission) => permission;

        public bool TryParse(string policyName, out string permission)
        {
            permission = policyName;
            return true;
        }
    }

    private sealed class ReplacementCatalog : IPermissionCatalog
    {
        public IReadOnlyCollection<Permission> List() => [];

        public Permission? Find(string key) => null;
    }

    private sealed class SecondCatalog : IPermissionCatalog
    {
        public IReadOnlyCollection<Permission> List() => [];

        public Permission? Find(string key) => null;
    }

    private sealed class LatePolicyProvider : IAuthorizationPolicyProvider
    {
        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) => Task.FromResult<AuthorizationPolicy?>(null);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            Task.FromResult(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);
    }

    private class RecordingResultHandler : IAuthorizationMiddlewareResultHandler
    {
        public RecordingResultHandler(ResultHandlerState? state = null) => state?.Handler = this;

        public List<PolicyAuthorizationResult> Results { get; } = [];

        public int NextCalls { get; private set; }

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            Results.Add(authorizeResult);

            if (authorizeResult.Succeeded)
            {
                NextCalls++;
                return next(context);
            }

            context.Response.StatusCode = authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }

    private sealed class ResultHandlerState
    {
        public RecordingResultHandler? Handler { get; set; }
    }

    private sealed class FirstResultHandler : RecordingResultHandler;

    private sealed class SecondResultHandler : RecordingResultHandler;

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int ChallengeCalls { get; private set; }

        public int ForbidCalls { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ChallengeCalls++;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ForbidCalls++;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }
}
