using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretScopeRegistryTests
{
    [Fact]
    public void AddSecrets_composes_enumerable_providers_into_a_stable_read_only_snapshot()
    {
        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretScopeProvider, WorkflowScopeProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretScopeProvider, ConnectionsScopeProvider>());

        using var provider = services.AddSecrets().AddSecrets().BuildServiceProvider();
        var registry = provider.GetRequiredService<ISecretScopeRegistry>();
        var scopes = registry.List();

        Assert.Equal(2, provider.GetServices<ISecretScopeProvider>().Count());
        Assert.Equal(["connections", "database", "Workflow"], scopes.Select(scope => scope.Name));
        Assert.Same(scopes, registry.List());
        Assert.Null(scopes.First().Source);
        Assert.Equal("Workflows", scopes.Last().Source);
        Assert.Throws<NotSupportedException>(() => ((IList<SecretScopeDescriptor>)scopes)[0] = new("other", "Other"));
    }

    [Fact]
    public void AddSecrets_has_an_empty_catalog_when_no_scope_providers_are_registered()
    {
        using var provider = new ServiceCollection().AddSecrets().BuildServiceProvider();

        Assert.Empty(provider.GetServices<ISecretScopeProvider>());
        Assert.Empty(provider.GetRequiredService<ISecretScopeRegistry>().List());
    }

    [Fact]
    public void Case_insensitive_duplicates_fail_deterministically_and_name_both_contributors()
    {
        var first = Assert.Throws<InvalidOperationException>(() =>
            new SecretScopeRegistry([new ConnectionsScopeProvider(), new NuplaneScopeProvider()]));
        var reversed = Assert.Throws<InvalidOperationException>(() =>
            new SecretScopeRegistry([new NuplaneScopeProvider(), new ConnectionsScopeProvider()]));

        Assert.Equal(first.Message, reversed.Message);
        Assert.Contains("Connections, connections", first.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(ConnectionsScopeProvider).FullName!, first.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(NuplaneScopeProvider).FullName!, first.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Display")]
    [InlineData("scope", " ")]
    public void Empty_descriptor_name_or_display_name_is_rejected(string name, string displayName)
    {
        var provider = new InvalidScopeProvider(new SecretScopeDescriptor(name, displayName));

        var exception = Assert.Throws<InvalidOperationException>(() => new SecretScopeRegistry([provider]));

        Assert.Contains(typeof(InvalidScopeProvider).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private sealed class ConnectionsScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("connections", "Connections"),
            new("database", "Database")
        ];
    }

    private sealed class NuplaneScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("Connections", "Nuplane connections")
        ];
    }

    private sealed class WorkflowScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("Workflow", "Workflow", "Workflow secrets", "Workflows")
        ];
    }

    private sealed class InvalidScopeProvider(SecretScopeDescriptor descriptor) : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() => [descriptor];
    }
}
