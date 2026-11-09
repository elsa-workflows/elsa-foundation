using System.Text.Json;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Design;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Publication;

public sealed class SpecialistScopeTests
{
    [Fact]
    public async Task Real_registries_preserve_each_explicit_publication_scope_for_metadata_validation()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);

        new SpecialistsDesignFeature().ConfigureServices(services);
        services.AddScoped<ISpecialistModelCapabilitiesSource, ScopedModel>();
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        foreach (var partition in new[] { "tenant-a", "tenant-b" })
        {
            await using var scope = root.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new(partition)));
            var provider = scope.ServiceProvider.GetRequiredService<IActivityProviderRegistry>().Resolve(SpecialistRuntimeIds.ProviderKey, "1");
            var compiler = scope.ServiceProvider.GetRequiredService<IActivityTemplateProviderCompilerRegistry>().Resolve(SpecialistRuntimeIds.ProviderKey, "1");
            var discoverer = scope.ServiceProvider.GetRequiredService<IActivityTemplateDependencyDiscovererRegistry>().Resolve(SpecialistRuntimeIds.ProviderKey, "1");
            var contract = new Elsa.Activities.Design.Core.Models.ActivityContract("1", [], [], [new("done", "Done", true)]);
            var manifest = JsonSerializer.SerializeToElement(new SpecialistManifest("Instructions", new("fixture", "connection", "model"), Json("{}"),
                new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), new()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var request = new ActivityTemplateCompilationRequest("definition", "type", "draft", 1, "1.0.0", contract, new(SpecialistRuntimeIds.ProviderKey, "1", manifest), [], "compiler/1");
            Assert.Empty(await provider.ValidateAsync(request.Provider, request.Contract));
            var result = await compiler.CompileAsync(request);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(partition, result.ExecutableRoot!.DescriptorPayload.GetProperty("capabilitiesFingerprint").GetString());
            Assert.Empty((await discoverer.DiscoverDependenciesAsync(new("definition", "draft", 1, request.Provider))).Diagnostics);
        }
    }

    private sealed class ScopedModel(IPersistenceAccessContextAccessor access) : ISpecialistModelCapabilitiesSource
    {
        public string ProviderKey => "fixture";
        public ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SpecialistModelCapabilities(access.Current.RequireScope().Value, Json("{}"), true, 4096));
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
