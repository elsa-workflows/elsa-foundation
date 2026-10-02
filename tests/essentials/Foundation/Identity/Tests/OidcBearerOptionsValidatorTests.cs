using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class OidcBearerOptionsValidatorTests
{
    [Fact]
    public void Legacy_options_are_unchanged_but_late_opt_in_refuses()
    {
        var (validator, _, configured) = Setup(false);
        Assert.True(validator.Validate(null, configured).Succeeded);
        configured.NormalizeBearerClaims = true;
        Assert.True(validator.Validate(null, configured).Failed);
        var target = new JwtBearerOptions();
        validator.PostConfigure(configured.JwtBearerScheme, target);
        Assert.Null(target.EventsType);
        Assert.True(validator.Validate(configured.JwtBearerScheme, target).Skipped);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("enabled")]
    [InlineData("scheme")]
    [InlineData("authority")]
    [InlineData("audience")]
    [InlineData("provider")]
    [InlineData("tenant")]
    [InlineData("metadata")]
    public void Changed_registration_trust_refuses(string field)
    {
        var (validator, _, configured) = Setup();
        Assert.True(validator.Validate(null, configured).Succeeded);
        switch (field)
        {
            case "mode": configured.NormalizeBearerClaims = false; break;
            case "enabled": configured.Enabled = false; break;
            case "scheme": configured.JwtBearerScheme = "replacement"; break;
            case "authority": configured.Authority = "https://changed.example/"; break;
            case "audience": configured.Audience = "changed"; break;
            case "provider": configured.ProviderId = "changed"; break;
            case "tenant": configured.TenantId = "changed"; break;
            case "metadata": configured.RequireHttpsMetadata = false; break;
        }
        Assert.Equal(OidcBearerOptionsValidator.ConfigurationInvalid, Assert.Single(validator.Validate(null, configured).Failures!));
    }

    [Theory]
    [InlineData("provider-blank")]
    [InlineData("provider-padding")]
    [InlineData("tenant-blank")]
    [InlineData("tenant-padding")]
    [InlineData("scheme-blank")]
    [InlineData("scheme-padding")]
    [InlineData("scheme-collision")]
    [InlineData("audience-blank")]
    [InlineData("authority-relative")]
    [InlineData("authority-http")]
    [InlineData("authority-other")]
    public void Invalid_captured_configuration_refuses(string field)
    {
        var configured = Configuration();
        switch (field)
        {
            case "provider-blank": configured.ProviderId = " "; break;
            case "provider-padding": configured.ProviderId = " oidc"; break;
            case "tenant-blank": configured.TenantId = null; break;
            case "tenant-padding": configured.TenantId = " tenant"; break;
            case "scheme-blank": configured.JwtBearerScheme = " "; break;
            case "scheme-padding": configured.JwtBearerScheme = " jwt"; break;
            case "scheme-collision": configured.JwtBearerScheme = OidcBearerNormalizationEvents.NormalizedAuthenticationType; break;
            case "audience-blank": configured.Audience = " "; break;
            case "authority-relative": configured.Authority = "relative"; break;
            case "authority-http": configured.Authority = "http://issuer.example/"; break;
            case "authority-other": configured.Authority = "ftp://issuer.example/"; configured.RequireHttpsMetadata = false; break;
        }
        var validator = new OidcBearerOptionsValidator(Registration(configured));
        Assert.True(validator.Validate(null, configured).Failed);
    }

    [Fact]
    public void Loopback_http_opt_out_and_absent_audience_fallback_are_valid()
    {
        var configured = Configuration();
        configured.Authority = "http://127.0.0.1:1234/";
        configured.RequireHttpsMetadata = false;
        configured.Audience = null;
        configured.ClientId = "legacy";
        var validator = new OidcBearerOptionsValidator(Registration(configured));
        Assert.True(validator.Validate(null, configured).Succeeded);
    }

    [Fact]
    public void Default_delegate_equality_is_accepted_and_callbacks_are_captured()
    {
        var (validator, registration, configured) = Setup();
        var target = Target(configured);
        validator.PostConfigure("another", target);
        Assert.Null(target.EventsType);
        Assert.True(validator.Validate("another", target).Skipped);
        var callback = target.Events.OnMessageReceived;
        validator.PostConfigure(configured.JwtBearerScheme, target);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), target.EventsType);
        Assert.False(target.IncludeErrorDetails);
        Assert.Equal("Bearer", target.Challenge);
        Assert.Same(callback, registration.CapturedCallbacks.GetValue(target, _ => throw new InvalidOperationException()).MessageReceived);
        Assert.True(validator.Validate(configured.JwtBearerScheme, target).Succeeded);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("subclass")]
    [InlineData("null")]
    [InlineData("challenge")]
    [InlineData("forbidden")]
    public void Unsupported_events_refuse_before_installation(string field)
    {
        var (validator, _, configured) = Setup();
        var target = Target(configured);
        ChangeEvents(target, field);
        var exception = Assert.Throws<OptionsValidationException>(() => validator.PostConfigure(configured.JwtBearerScheme, target));
        Assert.Equal(OidcBearerOptionsValidator.EventsIncompatible, Assert.Single(exception.Failures));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("subclass")]
    [InlineData("null")]
    [InlineData("challenge")]
    [InlineData("forbidden")]
    [InlineData("unregistered-options")]
    [InlineData("message")]
    [InlineData("validated")]
    [InlineData("failed")]
    public void Final_event_replacement_refuses(string field)
    {
        var (validator, _, configured) = Setup();
        var target = Target(configured);
        validator.PostConfigure(configured.JwtBearerScheme, target);
        if (field == "unregistered-options")
        {
            target = Target(configured);
            target.EventsType = typeof(OidcBearerNormalizationEvents);
        }
        else
            ChangeEvents(target, field);
        Assert.True(validator.Validate(configured.JwtBearerScheme, target).Failed);
    }

    [Theory]
    [InlineData("forward-authenticate")]
    [InlineData("forward-challenge")]
    [InlineData("forward-forbid")]
    [InlineData("forward-signin")]
    [InlineData("forward-signout")]
    [InlineData("forward-default")]
    [InlineData("forward-selector")]
    [InlineData("validation-missing")]
    [InlineData("require-audience")]
    [InlineData("issuer")]
    [InlineData("audience-validation")]
    [InlineData("lifetime")]
    [InlineData("expiration")]
    [InlineData("signed")]
    [InlineData("key")]
    [InlineData("raw-type")]
    [InlineData("authority")]
    [InlineData("audience")]
    [InlineData("valid-audience")]
    [InlineData("extra-audience")]
    [InlineData("metadata")]
    [InlineData("details")]
    [InlineData("challenge")]
    [InlineData("normalizer-missing")]
    [InlineData("normalizer-duplicate")]
    [InlineData("store-missing")]
    [InlineData("store-duplicate")]
    [InlineData("events-missing")]
    [InlineData("events-duplicate")]
    [InlineData("events-replaced")]
    [InlineData("events-lifetime")]
    [InlineData("handler-missing")]
    [InlineData("handler-duplicate")]
    [InlineData("handler-replaced")]
    [InlineData("handler-lifetime")]
    public void Final_validation_and_collaborator_changes_refuse(string field)
    {
        var (validator, registration, configured) = Setup();
        var target = Target(configured);
        validator.PostConfigure(configured.JwtBearerScheme, target);
        switch (field)
        {
            case "forward-authenticate": target.ForwardAuthenticate = "other"; break;
            case "forward-challenge": target.ForwardChallenge = "other"; break;
            case "forward-forbid": target.ForwardForbid = "other"; break;
            case "forward-signin": target.ForwardSignIn = "other"; break;
            case "forward-signout": target.ForwardSignOut = "other"; break;
            case "forward-default": target.ForwardDefault = "other"; break;
            case "forward-selector": target.ForwardDefaultSelector = _ => "other"; break;
            case "validation-missing": target.TokenValidationParameters = null!; break;
            case "require-audience": target.TokenValidationParameters.RequireAudience = false; break;
            case "issuer": target.TokenValidationParameters.ValidateIssuer = false; break;
            case "audience-validation": target.TokenValidationParameters.ValidateAudience = false; break;
            case "lifetime": target.TokenValidationParameters.ValidateLifetime = false; break;
            case "expiration": target.TokenValidationParameters.RequireExpirationTime = false; break;
            case "signed": target.TokenValidationParameters.RequireSignedTokens = false; break;
            case "key": target.TokenValidationParameters.ValidateIssuerSigningKey = false; break;
            case "raw-type": target.TokenValidationParameters.AuthenticationType = OidcBearerNormalizationEvents.NormalizedAuthenticationType; break;
            case "authority": target.Authority = "https://changed.example/"; break;
            case "audience": target.Audience = "changed"; break;
            case "valid-audience": target.TokenValidationParameters.ValidAudience = "changed"; break;
            case "extra-audience": target.TokenValidationParameters.ValidAudiences = ["another"]; break;
            case "metadata": target.RequireHttpsMetadata = false; break;
            case "details": target.IncludeErrorDetails = true; break;
            case "challenge": target.Challenge = "Bearer extra"; break;
            default:
                var type = field.StartsWith("normalizer", StringComparison.Ordinal) ? typeof(IClaimsNormalizer) :
                    field.StartsWith("store", StringComparison.Ordinal) ? typeof(IClaimMappingStore) :
                    field.StartsWith("handler", StringComparison.Ordinal) ? typeof(JwtBearerHandler) : typeof(OidcBearerNormalizationEvents);
                var descriptor = registration.Services.Single(d => d.ServiceType == type);
                if (field.EndsWith("duplicate", StringComparison.Ordinal)) registration.Services.Add(descriptor);
                else
                {
                    registration.Services.Remove(descriptor);
                    if (field.EndsWith("replaced", StringComparison.Ordinal)) registration.Services.Add(ServiceDescriptor.Scoped(type, typeof(object)));
                    if (field.EndsWith("lifetime", StringComparison.Ordinal)) registration.Services.Add(ServiceDescriptor.Singleton(type, type));
                }
                break;
        }
        Assert.Equal(OidcBearerOptionsValidator.ConfigurationInvalid, Assert.Single(validator.Validate(configured.JwtBearerScheme, target).Failures!));
    }

    [Fact]
    public void Trusted_host_validation_code_is_preserved_without_claiming_sandboxing()
    {
        var (validator, _, configured) = Setup();
        var target = Target(configured);
        Microsoft.IdentityModel.Tokens.AudienceValidator hostValidator = (_, _, _) => true;
        target.TokenValidationParameters.AudienceValidator = hostValidator;
        validator.PostConfigure(configured.JwtBearerScheme, target);
        Assert.True(validator.Validate(configured.JwtBearerScheme, target).Succeeded);
        Assert.Same(hostValidator, target.TokenValidationParameters.AudienceValidator);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("validated")]
    [InlineData("failed")]
    [InlineData("raw-type")]
    [InlineData("metadata")]
    public void Reload_cannot_change_captured_callbacks_raw_type_or_discovery_address(string field)
    {
        var (validator, _, configured) = Setup();
        var first = Target(configured);
        validator.PostConfigure(configured.JwtBearerScheme, first);
        Assert.True(validator.Validate(configured.JwtBearerScheme, first).Succeeded);
        // An unchanged options recreation is supported; a new trust contribution requires activation.
        var recreated = Target(configured);
        validator.PostConfigure(configured.JwtBearerScheme, recreated);
        Assert.True(validator.Validate(configured.JwtBearerScheme, recreated).Succeeded);
        var changed = Target(configured);
        switch (field)
        {
            case "message": changed.Events.OnMessageReceived = _ => Task.CompletedTask; break;
            case "validated": changed.Events.OnTokenValidated = _ => Task.CompletedTask; break;
            case "failed": changed.Events.OnAuthenticationFailed = _ => Task.CompletedTask; break;
            case "metadata": changed.MetadataAddress = "https://metadata.example.test/changed.json"; break;
            default: changed.TokenValidationParameters.AuthenticationType = "another-raw-type"; break;
        }
        if (field is "raw-type" or "metadata")
        {
            validator.PostConfigure(configured.JwtBearerScheme, changed);
            Assert.True(validator.Validate(configured.JwtBearerScheme, changed).Failed);
        }
        else
            Assert.Throws<OptionsValidationException>(() => validator.PostConfigure(configured.JwtBearerScheme, changed));
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("raw-type")]
    [InlineData("forward")]
    [InlineData("metadata")]
    [InlineData("namespace")]
    public void Actual_options_monitor_recreation_refuses_changed_trust(string field)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClaimMappingStore, MonitorMappingStub>();
        var configured = Configuration();
        services.AddFoundationIdentityOidc(target =>
        {
            target.NormalizeBearerClaims = true;
            target.Authority = configured.Authority;
            target.Audience = configured.Audience;
            target.TenantId = configured.TenantId;
        });
        var changed = false;
        const string initialMetadataAddress = "https://metadata.example.test/issuer-a.json";
        const string changedMetadataAddress = "https://metadata.example.test/issuer-b.json";
        Microsoft.IdentityModel.Tokens.IssuerValidator hostIssuerValidator = (issuer, _, _) => issuer;
        services.Configure<JwtBearerOptions>(configured.JwtBearerScheme, target =>
        {
            if (field == "metadata")
            {
                target.MetadataAddress = changed ? changedMetadataAddress : initialMetadataAddress;
                target.TokenValidationParameters.IssuerValidator = hostIssuerValidator;
            }
            if (!changed) return;
            if (field == "callback") target.Events.OnMessageReceived = _ => Task.CompletedTask;
            if (field == "raw-type") target.TokenValidationParameters.AuthenticationType = "changed-raw";
            if (field == "forward") target.ForwardAuthenticate = "other";
        });
        services.Configure<OidcAuthenticationOptions>(target =>
        {
            if (changed && field == "namespace") target.ProviderId = "changed-provider";
        });
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var initial = monitor.Get(configured.JwtBearerScheme);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), initial.EventsType);
        if (field == "metadata")
        {
            Assert.Equal(initialMetadataAddress, initial.MetadataAddress);
            Assert.Same(hostIssuerValidator, initial.TokenValidationParameters.IssuerValidator);
        }
        provider.GetRequiredService<IOptionsMonitorCache<JwtBearerOptions>>().TryRemove(configured.JwtBearerScheme);
        var unchanged = monitor.Get(configured.JwtBearerScheme);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), unchanged.EventsType);
        if (field == "metadata")
        {
            Assert.Equal(initialMetadataAddress, unchanged.MetadataAddress);
            Assert.Same(hostIssuerValidator, unchanged.TokenValidationParameters.IssuerValidator);
        }
        changed = true;
        if (field == "namespace")
        {
            provider.GetRequiredService<IOptionsMonitorCache<OidcAuthenticationOptions>>().TryRemove(Options.DefaultName);
            Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<OidcAuthenticationOptions>>().CurrentValue);
        }
        else
        {
            provider.GetRequiredService<IOptionsMonitorCache<JwtBearerOptions>>().TryRemove(configured.JwtBearerScheme);
            Assert.Throws<OptionsValidationException>(() => monitor.Get(configured.JwtBearerScheme));
        }
    }

    private sealed class MonitorMappingStub : IClaimMappingStore
    {
        public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenantId, string provider, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Options checks must not query mappings.");
        public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Options checks must not write mappings.");
    }

    private static void ChangeEvents(JwtBearerOptions target, string field)
    {
        switch (field)
        {
            case "null": target.Events = null!; break;
            case "message": target.Events.OnMessageReceived = _ => Task.CompletedTask; break;
            case "validated": target.Events.OnTokenValidated = _ => Task.CompletedTask; break;
            case "failed": target.Events.OnAuthenticationFailed = _ => Task.CompletedTask; break;
            case "type": target.EventsType = typeof(JwtBearerEvents); break;
            case "subclass": target.Events = new ForeignEvents(); break;
            case "challenge": target.Events.OnChallenge = _ => Task.CompletedTask; break;
            case "forbidden": target.Events.OnForbidden = _ => Task.CompletedTask; break;
        }
    }

    private static (OidcBearerOptionsValidator, OidcBearerRegistration, OidcAuthenticationOptions) Setup(bool enabled = true)
    {
        var configured = Configuration();
        configured.NormalizeBearerClaims = enabled;
        var registration = Registration(configured);
        return (new OidcBearerOptionsValidator(registration), registration, configured);
    }

    private static OidcAuthenticationOptions Configuration() => new()
    {
        NormalizeBearerClaims = true, Authority = "https://issuer.example/", Audience = "worker-api", TenantId = "tenant-worker"
    };

    private static OidcBearerRegistration Registration(OidcAuthenticationOptions configured)
    {
        IServiceCollection services = new ServiceCollection();
        // Direct validation inspects descriptors only; collaborators are stubs, never resolved here.
        services.Add(ServiceDescriptor.Scoped(typeof(IClaimsNormalizer), typeof(object)));
        services.Add(ServiceDescriptor.Scoped(typeof(IClaimMappingStore), typeof(object)));
        services.AddScoped<OidcBearerNormalizationEvents>();
        services.AddTransient<JwtBearerHandler>();
        return new(configured.NormalizeBearerClaims, configured.JwtBearerScheme, configured.Enabled, configured.Authority,
            configured.Audience ?? configured.ClientId, configured.ProviderId, configured.TenantId, configured.RequireHttpsMetadata, services);
    }

    private static JwtBearerOptions Target(OidcAuthenticationOptions configured)
    {
        var target = new JwtBearerOptions { Authority = configured.Authority, Audience = configured.Audience, RequireHttpsMetadata = configured.RequireHttpsMetadata };
        target.TokenValidationParameters.ValidAudience = configured.Audience;
        target.TokenValidationParameters.ValidateIssuerSigningKey = true;
        return target;
    }

    private sealed class ForeignEvents : JwtBearerEvents;
}
