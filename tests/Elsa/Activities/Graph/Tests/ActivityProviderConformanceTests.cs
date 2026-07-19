using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Activities.Graph.Tests;

public sealed class ActivityProviderConformanceTests
{
    [Fact]
    public async Task A_second_provider_uses_only_stable_provider_and_runtime_seams()
    {
        var provider = new ScriptProvider();
        var designRegistry = new ActivityProviderRegistry([provider]);
        var compilerRegistry = new ActivityTemplateProviderCompilerRegistry([provider]);
        var manifest = new ActivityProviderManifest(ScriptProvider.ProviderKey, "1", Json("{\"source\":\"return input;\"}"));
        var contract = Contract();

        var proposal = await designRegistry.Resolve(ScriptProvider.ProviderKey, "1").ProposeContractAsync(new(manifest, contract));
        var validation = await designRegistry.Resolve(ScriptProvider.ProviderKey, "1").ValidateAsync(manifest, contract);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1", "sample.script", "draft-1", 7, "1.0.0", contract, manifest, [], "script-compiler/1");
        var first = await compilerRegistry.Resolve(ScriptProvider.ProviderKey, "1").CompileAsync(request);
        var second = await compilerRegistry.Resolve(ScriptProvider.ProviderKey, "1").CompileAsync(request);

        Assert.Empty(proposal.Changes);
        Assert.Empty(validation);
        Assert.Equal(first.ExecutableRoot!.Descriptor.ConsumerKey, second.ExecutableRoot!.Descriptor.ConsumerKey);
        Assert.Equal(first.ExecutableRoot.Descriptor.SchemaVersion, second.ExecutableRoot.Descriptor.SchemaVersion);
        Assert.Equal(first.ExecutableRoot.Descriptor.Payload.GetRawText(), second.ExecutableRoot.Descriptor.Payload.GetRawText());
        Assert.Equal(new RuntimeRequirement(ScriptProvider.ConsumerKey, "1"), Assert.Single(first.RuntimeRequirements));
        Assert.Equal(new RuntimeStorageDriverRequirement("elsa.json"), Assert.Single(first.StorageDriverRequirements));
        Assert.DoesNotContain("Graph", first.ExecutableRoot.Descriptor.ConsumerKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_provider_and_compiler_owners_fail_registry_activation()
    {
        Assert.Throws<InvalidOperationException>(() => new ActivityProviderRegistry([new ScriptProvider(), new DuplicateScriptProvider()]));
        Assert.Throws<InvalidOperationException>(() => new ActivityTemplateProviderCompilerRegistry([new ScriptProvider(), new DuplicateScriptProvider()]));
    }

    private static DesignActivityContract Contract() => new(
        "1",
        [new("input", "Input", new TypeReference("string"), false, false, null, "elsa.json")],
        [],
        [new("done", "Done", true)]);

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private class ScriptProvider : IActivityProvider, IActivityTemplateProviderCompiler
    {
        public const string ProviderKey = "sample.script";
        public const string ConsumerKey = "sample.script-runtime";
        string IActivityProvider.ProviderKey => ProviderKey;
        string IActivityTemplateProviderCompiler.ProviderKey => ProviderKey;
        string IActivityTemplateProviderCompiler.CompilerFingerprint => "sample.script/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string>(StringComparer.Ordinal) { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Sample Script",
            [new("1", true, new HashSet<string> { "1" })],
            new([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal([], []));

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
            ActivityProviderManifest manifest,
            DesignActivityContract contract,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(request.Source with { SchemaVersion = request.TargetSchemaVersion }, []));

        public ValueTask<ActivityTemplateCompilation> CompileAsync(
            ActivityTemplateCompilationRequest request,
            CancellationToken cancellationToken = default)
        {
            var root = new ExecutableNode(
                "script-root",
                "script-root",
                "sample.script",
                "1",
                new RuntimeActivityDescriptor(ConsumerKey, "1", Json("{\"entry\":\"main\"}")),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>());
            return ValueTask.FromResult(new ActivityTemplateCompilation(
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                [],
                [new RuntimeRequirement(ConsumerKey, "1")],
                [new RuntimeStorageDriverRequirement("elsa.json")],
                new(1, 1, 0, 1, 16, 0, 1),
                request.ProviderFingerprint,
                [],
                []));
        }
    }

    private sealed class DuplicateScriptProvider : ScriptProvider;
}
