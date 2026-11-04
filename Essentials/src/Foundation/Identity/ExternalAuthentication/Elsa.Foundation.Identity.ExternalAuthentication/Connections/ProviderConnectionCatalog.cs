using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

/// <summary>One bounded catalog in the existing global provider store, with an isolated explicit access scope.</summary>
internal sealed class ProviderConnectionCatalog(IServiceScopeFactory scopes) : IConnectionCatalogStore
{
    private const string Provider = "external-authentication";
    private const string Kind = "external-connections/v1";
    private const string CatalogSetting = "catalog";
    private const int MaximumCharacters = 1_048_576;

    public async ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        Bind(scope.ServiceProvider, write: false);
        var store = scope.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();
        var found = await store.FindGlobalWithRevisionAsync(Provider, cancellationToken);
        if (found is null) return new ConnectionCatalog([]);
        if (found.Record.Provider != Provider || found.Record.TenantId is not null || found.Record.Kind != Kind || !found.Record.Enabled ||
            !found.Record.Settings.TryGetValue(CatalogSetting, out var json) || json is null || json.Length > MaximumCharacters)
            throw new InvalidOperationException("The stored external connection catalog is invalid or unsupported.");
        try
        {
            var records = JsonSerializer.Deserialize<ConnectionRecord[]>(json)
                ?? throw new InvalidOperationException("The stored external connection catalog is missing.");
            ConnectionSelection.Validate(records);
            return new ConnectionCatalog(records, found.Revision);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The stored external connection catalog cannot be read.");
        }
    }

    public async ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ConnectionSelection.Validate(catalog.Records);
        var json = JsonSerializer.Serialize(catalog.Records);
        if (json.Length > MaximumCharacters)
            throw new InvalidOperationException("The external connection catalog exceeds its storage bound.");
        await using var scope = scopes.CreateAsyncScope();
        Bind(scope.ServiceProvider, write: true);
        var store = scope.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();
        var result = await store.SaveWithRevisionAsync(new ProviderConfigurationRecord(Provider, null, Kind,
            true, false, ProviderCapabilities.ExternalOidcDefault, new Dictionary<string, string> { [CatalogSetting] = json }),
            catalog.Revision, cancellationToken);
        return new CatalogSaveResult(result.Status == IamRevisionSaveStatus.Saved, result.Revision);
    }

    private static void Bind(IServiceProvider provider, bool write) => provider.GetRequiredService<IPersistenceAccessContextBinder>()
        .Bind(write ? PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("external-connection-catalog")) : PersistenceAccessContext.Global);
}
