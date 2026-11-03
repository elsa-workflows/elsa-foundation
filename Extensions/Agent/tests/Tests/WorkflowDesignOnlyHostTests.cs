using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Agent.Tests;

// Spec 1193 SC-009 (FR-027), the started Design-only shape: a real CShells shell with the Workflows Design API and the
// Design-side workflow agent package, no Runtime-side package. A version is read through the real Design API version reader
// over the version store; the Executable journey is explicitly unavailable. The Runtime-only shape is proven, AppDomain-wide,
// in Elsa.Agent.Workflows.Runtime.Tests (WorkflowRuntimeOnlyHostTests).
public sealed class WorkflowDesignOnlyHostTests : IAsyncDisposable
{
    private const string ShellName = "design-only-weaver";
    private const string Module = "Elsa.Studio.Workflows";
    private readonly ServiceProvider root;

    public WorkflowDesignOnlyHostTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(EventsFeature).Assembly, typeof(ApiCapabilitiesFeature).Assembly, typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(FoundationWorkflowsAgentFeature).Assembly, typeof(DesignOnlyFixtureFeature).Assembly,
                typeof(Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<WorkflowsDesignApiFeature>()
                .WithFeature<FoundationWorkflowsAgentFeature>()
                .WithFeature<DesignOnlyFixtureFeature>()));
        root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_started_design_only_shell_reads_a_version_through_the_design_api_and_refuses_the_Executable_journey()
    {
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        Assert.Equal("WorkflowVersionReader", Assert.Single(services.GetServices<IWorkflowVersionReader>()).GetType().Name);
        var session = await services.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain",
            null, AgentPolicy.Default, new Dictionary<string, string>()));
        var collector = services.GetRequiredService<IAgentContextCollector>();

        var version = await collector.CollectAsync(AgentPolicy.Default, new(session.Id, "unbound", new Dictionary<string, string>())
            { CallerAttachments = [WorkflowVersionAgentTests.Reference("definition-design-only", "version-design-only")] });
        Assert.True(version.Succeeded, version.Error?.Message);
        var admitted = Assert.Single(version.Value!.Attachments, item => item.ContentType == "workflows.version");
        Assert.Equal("Design only", ((JsonElement)admitted.Content!).GetProperty("definitionName").GetString());

        var executable = await collector.CollectAsync(AgentPolicy.Default, new(session.Id, "unbound", new Dictionary<string, string>())
        {
            CallerAttachments = [new("workflows.executable:artifact-1", "workflow-executable", "artifact-1", "Executable", "workflows.executable",
                AgentContextSensitivity.Internal, "screen", "Workflow Executable",
                JsonSerializer.SerializeToElement(new { artifactId = "artifact-1", artifactHash = "sha256:artifact", sourceReferenceId = (string?)null }),
                new Dictionary<string, string>())
            { ModuleId = Module, SchemaVersion = "1", Target = new(Module, "workflow-executable", "artifact-1", "sha256:artifact"), Origin = AgentContextOrigin.ClientSnapshot }]
        });
        Assert.False(executable.Succeeded);
        Assert.Equal("agent.context.kind_unsupported", Assert.Single(executable.Value!.Diagnostics, item => item.Required).Code);
        var tools = (await services.GetRequiredService<IAgentToolRegistry>().ListAsync()).Select(tool => tool.Descriptor.Name).ToArray();
        Assert.Contains("workflow_read_version", tools);
        Assert.DoesNotContain("workflow_read_executable", tools);
    }

    public ValueTask DisposeAsync() => root.DisposeAsync();
}

/// <summary>
/// The Design-only host's fixtures: the version stores a persistence provider would register, and the owner's read decision
/// and the host's tool permissions in place of the HTTP adapters. The Design API's validations dependency is the real feature.
/// </summary>
[ShellFeature(name: "DesignOnlyFixture", DisplayName = "Design-only fixture", Description = "Version stores and authority for composition tests.",
    DependsOn = new object[] { typeof(FoundationWorkflowsAgentFeature), typeof(WorkflowsDesignApiFeature) })]
public sealed class DesignOnlyFixtureFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new VersionStore());
        services.AddSingleton<IWorkflowDefinitionVersionLayoutStore>(new NoLayouts());
        services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionAuthorizer>(_ => new WorkflowVersionAgentTests.Owner("definition-design-only")));
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, AllowTestAgentToolAuthorizer>());
        services.TryAddScoped<IActivityStructureService>(_ => new Elsa.Workflows.Design.Core.Services.DefaultActivityStructureService([]));
    }

    private sealed class NoLayouts : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }

    private sealed class VersionStore : IWorkflowDefinitionVersionStore
    {
        private static WorkflowDefinitionVersion Version() => new("definition-design-only", "1.0.0")
        {
            Id = "version-design-only", Definition = new WorkflowDefinition { Id = "definition-design-only", Name = "Design only" },
            State = new([], new ActivityNode("root", "version-write-line", [], []), [], [], null)
        };

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            versionId == "version-design-only" ? Task.FromResult(Version())
                : throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionVersion), versionId);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(definitionId == "definition-design-only" ? [Version()] : []);
        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(versionId == "version-design-only" ? Version() : null);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
