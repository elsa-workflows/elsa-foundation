using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ReusableActivityFoundationalContractTests
{
    [Fact]
    public void PublicContract_UsesStableReferenceAndDurabilityKeys()
    {
        var contract = new ActivityContract(
            "1",
            [
                new ActivityInputContract(
                    "order",
                    "Order",
                    new TypeReference("acme.orders.order"),
                    true,
                    false,
                    new ActivityInputDefault("Literal", Json("{}")),
                    "elsa.json")
            ],
            [
                new ActivityOutputContract(
                    "total",
                    "Total",
                    new TypeReference("decimal"),
                    true,
                    true,
                    "elsa.json")
            ],
            [new ActivityOutcomeContract("done", "Done", true)]);

        Assert.Equal("order", contract.Inputs.Single().ReferenceKey);
        Assert.Equal("elsa.json", contract.Inputs.Single().StorageDriverKey);
        Assert.False(contract.Inputs.Single().IsNullable);
        Assert.True(contract.Outputs.Single().IsNullable);
        Assert.True(contract.Inputs.Single().IsRequired);
        Assert.True(contract.Outputs.Single().IsRequired);
        Assert.Equal(ActivityBoundaryDurability.Required, contract.Outputs.Single().Durability);
        Assert.Equal("done", contract.Outcomes.Single().ReferenceKey);
    }

    [Fact]
    public void Draft_and_immutable_version_serialization_preserve_exact_member_nullability()
    {
        var contract = new ActivityContract(
            "1",
            [new("input", "Input", new("String"), true, true, null, "elsa.json")],
            [new("output", "Output", new("String"), true, false, "elsa.json")],
            []);
        var provider = new ActivityProviderManifest("elsa.activity-graph", "1", Json("{}"));
        var draftState = new ActivityDefinitionDraftState(contract, provider, new Dictionary<string, string>());
        var version = new ActivityDefinitionVersionPublication
        {
            Id = "publication-1",
            DefinitionVersionId = "version-1",
            DefinitionId = "definition-1",
            Version = "1.0.0",
            ActivityTypeKey = "elsa.user.example.definition-1",
            Contract = contract,
            Provider = provider,
            TemplateId = "template-1",
            TemplateHash = "sha256-template",
            SourceReferenceId = "source-1",
            ProviderFingerprint = "provider/1",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 1,
            RuntimeRequirements = [],
            PublishedAt = DateTimeOffset.UnixEpoch
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var restoredDraft = JsonSerializer.Deserialize<ActivityDefinitionDraftState>(
            JsonSerializer.Serialize(draftState, options),
            options)!;
        var restoredVersion = JsonSerializer.Deserialize<ActivityDefinitionVersionPublication>(
            JsonSerializer.Serialize(version, options),
            options)!;

        Assert.True(Assert.Single(restoredDraft.Contract.Inputs).IsNullable);
        Assert.False(Assert.Single(restoredDraft.Contract.Outputs).IsNullable);
        Assert.True(Assert.Single(restoredVersion.Contract.Inputs).IsNullable);
        Assert.False(Assert.Single(restoredVersion.Contract.Outputs).IsNullable);
    }

    [Fact]
    public void SourceAuthorityAndForkOrigin_AreIndependentFacts()
    {
        var authority = new ActivityContentAuthority(
            ActivityContentAuthorityKind.ProviderSource,
            "elsa.clr",
            "Elsa.Activities.WriteLine");
        var origin = new ActivityDefinitionForkOrigin("definition-source", "version-source-3", "3.0.0");

        Assert.Equal(ActivityContentAuthorityKind.ProviderSource, authority.Kind);
        Assert.Equal("definition-source", origin.DefinitionId);
        Assert.NotEqual(authority.SourceId, origin.VersionId);
    }

    [Fact]
    public void DraftValidation_IsDerivedFromErrorDiagnostics()
    {
        var valid = Validation(ActivityDiagnosticSeverity.Warning);
        var invalid = Validation(ActivityDiagnosticSeverity.Error);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void ReusablePersistenceFacts_AreSiblingEntitiesAndDoNotExpandLegacyEfEntities()
    {
        Assert.True(typeof(TenantEntity).IsAssignableFrom(typeof(ActivityDefinitionAuthoringState)));
        Assert.True(typeof(TenantEntity).IsAssignableFrom(typeof(ActivityDefinitionDraft)));
        Assert.True(typeof(TenantEntity).IsAssignableFrom(typeof(ActivityDefinitionVersionPublication)));
        Assert.True(typeof(TenantEntity).IsAssignableFrom(typeof(ActivityDependencyEdge)));

        Assert.Null(typeof(ActivityDefinition).GetProperty(nameof(ActivityDefinitionAuthoringState.ContentAuthority)));
        Assert.Null(typeof(ActivityDefinition).GetProperty(nameof(ActivityDefinitionAuthoringState.HeadVersionId)));
        Assert.Null(typeof(ActivityDefinitionVersion).GetProperty(nameof(ActivityDefinitionVersionPublication.TemplateHash)));
        Assert.Null(typeof(ActivityDefinitionVersion).GetProperty(nameof(ActivityDefinitionVersionPublication.Contract)));
    }

    [Fact]
    public void ReadStoresAndMutationCommands_AreSeparatePorts()
    {
        Assert.All(
            typeof(IActivityDefinitionDraftStore).GetMethods(),
            method => Assert.True(method.Name.StartsWith("Find", StringComparison.Ordinal) || method.Name.StartsWith("List", StringComparison.Ordinal)));

        Assert.Contains(
            typeof(IReplaceActivityDraftCommand).GetMethods(),
            method => method.Name == nameof(IReplaceActivityDraftCommand.ExecuteAsync));
        Assert.DoesNotContain(
            typeof(IActivityDefinitionDraftStore).GetMethods(),
            method => method.Name.Contains("Save", StringComparison.Ordinal) || method.Name.Contains("Update", StringComparison.Ordinal));
    }

    [Fact]
    public void AtomicPublicationPort_CanCloseOverArtifactTypesWithoutRuntimeDependency()
    {
        var contract = typeof(ICommitActivityPublicationCommand<,>);
        var designCoreReferences = typeof(IActivityProvider).Assembly.GetReferencedAssemblies();
        var persistenceCoreReferences = typeof(ICommitActivityPublicationCommand<,>).Assembly.GetReferencedAssemblies();

        Assert.Equal(2, contract.GetGenericArguments().Length);
        Assert.DoesNotContain(designCoreReferences, x => x.Name!.Contains("Workflows.Runtime", StringComparison.Ordinal));
        Assert.DoesNotContain(designCoreReferences, x => x.Name!.Contains("Publishing", StringComparison.Ordinal));
        Assert.DoesNotContain(persistenceCoreReferences, x => x.Name!.Contains("Workflows.Runtime", StringComparison.Ordinal));
        Assert.DoesNotContain(persistenceCoreReferences, x => x.Name!.Contains("Publishing", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderContract_IsKeyedByStableProviderAndManifestSchema()
    {
        Assert.NotNull(typeof(IActivityProvider).GetProperty(nameof(IActivityProvider.ProviderKey)));
        Assert.Equal(
            typeof(IReadOnlySet<string>),
            typeof(IActivityProvider).GetProperty(nameof(IActivityProvider.SupportedManifestSchemas))!.PropertyType);
        Assert.NotNull(typeof(IActivityProviderRegistry).GetMethod(nameof(IActivityProviderRegistry.Resolve)));
    }

    private static ActivityDraftValidationState Validation(ActivityDiagnosticSeverity severity) => new()
    {
        Id = $"validation-{severity}",
        DraftId = "draft-1",
        Revision = 3,
        ValidatedAt = DateTimeOffset.UtcNow,
        Diagnostics =
        [
            new ActivityDiagnostic(
                "activity.test",
                severity,
                "Test diagnostic",
                new ActivityDiagnosticSubject("ActivityDraft", "draft-1", "definition-1", Revision: 3),
                Metadata: new Dictionary<string, string>())
        ]
    };

    private static JsonElement Json(string source) => JsonDocument.Parse(source).RootElement.Clone();
}
