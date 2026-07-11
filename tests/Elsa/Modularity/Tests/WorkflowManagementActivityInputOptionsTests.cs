using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Server;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class WorkflowManagementActivityInputOptionsTests
{
    [Fact]
    public async Task Operation_resolves_catalog_provider_for_a_nested_activity_and_returns_no_store_options()
    {
        var child = new ActivityNode("child", "activity-v1", [], []);
        var root = new ActivityNode("root", "container-v1", [], []);
        var provider = new CapturingProvider("catalog.fields", [new ActivityInputOption("Customer number", "customerNumber")]);
        var services = CreateServices(DynamicInput(), root, child, provider);
        var request = new ActivityInputOptionsRequest("child", State(root));

        var result = await ElsaWorkflowManagementApi.ResolveActivityInputOptionsAsync(
            services, "activity-v1", "Field", request, CancellationToken.None);
        var response = await ExecuteAsync(result, services);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl);
        Assert.Equal("customerNumber", JsonDocument.Parse(response.Body).RootElement.GetProperty("options")[0].GetProperty("value").GetString());
        Assert.Equal("child", provider.Context!.Activity.NodeId);
        Assert.Equal("Field", provider.Context.Input.Name);
        Assert.Equal("root", provider.Context.WorkflowState.RootActivity!.NodeId);
    }

    [Fact]
    public async Task Operation_rejects_a_spoofed_node_activity_version_before_invoking_provider()
    {
        var node = new ActivityNode("node", "different-v1", [], []);
        var provider = new CapturingProvider("catalog.fields", []);
        var services = CreateServices(DynamicInput(), node, null, provider);

        var result = await ElsaWorkflowManagementApi.ResolveActivityInputOptionsAsync(
            services,
            "activity-v1",
            "Field",
            new ActivityInputOptionsRequest("node", State(node)),
            CancellationToken.None);
        var response = await ExecuteAsync(result, services);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Null(provider.Context);
    }

    [Fact]
    public async Task Operation_hides_provider_failures_behind_structured_unavailable_response()
    {
        var node = new ActivityNode("node", "activity-v1", [], []);
        var provider = new ThrowingProvider("catalog.fields");
        var services = CreateServices(DynamicInput(), node, null, provider);

        var result = await ElsaWorkflowManagementApi.ResolveActivityInputOptionsAsync(
            services,
            "activity-v1",
            "Field",
            new ActivityInputOptionsRequest("node", State(node)),
            CancellationToken.None);
        var response = await ExecuteAsync(result, services);
        var body = JsonDocument.Parse(response.Body).RootElement;

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("OPTIONS_PROVIDER_UNAVAILABLE", body.GetProperty("code").GetString());
        Assert.DoesNotContain("provider details", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_returns_structured_unavailable_when_catalog_provider_is_not_registered()
    {
        var node = new ActivityNode("node", "activity-v1", [], []);
        var services = CreateServices(DynamicInput(), node, null, null);

        var result = await ElsaWorkflowManagementApi.ResolveActivityInputOptionsAsync(
            services,
            "activity-v1",
            "Field",
            new ActivityInputOptionsRequest("node", State(node)),
            CancellationToken.None);
        var response = await ExecuteAsync(result, services);
        var body = JsonDocument.Parse(response.Body).RootElement;

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("OPTIONS_PROVIDER_UNAVAILABLE", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Operation_propagates_provider_cancellation()
    {
        var node = new ActivityNode("node", "activity-v1", [], []);
        var services = CreateServices(DynamicInput(), node, null, new CancellingProvider("catalog.fields"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ElsaWorkflowManagementApi.ResolveActivityInputOptionsAsync(
            services,
            "activity-v1",
            "Field",
            new ActivityInputOptionsRequest("node", State(node)),
            cancellation.Token));
    }

    private static InputDefinition DynamicInput() => new(
        "Field",
        "Field",
        new TypeReference("String"),
        null,
        "Field",
        null,
        UISpecifications: JsonSerializer.SerializeToElement(new
        {
            optionsProvider = new { key = "catalog.fields", dependsOn = Array.Empty<string>() }
        }));

    private static WorkflowDefinitionStateView State(ActivityNode root) => new(RootActivity: root);

    private static IServiceProvider CreateServices(
        InputDefinition input,
        ActivityNode root,
        ActivityNode? child,
        IActivityInputOptionsProvider? provider)
    {
        var version = new ActivityDefinitionVersion("1.0.0", "definition-1")
        {
            Id = "activity-v1",
            DescriptorType = "Test",
            Inputs = [input]
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActivityDefinitionVersionStore>(new StubVersionStore(version));
        services.AddSingleton<IActivityStructureService>(new StubStructureService(root, child));
        services.AddSingleton<IActivityInputOptionsProviderResolver>(new ActivityInputOptionsProviderResolver(
            provider is null ? [] : [provider]));
        return services.BuildServiceProvider();
    }

    private static async Task<(int StatusCode, IHeaderDictionary Headers, string Body)> ExecuteAsync(IResult result, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        await using var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, context.Response.Headers, await reader.ReadToEndAsync());
    }

    private sealed class CapturingProvider(string key, IReadOnlyList<ActivityInputOption> options) : IActivityInputOptionsProvider
    {
        public string Key { get; } = key;
        public ActivityInputOptionsContext? Context { get; private set; }

        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return ValueTask.FromResult(options);
        }
    }

    private sealed class ThrowingProvider(string key) : IActivityInputOptionsProvider
    {
        public string Key { get; } = key;
        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider details must remain private");
    }

    private sealed class CancellingProvider(string key) : IActivityInputOptionsProvider
    {
        public string Key { get; } = key;
        public async ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class StubStructureService(ActivityNode root, ActivityNode? child) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            child is not null && activity.NodeId == root.NodeId ? [new ActivityChildProjection("Children", [child])] : [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class StubVersionStore(ActivityDefinitionVersion version) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            versionId == version.Id ? Task.FromResult(version) : throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
