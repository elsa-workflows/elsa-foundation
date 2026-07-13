using CShells.Lifecycle;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite;

public sealed class PublishingGroundworkSqliteInitializer(
    string connectionString,
    PublishingGroundworkDocumentStoreHolder holder) : IHostedService, IShellInitializer
{
    private static readonly ProviderIdentity Provider = new("groundwork-publishing-sqlite", "1.0.0");

    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (holder.IsInitialized)
            return;
        var store = await SqliteDocumentStoreFactory.CreateAsync(
            connectionString,
            PublishingGroundworkStorageManifest.Create(),
            Provider,
            DocumentStoreAccess.Global,
            cancellationToken: cancellationToken);
        holder.Set(store);
    }
}
