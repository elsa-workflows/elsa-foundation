using Elsa.Events.Core.Contracts;
using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Framework §2.23.1 registration smoke for <see cref="WorkflowsDesignReconciliationFeature"/>.
/// Concrete features extend the abstract base and override <c>Sources</c>; here we instantiate
/// a minimal subclass and verify every claimed service resolves from the built provider.
/// </summary>
public sealed class WorkflowsDesignReconciliationFeatureRegistrationTests
{
    [Fact]
    public void Feature_registers_reconciler_and_handler()
    {
        var services = MinimalServices();
        new TestWorkflowsDesignReconciliationFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IWorkflowVersionReconciler>());

        var handlers = scope.ServiceProvider.GetServices<IEventHandler>().ToList();
        Assert.Contains(handlers, h => h is IEventHandler<WorkflowVersionsReconciling>);
    }

    [Fact]
    public void Sources_set_on_feature_register_as_singletons()
    {
        var services = MinimalServices();
        var source = new StubSource("Test", "test-1");
        new TestWorkflowsDesignReconciliationFeature
        {
            Sources = [source]
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var registered = provider.GetServices<IWorkflowReconciliationSource>().ToList();

        Assert.Single(registered);
        Assert.Same(source, registered[0]);
    }

    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        services.AddSingleton<IIdentityGenerator, StubIdentityGenerator>();
        services.AddSingleton<IInlineEventPublisher, StubEventPublisher>();
        services.AddSingleton<IWorkflowDefinitionStore, ThrowingDefinitionStore>();
        services.AddSingleton<IWorkflowDefinitionVersionStore, ThrowingVersionStore>();
        services.AddSingleton<IMaterializeWorkflowDefinitionCommand, StubMaterializeDefinitionCommand>();
        services.AddSingleton<IMaterializeWorkflowDefinitionVersionCommand, StubMaterializeVersionCommand>();
        services.AddSingleton<ISaveWorkflowDefinitionCommand, ThrowingSaveDefinitionCommand>();
        services.AddSingleton<IPayloadSerializer, StubPayloadSerializer>();
        // Entity factories are registered by the persistence feature in the host; the universal
        // reconciling handler depends on them, so the smoke test supplies the real implementations.
        services.AddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.AddSingleton<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();
        return services;
    }

    private sealed class TestWorkflowsDesignReconciliationFeature : WorkflowsDesignReconciliationFeature;

    private sealed class StubSource(string kind, string id) : IWorkflowReconciliationSource
    {
        public string SourceKind { get; } = kind;
        public string SourceId { get; } = id;
        public ValueTask<IEnumerable<Workflows.Design.Reconciliation.Models.WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Workflows.Design.Reconciliation.Models.WorkflowVersionReconciliationModel>>([]);
    }

    private sealed class StubIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class StubPayloadSerializer : IPayloadSerializer
    {
        public string Serialize(object payload) => JsonSerializer.Serialize(payload);
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(string serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }

    private sealed class StubEventPublisher : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubMaterializeDefinitionCommand : IMaterializeWorkflowDefinitionCommand
    {
        public Task<string> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(definition.Id);
    }

    private sealed class StubMaterializeVersionCommand : IMaterializeWorkflowDefinitionVersionCommand
    {
        public Task<WorkflowDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinitionVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new WorkflowDefinitionVersionAdded(
                    version.DefinitionId,
                    version.Id,
                    version.Version));
    }

    private sealed class ThrowingSaveDefinitionCommand : ISaveWorkflowDefinitionCommand
    {
        public Task Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Registration smoke test: command should not have been called.");
    }

    private sealed class ThrowingDefinitionStore : IWorkflowDefinitionStore
    {
        private const string Msg = "Registration smoke test: store should not have been called.";
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(Persistence.Core.Filters.WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }

    private sealed class ThrowingVersionStore : IWorkflowDefinitionVersionStore
    {
        private const string Msg = "Registration smoke test: store should not have been called.";
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }
}
