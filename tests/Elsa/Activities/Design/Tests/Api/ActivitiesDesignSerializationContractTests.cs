using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>
/// Executable contract for the Activities Design owner serializer boundary.
/// These tests intentionally exercise the generated resolver rather than a general-purpose
/// <see cref="JsonSerializerOptions"/> instance: a reflection fallback would defeat the unload
/// boundary and would make the Minimal API/OpenAPI metadata graph generation-dependent.
/// </summary>
public sealed class ActivitiesDesignSerializationContractTests
{
    private static readonly Type ContextType = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
        "Elsa.Activities.Design.Api.ActivitiesDesignJsonContext",
        throwOnError: true)!;

    private static readonly Type ResolverType = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
        "Elsa.Activities.Design.Api.ActivitiesDesignJsonTypeInfoResolver",
        throwOnError: true)!;

    private static readonly Type EnumConverterType = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
        "Elsa.Activities.Design.Api.ActivitiesDesignCamelCaseEnumConverter",
        throwOnError: true)!;

    private static readonly Type[] RouteContractTypes =
    [
        // Availability, catalog, and capability operations.
        typeof(GetActivityAvailabilitySettings),
        typeof(ActivityAvailabilitySettings),
        typeof(ListActivityAvailabilityDiagnostics),
        typeof(ActivityAvailabilityDiagnostics),
        typeof(SaveActivityAvailabilitySettings),
        typeof(ListActivityAuthoringCatalog),
        typeof(ActivityAuthoringCatalogView),
        typeof(GetActivityAuthoringCapabilities),
        typeof(ActivityAuthoringCapabilitiesView),
        typeof(GetDefinition),
        typeof(GetReusableActivityVersion),

        // Definitions and reusable authoring operations.
        typeof(CreateReusableActivityDefinition),
        typeof(ReusableActivityDefinitionMutationView),
        typeof(ListReusableActivityDefinitions),
        typeof(ActivityManagementPageView<ReusableActivityDefinitionManagementView>),
        typeof(GetReusableActivityDefinition),
        typeof(ReusableActivityDefinitionManagementView),
        typeof(UpdateReusableActivityDefinition),
        typeof(ActivityDefinitionIdentityView),
        typeof(PreviewReusableActivityFork),
        typeof(ActivityForkPreviewView),
        typeof(SetRecommendedReusableActivityVersion),
        typeof(ActivityDefinitionRecommendationView),
        typeof(ListRecommendedActivityDefinitions),
        typeof(RecommendedActivityDefinitionPageView),
        typeof(ListReusableActivityDrafts),
        typeof(ActivityManagementPageView<ReusableActivityDraftManagementView>),
        typeof(ListReusableActivityVersions),
        typeof(ActivityManagementPageView<ReusableActivityVersionManagementView>),
        typeof(CreateReusableActivityDraft),
        typeof(ReusableActivityDraftView),

        // Draft, fork, and contract operations.
        typeof(GetReusableActivityDraft),
        typeof(ReplaceReusableActivityDraft),
        typeof(UpdateReusableActivityDraftPresentation),
        typeof(CreateReusableActivityDraftConflictCopy),
        typeof(ValidateReusableActivityDraft),
        typeof(ActivityDraftValidationView),
        typeof(MigrateReusableActivityDraft),
        typeof(ProposeReusableActivityContract),
        typeof(ActivityContractProposalView),
        typeof(ApplyReusableActivityContractProposal),
        typeof(DiscardReusableActivityDraft),
        typeof(PreviewActivityDraftDiff),
        typeof(ActivityVersionDiffView),
        typeof(ApplyReusableActivityFork),
        typeof(ActivityForkReceiptView),
        typeof(GetReusableActivityForkStatus),

        // Versions, dependencies, lifecycle, and upgrades.
        typeof(GetActivityDependencies),
        typeof(ActivityDependencyPageView),
        typeof(CompareActivityVersions),
        typeof(GetVersion),
        typeof(ActivityDefinitionVersionDetailsView),
        typeof(ListDefinitionVersions),
        typeof(IEnumerable<ActivityDefinitionVersionSummary>),
        typeof(RetireReusableActivityVersion),
        typeof(RestoreReusableActivityVersion),
        typeof(RevokeReusableActivityVersion),
        typeof(ReusableActivityVersionLifecycleView),
        typeof(CreateActivityUpgradePlan),
        typeof(ActivityUpgradePlanView),
        typeof(GetActivityUpgradePlan),
        typeof(ApplyActivityUpgradePlan),
        typeof(ActivityUpgradeApplyResultView),
        typeof(GetActivityUpgradeApplyReceipt),
        typeof(ActivityUpgradeApplyReceiptView),
        typeof(RefreshActivityUpgradePlan),

        // Shared errors and explicit nested provider/contract roots.
        typeof(ActivityProblemDetailsView),
        typeof(ActivityRecoveryView),
        typeof(ActivityContractView),
        typeof(ActivityProviderManifestView),
        typeof(ActivityProviderManifest),
        typeof(ActivityLayoutRecord),
        typeof(ActivityArgumentValue),
        typeof(ActivityDesignFacet),
        typeof(InputDefinition),
        typeof(OutputDefinition),
        typeof(ActivityTypeKeyRules),
        typeof(ActivityAvailabilityRuleSet),
        typeof(ActivityDiagnostic),
        typeof(ActivityUpgradeExpectedSnapshot),
        typeof(ActivityUpgradeStep),
        typeof(ActivityUpgradeStage),
        typeof(ActivityUpgradeApplyReceipt),
        typeof(ActivityUpgradePublicationReceipt),
        typeof(JsonElement)
    ];

    [Fact]
    public void Every_route_accept_and_produces_type_has_generated_metadata()
    {
        var context = Context;
        var missing = RouteContractTypes
            .Where(type => context.GetTypeInfo(type) is null)
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_concrete_public_api_model_has_generated_metadata()
    {
        var context = Context;
        var missing = typeof(ActivitiesDesignApiFeature).Assembly
            .GetTypes()
            .Where(type =>
                type.Namespace == "Elsa.Activities.Design.Api.Models" &&
                type.IsPublic &&
                !type.IsAbstract &&
                !typeof(Exception).IsAssignableFrom(type) &&
                !type.ContainsGenericParameters)
            .Where(type => context.GetTypeInfo(type) is null)
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Resolver_precedes_fallback_and_honors_the_host_options()
    {
        var fallback = new CountingResolver();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add((JsonConverter)Activator.CreateInstance(EnumConverterType)!);
        options.TypeInfoResolverChain.Add((IJsonTypeInfoResolver)Activator.CreateInstance(ResolverType)!);
        options.TypeInfoResolverChain.Add(fallback);

        foreach (var type in RouteContractTypes)
        {
            var info = options.GetTypeInfo(type);
            Assert.NotNull(info);
            Assert.Same(options, info!.Options);
        }

        var summary = new ReusableActivityDraftSummaryView(
            "draft-1",
            "definition-1",
            2,
            null,
            ActivityDefinitionDraftStatus.Active,
            "provider",
            "1.0",
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        var summaryJson = JsonSerializer.Serialize(summary, options.GetTypeInfo(typeof(ReusableActivityDraftSummaryView))!);
        Assert.Contains("\"status\":\"active\"", summaryJson, StringComparison.Ordinal);

        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public void Feature_registers_owner_resolver_and_fastendpoints_compatible_enum_options()
    {
        var services = new ServiceCollection();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        Assert.IsType(ResolverType, options.TypeInfoResolverChain[0]);
        Assert.Contains(options.Converters, converter => converter.GetType() == EnumConverterType);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Same(JsonNamingPolicy.CamelCase, options.DictionaryKeyPolicy);

        var summary = new ReusableActivityDraftSummaryView(
            "draft-1",
            "definition-1",
            2,
            null,
            ActivityDefinitionDraftStatus.Active,
            "provider",
            "1.0",
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        var json = JsonSerializer.Serialize(summary, options.GetTypeInfo(typeof(ReusableActivityDraftSummaryView))!);

        Assert.Contains("\"status\":\"active\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_options_match_fastendpoints_for_case_enum_dictionary_and_nulls()
    {
        var options = Context.Options;

        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Same(JsonNamingPolicy.CamelCase, options.DictionaryKeyPolicy);

        var summary = new ReusableActivityDraftSummaryView(
            "draft-1",
            "definition-1",
            2,
            null,
            ActivityDefinitionDraftStatus.Active,
            "provider",
            "1.0",
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        var json = Serialize(summary);

        Assert.Contains("\"status\":\"active\"", json, StringComparison.Ordinal);
        Assert.Contains("\"updatedAt\":\"2026-01-02T03:04:05+00:00\"", json, StringComparison.Ordinal);

        var definition = new ActivityDefinitionView("id", "type", "category", null, null);
        var definitionJson = Serialize(definition);
        Assert.Contains("\"displayName\":null", definitionJson, StringComparison.Ordinal);
        Assert.Contains("\"description\":null", definitionJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Input_is_case_insensitive_and_dictionary_keys_are_camel_case()
    {
        var input = """
            {
              "CONTRACTSCHEMAVERSION": "1.0",
              "inputs": [],
              "outputs": [],
              "outcomes": []
            }
            """;
        var contract = Deserialize<ActivityContractView>(input);
        Assert.Equal("1.0", contract.ContractSchemaVersion);

        var template = new ActivityAuthoringTemplateView(
            "node-1",
            "version-1",
            new Dictionary<string, ActivityArgumentValue>
            {
                ["SomeInput"] = new(JsonDocument.Parse("{\"value\":42}").RootElement.Clone(), "Literal")
            },
            new Dictionary<string, ActivityArgumentValue>(),
            null);
        var json = Serialize(template);

        Assert.Contains("\"someInput\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SomeInput\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_activity_type_key_and_opaque_provider_payload_preserve_wire_rules()
    {
        var contract = new ActivityContractView("1.0", [], [], []);
        var command = new CreateReusableActivityDefinition(
            "category",
            "Display",
            null,
            new ActivityProviderManifest(
                "provider",
                "1.0",
                JsonDocument.Parse("{\"providerSpecific\":{\"keep\":true}}").RootElement.Clone()),
            contract,
            [],
            ActivityTypeKey: null);
        var commandJson = Serialize(command);

        Assert.DoesNotContain("activityTypeKey", commandJson, StringComparison.Ordinal);

        var provider = new ActivityProviderManifestView(
            "provider",
            "1.0",
            "fingerprint",
            JsonDocument.Parse("{\"opaque\":{\"nested\":true},\"items\":[1,2]}").RootElement.Clone());
        var providerJson = Serialize(provider);
        var roundTrip = Deserialize<ActivityProviderManifestView>(providerJson);

        Assert.True(JsonElement.DeepEquals(provider.Payload!.Value, roundTrip.Payload!.Value));
        Assert.Equal(JsonValueKind.Object, roundTrip.Payload!.Value.ValueKind);
        Assert.True(roundTrip.Payload.Value.GetProperty("opaque").GetProperty("nested").GetBoolean());
    }

    private static JsonSerializerContext Context
    {
        get
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };
            options.Converters.Add((JsonConverter)Activator.CreateInstance(EnumConverterType)!);
            return (JsonSerializerContext)(Activator.CreateInstance(ContextType, options)
                ?? throw new InvalidOperationException("ActivitiesDesignJsonContext could not be constructed."));
        }
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Context.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"No generated metadata for {typeof(T).FullName}."));

    private static T Deserialize<T>(string json) =>
        (T)(JsonSerializer.Deserialize(json, Context.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"No generated metadata for {typeof(T).FullName}."))
            ?? throw new InvalidOperationException($"Generated metadata returned null for {typeof(T).FullName}."));

    private sealed class CountingResolver : IJsonTypeInfoResolver
    {
        public int Calls { get; private set; }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            Calls++;
            return null;
        }
    }
}
