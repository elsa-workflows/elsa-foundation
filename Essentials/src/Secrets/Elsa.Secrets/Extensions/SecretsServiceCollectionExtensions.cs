using CShells.Lifecycle;
using Elsa.Expressions.Core.Contracts;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Options;
using Elsa.Secrets.Expressions;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Elsa.Secrets.Stores;
using Elsa.Secrets.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Secrets.Extensions;

public static class SecretsServiceCollectionExtensions
{
    public static IServiceCollection AddSecrets(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddOptions<SecretsOptions>();
        services.TryAddSingleton<IConfiguration>(_ => configuration ?? EmptyConfiguration.Instance);

        if (configuration is not null)
        {
            var section = configuration.GetSection(SecretsOptions.SectionName);
            services.Configure<SecretsOptions>(options =>
            {
                options.EncryptionKey = section[nameof(SecretsOptions.EncryptionKey)];
                var storeName = section[nameof(SecretsOptions.DefaultWritableStoreName)];
                if (!string.IsNullOrWhiteSpace(storeName))
                    options.DefaultWritableStoreName = storeName;
            });
        }

        services.AddOptions<WritableSecretStoreOptions>()
            .Configure<IOptions<SecretsOptions>>((options, secrets) =>
                options.DefaultWritableStoreName = DefaultWritableSecretStore.ResolveName(secrets.Value.DefaultWritableStoreName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecretNameValidator, DefaultSecretNameValidator>();
        services.TryAddSingleton<ISecretKeyRing, DefaultSecretKeyRing>();
        services.TryAddSingleton<ISecretValueProtector, DefaultSecretValueProtector>();
        services.TryAddSingleton<ISecretRepository, InMemorySecretRepository>();
        services.TryAddSingleton<ISecretAuditSink>(sp =>
            new LoggingSecretAuditSink((sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger<LoggingSecretAuditSink>()));
        services.TryAddSingleton<SecretLifecyclePolicy>();
        services.TryAddSingleton<SecretModelMapper>();
        // These operation facades consume the host-selected repository, which is scoped for durable providers.
        // Keeping the facades scoped prevents one request from capturing another request's access context.
        services.TryAddScoped<ISecretManager, DefaultSecretManager>();
        services.TryAddScoped<ISecretValueResolver, DefaultSecretValueResolver>();
        services.TryAddSingleton<ISecretStoreRegistry, SecretStoreRegistry>();
        services.TryAddSingleton<ISecretScopeRegistry, SecretScopeRegistry>();
        services.TryAddSingleton<ISecretTypeRegistry, SecretTypeRegistry>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore, EncryptedSecretStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore, ConfigurationSecretStore>());
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DefaultWritableSecretStoreValidator)))
        {
            services.AddShellInitializer<DefaultWritableSecretStoreValidator>(LifecyclePhase.Prepare, 0);
            // AddShellInitializer registers the initializer transiently; this last-wins registration makes the shell and
            // the hosted-service paths resolve one instance.
            services.AddSingleton<DefaultWritableSecretStoreValidator>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DefaultWritableSecretStoreValidator>());
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretTypeProvider, TextSecretTypeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretTypeProvider, RsaKeySecretTypeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretTypeProvider, X509CertificateSecretTypeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExpressionDescriptorProvider, SecretExpressionDescriptorProvider>());

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
