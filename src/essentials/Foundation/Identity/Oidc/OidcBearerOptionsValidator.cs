using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

/// <summary>Installs guarded events and validates final named options against registration-time intent.</summary>
public sealed class OidcBearerOptionsValidator(
    OidcBearerRegistration registration) :
    IPostConfigureOptions<JwtBearerOptions>,
    IValidateOptions<JwtBearerOptions>,
    IValidateOptions<OidcAuthenticationOptions>
{
    private readonly object _callbacksGate = new();
    private OidcBearerCallbacks? _frozenCallbacks;
    private string? _frozenRawType;
    private bool _metadataFrozen;
    private string? _frozenMetadataAddress;

    public const string ConfigurationInvalid = "oidc-normalization-configuration-invalid";
    public const string EventsIncompatible = "oidc-normalization-events-incompatible";

    public ValidateOptionsResult Validate(string? name, OidcAuthenticationOptions target)
    {
        if (!registration.NormalizeBearerClaims && !target.NormalizeBearerClaims)
            return ValidateOptionsResult.Success;
        if (!registration.NormalizeBearerClaims || !target.NormalizeBearerClaims || !target.Enabled || target.Enabled != registration.Enabled ||
            !string.Equals(target.JwtBearerScheme, registration.JwtBearerScheme, StringComparison.Ordinal) ||
            !string.Equals(target.Authority, registration.Authority, StringComparison.Ordinal) ||
            !string.Equals(target.Audience ?? target.ClientId, registration.Audience, StringComparison.Ordinal) ||
            !string.Equals(target.ProviderId, registration.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(target.TenantId, registration.TenantId, StringComparison.Ordinal) ||
            target.RequireHttpsMetadata != registration.RequireHttpsMetadata ||
            string.IsNullOrWhiteSpace(target.ProviderId) || target.ProviderId != target.ProviderId.Trim() ||
            string.IsNullOrWhiteSpace(target.TenantId) || target.TenantId != target.TenantId.Trim() ||
            string.IsNullOrWhiteSpace(target.JwtBearerScheme) || target.JwtBearerScheme != target.JwtBearerScheme.Trim() ||
            string.Equals(target.JwtBearerScheme, OidcBearerNormalizationEvents.NormalizedAuthenticationType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.Audience ?? target.ClientId) ||
            !Uri.TryCreate(target.Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttps && (target.RequireHttpsMetadata || authority.Scheme != Uri.UriSchemeHttp)))
            return ValidateOptionsResult.Fail(ConfigurationInvalid);
        return ValidateOptionsResult.Success;
    }

    public void PostConfigure(string? name, JwtBearerOptions target)
    {
        if (!registration.NormalizeBearerClaims || !string.Equals(name, registration.JwtBearerScheme, StringComparison.Ordinal))
            return;
        var defaults = new JwtBearerEvents();
        if (target.EventsType is not null || target.Events is null || target.Events.GetType() != typeof(JwtBearerEvents) ||
            !Equals(target.Events.OnChallenge, defaults.OnChallenge) || !Equals(target.Events.OnForbidden, defaults.OnForbidden))
            throw new OptionsValidationException(name ?? Options.DefaultName, typeof(JwtBearerOptions), [EventsIncompatible]);
        var callbacks = new OidcBearerCallbacks(target.Events.OnMessageReceived, target.Events.OnTokenValidated, target.Events.OnAuthenticationFailed);
        lock (_callbacksGate)
        {
            if (_frozenCallbacks is not null && _frozenCallbacks != callbacks)
                throw new OptionsValidationException(name ?? Options.DefaultName, typeof(JwtBearerOptions), [EventsIncompatible]);
            _frozenCallbacks ??= callbacks;
            registration.CapturedCallbacks.Add(target, callbacks);
        }
        target.EventsType = typeof(OidcBearerNormalizationEvents);
        target.IncludeErrorDetails = false;
        target.Challenge = "Bearer";
    }

    public ValidateOptionsResult Validate(string? name, JwtBearerOptions target)
    {
        if (!registration.NormalizeBearerClaims || !string.Equals(name, registration.JwtBearerScheme, StringComparison.Ordinal))
            return ValidateOptionsResult.Skip;
        var defaults = new JwtBearerEvents();
        if (target.EventsType != typeof(OidcBearerNormalizationEvents) || target.Events is null || target.Events.GetType() != typeof(JwtBearerEvents) ||
            !registration.CapturedCallbacks.TryGetValue(target, out var captured) ||
            !Equals(target.Events.OnMessageReceived, captured.MessageReceived) ||
            !Equals(target.Events.OnTokenValidated, captured.TokenValidated) ||
            !Equals(target.Events.OnAuthenticationFailed, captured.AuthenticationFailed) ||
            !Equals(target.Events.OnChallenge, defaults.OnChallenge) || !Equals(target.Events.OnForbidden, defaults.OnForbidden))
            return ValidateOptionsResult.Fail(EventsIncompatible);
        var validation = target.TokenValidationParameters;
        if (validation is null ||
            !validation.RequireAudience || !validation.ValidateIssuer || !validation.ValidateAudience || !validation.ValidateLifetime ||
            !validation.RequireExpirationTime || !validation.RequireSignedTokens || !validation.ValidateIssuerSigningKey ||
            string.Equals(validation.AuthenticationType, OidcBearerNormalizationEvents.NormalizedAuthenticationType, StringComparison.Ordinal) ||
            target.ForwardAuthenticate is not null || target.ForwardChallenge is not null || target.ForwardForbid is not null ||
            target.ForwardSignIn is not null || target.ForwardSignOut is not null || target.ForwardDefault is not null || target.ForwardDefaultSelector is not null ||
            target.Authority != registration.Authority || target.Audience != registration.Audience ||
            validation.ValidAudience != registration.Audience || validation.ValidAudiences?.Any() == true ||
            target.RequireHttpsMetadata != registration.RequireHttpsMetadata || target.IncludeErrorDetails || target.Challenge != "Bearer" ||
            registration.Services.Count(descriptor => descriptor.ServiceType == typeof(IClaimsNormalizer)) != 1 ||
            registration.Services.Count(descriptor => descriptor.ServiceType == typeof(IClaimMappingStore)) != 1 ||
            registration.Services.Count(descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents)) != 1 ||
            registration.Services.Single(descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents)).ImplementationType != typeof(OidcBearerNormalizationEvents) ||
            registration.Services.Single(descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents)).Lifetime != Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped ||
            registration.Services.Count(descriptor => descriptor.ServiceType == typeof(JwtBearerHandler)) != 1 ||
            registration.Services.Single(descriptor => descriptor.ServiceType == typeof(JwtBearerHandler)).ImplementationType != typeof(JwtBearerHandler) ||
            registration.Services.Single(descriptor => descriptor.ServiceType == typeof(JwtBearerHandler)).Lifetime != Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)
            return ValidateOptionsResult.Fail(ConfigurationInvalid);
        var rawType = validation.AuthenticationType ?? "AuthenticationTypes.Federation";
        lock (_callbacksGate)
        {
            if ((_frozenRawType is not null && _frozenRawType != rawType) ||
                (_metadataFrozen && !string.Equals(_frozenMetadataAddress, target.MetadataAddress, StringComparison.Ordinal)))
                return ValidateOptionsResult.Fail(ConfigurationInvalid);
            _frozenRawType ??= rawType;
            _frozenMetadataAddress = target.MetadataAddress;
            _metadataFrozen = true;
        }
        return ValidateOptionsResult.Success;
    }
}
