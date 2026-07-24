extern alias DiagnosticsGroundwork;

using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Groundwork.DiagnosticRecords;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using DiagnosticsDeploymentSchema =
    DiagnosticsGroundwork::Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkDeploymentSchema;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Readiness is an admission boundary: a selected durable diagnostics feature must surface its provider's
/// schema or operational failure. It must never replace that failure with an empty read or an in-memory store.
/// </summary>
public sealed class DiagnosticsPersistenceReadinessTests
{
    public static TheoryData<DiagnosticsReadinessFailure, bool> Failures =>
        new()
        {
            { new MissingDiagnosticsSchemaFailure(), false },
            { new DiagnosticsSchemaDriftFailure(), true },
            { new DiagnosticsProviderUnavailableFailure(), false }
        };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Durable_readiness_failures_remain_visible_and_never_degrade_to_an_empty_or_in_memory_store(
        DiagnosticsReadinessFailure failure,
        bool useShellLifecycle)
    {
        var services = ConfigureDurableStructuredLogs(failure);
        await using var provider = services.BuildServiceProvider();

        var startupFailure = await Assert.ThrowsAnyAsync<DiagnosticsReadinessFailure>(async () =>
        {
            if (useShellLifecycle)
                await StartShellAsync(provider);
            else
                await StartHostAsync(provider);
        });

        Assert.Same(failure, startupFailure);
        Assert.Equal(1, ((ThrowingDiagnosticRecordStoreSessionFactory)provider
            .GetRequiredService<IDiagnosticRecordStoreSessionFactory>()).OpenCount);

        await AssertDoesNotMaskFailureAsAnEmptyStructuredLogStoreAsync(provider, failure);
    }

    [Fact]
    public void Both_durable_features_replace_only_their_own_default_store_and_never_select_a_provider_leaf()
    {
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        new StructuredLogsFeature().ConfigureServices(services);

        ConfigureFeature<GroundworkOpenTelemetryStore>(services, "GroundworkOpenTelemetryPersistenceFeature");
        ConfigureFeature<GroundworkStructuredLogStore>(services, "GroundworkStructuredLogsPersistenceFeature");

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOpenTelemetryStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStructuredLogStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkOpenTelemetryStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryOpenTelemetryStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryStructuredLogStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Persistence.Groundwork.DependencyInjection.GroundworkProviderLeafSelection");
    }

    [Fact]
    public async Task Selected_durable_stores_resolve_and_start_through_the_shared_host_lifecycle()
    {
        var diagnosticSessions = new RecordingDiagnosticRecordStoreSessionFactory();
        var documents = new RecordingDocumentSessionSource();
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        new StructuredLogsFeature().ConfigureServices(services);
        services.AddSingleton<IDiagnosticRecordDeploymentManifestSource>(new DiagnosticsDeploymentSchema());
        services.AddSingleton<IDiagnosticRecordStoreSessionFactory>(diagnosticSessions);
        services.AddSingleton<IGroundworkStoreSessionSource>(documents);
        new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);
        new GroundworkStructuredLogsPersistenceFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<GroundworkOpenTelemetryStore>(provider.GetRequiredService<IOpenTelemetryStore>());
        Assert.IsType<GroundworkStructuredLogStore>(provider.GetRequiredService<IStructuredLogStore>());

        await StartHostAsync(provider);

        Assert.Equal(2, diagnosticSessions.Sessions.Count);
        Assert.Equal(5, diagnosticSessions.Sessions.Sum(session => session.OpenedStreams.Count));
        Assert.Equal(1, documents.OpenCount);

        foreach (var hostedService in provider.GetServices<IHostedService>())
            await hostedService.StopAsync(CancellationToken.None);

