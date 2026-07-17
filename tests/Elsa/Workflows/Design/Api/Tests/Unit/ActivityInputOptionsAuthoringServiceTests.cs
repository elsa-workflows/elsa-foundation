using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ActivityInputOptionsAuthoringServiceTests
{
    [Fact]
    public async Task Resolves_provider_with_the_selected_nested_node_and_workflow_context()
    {
        var child = new ActivityNode("child", "activity-v1", [], []);
        var root = new ActivityNode("root", "container-v1", [], []);
        var provider = new CapturingProvider([new ActivityInputOption("Customer number", "customerNumber")]);
        var service = CreateService(root, child, provider);

        var result = await service.ResolveAsync("activity-v1", "Field", "child", new(RootActivity: root));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("customerNumber", result.Options!.Single().Value.GetString());
        Assert.Equal("child", provider.Context!.Activity.NodeId);
        Assert.Equal("Field", provider.Context.Input.Name);
        Assert.Equal("root", provider.Context.WorkflowState.RootActivity!.NodeId);
    }

    [Fact]
    public async Task Rejects_a_spoofed_node_activity_version_before_invoking_the_provider()
    {
        var node = new ActivityNode("node", "different-v1", [], []);
        var provider = new CapturingProvider([]);
        var service = CreateService(node, null, provider);

        var result = await service.ResolveAsync("activity-v1", "Field", "node", new(RootActivity: node));

        Assert.Equal(409, result.StatusCode);
        Assert.Null(provider.Context);
    }

    [Fact]
    public async Task Hides_provider_failures_behind_the_stable_unavailable_contract()
    {
        var node = new ActivityNode("node", "activity-v1", [], []);
        var service = CreateService(node, null, new ThrowingProvider());

        var result = await service.ResolveAsync("activity-v1", "Field", "node", new(RootActivity: node));

        Assert.Equal(503, result.StatusCode);
        Assert.Equal("OPTIONS_PROVIDER_UNAVAILABLE", result.Code);
        Assert.DoesNotContain("provider details", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Propagates_provider_cancellation()
    {
        var node = new ActivityNode("node", "activity-v1", [], []);
        var service = CreateService(node, null, new CancellingProvider());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ResolveAsync("activity-v1", "Field", "node", new(RootActivity: node), cancellation.Token));
    }

    private static ActivityInputOptionsAuthoringService CreateService(
        ActivityNode root,
        ActivityNode? child,
        IActivityInputOptionsProvider provider)
    {
        var input = new InputDefinition(
            "Field",
            "Field",
            new TypeReference("String"),
            null,
            "Field",
            null,
            false,
            UISpecifications: JsonSerializer.SerializeToElement(new
            {
                optionsProvider = new { key = "catalog.fields", dependsOn = Array.Empty<string>() }
            }));
        var version = new ActivityDefinitionVersion("1.0.0", "definition-1")
        {
            Id = "activity-v1",
            DescriptorType = "Test",
            Inputs = [input]
        };

        return new(
            new StubVersionStore(version),
            new StubStructureService(root, child),
            new ActivityInputOptionsProviderResolver([provider]),
            NullLogger<ActivityInputOptionsAuthoringService>.Instance);
    }

    private sealed class CapturingProvider(IReadOnlyList<ActivityInputOption> options) : IActivityInputOptionsProvider
    {
        public string Key => "catalog.fields";
        public ActivityInputOptionsContext? Context { get; private set; }

        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
            ActivityInputOptionsContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return ValueTask.FromResult(options);
        }
    }

    private sealed class ThrowingProvider : IActivityInputOptionsProvider
    {
        public string Key => "catalog.fields";
        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
            ActivityInputOptionsContext context,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("provider details must remain private");
    }

    private sealed class CancellingProvider : IActivityInputOptionsProvider
    {
        public string Key => "catalog.fields";

        public async ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
            ActivityInputOptionsContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class StubStructureService(ActivityNode root, ActivityNode? child) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            child is not null && activity.NodeId == root.NodeId ? [new("Children", [child])] : [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class StubVersionStore(ActivityDefinitionVersion version) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            versionId == version.Id
                ? Task.FromResult(version)
                : throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
