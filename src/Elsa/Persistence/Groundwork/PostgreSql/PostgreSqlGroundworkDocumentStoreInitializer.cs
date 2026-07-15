using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Groundwork.PostgreSql.PhysicalStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.PostgreSql;

/// <summary>
/// Admits the exact host-selected PostgreSQL schema and then exposes one physical document store.
/// Runtime startup only inspects durable history/live state; it never applies or repairs schema.
/// </summary>
public sealed class PostgreSqlGroundworkDocumentStoreInitializer(
    string connectionString,
    IServiceScopeFactory scopeFactory,
    IServiceProvider serviceProvider,
    GroundworkDocumentStoreHolder holder) : IHostedService, IShellInitializer
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => EnsureInitializedAsync(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => EnsureInitializedAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            await using var scope = scopeFactory.CreateAsyncScope();
            var source = await scope.ServiceProvider
                .GetRequiredService<GroundworkStorageCompositionFactory>()
                .CreateSourceAsync(
                    new GroundworkProviderCapabilitySnapshot(
                        PostgreSqlGroundworkCapabilities.Runtime(),
                        new GroundworkProviderTopologySnapshot(
                            PostgreSqlGroundworkCapabilities.Provider.Name,
                            "postgresql-server",
                            new HashSet<string>(StringComparer.Ordinal)),
                        []),
                    PostgreSqlGroundworkCapabilities.PhysicalNames,
                    cancellationToken);

            var admission = await source.InspectRuntimeAdmissionAsync(
                new PostgreSqlPhysicalSchemaExecutor(connectionString),
                cancellationToken);
            if (!admission.IsReady)
                throw new GroundworkRuntimeSchemaAdmissionException(admission);

            var workflowRoute = source.PhysicalTarget.Routes.SingleOrDefault(route =>
                route.StorageUnit.Value == ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);
            if (workflowRoute is not null)
            {
                var historyQuery = serviceProvider.GetRequiredService<IGroundworkWorkflowExecutionStatePageQuery>();
                historyQuery.Bind(workflowRoute);
                await historyQuery.PrepareAsync(cancellationToken);
            }

            if (!holder.IsInitialized)
            {
                var manifest = source.CreateManifest();
                var store = new PostgreSqlPhysicalDocumentStore(
                    connectionString,
                    manifest,
                    source.PhysicalTarget.Routes,
                    DocumentStoreAccess.Global);
                var boundedStore = new GroundworkBoundedDocumentStoreRouter(
                    source.PhysicalTarget.Routes.Select(route =>
                        KeyValuePair.Create<string, IBoundedDocumentStore>(
                            route.StorageUnit.Value,
                            PostgreSqlPhysicalQueryRuntime.Create(
                                store,
                                manifest,
                                route,
                                source.PhysicalTarget.Provider))));
                holder.TrySet(store, boundedStore);
            }

            initialized = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkRuntimeSchemaAdmissionException)
        {
            throw;
        }
        catch (GroundworkStorageCompositionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw SanitizedFailure(exception);
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static InvalidOperationException SanitizedFailure(Exception exception) => new(
        $"PostgreSQL Groundwork runtime initialization failed ({exception.GetType().Name}); provider and connection details were suppressed.");
}
