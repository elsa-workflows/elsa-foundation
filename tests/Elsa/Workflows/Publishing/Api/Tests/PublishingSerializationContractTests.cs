using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Executable compatibility checks for the Publishing owner serializer boundary. These tests use
/// the generated resolver with the host's options so a reflection fallback cannot silently become
/// the API contract or retain a replaceable implementation generation.
/// </summary>
public sealed class PublishingSerializationContractTests
{
    private static readonly Type ContextType = typeof(WorkflowsPublishingApiFeature).Assembly.GetType(
        "Elsa.Workflows.Publishing.Api.WorkflowsPublishingJsonContext", throwOnError: true)!;

    private static readonly Type ResolverType = typeof(WorkflowsPublishingApiFeature).Assembly.GetType(
        "Elsa.Workflows.Publishing.Api.JsonTypeInfoResolver", throwOnError: true)!;

    private static readonly Type EnumConverterType = typeof(WorkflowsPublishingApiFeature).Assembly.GetType(
        "Elsa.Workflows.Publishing.Api.CamelCaseEnumConverter", throwOnError: true)!;

    private static readonly Type[] RouteContractTypes =
    [
        // Requests accepted by the 23 route registrations.
        typeof(ConstructActivity),
        typeof(ListConstructableActivities),
        typeof(ListIncidentStrategies),
        typeof(ListValueConversionProfiles),
        typeof(PreflightActivityDraftPublication),
        typeof(GetActivityPublicationReceipt),
        typeof(ListPublicationSlots),
        typeof(GetPublicationSlot),
        typeof(GetWorkflowPublicationPolicy),
        typeof(SetWorkflowPublicationPolicy),
        typeof(PreflightWorkflowPublication),
        typeof(PreflightWorkflowPublicationSnapshot),
        typeof(PublishWorkflowRequest),
        typeof(UnpublishPublicationSlotRequest),
        typeof(RestorePublicationSlotRequest),
        typeof(PublishActivityDraft),
        typeof(RunRuntimeRequirementPreflight),
        typeof(StartWorkflowTestRun),
        typeof(StartWorkflowDraftTestRun),
        typeof(StartActivityDraftTestRun),
        typeof(GetActivityDraftTestRun),
        typeof(GetActivityDraftTestRunByIdempotencyKey),
        typeof(CancelActivityDraftTestRun),

        // Responses emitted by the route mapper.
        typeof(IEnumerable<ConstructableActivityView>),
        typeof(ConstructableActivityView),
        typeof(ConstructedActivityView),
        typeof(IncidentStrategiesResponse),
        typeof(ValueConversionProfilesResponse),
        typeof(PublicationPreflightView),
        typeof(PublicationSnapshotPreflightView),
        typeof(PublicationSlotListResponse),
        typeof(PublicationSlotView),
        typeof(PublicationPolicyView),
        typeof(PublishedWorkflowView),
        typeof(RuntimeRequirementPreflightView),
        typeof(ActivityPublicationPreflightView),
        typeof(ActivityPublicationReceiptView),
        typeof(ActivityDraftTestRunView),
        typeof(WorkflowTestRunView),
        typeof(ActivityPublishingDiagnosticView),
        typeof(ActivityPublishingProblemDetails),
        typeof(ExpressionPublicationValidationDiagnosticView),
        typeof(ExpressionPublicationValidationProblemDetails),
        typeof(RuntimePreflightProblemDetails),

        // Explicit roots for nested/opaque values and enum wire contracts.
        typeof(ActivityDraftTestRunInput),
        typeof(ActivityDiagnostic),
        typeof(ActivityDependencyPathItem),
        typeof(ActivityNode),
        typeof(ActivityNodeOrigin),
        typeof(ActivityNodeStructure),
        typeof(ActivityVersionBump),
        typeof(ActivityVersionCompatibility),
        typeof(ActivityVersionDiff),
        typeof(ActivityVersionDiffSummary),
        typeof(ActivityVersionChange),
        typeof(ActivityVersionChangeSubject),
        typeof(ActivityVersionIdentity),
        typeof(ActivityVersionProviderDiff),
        typeof(ActivityPublicationOutcome),
        typeof(ArgumentState),
        typeof(ArgumentValue),
        typeof(AuthoredValueConversionLimits),
        typeof(AuthoredValueConversionMode),
        typeof(AuthoredValueConversionProfile),
        typeof(AuthoredValueConversionRequest),
        typeof(AuthoredWorkflowIntrinsic),
        typeof(AuthoredWorkflowIntrinsicKind),
        typeof(CollectionKind),
        typeof(IncidentStrategyReference),
        typeof(PublicationFailure),
        typeof(WorkflowDefinitionState),
        typeof(ActivityPresentationRecord),
        typeof(InputDefinition),
        typeof(OutputDefinition),
        typeof(TypeReference),
        typeof(ValueRepresentation),
        typeof(VariableDefinition),
        typeof(VariableReference),
        typeof(WorkflowCheckpointCadenceOptions),
        typeof(WorkflowStrategyOptions),
        typeof(JsonElement),
        typeof(PublicationActionView),
        typeof(PublicationPolicyDefaultActionView),
        typeof(PublicationPolicySourceView),
        typeof(PublicationTriggerChangeKindView),
        typeof(PublicationTriggerCardinalityView),
        typeof(PublicationStatusView)
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
        var missing = PublishingApiContractSurface.ExportedContractNamespaceTypes()
            .Except(PublishingApiContractSurface.ImplementationOnlyModelTypes)
            .Where(type => !type.IsAbstract && !typeof(Exception).IsAssignableFrom(type) && !type.ContainsGenericParameters)
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
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add((JsonConverter)Activator.CreateInstance(EnumConverterType)!);
        options.TypeInfoResolverChain.Add((IJsonTypeInfoResolver)Activator.CreateInstance(ResolverType)!);
        options.TypeInfoResolverChain.Add(fallback);

        foreach (var type in RouteContractTypes)
        {
            var info = options.GetTypeInfo(type);
            Assert.NotNull(info);
            Assert.Same(options, info!.Options);
        }

        var view = new PublicationPolicyView(
            "definition-1",
            PublicationPolicyDefaultActionView.RequireExplicitSlot,
            "default",
            PublicationPolicySourceView.Workflow,
            4,
            DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        var json = JsonSerializer.Serialize(view, options.GetTypeInfo(typeof(PublicationPolicyView))!);

        Assert.Contains("\"defaultAction\":\"requireExplicitSlot\"", json, StringComparison.Ordinal);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public void Feature_registers_owner_resolver_and_fastendpoints_compatible_options()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        Assert.IsType(ResolverType, options.TypeInfoResolverChain[0]);
        Assert.Contains(options.Converters, converter => converter.GetType() == EnumConverterType);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Same(JsonNamingPolicy.CamelCase, options.DictionaryKeyPolicy);
    }

    [Fact]
    public void Effective_options_preserve_casing_string_enums_dictionary_keys_and_explicit_nulls()
    {
        var options = Context.Options;

        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Same(JsonNamingPolicy.CamelCase, options.DictionaryKeyPolicy);

        var testRun = new WorkflowTestRunView(
            "run-1",
            "definition-1",
            "version-1",
            null,
            null,
            "completed",
            null,
            null,
            null,
            new Dictionary<string, string> { ["SomeMetadata"] = "value" });
        var json = Serialize(testRun);

        Assert.Contains("\"someMetadata\":\"value\"", json, StringComparison.Ordinal);
        Assert.Contains("\"artifactId\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"expiresAt\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Input_is_case_insensitive_and_dictionary_keys_are_camel_case()
    {
        var input = """
            {
              "VERSIONID": "version-1",
              "action": "sideBySide",
              "slotName": "canary"
            }
            """;
        var request = Deserialize<PublishWorkflowRequest>(input);

        Assert.Equal("version-1", request.VersionId);
        Assert.Equal(PublicationActionView.SideBySide, request.Action);

        var testRun = new StartWorkflowTestRun(
            "version-1",
            new Dictionary<string, JsonElement> { ["SomeInput"] = JsonDocument.Parse("42").RootElement.Clone() });
        var testRunJson = Serialize(testRun);
        Assert.Contains("\"someInput\":42", testRunJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SomeInput\"", testRunJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Opaque_json_and_route_fields_preserve_the_wire_contract()
    {
        var view = new ConstructedActivityView(
            "activity-version-1",
            "Acme.Activity",
            JsonDocument.Parse("{\"providerSpecific\":{\"keep\":true}}").RootElement.Clone(),
            [new ArgumentView("input.value", "Value", "System.String")],
            []);
        var json = Serialize(view);
        var roundTrip = Deserialize<ConstructedActivityView>(json);

        Assert.True(JsonElement.DeepEquals(view.DescriptorPayload, roundTrip.DescriptorPayload));
        Assert.True(roundTrip.DescriptorPayload.GetProperty("providerSpecific").GetProperty("keep").GetBoolean());

        var preflight = new PreflightActivityDraftPublication("draft-1", 7, null) { Version = "2.0" };
        var preflightJson = Serialize(preflight);
        Assert.DoesNotContain("draftId", preflightJson, StringComparison.Ordinal);
        Assert.Contains("\"expectedDraftRevision\":7", preflightJson, StringComparison.Ordinal);
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
                ?? throw new InvalidOperationException("WorkflowsPublishingJsonContext could not be constructed."));
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
