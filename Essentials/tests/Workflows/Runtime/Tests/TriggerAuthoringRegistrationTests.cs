using System.Text.Json;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers.Management;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TriggerAuthoringRegistrationTests
{
    [Fact]
    public async Task Authoring_composes_metadata_and_validation_without_installing_workflow_runtime()
    {
        var services = Services();
        services.AddSingleton<IStandaloneTriggerTypeSource, Source>();
        services.AddScoped<ITriggerConfigurationValidator, Validator>();
        TriggerAuthoringRegistration.AddServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await Initialize(scope.ServiceProvider);
        Assert.Equal("test.entry", Assert.Single(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerTypeCatalog>().List()).TypeKey);
        Assert.Null(scope.ServiceProvider.GetService<IWorkflowExecutableStore>());
        Assert.Null(scope.ServiceProvider.GetService<IWorkflowActivationAuthority>());
        var validation = new StandaloneTriggerConfigurationValidating("test.entry", 2, JsonSerializer.SerializeToElement(new { }));
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(validation, CancellationToken.None);
        Assert.Equal("unsupported-format", Assert.Single(validation.Findings).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_duplicate_validator_refuses_initialization(bool duplicate)
    {
        var services = Services();
        services.AddSingleton<IStandaloneTriggerTypeSource, Source>();
        if (duplicate)
        {
            services.AddScoped<ITriggerConfigurationValidator, Validator>();
            services.AddScoped<ITriggerConfigurationValidator, Validator>();
        }
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Initialize(scope.ServiceProvider));
        Assert.Throws<TriggerOperationException>(() => scope.ServiceProvider.GetRequiredService<IStandaloneTriggerTypeCatalog>().List());
    }

    [Fact]
    public async Task Duplicate_type_owners_refuse_catalog_initialization()
    {
        var services = Services();
        services.AddSingleton<IStandaloneTriggerTypeSource, Source>();
        services.AddSingleton<IStandaloneTriggerTypeSource, Source>();
        services.AddScoped<ITriggerConfigurationValidator, Validator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Initialize(scope.ServiceProvider));
        Assert.Throws<TriggerOperationException>(() => scope.ServiceProvider.GetRequiredService<IStandaloneTriggerTypeCatalog>().List());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        new EventsFeature().ConfigureServices(services);
        TriggerAuthoringRegistration.AddServices(services);
        return services;
    }

    private static Task Initialize(IServiceProvider services) => Assert.Single(services.GetServices<IStartupTask>()).ExecuteAsync(CancellationToken.None);

    private sealed class Source : IStandaloneTriggerTypeSource
    {
        public IEnumerable<TriggerTypeDescriptor> GetTypes() => [new("test.entry", 1, "Test entry", "Test", [], [])];
    }

    private sealed class Validator : ITriggerConfigurationValidator
    {
        public string TypeKey => "test.entry";
        public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(schemaVersion == 1 ? [] : [new("unsupported-format", "Unsupported test format.")]);
    }
}
