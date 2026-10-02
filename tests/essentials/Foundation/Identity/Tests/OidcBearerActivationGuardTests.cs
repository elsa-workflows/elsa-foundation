using System.Text.Encodings.Web;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class OidcBearerActivationGuardTests
{
    [Fact]
    public async Task Both_lifecycles_resolve_the_owned_path_without_querying_collaborators()
    {
        var fixture = new GuardFixture();
        await fixture.Guard.StartAsync(CancellationToken.None);
        await fixture.Guard.InitializeAsync(CancellationToken.None);
        await fixture.Guard.StopAsync(CancellationToken.None);
        Assert.Equal(2, fixture.ScopeFactory.Created);
        Assert.Equal(2, fixture.ScopeFactory.Disposed);
        Assert.Equal(0, fixture.Mappings.Queries);
    }

    [Fact]
    public async Task Legacy_activation_does_not_touch_bearer_or_scope_services()
    {
        var fixture = new GuardFixture();
        fixture.Options.NormalizeBearerClaims = false;
        await fixture.Guard.InitializeAsync(CancellationToken.None);
        Assert.Equal(0, fixture.Schemes.Lookups);
        Assert.Equal(0, fixture.ScopeFactory.Created);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    [InlineData("global")]
    [InlineData("privileged")]
    [InlineData("across")]
    public async Task Invalid_ordinary_scope_refuses_without_a_query_or_rebinding(string kind)
    {
        var fixture = new GuardFixture();
        if (kind == "missing") fixture.Services.Remove(typeof(IPersistenceAccessContextAccessor));
        else
        {
            var context = kind switch
            {
                "mismatch" => PersistenceAccessContext.Scoped(new PersistenceScope("another")),
                "global" => PersistenceAccessContext.Global,
                "privileged" => PersistenceAccessContext.PrivilegedScoped(new PersistenceScope(GuardFixture.Tenant), new PersistenceAccessPurpose("guard-test")),
                _ => PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("guard-test"))
            };
            fixture.Services[typeof(IPersistenceAccessContextAccessor)] = new Accessor(context);
        }
        await AssertFixedRefusal(fixture);
        Assert.Equal(0, fixture.Mappings.Queries);
        Assert.Equal(1, fixture.ScopeFactory.Disposed);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("foreign")]
    public async Task Actual_scheme_must_select_the_stock_handler(string kind)
    {
        var fixture = new GuardFixture();
        fixture.Schemes.Scheme = kind == "none" ? null : new AuthenticationScheme(fixture.Options.JwtBearerScheme, null, typeof(ForeignHandler));
        await AssertFixedRefusal(fixture);
        Assert.Equal(0, fixture.ScopeFactory.Created);
    }

    [Theory]
    [InlineData("normalized-missing")]
    [InlineData("raw-scheme")]
    [InlineData("default-raw-type")]
    [InlineData("explicit-raw-type")]
    public async Task Only_the_owned_normalized_type_can_be_trusted_for_this_path(string kind)
    {
        var fixture = new GuardFixture();
        var types = new HashSet<string>(StringComparer.Ordinal) { OidcBearerNormalizationEvents.NormalizedAuthenticationType };
        switch (kind)
        {
            case "normalized-missing": types.Clear(); break;
            case "raw-scheme": types.Add(fixture.Options.JwtBearerScheme); break;
            case "default-raw-type": types.Add("AuthenticationTypes.Federation"); break;
            default: fixture.Bearer.TokenValidationParameters.AuthenticationType = "raw-external"; types.Add("raw-external"); break;
        }
        fixture.Services[typeof(IOptions<FoundationIdentityOptions>)] = Options.Create(new FoundationIdentityOptions { NormalizedAuthenticationTypes = types });
        await AssertFixedRefusal(fixture);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("normalizer")]
    [InlineData("events")]
    [InlineData("handler")]
    public async Task Missing_owned_services_refuse_with_a_fixed_classification(string kind)
    {
        var fixture = new GuardFixture();
        fixture.Services.Remove(kind switch
        {
            "mapping" => typeof(IClaimMappingStore), "normalizer" => typeof(IClaimsNormalizer),
            "events" => typeof(OidcBearerNormalizationEvents), _ => typeof(JwtBearerHandler)
        });
        await AssertFixedRefusal(fixture);
    }

    [Fact]
    public async Task A_foreign_resolved_handler_refuses_even_when_the_scheme_declares_stock_type()
    {
        var fixture = new GuardFixture();
        fixture.Services[typeof(JwtBearerHandler)] = new ForeignHandler(new Monitor<JwtBearerOptions>(fixture.Bearer));
        await AssertFixedRefusal(fixture);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after-scheme")]
    [InlineData("after-resolution")]
    public async Task Observed_cancellation_propagates_at_activation_boundaries(string stage)
    {
        var fixture = new GuardFixture();
        using var abort = new CancellationTokenSource();
        if (stage == "before") abort.Cancel();
        else if (stage == "after-scheme") fixture.Schemes.OnLookup = abort.Cancel;
        else fixture.ScopeFactory.OnCreate = abort.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Guard.InitializeAsync(abort.Token));
        Assert.Equal(0, fixture.Mappings.Queries);
    }

    private static async Task AssertFixedRefusal(GuardFixture fixture)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Guard.InitializeAsync(CancellationToken.None));
        Assert.Equal(OidcBearerOptionsValidator.ConfigurationInvalid, exception.Message);
    }

    private sealed class GuardFixture
    {
        public const string Tenant = "worker-tenant";
        public OidcAuthenticationOptions Options { get; } = new() { NormalizeBearerClaims = true, TenantId = Tenant };
        public JwtBearerOptions Bearer { get; } = new();
        public Dictionary<Type, object> Services { get; } = [];
        public MappingStub Mappings { get; } = new();
        public SchemeStub Schemes { get; }
        public ScopeFactoryStub ScopeFactory { get; }
        public OidcBearerActivationGuard Guard { get; }

        public GuardFixture()
        {
            var registration = new OidcBearerRegistration(true, Options.JwtBearerScheme, true, null, null, Options.ProviderId, Tenant, true, new ServiceCollection());
            var monitor = new Monitor<OidcAuthenticationOptions>(Options);
            var accessor = new Accessor(PersistenceAccessContext.Scoped(new PersistenceScope(Tenant)));
            var normalizer = new NormalizerStub();
            Services[typeof(IPersistenceAccessContextAccessor)] = accessor;
            Services[typeof(IOptions<FoundationIdentityOptions>)] = Microsoft.Extensions.Options.Options.Create(new FoundationIdentityOptions
            {
                NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { OidcBearerNormalizationEvents.NormalizedAuthenticationType }
            });
            Services[typeof(IClaimMappingStore)] = Mappings;
            Services[typeof(IClaimsNormalizer)] = normalizer;
            Services[typeof(OidcBearerNormalizationEvents)] = new OidcBearerNormalizationEvents(registration, monitor, Mappings, normalizer, accessor);
            Services[typeof(JwtBearerHandler)] = new JwtBearerHandler(new Monitor<JwtBearerOptions>(Bearer), NullLoggerFactory.Instance, UrlEncoder.Default);
            Schemes = new SchemeStub(new AuthenticationScheme(Options.JwtBearerScheme, null, typeof(JwtBearerHandler)));
            ScopeFactory = new ScopeFactoryStub(new ProviderStub(Services));
            Guard = new OidcBearerActivationGuard(registration, monitor, new Monitor<JwtBearerOptions>(Bearer), Schemes, ScopeFactory);
        }
    }

    private sealed class Accessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed class ProviderStub(Dictionary<Type, object> values) : IServiceProvider { public object? GetService(Type type) => values.GetValueOrDefault(type); }
    private sealed class ScopeFactoryStub(IServiceProvider services) : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }
        public Action? OnCreate { get; set; }
        public IServiceScope CreateScope() { Created++; OnCreate?.Invoke(); return new ScopeStub(services, () => Disposed++); }
    }
    private sealed class ScopeStub(IServiceProvider services, Action dispose) : IServiceScope { public IServiceProvider ServiceProvider => services; public void Dispose() => dispose(); }
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T> { public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T, string?> listener) => null; }
    private sealed class MappingStub : IClaimMappingStore
    {
        public int Queries { get; private set; }
        public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenant, string provider, CancellationToken cancellationToken = default) { Queries++; throw new InvalidOperationException("must not query at activation"); }
        public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not write at activation");
    }
    private sealed class NormalizerStub : IClaimsNormalizer { public ValueTask<ClaimsNormalizationResult> NormalizeAsync(ClaimsNormalizationContext context, CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not normalize at activation"); }
    private sealed class ForeignHandler(IOptionsMonitor<JwtBearerOptions> options) : JwtBearerHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default);
    private sealed class SchemeStub(AuthenticationScheme? scheme) : IAuthenticationSchemeProvider
    {
        public AuthenticationScheme? Scheme { get; set; } = scheme;
        public int Lookups { get; private set; }
        public Action? OnLookup { get; set; }
        public Task<AuthenticationScheme?> GetSchemeAsync(string name) { Lookups++; OnLookup?.Invoke(); return Task.FromResult(Scheme); }
        public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() => Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() => Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
        public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public void AddScheme(AuthenticationScheme value) => throw new NotSupportedException();
        public void RemoveScheme(string name) => throw new NotSupportedException();
    }
}
