using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using RuntimeActivityContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using WriteLineActivity = Elsa.Activities.Primitives.Activities.WriteLine;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityTemplateSecretAdmissionTests
{
    private const string AuthoredNodeId = "write-line";
    private static readonly JsonSerializerOptions NodeOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Secret_on_exact_write_line_dependency_blocks_the_candidate_at_its_authored_node()
    {
        var result = await CompileAsync(typeof(WriteLineActivity), SecretBindingCompilerFixture.Secret("text"));

        Assert.Null(result.Template);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "VF-ACT-012");
        Assert.Equal(ActivityDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(GraphActivityProvider.Key, diagnostic.Location?.ProviderKey);
        Assert.Equal(AuthoredNodeId, diagnostic.Location?.ReferenceKey);
        Assert.Equal(AuthoredNodeId, Assert.Single(diagnostic.Location!.NodeOrigin!).Id);
        Assert.Contains("text", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretBindingCompilerFixture.SecretName, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secret_on_an_allowed_clr_input_does_not_block_the_candidate()
    {
        var result = await CompileAsync(typeof(TestWriteLineActivity), SecretBindingCompilerFixture.Secret("Text"));

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.NotNull(result.Template);
    }

    [Fact]
    public async Task Literal_on_exact_write_line_dependency_remains_publishable()
    {
        var result = await CompileAsync(typeof(WriteLineActivity),
            new WorkflowArgumentState("text", global::Elsa.Expressions.Core.Models.ArgumentValue.FromClr("Hello", "Literal"), null, null, null, null));

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.NotNull(result.Template);
    }

    private static async Task<ActivityTemplateCompilerResult> CompileAsync(Type activityType, WorkflowArgumentState input)
    {
        var activityVersion = SecretBindingCompilerFixture.ClrActivityVersion(activityType);
        var activityDefinition = activityVersion.Definition
            ?? throw new InvalidOperationException("The exact CLR activity version has no definition.");
        var activityTypeKey = activityDefinition.ActivityTypeKey
            ?? throw new InvalidOperationException("The exact CLR activity definition has no activity type key.");
        var typeRegistry = TestWellKnownTypeRegistry.Create();
        if (activityType != typeof(TestWriteLineActivity))
            typeRegistry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));

        var bindingCompiler = new RuntimeInputBindingCompiler(typeRegistry);
        var structures = new LeafStructureService();
        var provider = new GraphActivityProvider(structures, new ActivityContractAuthoringValidator(new EmptyCapabilities()));
        var descriptorPayload = JsonSerializer.SerializeToElement(
            new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)), Elsa.Serialization.Core.JsonBaseline.Options);
        var runtimeInputs = activityVersion.Inputs.Select(definition => new RuntimeActivityInputContract(
            definition.ReferenceKey,
            definition.Name,
            new ValueTypeDescriptor(definition.Type.Alias, definition.Type.CollectionKind),
            definition.IsRequired,
            definition.IsNullable,
            definition.DefaultValue.HasValue,
            definition.DefaultValue,
            ActivityValuePolicy.Default,
            isCredential: definition.IsCredential == true)).ToArray();
        var runtimeContract = new RuntimeActivityContract(
            activityTypeKey,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            activityType.Name,
            descriptorPayload,
            runtimeInputs,
            new ActivityResultContract(new ValueTypeDescriptor("Object"), isRequired: true, ActivityValuePolicy.Default, projections: []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(activityTypeKey, TypeAliasConvention.CanonicalAlias(activityType)));
        var exactRoot = new ExecutableNode(
            "exact-root",
            "exact-root",
            activityTypeKey,
            activityVersion.Version,
            new(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, descriptorPayload),
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            activityContract: runtimeContract);
        var exactTemplate = new ExecutableActivityTemplate(
            "template-exact-write-line",
            "hash-exact-write-line",
            exactRoot,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
            [],
            [],
            [],
            "exact-activity-compiler",
            new Dictionary<string, string>(StringComparer.Ordinal),
            DateTimeOffset.UnixEpoch);
        var activityContract = new DesignActivityContract("1", [], [], [new("done", "Done", true)]);
        var publication = new ActivityDefinitionVersionPublication
        {
            Id = activityVersion.Id,
            DefinitionId = activityVersion.DefinitionId,
            DefinitionVersionId = activityVersion.Id,
            Version = activityVersion.Version,
            ActivityTypeKey = activityVersion.Definition.ActivityTypeKey,
            Contract = activityContract,
            Provider = new("test", "1", JsonSerializer.SerializeToElement(new { })),
            TemplateId = exactTemplate.TemplateId,
            TemplateHash = exactTemplate.TemplateHash,
            SourceReferenceId = "source-exact-write-line",
            ProviderFingerprint = "exact-activity-compiler",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 0,
            RuntimeRequirements = [],
            Lifecycle = ActivityDefinitionVersionLifecycle.Active
        };

        var authoredNode = new ActivityNode(AuthoredNodeId, activityVersion.Id, [input], []);
        var manifest = new ActivityProviderManifest(
            GraphActivityProvider.Key,
            "1",
            JsonSerializer.SerializeToElement(new
            {
                rootActivity = authoredNode,
                variables = Array.Empty<object>(),
                outputMappings = Array.Empty<object>()
            }, NodeOptions));
        var candidateContract = new DesignActivityContract("1", [], [], [new("done", "Done", true)]);
        var request = new ActivityTemplateCompilerRequest(
            new ActivityDefinition { Id = "greeter-definition", ActivityTypeKey = "greeter" },
            new ActivityTemplateCandidate("ActivityDraft", "greeter-draft", 1, candidateContract, manifest),
            "greeter-version",
            "1.0.0",
            0);
        var compiler = new ActivityTemplateCompiler(
            new ActivityTemplateProviderCompilerRegistry([provider]),
            new ActivityTemplateDependencyDiscovererRegistry([provider]),
            new PublicationStore([publication]),
            new FakeActivityVersionStore([activityVersion]),
            new TemplateReader([exactTemplate]),
            new AcceptAllActivityTemplateAdmissionPolicy(),
            TimeProvider.System,
            [provider],
            new EmptyDataSelectionAnalyzer(),
            structures,
            bindingCompiler);

        return await compiler.CompileAsync(request);
    }

    private sealed class EmptyCapabilities : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => [];
    }

    private sealed class EmptyDataSelectionAnalyzer : IActivityDataSelectionAnalyzer
    {
        public ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(
            ActivityDataSelectionDocument document,
            ActivityDataSelectionTarget? target = null,
            ActivityDataSelectionQuery? query = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDataSelectionAnalysis(document.ContractRevision, null, [], []));
    }

    private sealed class LeafStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => activity.Structure;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public bool HasHandler(ActivityNodeStructure structure) => false;
    }

    private sealed class PublicationStore(IEnumerable<ActivityDefinitionVersionPublication> values) : IActivityDefinitionVersionPublicationStore
    {
        private readonly IReadOnlyDictionary<string, ActivityDefinitionVersionPublication> _values =
            values.ToDictionary(value => value.DefinitionVersionId, StringComparer.Ordinal);

        public Task<ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(definitionVersionId));

        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(
                _values.Values.Where(value => value.DefinitionId == definitionId).ToArray());
    }

    private sealed class TemplateReader(IEnumerable<ExecutableActivityTemplate> values) : IExecutableActivityTemplateReader
    {
        private readonly IReadOnlyDictionary<string, ExecutableActivityTemplate> _values =
            values.ToDictionary(value => value.TemplateId, StringComparer.Ordinal);

        public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.GetValueOrDefault(templateId));

        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.Values.SingleOrDefault(value => value.TemplateHash == templateHash));

        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(
            RuntimeStorePageRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, _values.Values, value => value.TemplateId, "activity-template"));
    }
}
