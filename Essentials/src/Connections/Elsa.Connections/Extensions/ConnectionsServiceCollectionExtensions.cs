using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Expressions;
using Elsa.Connections.Http;
using Elsa.Connections.Options;
using Elsa.Connections.Services;
using Elsa.Expressions.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;

namespace Elsa.Connections.Extensions;

public static class ConnectionsServiceCollectionExtensions
{
    public static IServiceCollection AddConnections(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddOptions<ConnectionsOptions>();
        if (configuration is not null)
        {
            var section = configuration.GetSection(ConnectionsOptions.SectionName);
            services.Configure<ConnectionsOptions>(options => options.PublicBaseUrl = section[nameof(ConnectionsOptions.PublicBaseUrl)]);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConfiguration>(_ => configuration ?? EmptyConfiguration.Instance);
        services.TryAddSingleton<IConnectionRepository, InMemoryConnectionRepository>();
        services.TryAddScoped<IConnectionManager, DefaultConnectionManager>();
        services.TryAddScoped<IConnectionResolver, DefaultConnectionResolver>();
        services.TryAddScoped<IConnectionGrantWriter, DefaultConnectionGrantWriter>();
        services.TryAddSingleton<IConnectionTypeRegistry, ConnectionTypeRegistry>();
        services.TryAddSingleton<IAuthenticationSchemeRegistry, AuthenticationSchemeRegistry>();
        services.TryAddScoped<IConnectionCredentialSourceRegistry, ConnectionCredentialSourceRegistry>();
        services.TryAddSingleton<IAccessTokenSourceRegistry, AccessTokenSourceRegistry>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionTypeProvider, HttpConnectionTypeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, ApiKeySchemeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, BasicSchemeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, BearerSchemeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpRequestAuthenticator, ApiKeyHttpRequestAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpRequestAuthenticator, BasicHttpRequestAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpRequestAuthenticator, BearerHttpRequestAuthenticator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConnectionCredentialSource, SecretConnectionCredentialSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConnectionCredentialSource, NamedCredentialConnectionCredentialSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAccessTokenSource, ConfigurationAccessTokenSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExpressionDescriptorProvider, ConnectionExpressionDescriptorProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IVariableTypeDescriptorProvider, ConnectionTypeDescriptorProvider>());
        services.TryAddScoped<IConnectionPublishTenantAccessor, NullConnectionPublishTenantAccessor>();
        services.TryAddScoped<IConnectionPermissionGate, NullConnectionPermissionGate>();

        return services;
    }

    private sealed class EmptyConfiguration : IConfiguration, IConfigurationSection
    {
        public static readonly EmptyConfiguration Instance = new();

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public string Key => "";
        public string Path => "";
        public string? Value { get; set; }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => EmptyChangeToken.Instance;
        public IConfigurationSection GetSection(string key) => this;
    }

    private sealed class EmptyChangeToken : IChangeToken
    {
        public static readonly EmptyChangeToken Instance = new();

        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
