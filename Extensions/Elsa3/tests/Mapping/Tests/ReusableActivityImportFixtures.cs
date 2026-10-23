using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Elsa3.Activities.Design.Import;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa3.Mapping.Tests;

internal static class ReusableActivityImportFixtures
{
    public static Elsa3WorkflowDefinition Workflow(
        string definitionId,
        string versionId,
        int version,
        bool reusable,
        Elsa3Activity root,
        params Elsa3WorkflowArgumentDefinition[] inputs) => new()
        {
            Id = versionId,
            DefinitionId = definitionId,
            Name = $"Workflow {definitionId}",
            Description = $"Description {definitionId}",
            Version = version,
            CreatedAt = DateTimeOffset.UnixEpoch.AddDays(version),
            Options = new() { UsableAsActivity = reusable },
            Inputs = inputs,
            Outputs = [],
            Root = root
        };

    /// <summary>
    /// A leaf with no members. A direct-start leaf is an HTTP trigger by default, because only a trigger can start a
    /// workflow in Elsa 4 (a start-marked WriteLine is unconvertible).
    /// </summary>
    public static Elsa3Activity Leaf(string nodeId, string? type = null, bool directStart = false) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = type ?? (directStart ? "Elsa.HttpEndpoint" : "Elsa.WriteLine"),
        Version = 1,
        CustomProperties = new() { CanStartWorkflow = directStart }
    };

    public static Elsa3Activity Reference(
        string nodeId,
        string? targetVersionId = null,
        string? targetDefinitionId = null,
        int? targetVersion = null) => new()
    {
        Id = $"activity-{nodeId}",
        NodeId = nodeId,
        Name = nodeId,
        Type = targetDefinitionId ?? "Elsa.Workflow",
        Version = targetVersion,
        AdditionalProperties = new Dictionary<string, JsonElement>
        {
            [targetVersionId is null ? "workflowDefinitionId" : "workflowDefinitionVersionId"] =
                JsonSerializer.SerializeToElement(targetVersionId ?? targetDefinitionId)
        }
    };

    public static ReusableActivityImportCollection Collection(params Elsa3WorkflowDefinition[] definitions) =>
        new("fixture-collection", definitions);

    public static Elsa3ReusableActivityImportMaterializer Materializer(IActivityDefinitionLookup? catalog = null)
    {
        var scope = Support.ConversionServices.Create(catalog ?? new BuiltInActivityLookup()).CreateScope();
        return new(
            scope.ServiceProvider.GetRequiredService<Conversion.IElsa3DefinitionConverter>(),
            scope.ServiceProvider.GetRequiredService<Elsa3ArgumentDefinitionToInputOutput>(),
            scope.ServiceProvider.GetRequiredService<IActivityDefinitionLookup>());
    }

    /// <summary>
    /// The real credential-literal validator over a structure service with no handler, judging against
    /// <paramref name="catalog"/>; for tests whose catalog declares no credential input, so the rule refuses nothing.
    /// The tests of the rule at the import compose it as a host does, through <see cref="ImportServices"/>.
    /// </summary>
    public static ICredentialLiteralValidator Validator(IActivityDefinitionLookup? catalog = null) => new CredentialLiteralValidator(
        new CatalogVersionResolver(catalog ?? new BuiltInActivityLookup()),
        Options.Create(new WorkflowDesignValidatorOptions()),
        new ActivityTreeWalker(new DefaultActivityStructureService([])));

    /// <summary>
    /// The collection import as a host composes it: the registrations of <c>WorkflowDesignValidations</c> (the rule, its
    /// tree walker and the host's structure service), <c>Elsa3Mapping</c> and <c>Elsa3ImportJsonActivities</c>, over
    /// <paramref name="catalog"/> as the activity catalog and <paramref name="command"/> as the commit port. None of them
    /// registers a native structure handler; the import walks native payloads itself.
    /// </summary>
    public static ServiceProvider ImportServices(IActivityDefinitionLookup catalog, IReusableActivityImportCommand command)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActivityDefinitionLookup>(catalog);
        services.AddSingleton<IWellKnownTypeRegistry>(TypeRegistry());
        services.AddSingleton(SerializationServices.ObjectConverter());
        services.AddSingleton(command);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new Elsa3MappingFeature().ConfigureServices(services);
        new Elsa3ImportActivitiesFeature().ConfigureServices(services);
        // A host also composes the publish phase (spec 246); these tests stop at the commit port, so it publishes
        // into nothing and reports every version published.
        services.AddScoped(_ => new ReusableActivityImportPublishPhase(
            new NothingPublished(), new NothingPublished(), new NothingPublished(), new NothingPublished(), new NothingPublished()));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Empty Design stores and a publisher that accepts everything, for compositions that capture the commit.</summary>
    private sealed class NothingPublished :
        Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionPublicationStore,
        Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionAuthoringStore,
        Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionDraftStore,
        Elsa.Activities.Design.Persistence.Core.Contracts.ICreateActivityDraftCommand,
        IReusableActivityImportPublisher
    {
        public Task<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication?>(null);

        public Task<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication>>([]);

        Task<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionAuthoringState?> Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionAuthoringState?>(null);

        public Task<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionAuthoringState>>([]);

        Task<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionDraft?> Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionDraft?>(null);

        Task<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionDraft>> Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionDraftStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionDraft>>([]);

        public Task ExecuteAsync(Elsa.Activities.Design.Persistence.Core.Contracts.CreateActivityDraftRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(ReusableActivityImportPublishRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReusableActivityImportPublishOutcome.Applied);
    }

    /// <summary>The payload serializer the design stores write workflow state with.</summary>
    public static IPayloadSerializer PayloadSerializer() => SerializationServices.PayloadSerializer();

    private static IWellKnownTypeRegistry TypeRegistry()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        return registry;
    }

    /// <summary>A commit port that records the mutation it is handed and commits nothing.</summary>
    internal sealed class CapturingCommand : IReusableActivityImportCommand
    {
        public ReusableActivityImportMutation? Mutation { get; private set; }

        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default)
        {
            Mutation = mutation;
            return ValueTask.FromResult(new ReusableActivityImportCommitResult(false));
        }
    }

    /// <summary>
    /// A catalog holding one activity, <c>builtin-version</c>, that every Elsa 3 activity type maps to, declaring
    /// <paramref name="inputs"/>.
    /// </summary>
    internal sealed class BuiltInActivityLookup(params InputDefinition[] inputs) : IActivityDefinitionLookup
    {
        private readonly IActivityDefinition definition = new Definition();
        private readonly IActivityDefinitionVersion version = new VersionModel(inputs);

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<IActivityDefinition>>([definition]);
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(version);
        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) => Task.FromResult<IActivityDefinitionVersion?>(version);
        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>([new("builtin-version", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action)]);

        private sealed record Definition : IActivityDefinition
        {
            public string Id => "builtin-definition";
            public string ActivityTypeKey => "Elsa.WriteLine";
            public string Category => "Test";
            public string? DisplayName => "Built in";
            public string? Description => null;
        }

        private sealed record VersionModel(IEnumerable<InputDefinition> Inputs) : IActivityDefinitionVersion
        {
            public string Id => "builtin-version";
            public string Version => "1.0.0";
            public string DefinitionId => "builtin-definition";
            public string ProviderKey => "elsa.clr";
            public string ProviderSchemaVersion => "1";
            public string ConsumerKey => "elsa.clr";
            public string ConsumerSchemaVersion => "1";
            public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
            public string SourceKind => "CLR";
            public string SourceId => "Elsa.WriteLine";
            public IActivityDefinition Definition => new Definition();
            public IEnumerable<OutputDefinition> Outputs => [];
            public IEnumerable<ActivityDesignFacet> DesignFacets => [];
            public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
            public string? Hash => "builtin";
        }
    }
}
