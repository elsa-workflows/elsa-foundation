using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Design;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Agent.Specialists.Tests.Publication;

public sealed class SpecialistProviderTests
{
    [Fact]
    public async Task Explicit_continuity_proposals_compile_as_schema_two_without_model_business_controls()
    {
        await using var services = Services(new MetadataFactory());
        var provider = services.GetRequiredService<IActivityProvider>();
        var request = Request(Manifest() with { AllowContinuity = true });
        var legacy = await provider.ProposeContractAsync(new(request.Provider, request.Contract));
        Assert.Empty(legacy.Changes);
        var old = (await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(request)).ExecutableRoot!;
        Assert.Equal("1", old.DescriptorSchemaVersion);
        Assert.False(old.DescriptorPayload.TryGetProperty("invocationControlsProfile", out _));
        request = request with { Provider = request.Provider with { SchemaVersion = SpecialistRuntimeIds.ContinuityManifestSchema } };
        var proposed = await provider.ProposeContractAsync(new(request.Provider, request.Contract));
        Assert.Empty(proposed.Diagnostics);
        Assert.Equal(2, proposed.Changes.Count);
        Assert.Contains((await provider.ValidateAsync(request.Provider, request.Contract)), x => x.Code == "specialist.controls.required");
        request = request with { Contract = request.Contract with { Inputs = [.. request.Contract.Inputs, Assert.Single(proposed.Changes, x => x.Input is not null).Input!],
            Outputs = [.. request.Contract.Outputs, Assert.Single(proposed.Changes, x => x.Output is not null).Output!] } };
        Assert.Empty((await provider.ProposeContractAsync(new(request.Provider, request.Contract))).Changes);
        var compiled = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(request);
        Assert.Empty(compiled.Diagnostics);
        var root = compiled.ExecutableRoot!;
        Assert.Equal("2", root.DescriptorSchemaVersion);
        Assert.Equal("2", Assert.Single(compiled.RuntimeRequirements).SchemaVersion);
        Assert.False(root.ActivityContract!.Inputs[SpecialistInvocationControls.InputKey].IsRequired);
        // The native profile represents optional absence through IsNullable; control parsing still refuses explicit null.
        Assert.True(root.ActivityContract.Inputs[SpecialistInvocationControls.InputKey].IsNullable);
        Assert.Equal("Brief", Assert.Single(root.DescriptorPayload.GetProperty("inputSchema").GetProperty("properties").EnumerateObject()).Name);
        Assert.Equal("Article", Assert.Single(root.DescriptorPayload.GetProperty("resultSchema").GetProperty("properties").EnumerateObject()).Name);
        Assert.NotNull((await provider.MigrateAsync(new(Request().Provider, SpecialistRuntimeIds.ContinuityManifestSchema))).Manifest);
        Assert.Null((await provider.MigrateAsync(new(request.Provider, SpecialistRuntimeIds.ManifestSchema))).Manifest);
    }

    [Theory]
    [InlineData("input-name")]
    [InlineData("input-key")]
    [InlineData("output-name")]
    [InlineData("output-root")]
    [InlineData("output-key")]
    public async Task Continuity_does_not_reinterpret_business_member_collisions(string collision)
    {
        await using var services = Services(new MetadataFactory());
        var provider = services.GetRequiredService<IActivityProvider>();
        var request = Request(Manifest() with { AllowContinuity = true });
        var contract = request.Contract;
        if (collision.StartsWith("input", StringComparison.Ordinal)) contract = contract with { Inputs = [contract.Inputs[0] with
            { Name = collision == "input-name" ? "agentOptions" : "Brief", ReferenceKey = collision == "input-key" ? "$SPECIALIST.options" : "brief-key" }] };
        else contract = contract with { Outputs = [contract.Outputs[0] with { Name = collision switch { "output-name" => "conversation", "output-root" => "Conversation.id", _ => "Article" },
            ReferenceKey = collision == "output-key" ? "$specialist.conversation" : "article-key" }] };
        Assert.Empty((await provider.ProposeContractAsync(new(request.Provider, contract))).Diagnostics);
        var manifest = request.Provider with { SchemaVersion = SpecialistRuntimeIds.ContinuityManifestSchema };
        Assert.Contains((await provider.ProposeContractAsync(new(manifest, contract))).Diagnostics, x => x.Code == "specialist.controls.collision");
        Assert.Contains(await provider.ValidateAsync(manifest, contract), x => x.Code == "specialist.controls.collision");
    }

