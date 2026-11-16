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

    [Fact]
    public async Task Catalog_detaches_metadata_before_a_source_iterator_disposes_its_document()
    {
        var source = new BorrowedSource();
        var services = Services();
        services.AddSingleton<IStandaloneTriggerTypeSource>(source);
        services.AddScoped<ITriggerConfigurationValidator, Validator>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await Initialize(scope.ServiceProvider);
        var metadata = Assert.Single(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerTypeCatalog>().List());
        var borrowed = Assert.Single(source.Fields).DefaultValue!.Value;
        Assert.Throws<ObjectDisposedException>(() => borrowed.GetInt32());
        source.Fields.Clear();
        source.SourceFields.Clear();
        source.Choices.Clear();
        source.Path[0] = "retargeted";

        var field = Assert.Single(metadata.ConfigurationFields);
        Assert.Equal(17, field.DefaultValue!.Value.GetInt32());
        Assert.Equal("scheduled", Assert.Single(field.Choices!).Value);
        Assert.Equal("payload", Assert.Single(Assert.Single(metadata.SourceFields).Path));
        Assert.NotEmpty(JsonSerializer.Serialize(metadata));
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

    private sealed class BorrowedSource : IStandaloneTriggerTypeSource
    {
        public List<TriggerConfigurationField> Fields { get; } = [];
        public List<TriggerSourceField> SourceFields { get; } = [];
        public List<TriggerFieldChoice> Choices { get; } = [new("scheduled", "Scheduled")];
        public List<string> Path { get; } = ["payload"];

        public IEnumerable<TriggerTypeDescriptor> GetTypes()
        {
            using var document = JsonDocument.Parse("17");
            Fields.Add(new("count", "Count", DefaultValue: document.RootElement, Choices: Choices));
            SourceFields.Add(new(Path, "Payload"));
            yield return new("test.entry", 1, "Test entry", "Test", Fields, SourceFields);
        }
    }
}
