using System.Text.Json;
using Acme.Agents.Runtime;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using RuntimeContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;

namespace Acme.Agents.Design;

public sealed class AgentProvider : IActivityProvider, IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
{
    public string ProviderKey => "acme.agent";
    public string CompilerFingerprint => "acme.agent/compiler/1";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
    public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Agent research", [new("1", true, new HashSet<string> { "1" })], new([]));
    public static DesignContract Contract() => new("1",
        [new("Brief", "Brief", new TypeReference("String"), true, false, null, "elsa.json")],
        [new("article", "Article", new TypeReference("String"), true, false, "elsa.json")], [new("Done", "Done", true)]);

    public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ActivityContractProposal([], []));
    public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, DesignContract contract, CancellationToken cancellationToken = default)
    {
        var descriptor = manifest.Payload.Deserialize<AgentDescriptor>() ?? throw new InvalidOperationException("Missing descriptor");
        if (string.IsNullOrWhiteSpace(descriptor.AgentVersionId) || string.IsNullOrWhiteSpace(descriptor.Instructions))
            throw new InvalidOperationException("Agent identity and instructions required");
        return ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
    }
    public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No research manifest migration");
    public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], []));

    public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default)
    {
        var resultPolicy = ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result };
        var runtimeContract = new RuntimeContract(request.ActivityTypeKey, request.CandidateVersion, AgentActivationStrategy.Key,
            request.Provider.Payload, [new Elsa.Activities.Runtime.Core.Models.ActivityInputContract("Brief", "Brief", new ValueTypeDescriptor("String"), true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("JsonElement"), true, resultPolicy,
                [new ActivityResultProjectionContract("article", "article", new ValueTypeDescriptor("String"), true, resultPolicy)], ValueRepresentation.StructuredValue),
            ["Done"], new ActivityActivationRequirement(AgentActivationStrategy.Key, AgentActivationStrategy.Key));
        var root = new ExecutableNode("agent", "agent", request.ActivityTypeKey, request.CandidateVersion,
            new RuntimeActivityDescriptor(AgentActivationStrategy.Key, "1", request.Provider.Payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(), activityContract: runtimeContract);
        return ValueTask.FromResult(new ActivityTemplateCompilation(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [],
            [new RuntimeRequirement(AgentActivationStrategy.Key, "1")], [new RuntimeStorageDriverRequirement("elsa.json")],
            new(1, 1, 0, 1, request.Provider.Payload.GetRawText().Length, 0, 1), request.ProviderFingerprint, [], []));
    }
}