        Assert.All(diagnosticSessions.Sessions, session => Assert.Equal(1, session.DisposeCount));
        Assert.Equal(1, documents.Lease.DisposeCount);
    }

    [Fact]
    public async Task OpenTelemetry_defers_provider_access_until_lifecycle_and_rethrows_the_exact_admission_failure()
    {
        var failure = new DiagnosticsProviderUnavailableFailure();
        var diagnosticSessions = new ThrowingDiagnosticRecordStoreSessionFactory(failure);
        var documents = new UnusedDocumentSessionSource();
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        services.AddSingleton<IDiagnosticRecordDeploymentManifestSource>(new DiagnosticsDeploymentSchema());
        services.AddSingleton<IDiagnosticRecordStoreSessionFactory>(diagnosticSessions);
        services.AddSingleton<IGroundworkStoreSessionSource>(documents);
        new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IOpenTelemetryStore>();
        Assert.Equal(0, diagnosticSessions.OpenCount);
        Assert.Equal(0, documents.OpenCount);

        var resource = Assert.IsAssignableFrom<IDiagnosticsPersistenceStartupResource>(store);
        var startupFailure = await Assert.ThrowsAnyAsync<DiagnosticsReadinessFailure>(async () =>
            await resource.AcquireAsync());
        Assert.Same(failure, startupFailure);
        Assert.Equal(1, diagnosticSessions.OpenCount);
        Assert.Equal(0, documents.OpenCount);

        var repeatedFailure = await Assert.ThrowsAnyAsync<DiagnosticsReadinessFailure>(async () =>
            await resource.AcquireAsync());
        Assert.Same(failure, repeatedFailure);
        Assert.Equal(1, diagnosticSessions.OpenCount);

        var queryFailure = await Record.ExceptionAsync(() =>
            store.QueryResourcesAsync(new OpenTelemetryResourceFilter()).AsTask());
        Assert.NotNull(queryFailure);
        Assert.IsNotType<InMemoryOpenTelemetryStore>(store);
    }

    [Fact]
    public async Task OpenTelemetry_startup_opens_the_exact_scope_and_streams_then_releases_both_sessions_on_shutdown()
    {
        var diagnosticSessions = new RecordingDiagnosticRecordStoreSessionFactory();
        var documents = new RecordingDocumentSessionSource();
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        services.AddSingleton<IDiagnosticRecordDeploymentManifestSource>(new DiagnosticsDeploymentSchema());
        services.AddSingleton<IDiagnosticRecordStoreSessionFactory>(diagnosticSessions);
        services.AddSingleton<IGroundworkStoreSessionSource>(documents);
        new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var binding = provider.GetRequiredService<GroundworkOpenTelemetryBinding>();
        await StartHostAsync(provider);

        var session = Assert.Single(diagnosticSessions.Sessions);
        Assert.Equal(binding.TenantId, diagnosticSessions.Scope!.Value.TenantId);
        Assert.Equal(binding.ScopeId, diagnosticSessions.Scope.Value.ScopeId);
        Assert.Equal(
            [binding.TraceStreamId, binding.SpanStreamId, binding.MetricPointStreamId, binding.LogStreamId],
            session.OpenedStreams.Select(stream => stream.Value));
        Assert.Equal(binding.DocumentStorageScope, documents.Access!.Scope);

        foreach (var hostedService in provider.GetServices<IHostedService>())
            await hostedService.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.DisposeCount);
        Assert.Equal(1, documents.Lease.DisposeCount);
    }

    private static ServiceCollection ConfigureDurableStructuredLogs(DiagnosticsReadinessFailure failure)
    {
        var services = new ServiceCollection();
        new StructuredLogsFeature().ConfigureServices(services);
        services.AddSingleton(StructuredLogStoreBinding.Default);
        services.AddSingleton<IDiagnosticRecordDeploymentManifestSource>(
            new DiagnosticsDeploymentSchema());
        services.AddSingleton<IDiagnosticRecordStoreSessionFactory>(
            new ThrowingDiagnosticRecordStoreSessionFactory(failure));

        ConfigureFeature<GroundworkStructuredLogStore>(services, "GroundworkStructuredLogsPersistenceFeature");

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStructuredLogStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkStructuredLogStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryStructuredLogStore));
        return services;
    }

    private static void ConfigureFeature<TAssemblyAnchor>(IServiceCollection services, string featureTypeName)
    {
        var featureType = typeof(TAssemblyAnchor).Assembly.GetType(
            $"{typeof(TAssemblyAnchor).Namespace}.{featureTypeName}",
            throwOnError: false);
        Assert.NotNull(featureType);

        var feature = Assert.IsAssignableFrom<IShellFeature>(Activator.CreateInstance(featureType!));
        feature.ConfigureServices(services);
    }

    private static async Task StartHostAsync(IServiceProvider provider)
    {
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        Assert.NotEmpty(hostedServices);
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);
    }

    private static async Task StartShellAsync(IServiceProvider provider)
    {
        var initializers = provider.GetServices<IShellInitializer>().ToArray();
        Assert.NotEmpty(initializers);
        foreach (var initializer in initializers)
            await initializer.InitializeAsync(CancellationToken.None);
    }

    private static async Task AssertDoesNotMaskFailureAsAnEmptyStructuredLogStoreAsync(
        IServiceProvider provider,
        DiagnosticsReadinessFailure expected)
    {
        var resolution = Record.Exception(() => provider.GetRequiredService<IStructuredLogStore>());
        if (resolution is not null)
        {
            Assert.Same(expected, resolution);
            return;
        }

        var store = provider.GetRequiredService<IStructuredLogStore>();
        var readFailure = await Record.ExceptionAsync(() =>
            store.GetRecentAsync(StructuredLogFilter.None));

        Assert.Same(expected, readFailure);
    }

    public abstract class DiagnosticsReadinessFailure(string code, string message) : InvalidOperationException(message)
    {
        public string Code { get; } = code;
    }

    public sealed class MissingDiagnosticsSchemaFailure()
        : DiagnosticsReadinessFailure("ELSA-GW-SCHEMA-PENDING", "The declared diagnostics schema is missing.");

    public sealed class DiagnosticsSchemaDriftFailure()
        : DiagnosticsReadinessFailure("ELSA-GW-SCHEMA-DRIFT", "The declared diagnostics schema has drifted.");

    public sealed class DiagnosticsProviderUnavailableFailure()
        : DiagnosticsReadinessFailure("ELSA-GW-DIAGNOSTICS-PROVIDER", "The diagnostics provider is unavailable.");

    private sealed class ThrowingDiagnosticRecordStoreSessionFactory(DiagnosticsReadinessFailure failure)
        : IDiagnosticRecordStoreSessionFactory
    {
        public int OpenCount { get; private set; }

        public ValueTask<IDiagnosticRecordStoreSession> OpenAsync(
            DiagnosticRecordDeploymentManifest deployment,
            DiagnosticStorageScope scope,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(deployment);
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            throw failure;
        }
    }

    private sealed class UnusedDocumentSessionSource : IGroundworkStoreSessionSource
    {
        public int OpenCount { get; private set; }

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            throw new InvalidOperationException("The document source must not open after diagnostic admission fails.");
        }
    }

    private sealed class RecordingDiagnosticRecordStoreSessionFactory : IDiagnosticRecordStoreSessionFactory
    {
        public DiagnosticStorageScope? Scope { get; private set; }
        public List<RecordingDiagnosticRecordStoreSession> Sessions { get; } = [];

        public ValueTask<IDiagnosticRecordStoreSession> OpenAsync(
            DiagnosticRecordDeploymentManifest deployment,
            DiagnosticStorageScope scope,
            CancellationToken cancellationToken = default)
        {
            Scope = scope;
            var session = new RecordingDiagnosticRecordStoreSession();
            Sessions.Add(session);
            return ValueTask.FromResult<IDiagnosticRecordStoreSession>(session);
        }
    }

    private sealed class RecordingDiagnosticRecordStoreSession : IDiagnosticRecordStoreSession
    {
        private static readonly IDiagnosticRecordStore Store = CreateProxy<IDiagnosticRecordStore>();
        public List<DiagnosticStreamId> OpenedStreams { get; } = [];
        public int DisposeCount { get; private set; }
        public DiagnosticStorageScope Scope { get; } = new("test", "test");

        public ValueTask<IDiagnosticRecordStore> OpenStoreAsync(
            DiagnosticStreamId stream,
            CancellationToken cancellationToken = default)
        {
            OpenedStreams.Add(stream);
            return ValueTask.FromResult(Store);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDocumentSessionSource : IGroundworkStoreSessionSource
    {
        private static readonly IDocumentStore Documents = CreateProxy<IDocumentStore>();
        private static readonly IBoundedDocumentStore Queries = CreateProxy<IBoundedDocumentStore>();

        public DocumentStoreAccess? Access { get; private set; }
        public RecordingLease Lease { get; } = new();

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            Access = access;
            return ValueTask.FromResult(new GroundworkStoreSessionResources(Documents, Queries, Lease));
        }

        public int OpenCount { get; private set; }
    }

    private sealed class RecordingLease : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private static T CreateProxy<T>() where T : class =>
        DispatchProxy.Create<T, ThrowingDispatchProxy>();

    private class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Test proxy member '{targetMethod?.Name}' must not be invoked.");
    }
}