    [Fact]
    public async Task Disabling_continuity_proposes_removal_of_its_exact_owned_members()
    {
        await using var services = Services(new MetadataFactory());
        var provider = services.GetRequiredService<IActivityProvider>();
        var request = Request(Manifest() with { AllowContinuity = true });
        request = request with { Provider = request.Provider with { SchemaVersion = SpecialistRuntimeIds.ContinuityManifestSchema } };
        var additions = await provider.ProposeContractAsync(new(request.Provider, request.Contract));
        var contract = request.Contract with { Inputs = [.. request.Contract.Inputs, additions.Changes.Single(x => x.Input is not null).Input!],
            Outputs = [.. request.Contract.Outputs, additions.Changes.Single(x => x.Output is not null).Output!] };
        var disabled = request.Provider with { Payload = JsonSerializer.SerializeToElement(Manifest() with { AllowContinuity = false }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        var removal = await provider.ProposeContractAsync(new(disabled, contract));
        Assert.Empty(removal.Diagnostics);
        Assert.Equal(2, removal.Changes.Count);
        Assert.All(removal.Changes, change => Assert.Equal(ActivityContractProposalOperation.Remove, change.Operation));
        Assert.Contains(await provider.ValidateAsync(disabled, contract), x => x.Code == "specialist.controls.collision");
        Assert.Empty(await provider.ValidateAsync(disabled, request.Contract));
    }

    [Fact]
    public async Task Actual_result_schema_is_admitted_before_publication_even_when_json_support_is_declared()
    {
        var factory = new MetadataFactory { SupportsSchema = false };
        await using var services = Services(factory);
        var result = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(Request());
        Assert.Null(result.ExecutableRoot);
        Assert.Contains(result.Diagnostics, x => x.Code == "specialist.model.capability");
        Assert.Equal("Article", Assert.Single(factory.ResultSchema.GetProperty("properties").EnumerateObject()).Name);
    }

    [Fact]
    public async Task Compiles_a_native_boundary_and_background_leaf_without_a_model_call()
    {
        var factory = new MetadataFactory();
        await using var services = Services(factory);
        var compiler = services.GetRequiredService<IActivityTemplateProviderCompiler>();
        var result = await compiler.CompileAsync(Request());
        Assert.Empty(result.Diagnostics);
        var root = Assert.IsType<Elsa.Workflows.Runtime.Core.Models.ExecutableNode>(result.ExecutableRoot);
        Assert.Equal(SpecialistRuntimeIds.ConsumerKey, root.DescriptorType);
        Assert.Equal(SpecialistRuntimeIds.FacadeCapability, root.ActivityContract!.Activation.Capability);
        Assert.Equal("brief-key", Assert.Single(root.ActivityContract.Inputs).Key);
        Assert.Equal("Article", root.ActivityContract.Result.Projections["article-key"].Path);
        Assert.Equal("System.Text.Json.JsonElement", root.ActivityContract.Result.Type.Alias);
        Assert.False(root.ActivityContract.SupportsBackgroundExecution);
        var worker = Assert.Single(Assert.Single(root.ChildSlots).Activities);
        Assert.Equal(ActivityExecutionPlacement.Background, worker.ExecutionPlacement);
        Assert.True(worker.ActivityContract!.SupportsBackgroundExecution);
        Assert.Equal(SpecialistRuntimeIds.TurnCapability, worker.ActivityContract.Activation.Capability);
        Assert.Empty(worker.InputBindings);
        Assert.Empty(worker.ActivityContract.Inputs);
        Assert.DoesNotContain("private-instructions", worker.DescriptorPayload.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1, factory.MetadataCalls);
        Assert.Empty(result.DirectDependencies);
        Assert.Equal(2, result.ResourceMeasurements.ClosedNodeCount);
    }

    [Theory]
    [InlineData("{\"temperature\":3}", "specialist.model.settings")]
    [InlineData("{\"unknown\":true}", "specialist.model.settings")]
    [InlineData("[]", "specialist.manifest.invalid")]
    public async Task Rejects_settings_outside_current_provider_metadata(string settings, string code)
    {
        await using var services = Services(new MetadataFactory());
        var request = Request(Manifest() with { Settings = Json(settings) });
        var result = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(request);
        Assert.Null(result.ExecutableRoot);
        Assert.Contains(result.Diagnostics, x => x.Code == code);
    }

    [Theory]
    [InlineData(0, 300_000, null)]
    [InlineData(33, 300_000, null)]
    [InlineData(1, 999, null)]
    [InlineData(1, 1_800_001, null)]
    [InlineData(1, 300_000, 0)]
    [InlineData(1, 300_000, 4097)]
    public async Task Refuses_invalid_or_unsupported_execution_limits(int calls, int timeout, int? tokens)
    {
        await using var services = Services(new MetadataFactory());
        var result = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(Request(Manifest() with { Limits = new(calls, timeout, tokens) }));
        Assert.Null(result.ExecutableRoot);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Metadata_is_authorized_again_on_each_compile_and_failures_are_redacted()
    {
        var factory = new MetadataFactory();
        await using var services = Services(factory);
        var compiler = services.GetRequiredService<IActivityTemplateProviderCompiler>();
        Assert.NotNull((await compiler.CompileAsync(Request())).ExecutableRoot);
        factory.Unavailable = true;
        var unavailable = await compiler.CompileAsync(Request());
        Assert.Null(unavailable.ExecutableRoot);
        Assert.Equal(2, factory.MetadataCalls);
        Assert.DoesNotContain(unavailable.Diagnostics, x => x.Message.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_model_owners_are_refused_before_any_metadata_access()
    {
        var first = new MetadataFactory();
        var second = new MetadataFactory();
        await using var services = Services(first, second);
        var result = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(Request());
        Assert.Null(result.ExecutableRoot);
        Assert.Contains(result.Diagnostics, x => x.Code == "specialist.model.provider");
        Assert.Equal(0, first.MetadataCalls + second.MetadataCalls);
    }

    [Fact]
    public async Task Literal_defaults_must_satisfy_schema_and_native_conversion()
    {
        await using var services = Services(new MetadataFactory());
        var request = Request() with { Contract = Contract() with { Inputs = [new("date", "Date", new("DateTimeOffset"), true, false, new("Literal", Json("\"yesterday\"")), "elsa.json")] } };
        var result = await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(request);
        Assert.Null(result.ExecutableRoot);
        Assert.Contains(result.Diagnostics, x => x.Code == "specialist.contract.default");
    }

    [Fact]
    public async Task Unknown_manifest_members_are_refused_and_current_migration_retains_owned_json()
    {
        await using var services = Services(new MetadataFactory());
        var provider = services.GetRequiredService<IActivityProvider>();
        var source = Request().Provider;
        var node = System.Text.Json.Nodes.JsonNode.Parse(source.Payload.GetRawText())!.AsObject();
        node["arbitraryPromptSetting"] = "PRIVATE";
        Assert.Contains(await provider.ValidateAsync(source with { Payload = JsonSerializer.SerializeToElement(node) }, Contract()), x => x.Code == "specialist.manifest.invalid");
        var migrated = await provider.MigrateAsync(new(source, SpecialistRuntimeIds.ManifestSchema));
        Assert.Empty(migrated.Diagnostics);
        Assert.True(JsonElement.DeepEquals(source.Payload, migrated.Manifest!.Payload));
        Assert.Null((await provider.MigrateAsync(new(source, "future"))).Manifest);
    }

    [Fact]
    public async Task Outcome_help_text_is_presentation_and_unsupported_outcomes_are_refused()
    {
        await using var services = Services(new MetadataFactory());
        var provider = services.GetRequiredService<IActivityProvider>();
        var contract = Contract() with { Outcomes = [new("done", "Done", true, "Friendly explanation")] };
        Assert.Empty((await provider.ProposeContractAsync(new(Request().Provider, contract))).Changes);
        Assert.Empty(await provider.ValidateAsync(Request().Provider, contract));
        Assert.Contains(await provider.ValidateAsync(Request().Provider, contract with { Outcomes = [new("rejected", "Rejected", true)] }), x => x.Code == "specialist.contract.outcome");
    }

    [Fact]
    public async Task Cancellation_is_not_converted_into_a_successful_publication()
    {
        await using var services = Services(new MetadataFactory());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await services.GetRequiredService<IActivityTemplateProviderCompiler>().CompileAsync(Request(), new CancellationToken(true)));
    }

    private static ScopedServices Services(params MetadataFactory[] factories)
    {
        var services = new ServiceCollection();
        new SpecialistsDesignFeature().ConfigureServices(services);
        foreach (var factory in factories) services.AddSingleton<ISpecialistModelCapabilitiesSource>(factory);
        return new(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }

    private sealed class ScopedServices(ServiceProvider root) : IServiceProvider, IAsyncDisposable
    {
        private readonly AsyncServiceScope scope = root.CreateAsyncScope();
        public object? GetService(Type serviceType) => scope.ServiceProvider.GetService(serviceType);
        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await root.DisposeAsync();
        }
    }

    private static ActivityTemplateCompilationRequest Request(SpecialistManifest? manifest = null) => new("definition", "specialist-type", "draft", 1, "1.0.0", Contract(),
        new(SpecialistRuntimeIds.ProviderKey, SpecialistRuntimeIds.ManifestSchema, JsonSerializer.SerializeToElement(manifest ?? Manifest(), new JsonSerializerOptions(JsonSerializerDefaults.Web))), [], "compiler-fixture");
    private static DesignContract Contract() => new("1", [new("brief-key", "Brief", new("String"), true, false, null, "elsa.json")],
        [new("article-key", "Article", new("String"), true, false, "elsa.json")], [new("done", "Done", true)]);
    private static SpecialistManifest Manifest() => new("private-instructions", new("fixture", "model-connection", "model"), Json("{\"temperature\":0.2}"),
        new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), new());
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private sealed class MetadataFactory : ISpecialistModelCapabilitiesSource
    {
        public string ProviderKey => "fixture";
        public int MetadataCalls { get; private set; }
        public bool Unavailable { get; set; }
        public bool SupportsSchema { get; set; } = true;
        public JsonElement ResultSchema { get; private set; }
        public ValueTask<bool> SupportsResultSchemaAsync(SpecialistModelReference model, JsonElement schema, CancellationToken cancellationToken = default)
        {
            ResultSchema = schema.Clone();
            return ValueTask.FromResult(SupportsSchema);
        }
        public ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default)
        {
            MetadataCalls++;
            if (Unavailable) throw new InvalidOperationException("PRIVATE credential error");
            return ValueTask.FromResult(new SpecialistModelCapabilities("fixture/1", Json("""{"type":"object","properties":{"temperature":{"type":"number","minimum":0,"maximum":2}},"additionalProperties":false}"""), true, 4096));
        }
    }
}
