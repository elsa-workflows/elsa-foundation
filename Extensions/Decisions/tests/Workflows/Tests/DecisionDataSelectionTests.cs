using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Decisions.Core;
using Elsa.Decisions.Workflows.Design;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Decisions.Workflows.Tests;

public sealed class DecisionDataSelectionTests
{
    [Fact]
    public async Task Headless_admission_rejects_a_dynamic_selector_and_discovery_exposes_its_owning_constraint()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
        var document = Document(nameof(EvaluateDecision.Decision));
        var admission = await analyzer.AnalyzeAsync(document);
        var error = Assert.Single(admission.Findings);
        Assert.Equal("decisions.selector-fixed-at-publication", error.Type);
        Assert.Equal("evaluate/inputs/Decision", error.Path);

        var discovery = await analyzer.AnalyzeAsync(document, new("evaluate", "Decision"));
        Assert.Empty(discovery.Findings);
        Assert.Equal("Elsa.Decisions", discovery.Target!.Constraint!.Owner);
        Assert.Equal(new[] { "Literal" }, discovery.Target.Constraint.AllowedExpressionTypes);
        Assert.Empty(discovery.Sources);
    }

    [Theory]
    [InlineData(nameof(EvaluateDecision.Inputs))]
    [InlineData(nameof(EvaluateDecision.OperationBudget))]
    public async Task Ordinary_decision_inputs_retain_shared_variable_discovery_and_admission(string input)
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
        var document = Document(input);
        Assert.Empty((await analyzer.AnalyzeAsync(document)).Findings);
        var discovery = await analyzer.AnalyzeAsync(document, new("evaluate", input));
        Assert.Null(discovery.Target!.Constraint);
        Assert.Contains(discovery.Sources, source => source.Binding?.ExpressionType == "Variable");
    }

    [Fact]
    public async Task Selector_constraint_identifies_evaluate_decision_without_a_loaded_definition_navigation()
    {
        var version = new ActivityDefinitionVersion("1.0.0", "decision-definition")
        {
            Id = "decision-version",
            DescriptorPayload = JsonSerializer.SerializeToElement(
                new { typeAlias = EvaluateDecision.ActivityType },
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        Assert.Null(version.Definition);
        Assert.Throws<ArgumentNullException>(() => _ = ((IActivityDefinitionVersion)version).Definition);

        var provider = new DecisionInputSourceConstraintProvider();
        var constraint = await provider.GetConstraintAsync(version, nameof(EvaluateDecision.Decision));
        Assert.Equal("Elsa.Decisions", constraint!.Owner);
        Assert.Equal(new[] { "Literal" }, constraint.AllowedExpressionTypes);
        Assert.Null(await provider.GetConstraintAsync(version, nameof(EvaluateDecision.Inputs)));
    }

    [Fact]
    public async Task Evaluate_decision_describes_whole_typed_result_without_output_rows_or_runtime_binding()
    {
        using var host = new EvaluateDecisionShapeHost();
        var version = host.Version;
        Assert.Empty(version.Outputs);
        var outcomes = Assert.Single(version.DesignFacets, facet => facet.Kind == "elsa.outcomes");
        Assert.Equal(
            new[] { "Done", "NoMatch", "Refused" },
            outcomes.Payload.GetProperty("ports").EnumerateArray().Select(port => port.GetProperty("name").GetString()).ToArray());

        var limits = new ActivitySerializedShapeLimits(8, 512);
        var shape = await host.Source.DescribeResultAsync(version, "$result", [], limits);
        Assert.NotNull(shape);
        Assert.Equal(ActivitySerializedShapeState.Known, shape.State);
        Assert.Equal(ActivitySerializedValueKind.Object, shape.Kind);
        Assert.Contains(shape.Members, member => member.SerializedName == "status" && member.Shape.Kind == ActivitySerializedValueKind.Scalar);
        Assert.Equal("activity.shape.dynamic-json", Assert.Single(shape.Members, member => member.SerializedName == "result").Shape.ReasonCode);
        Assert.Contains(shape.Members, member => member.SerializedName == "trace");

        await using var scope = host.Services.CreateAsyncScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<IActivityDataSelectionAnalyzer>();
        var discovery = await analyzer.AnalyzeAsync(host.Document, new("consumer", "CustomerId"),
            new() { ShapeLimits = limits });
        Assert.Empty(discovery.Findings);
        var source = Assert.Single(discovery.Sources, item => item.Id == "activity:evaluate:$result");
        Assert.Null(source.Binding);
        Assert.Equal("RuntimeScopeBindingUnavailable", source.ReasonCode);
        Assert.Equal(ActivityChildResultAvailability.Definite, source.Availability);
        Assert.Contains(source.Shape.Members, member => member.SerializedName == "status");
        Assert.Equal("activity.shape.dynamic-json", Assert.Single(source.Shape.Members, member => member.SerializedName == "result").Shape.ReasonCode);
        Assert.Contains(source.Shape.Members, member => member.SerializedName == "trace");
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new DecisionsWorkflowsDesignFeature().ConfigureServices(services);
        services.AddSingleton<IActivityDefinitionLookup>(new Catalog());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ActivityDataSelectionDocument Document(string input) => new(
        "WorkflowDefinition", "decision-workflow", "candidate-1",
        new ActivityNode("evaluate", "decision-version", [new(input,
            new ArgumentValue(new VariableReference("value"), "Variable"), null, null, null, null)], []),
        [], [new VariableDefinition("value", "Value", new("String"), null, null)], []);

    private sealed class Catalog : IActivityDefinitionLookup
    {
        private readonly IReadOnlyDictionary<string, IActivityDefinitionVersion> _versions;

        public Catalog() : this(DefaultDecision()) { }

        public Catalog(params IActivityDefinitionVersion[] versions) =>
            _versions = versions.ToDictionary(version => version.Id, StringComparer.Ordinal);

        private static ActivityDefinitionVersion DefaultDecision() => new("1.0.0", "decision-definition")
        {
            Id = "decision-version",
            Definition = new() { Id = "decision-definition", ActivityTypeKey = EvaluateDecision.ActivityType, DisplayName = "Evaluate decision" },
            Inputs = new[] { "Decision", "Inputs", "OperationBudget" }.Select(key =>
                new InputDefinition(key, key, new("String"), null, key, null, true))
        };

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.GetValueOrDefault(versionId));
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_versions.TryGetValue(versionId, out var version) ? version : throw new ArgumentException(nameof(versionId)));
        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null,
            string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EvaluateDecisionShapeHost : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "decision-shape-" + Guid.NewGuid().ToString("N"));
        private readonly IServiceScope _scope;
        public ServiceProvider Services { get; }
        public IActivitySerializedShapeSource Source { get; }
        public ActivityDefinitionVersionModel Version { get; }
        public ActivityDataSelectionDocument Document { get; }

        public EvaluateDecisionShapeHost()
        {
            Directory.CreateDirectory(_folder);
            foreach (var assembly in new[] { typeof(EvaluateDecision).Assembly, typeof(DecisionEvaluation).Assembly })
                File.Copy(assembly.Location, Path.Combine(_folder, Path.GetFileName(assembly.Location)), overwrite: true);
            var model = new ClrAssemblyScanner(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(),
                NullLogger<ClrAssemblyScanner>.Instance).Scan(_folder).Single(item => item.ActivityTypeKey == EvaluateDecision.ActivityType);
            var definition = new ActivityDefinitionModel("decision-definition", model.ActivityTypeKey, model.Category ?? "Elsa", model.DisplayName, model.Description);
            var version = new ActivityDefinitionVersionModel("decision-version", model.Version, definition.Id, model.ProviderKey, model.ProviderSchemaVersion,
                model.ConsumerKey, model.ConsumerSchemaVersion, JsonSerializer.SerializeToElement(model.Descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "CLR", "decision-tests", definition, model.Inputs, model.Outputs, model.DesignFacets, model.ExecutionType, string.Empty);
            Version = version with { Hash = new DefaultActivityDefinitionHasher().Hash(definition, version) };
            var consumer = new ActivityDefinitionVersion("1.0.0", "consumer-definition")
            {
                Id = "consumer-version",
                Definition = new() { Id = "consumer-definition", ActivityTypeKey = "consumer", DisplayName = "Consumer" },
                Inputs = [new InputDefinition("CustomerId", "CustomerId", new("String"), null, "Customer ID", null, true)]
            };
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSingleton<IVariableTypeDescriptorCatalog>(new EmptyTypes());
            services.AddSingleton<IActivityContractStorageDriverProvider, EmptyDrivers>();
            services.AddSingleton<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
            services.AddSingleton<IActivityDefinitionLookup>(new Catalog(Version, consumer));
            new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
            new WorkflowDesignValidationsFeature().ConfigureServices(services);
            new DecisionsWorkflowsDesignFeature().ConfigureServices(services);
            new ClrActivityReconciliationFeature { Options = { FolderPath = _folder, SourceId = "decision-tests" } }.ConfigureServices(services);
            Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = Services.CreateScope();
            Source = Assert.Single(_scope.ServiceProvider.GetServices<IActivitySerializedShapeSource>());
            Document = new(
                "WorkflowDefinition", "decision-workflow", "candidate-1",
                new ActivityNode("root", "sequence", [], [], new("elsa.sequence.structure", "1.0.0",
                    JsonSerializer.SerializeToElement(new
                    {
                        activities = new[]
                        {
                            new ActivityNode("evaluate", Version.Id, [], []),
                            new ActivityNode("consumer", consumer.Id, [new("CustomerId",
                                new ArgumentValue(new VariableReference("value"), "Variable"), null, null, null, null)], [])
                        },
                        variables = Array.Empty<VariableDefinition>()
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
                [], [new VariableDefinition("value", "Value", new("String"), null, null)], []);
        }

        public void Dispose()
        {
            _scope.Dispose();
            Services.Dispose();
            try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        }

        private sealed class EmptyTypes : IVariableTypeDescriptorCatalog
        {
            public IEnumerable<TypeDescriptor> GetDescriptors() => [];
            public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => [];
        }

        private sealed class EmptyDrivers : IActivityContractStorageDriverProvider
        {
            public IReadOnlyCollection<string> DriverKeys => [];
        }
    }
}
