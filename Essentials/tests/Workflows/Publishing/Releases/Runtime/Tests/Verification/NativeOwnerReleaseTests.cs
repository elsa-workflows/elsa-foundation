using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ActivityContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeHasher = Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

/// <summary>Real node compiler + real owner adapters. This does not assert publication custody, target import or activation.</summary>
public sealed class NativeOwnerReleaseTests
{
    [Theory]
    [InlineData("WriteLine")]
    [InlineData("WriteLines")]
    [InlineData("Delay")]
    public async Task Actual_compiler_body_passes_through_fixed_wire_and_real_owner_inspection(string activity)
    {
        using var source = Source(activity); var compiled = source.Compile(); var workflow = Workflow(compiled.Root, compiled.ResumeTargets);
        var fixture = new RuntimeReleaseFixture(); var artifact = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var selected = fixture.Add(fixture.Codec.EncodeWorkflowSelection(RuntimeReleaseFixture.Selection(workflow)));
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        var ownerFacts = await Inspect(fixture, artifact, scope.ServiceProvider);
        Assert.All(ownerFacts, answer => Assert.Equal(ReleaseSemanticStatus.Verified, answer.Status));
        var transport = await fixture.Transport(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), selected.Key, artifact.Key, ownerFacts);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(transport);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Contains(result.Prerequisites, prerequisite => prerequisite.Kind == "activity-module");
        if (activity == "Delay") Assert.Contains(result.Prerequisites, prerequisite => prerequisite.Kind == "timer-provider" && prerequisite.Identity == Delay.TimerTriggerProviderId);
    }

    [Fact]
    public async Task Source_owned_primitive_template_keeps_unbound_required_input_and_consumer_version_one()
    {
        using var source = Source("WriteLine"); var root = source.Compiler.CompileSourceOwnedRoot(source.Catalog);
        Assert.Equal("1", root.ActivityTypeVersion); Assert.Empty(root.InputBindings);
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Template(root))));
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.All(await Inspect(fixture, member, scope.ServiceProvider), answer => Assert.Equal(ReleaseSemanticStatus.Verified, answer.Status));
    }

    [Theory]
    [InlineData(RuntimeMetadataKeys.StructuralFaultHandling, "child")]
    [InlineData(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild, "true")]
    public async Task Well_formed_runtime_hint_cannot_grant_a_primitive_an_unsupported_capability(string key, string value)
    {
        using var source = Source("WriteLine"); var compiled = source.Compile(); var node = compiled.Root;
        var metadata = node.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value); metadata.Add(key, value);
        var changed = new ExecutableNode(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion, node.Descriptor,
            node.InputBindings, node.OutputCaptures, metadata, node.ChildSlots, node.Structure, node.ActivityContract);
        var fixture = new RuntimeReleaseFixture(); var artifact = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, Workflow(changed, compiled.ResumeTargets))));
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var answers = await Inspect(fixture, artifact, scope.ServiceProvider);
        Assert.Contains(answers, answer => answer.Status == ReleaseSemanticStatus.Invalid && answer.Diagnostics.Any(diagnostic => diagnostic.Code == "primitive_activity_shape_mismatch"));
    }

    [Fact]
    public async Task Delay_owner_checks_the_actual_resume_handler_even_when_native_hash_is_consistent()
    {
        using var source = Source("Delay"); var compiled = source.Compile();
        var changed = compiled.ResumeTargets.ToDictionary(pair => pair.Key, pair => pair.Value with { HandlerKey = "AnotherHandler" });
        var fixture = new RuntimeReleaseFixture(); var artifact = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, Workflow(compiled.Root, changed))));
        Assert.Equal(ReleaseSemanticStatus.Verified, (await fixture.Verify(artifact)).Status);
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Contains(await Inspect(fixture, artifact, scope.ServiceProvider), answer => answer.Diagnostics.Any(diagnostic => diagnostic.Code == "delay_resume_target_mismatch"));
    }

    [Fact]
    public async Task Retained_pascal_descriptor_alias_is_refused_without_rewriting_the_original_blob()
    {
        using var source = Source("WriteLine"); var compiled = source.Compile(); var node = compiled.Root;
        var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(WriteLine))));
        var changed = new ExecutableNode(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion,
            new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", descriptor), node.InputBindings, node.OutputCaptures,
            node.Metadata, activityContract: node.ActivityContract);
        var fixture = new RuntimeReleaseFixture(); var encoded = fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, Workflow(changed, compiled.ResumeTargets)));
        var retained = encoded.GetPayloadBytes(); var member = fixture.Add(encoded);
        Assert.Equal(ReleaseSemanticStatus.Unsupported, (await fixture.Verify(member)).Status);
        Assert.Equal(retained, encoded.GetPayloadBytes());
    }

    [Theory]
    [InlineData("valid", ReleaseSemanticStatus.Verified)]
    [InlineData("unknown-contract-field", ReleaseSemanticStatus.Unsupported)]
    [InlineData("changed-input-type", ReleaseSemanticStatus.Invalid)]
    [InlineData("changed-provider-alias", ReleaseSemanticStatus.Invalid)]
    [InlineData("future-default-syntax", ReleaseSemanticStatus.Unsupported)]
    public async Task Source_owned_CLR_catalog_checks_neutral_public_contract_and_exact_provider_association(string change, ReleaseSemanticStatus expected)
    {
        using var source = Source("WriteLine"); var root = source.Compiler.CompileSourceOwnedRoot(source.Catalog);
        var fingerprint = ActivityTemplateHasher.ComputeCanonicalValueHash(new
        {
            source.Catalog.ProviderKey, source.Catalog.ProviderSchemaVersion, source.Catalog.ConsumerKey,
            source.Catalog.ConsumerSchemaVersion, descriptor = source.Catalog.DescriptorPayload
        });
        Assert.Equal("sha256:2aa74acfae37a2868c562c227c95549074dff1679260a87ca0d4426f8d38a285", fingerprint);
        var requirements = new[] { new RuntimeRequirement(root.Descriptor.ConsumerKey, root.Descriptor.SchemaVersion) };
        var hash = ActivityTemplateHasher.Compute(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], requirements, [], fingerprint, new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate("activity-template-" + hash[7..], hash, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], requirements,
            fingerprint, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        var contract = new Elsa.Activities.Design.Core.Models.ActivityContract("1",
            [new("text", nameof(WriteLine.Text), new("String"), true, false, null, WellKnownRuntimeDurableValueStorageDrivers.Json)], [], [new("Done", "Done", true)]);
        var publication = new ActivityCatalogProjection().Publication(source.Catalog.Definition!, contract,
            new(source.Catalog.ProviderKey, source.Catalog.ProviderSchemaVersion, source.Catalog.DescriptorPayload), source.Catalog.Id, source.Catalog.Version,
            fingerprint, "source-reference", template, new(1, 1, 0, 1, 0, 0, 1), DateTimeOffset.UnixEpoch,
            Elsa.Activities.Design.Core.Models.ActivityDefinitionVersionResolutionKind.AuthorableActivity);
        var contractJson = JsonNode.Parse(JsonSerializer.Serialize(publication.Contract, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        if (change == "unknown-contract-field") contractJson["futureFacet"] = new JsonObject { ["opaque"] = true };
        if (change == "changed-input-type") contractJson["inputs"]![0]!["type"]!["alias"] = "Int32";
        if (change == "future-default-syntax") contractJson["inputs"]![0]!["default"] = new JsonObject { ["syntax"] = "Future", ["value"] = "retained" };
        var provider = change == "changed-provider-alias" ? RuntimeReleaseFixture.Json("{\"typeAlias\":\"another.Owner\"}") : publication.Provider.Payload;
        var projection = new ReleaseActivityVersion(RuntimeReleaseFixture.Scope, publication.DefinitionId, publication.DefinitionVersionId, publication.Version,
            publication.ActivityTypeKey, "AuthorableActivity", RuntimeReleaseFixture.Json(contractJson.ToJsonString()), publication.Provider.ProviderKey, publication.Provider.SchemaVersion,
            provider, fingerprint, template.TemplateId, template.TemplateHash, publication.SourceReferenceId, "ClrActivityType", typeof(WriteLine).FullName!);
        var fixture = new RuntimeReleaseFixture(); fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, template)));
        var member = fixture.Add(fixture.Codec.EncodeActivityVersion(projection));
        using var services = Services().BuildServiceProvider(); using var scope = services.CreateScope();
        var answers = await Inspect(fixture, member, scope.ServiceProvider);
        if (expected == ReleaseSemanticStatus.Verified) Assert.All(answers, answer => Assert.Equal(expected, answer.Status));
        else Assert.Contains(answers, answer => answer.Status == expected);
    }

    private static async Task<IReadOnlyList<ReleaseOccurrenceFacts>> Inspect(RuntimeReleaseFixture fixture, ReleaseContentMember member, IServiceProvider services)
    {
        var artifacts = await fixture.Verify(member); Assert.Equal(ReleaseSemanticStatus.Verified, artifacts.Status);
        var inspectors = services.GetServices<IReleaseOccurrenceInspector>().ToArray(); var result = new List<ReleaseOccurrenceFacts>();
        foreach (var site in artifacts.Sites)
            result.Add(await inspectors.Single(inspector => inspector.SupportedSites.Contains(site.Site)).InspectAsync(site, fixture));
        return result;
    }
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection(); var transport = new WorkflowReleaseTransportFeature(); transport.ConfigureServices(services); transport.PostConfigureServices(services);
        var semantic = new WorkflowReleaseVerificationFeature(); semantic.ConfigureServices(services); semantic.PostConfigureServices(services);
        services.AddScoped<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableHasher, RuntimeHasher>();
        var runtime = new WorkflowReleaseRuntimeFeature(); runtime.ConfigureServices(services); runtime.PostConfigureServices(services);
        new ActivitiesPrimitivesReleaseVerificationFeature().ConfigureServices(services); new ActivitiesSchedulingReleaseVerificationFeature().ConfigureServices(services);
        return services;
    }
    private static WorkflowExecutable Workflow(ExecutableNode root, IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resume)
    {
        var input = new WorkflowExecutableInputContract(1, []); var hasher = new RuntimeHasher(); var hash = hasher.ComputeHash(root, input, []);
        return new(new(hasher.CreateArtifactId("native-owner-", hash), "definition", "version", "1.0.0", hash), root, resume, DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(), input, [], IncidentStrategyBuiltIns.FaultReference);
    }

    private static CompilerSource Source(string activity) => new(activity);
    private sealed class CompilerSource : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly string _key;
        private readonly JsonElement _literal;
        public ActivityDefinitionVersion Catalog { get; }
        public ExecutableNodeCompiler Compiler { get; }
        private DefaultActivityStructureService Structures { get; } = new([]);
        public CompilerSource(string activity)
        {
            var services = new ServiceCollection(); new SerializationFeature().ConfigureServices(services); _services = services.BuildServiceProvider();
            var registry = _services.GetRequiredService<IWellKnownTypeRegistry>(); var serializer = _services.GetRequiredService<IPayloadSerializer>();
            var type = activity switch { "WriteLine" => typeof(WriteLine), "WriteLines" => typeof(WriteLines), _ => typeof(Delay) };
            foreach (var known in new[] { type, typeof(string), typeof(TimeSpan), typeof(ActivityUnit) }) registry.RegisterType(known, TypeAliasConvention.CanonicalAlias(known));
            _key = activity switch { "WriteLine" => "text", "WriteLines" => nameof(WriteLines.Lines), _ => nameof(Delay.Duration) };
            var typeReference = activity == "WriteLines" ? new TypeReference("String", CollectionKind.List) : new TypeReference(activity == "Delay" ? "TimeSpan" : "String");
            _literal = activity switch { "WriteLine" => serializer.SerializeToElement("captured text"), "WriteLines" => serializer.SerializeToElement(new[] { "one", "two" }), _ => serializer.SerializeToElement(TimeSpan.FromSeconds(2)) };
            Catalog = new("1.0.0", "catalog-definition")
            {
                Id = "catalog-version", Definition = new() { Id = "catalog-definition", ActivityTypeKey = TypeAliasConvention.CanonicalAlias(type), Category = "Test" },
                ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity, ProviderSchemaVersion = "1", ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity, ConsumerSchemaVersion = "1",
                DescriptorPayload = serializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(type))),
                Inputs = [new(_key, activity == "WriteLine" ? nameof(WriteLine.Text) : _key, typeReference, null, _key, null,
                    IsNullable: activity == "WriteLines", IsRequired: activity == "WriteLine")]
            };
            var inputs = new RuntimeInputBindingCompiler(registry); var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            Compiler = new(Structures, registry, inputs, outputs);
        }
        public (ExecutableNode Root, IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets) Compile()
        {
            var authored = new ActivityNode("compiled", Catalog.Id, [new(_key, new ArgumentValue(_literal, "Literal"), null, null, null, null)], []);
            var root = Compiler.CompileRoot(authored, new ActivityTreeProjector(Structures).Project(authored), new Dictionary<string, ActivityDefinitionVersion> { [Catalog.Id] = Catalog });
            return (root, Compiler.BuildResumeTargets(root));
        }
        public void Dispose() => _services.Dispose();
    }
}
