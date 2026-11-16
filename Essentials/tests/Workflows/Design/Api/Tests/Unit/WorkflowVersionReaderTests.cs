using Elsa.Primitives.Exceptions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

// Spec 1193 slice F (D4): exact version reads for other domains through Workflows Design Core.
public sealed class WorkflowVersionReaderTests
{
    [Fact]
    public async Task The_feature_registers_one_reader_over_the_version_details_composition()
    {
        var store = new Store();
        await using var provider = Compose(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IWorkflowVersionReader>();
        Assert.Equal("WorkflowVersionReader", reader.GetType().Name);
        Assert.True(reader.GetType().IsSealed);
        Assert.False(reader.GetType().IsPublic);

        var version = await reader.ReadAsync("version-2");
        Assert.NotNull(version);
        Assert.Equal(("version-2", "definition-1", "2.0.0", "Onboarding"), (version.VersionId, version.DefinitionId, version.Version, version.DefinitionName));
        Assert.Equal("root", version.State.RootActivity!.NodeId);
        Assert.Equal(["version-2"], store.Reads);
    }

    [Fact]
    public async Task A_missing_version_or_a_draft_identifier_reads_as_absent()
    {
        var store = new Store();
        await using var provider = Compose(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IWorkflowVersionReader>();
        Assert.Null(await reader.ReadAsync("missing"));
        // A synthetic draft identifier is never looked up as a version, as the endpoint refuses it.
        Assert.Null(await reader.ReadAsync("draft:draft-1"));
        Assert.Null(await reader.ReadAsync(" "));
        Assert.Equal(["missing"], store.Reads);
    }

    [Fact]
    public async Task Finds_a_versions_definition_without_reading_the_version()
    {
        var store = new Store();
        await using var provider = Compose(store).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IWorkflowVersionReader>();
        Assert.Equal("definition-1", await reader.FindDefinitionIdAsync("version-2"));
        Assert.Null(await reader.FindDefinitionIdAsync("missing"));
        Assert.Null(await reader.FindDefinitionIdAsync("draft:draft-1"));
        Assert.Equal(["version-2", "missing"], store.Lookups);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task Lists_the_definitions_versions_as_the_store_holds_them()
    {
        await using var provider = Compose(new Store()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<IWorkflowVersionReader>().ListAsync("definition-1");
        Assert.Equal([("version-1", "1.0.0"), ("version-2", "2.0.0")], listed.Select(item => (item.Id, item.Version)));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowVersionReader>().ListAsync("other"));
    }

    [Fact]
    public void A_second_reader_registration_fails_configuration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowVersionReader, Duplicate>();
        services.AddScoped<IWorkflowVersionReader, Duplicate>();
        var error = Assert.Throws<InvalidOperationException>(() => new WorkflowsDesignApiFeature().ConfigureServices(services));
        Assert.Contains("IWorkflowVersionReader has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_second_reader_registration_fails_shell_startup()
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowVersionReader, Duplicate>();
        var task = services.Where(item => item.ServiceType == typeof(IStartupTask)).Select(item => item.ImplementationInstance)
            .OfType<IStartupTask>().Single(item => item.GetType().Name == "WorkflowVersionReaderStartupTask");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IWorkflowVersionReader has multiple registrations", error.Message);
    }

    private static IServiceCollection Compose(Store store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowDefinitionVersionStore>(store);
        services.AddScoped<IWorkflowVersionDetailsReader>(_ => new WorkflowVersionDetailsReader(store, new NoLayouts()));
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        return services;
    }

    private sealed class Duplicate : IWorkflowVersionReader
    {
        public ValueTask<WorkflowVersionDetails?> ReadAsync(string versionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowVersionDetails?>(null);
        public ValueTask<string?> FindDefinitionIdAsync(string versionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask<IReadOnlyList<WorkflowDefinitionVersionSummary>> ListAsync(string definitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkflowDefinitionVersionSummary>>([]);
    }

    private sealed class NoLayouts : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }

    private sealed class Store : IWorkflowDefinitionVersionStore
    {
        public List<string> Reads { get; } = [];
        private static readonly WorkflowDefinition Definition = new() { Id = "definition-1", Name = "Onboarding" };

        private static WorkflowDefinitionVersion Version(string id, string version) => new("definition-1", version)
        {
            Id = id, Definition = Definition,
            State = new([], new ActivityNode("root", "Acme.Flowchart:1", [], []), [], [], null)
        };

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        {
            Reads.Add(versionId);
            return versionId == "version-2" ? Task.FromResult(Version("version-2", "2.0.0"))
                : throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);
        }

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(definitionId == "definition-1" ? [Version("version-1", "1.0.0"), Version("version-2", "2.0.0")] : []);

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
        {
            Lookups.Add(versionId);
            return Task.FromResult(versionId == "version-2" ? Version("version-2", "2.0.0") : null);
        }

        public List<string> Lookups { get; } = [];
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
