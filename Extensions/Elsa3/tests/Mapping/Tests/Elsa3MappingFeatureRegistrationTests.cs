using CShells.Lifecycle;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Exceptions;
using Elsa3.Mapping.Internal;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Framework §2.6.2: the mapping's replacement contracts (<see cref="IElsa3DefinitionConverter"/>,
/// <see cref="IElsa3NodeConverter"/>, <see cref="IElsa3ActivityMap"/>) are registered with TryAdd, a host's own
/// registration wins, and composing a second one, or replacing only one of the two converters, fails shell activation.
/// </summary>
public sealed class Elsa3MappingFeatureRegistrationTests
{
    [Fact]
    public async Task A_host_converter_registered_first_wins_and_also_converts_nodes()
    {
        var host = new HostConverter();
        var services = new ServiceCollection();
        services.AddScoped<IElsa3DefinitionConverter>(_ => host);

        new Elsa3MappingFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Same(host, scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>());
        Assert.Same(host, scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>());
        await CheckAsync(services);
    }

    [Fact]
    public async Task A_host_definition_converter_that_cannot_convert_nodes_is_reported_when_a_node_converter_is_resolved()
    {
        // A factory registration hides its type from the composition check, so the refusal comes at resolution.
        var services = new ServiceCollection();
        services.AddScoped<IElsa3DefinitionConverter>(_ => new DefinitionOnlyConverter());

        new Elsa3MappingFeature().ConfigureServices(services);
        await CheckAsync(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var refusal = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>());
        Assert.Contains(nameof(IElsa3NodeConverter), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_definition_converter_of_a_type_that_cannot_convert_nodes_fails_activation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IElsa3DefinitionConverter, DefinitionOnlyConverter>();
        new Elsa3MappingFeature().ConfigureServices(services);

        var refusal = await Assert.ThrowsAsync<PartialElsa3ConverterReplacementException>(() => CheckAsync(services));

        Assert.Equal(typeof(IElsa3DefinitionConverter), refusal.Replaced);
        Assert.Equal(typeof(IElsa3NodeConverter), refusal.Missing);
        Assert.Equal($"'{typeof(DefinitionOnlyConverter).FullName}'", refusal.Implementation);
    }

    [Fact]
    public async Task A_host_node_converter_beside_the_default_definition_converter_fails_activation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IElsa3NodeConverter, HostNodeConverter>();
        new Elsa3MappingFeature().ConfigureServices(services);

        var refusal = await Assert.ThrowsAsync<PartialElsa3ConverterReplacementException>(() => CheckAsync(services));

        Assert.Equal(typeof(IElsa3NodeConverter), refusal.Replaced);
        Assert.Equal(typeof(IElsa3DefinitionConverter), refusal.Missing);
    }

    [Fact]
    public async Task A_host_can_replace_both_converters_with_its_own_node_conversion_context()
    {
        var services = new ServiceCollection();
        services.AddScoped<IElsa3DefinitionConverter, DefinitionOnlyConverter>();
        services.AddScoped<IElsa3NodeConverter, HostNodeConverter>();
        new Elsa3MappingFeature().ConfigureServices(services);

        await CheckAsync(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var request = new Elsa3ConversionRequest(new Elsa3WorkflowDefinition { Root = new Elsa3Activity { Id = "root", Type = "Elsa.Flowchart" } });

        var prepared = await converter.PrepareAsync(request);

        Assert.IsType<HostNodeConverter>(converter);
        Assert.Same(request, Assert.IsType<HostNodeConversionContext>(prepared.Value).Request);
    }

    [Fact]
    public async Task The_feature_registers_the_composition_check_as_a_shell_initializer()
    {
        var services = new ServiceCollection();
        new Elsa3MappingFeature().ConfigureServices(services);
        services.AddSingleton<IElsa3ActivityMap, HostActivityMap>();

        await using var provider = services.BuildServiceProvider();
        var check = Assert.Single(provider.GetServices<IShellInitializer>(), initializer => initializer is Elsa3MappingCompositionCheck);
        await Assert.ThrowsAsync<MultipleElsa3MappingRegistrationsException>(() => check.InitializeAsync());
    }

    [Fact]
    public async Task A_second_activity_map_registered_after_the_feature_fails_activation_naming_both()
    {
        var services = new ServiceCollection();
        new Elsa3MappingFeature().ConfigureServices(services);
        services.AddSingleton<IElsa3ActivityMap, HostActivityMap>();

        var refusal = await Assert.ThrowsAsync<MultipleElsa3MappingRegistrationsException>(() => CheckAsync(services));

        Assert.Equal(typeof(IElsa3ActivityMap), refusal.Contract);
        Assert.Equal([$"'{typeof(Elsa3BuiltInActivityMap).FullName}'", $"'{typeof(HostActivityMap).FullName}'"], refusal.Implementations);
    }

    [Fact]
    public async Task Registering_the_feature_twice_composes_one_of_each_contract()
    {
        var services = new ServiceCollection();
        new Elsa3MappingFeature().ConfigureServices(services);
        new Elsa3MappingFeature().ConfigureServices(services);

        await CheckAsync(services);
        foreach (var contract in Elsa3MappingCompositionCheck.ReplacementContracts)
            Assert.Single(services, descriptor => descriptor.ServiceType == contract);
    }

    private static Task CheckAsync(IServiceCollection services) =>
        new Elsa3MappingCompositionCheck(new Elsa3MappingComposition(services)).InitializeAsync();

    private sealed class HostConverter : IElsa3DefinitionConverter, IElsa3NodeConverter
    {
        public ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(Elsa3ConversionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(Elsa3ConversionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(Elsa3NodeConversionContext context, Elsa3Activity node, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DefinitionOnlyConverter : IElsa3DefinitionConverter
    {
        public ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(Elsa3ConversionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class HostNodeConverter : IElsa3NodeConverter
    {
        public ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(Elsa3ConversionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Elsa3MigrationResult<Elsa3NodeConversionContext>.Success(new HostNodeConversionContext(request), []));

        public ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(Elsa3NodeConversionContext context, Elsa3Activity node, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class HostNodeConversionContext(Elsa3ConversionRequest request) : Elsa3NodeConversionContext(request);

    private sealed class HostActivityMap : IElsa3ActivityMap
    {
        public IReadOnlyList<Elsa3ActivityMapEntry> Entries => [];

        public Elsa3ActivityMapEntry? Find(string elsa3TypeName) => null;
    }
}
