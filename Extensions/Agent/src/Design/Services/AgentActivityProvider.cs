using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.JsonSchema;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using RuntimeContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;

namespace Elsa.Agent.Design.Services;

// All durable writes and reviewed-head concurrency remain with the generic publisher.
internal sealed class AgentActivityProvider(
    AgentSchemaCompiler schemas,
    AgentSchemaValidator validator,
    IEnumerable<IAgentModelCapabilitiesSource> modelFactories,
    IActivityDefinitionAuthoringStore? authoring = null,
    IActivityDefinitionVersionPublicationStore? publications = null) : IActivityProvider, IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer, IActivityProviderDiagnosticCatalog
{
    private static readonly IReadOnlyList<ActivityProviderDiagnosticDescriptor> PublicDiagnostics = Array.AsReadOnly<ActivityProviderDiagnosticDescriptor>([
        new("agent.manifest.invalid", "The saved agent configuration cannot be read.", "Review the agent settings. If this draft was imported, recreate it using the current agent editor."),
        new("agent.manifest.schema", "This agent uses an unsupported configuration version.", "Update the installed Agent module or recreate the draft using its current editor."),
        new("agent.manifest.bounds", "The agent configuration is too large or too deeply nested.", "Shorten the instructions and simplify the input or result constraints before checking again."),
        new("agent.manifest.instructions", "Agent instructions are required.", "Enter instructions of 1 to 32,768 characters in Designer.", "/instructions", "Edit instructions"),
        new("agent.manifest.instructions-length", "Agent instructions exceed 32,768 characters.", "Shorten the instructions to at most 32,768 characters.", "/instructions", "Edit instructions"),
        new("agent.manifest.model", "A complete model connection has not been selected.", "In Designer, choose a model connection with a configured model or deployment. Create or update it in Connections if none is available.", "/model", "Choose model connection"),
        new("agent.manifest.settings", "Model settings must be a JSON object.", "Review the model settings in Designer or recreate a malformed imported draft.", "/settings", "Review model settings"),
        new("agent.manifest.constraints", "The agent's input or result constraints are missing.", "Restore the input and result constraint objects or recreate this imported draft using the current agent editor."),
        new("agent.manifest.limits", "Execution limits are missing.", "Set execution limits in Designer's Advanced settings.", "/limits", "Edit execution limits"),
        new("agent.manifest.model-calls", "The maximum number of model calls is outside the allowed range.", "Enter a whole number from 1 to 32 in Advanced settings.", "/limits/maximumModelCalls", "Edit model-call limit"),
        new("agent.manifest.timeout", "The agent timeout is outside the allowed range.", "Enter a timeout from 1 to 1,800 seconds in Advanced settings.", "/limits/timeoutMilliseconds", "Edit timeout"),
        new("agent.manifest.output-tokens", "The output-token limit must be a positive whole number.", "Clear the optional limit or enter a positive whole number supported by the selected model.", "/limits/maximumOutputTokens", "Edit output-token limit"),
        new("agent.model.provider", "The selected model provider is unavailable or registered more than once.", "Choose a connection from an available model provider. Ask an administrator to check provider registration if the problem persists.", "/model", "Choose model connection"),
        new("agent.model.unavailable", "The selected model connection cannot be used right now.", "Choose another authorized model connection, or check its endpoint, credentials and access policy in Connections, then check again.", "/model", "Review model connection"),
        new("agent.model.capability", "The selected model cannot meet this agent's result or token requirements.", "Choose a model that supports the result schema, or reduce the maximum output-token limit.", "/model", "Review model connection"),
        new("agent.model.settings", "The model settings do not match the selected provider's requirements.", "Review the settings supported by the selected model in Designer.", "/settings", "Review model settings"),
        new("agent.contract.outcome", "Agents require the single Done outcome.", "Restore the Done outcome in the activity's Outcomes tab."),
        new("agent.contract.reserved", "An output uses a reserved agent result name.", "Rename outputs using the reserved $ name or $result reference key in the Outputs tab."),
        new("agent.contract.default", "An input default does not match its type or schema.", "Use a literal default that satisfies the input's type and constraints, or remove that default.")
    ]);
    public IReadOnlyList<ActivityProviderDiagnosticDescriptor> DiagnosticDescriptors => PublicDiagnostics;
    private static readonly ActivityOutcomeContract Done = new("done", "Done", true);
    public string ProviderKey => AgentRuntimeIds.ProviderKey;
    public string CompilerFingerprint => "elsa.agent/compiler/2.1.0";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string>(StringComparer.Ordinal) { AgentRuntimeIds.ManifestSchema, AgentRuntimeIds.ContinuityManifestSchema };
    public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Agent",
        [new(AgentRuntimeIds.ManifestSchema, true, new HashSet<string>(StringComparer.Ordinal) { AgentRuntimeIds.ManifestSchema }),
            new(AgentRuntimeIds.ContinuityManifestSchema, true, new HashSet<string>(StringComparer.Ordinal) { AgentRuntimeIds.ManifestSchema, AgentRuntimeIds.ContinuityManifestSchema })], new([Done]));

    public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        var manifest = Read(request.Manifest, Subject(), diagnostics);
        var current = request.Contract.Outcomes.FirstOrDefault(x => x.ReferenceKey == Done.ReferenceKey);
        var changes = new List<ActivityContractProposalChange>();
        if (!IsDone(current)) changes.Add(new("outcome:done", current is null ? ActivityContractProposalOperation.Add : ActivityContractProposalOperation.Replace,
            ActivityContractMemberKind.Outcome, Done.ReferenceKey, Outcome: Done));
        if (manifest is not null && request.Manifest.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema)
        {
            changes.AddRange(AgentControlContract.Propose(request.Contract, manifest.AllowContinuity));
            diagnostics.AddRange(AgentControlContract.Validate(request.Contract, manifest.AllowContinuity, proposing: true).Select(x => Problem(x.Code, x.Message, Subject(), x.Path)));
        }
        return ValueTask.FromResult(new ActivityContractProposal(changes, diagnostics));
    }

    public async ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, DesignContract contract, CancellationToken cancellationToken = default) =>
        (await PrepareAsync(manifest, contract, Subject(), cancellationToken)).Diagnostics;

    public async ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request.Provider, request.Contract, new(request.SubjectKind, request.DraftId, request.DefinitionId, Revision: request.Revision), cancellationToken);
        if (prepared.Descriptor is null)
            return new(null, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], new(0, 0, 0, 0, 0, 0, 0), request.ProviderFingerprint, [], prepared.Diagnostics);

        var payload = JsonSerializer.SerializeToElement(prepared.Descriptor, AgentJson.Options);
        var descriptorSchema = prepared.Descriptor.InvocationControlsProfile is null ? AgentRuntimeIds.DescriptorSchema : AgentRuntimeIds.ContinuityDescriptorSchema;
        var workerPayload = JsonSerializer.SerializeToElement(new { role = "model-turn" });
        var workerContract = new RuntimeContract(AgentRuntimeIds.TurnCapability, "1", AgentRuntimeIds.ConsumerKey, workerPayload, [],
            new(new("String"), true, ActivityValuePolicy.Default, [new("Result", "$", new("String"), true, ActivityValuePolicy.Default)]),
            ["Done"], new(AgentRuntimeIds.ConsumerKey, AgentRuntimeIds.TurnCapability), SideEffectProfile.External, supportsBackgroundExecution: true);
        var worker = new ExecutableNode("model-turn", "model-turn", AgentRuntimeIds.TurnCapability, "1",
            new RuntimeActivityDescriptor(AgentRuntimeIds.ConsumerKey, descriptorSchema, workerPayload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), ActivityExecutionPlacement.Background,
            activityContract: workerContract);
        var contract = BuildRuntimeContract(request, payload);
        var root = new ExecutableNode("agent", "agent", request.ActivityTypeKey, request.CandidateVersion,
            new RuntimeActivityDescriptor(AgentRuntimeIds.ConsumerKey, descriptorSchema, payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("model-turn", [worker])], activityContract: contract);
        return new(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [],
            [new(AgentRuntimeIds.ConsumerKey, descriptorSchema)],
            request.Contract.Inputs.Select(x => x.StorageDriverKey).Concat(request.Contract.Outputs.Select(x => x.StorageDriverKey))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(x => new RuntimeStorageDriverRequirement(x)).ToArray(),
            new(2, 2, 0, 1, Encoding.UTF8.GetByteCount(payload.GetRawText()) + Encoding.UTF8.GetByteCount(workerPayload.GetRawText()), 0, request.Contract.Inputs.Count + request.Contract.Outputs.Count),
            request.ProviderFingerprint, await CompatibilityChangesAsync(request, cancellationToken), prepared.Diagnostics);
    }

    public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        _ = Read(request.Provider, Subject(), diagnostics);
        return ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], diagnostics));
    }

    public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        var manifest = Read(request.Source, Subject(), diagnostics);
        if (!SupportedManifestSchemas.Contains(request.TargetSchemaVersion) || request.Source.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema && request.TargetSchemaVersion == AgentRuntimeIds.ManifestSchema)
            diagnostics.Add(Problem("agent.manifest.migration", "This agent manifest migration is unsupported.", Subject()));
        return ValueTask.FromResult(new ActivityManifestMigration(manifest is not null && diagnostics.Count == 0 ? request.Source with { SchemaVersion = request.TargetSchemaVersion, Payload = request.Source.Payload.Clone() } : null, diagnostics));
    }

    private async ValueTask<Preparation> PrepareAsync(ActivityProviderManifest source, DesignContract contract, ActivityDiagnosticSubject subject, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        var manifest = Read(source, subject, diagnostics);
        if (manifest is null) return new(null, diagnostics);
        var controls = source.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema && manifest.AllowContinuity;
        if (source.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema)
            diagnostics.AddRange(AgentControlContract.Validate(contract, controls).Select(x => Problem(x.Code, x.Message, subject, x.Path)));
        if (diagnostics.Count != 0) return new(null, diagnostics);
        var businessContract = controls ? contract with { Inputs = contract.Inputs.Where(x => !AgentControlContract.IsInput(x)).ToArray(),
            Outputs = contract.Outputs.Where(x => !AgentControlContract.IsOutput(x)).ToArray() } : contract;
        if (contract.Outcomes.Count != 1 || !IsDone(contract.Outcomes[0]))
            diagnostics.Add(Problem("agent.contract.outcome", "An agent exposes the single Done outcome.", subject));
        if (contract.Outputs.Any(x => x.Name == "$" || x.ReferenceKey == "$result"))
            diagnostics.Add(Problem("agent.contract.reserved", "An agent output cannot use the reserved whole-result selector.", subject));
        var schema = schemas.Compile(businessContract, manifest);
        diagnostics.AddRange(schema.Problems.Select(x => Problem(x.Code, x.Message, subject, x.Path)));
        if (schema.Inputs is not null)
            foreach (var input in businessContract.Inputs.Where(x => x.Default is not null))
            {
                var defaultValue = input.Default!;
                var memberSchema = validator.Prepare(schema.Inputs.Schema.GetProperty("properties").GetProperty(input.Name));
                var wrapped = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [input.Name] = defaultValue.Value });
                if (!StringComparer.OrdinalIgnoreCase.Equals(defaultValue.Syntax, "Literal") || memberSchema.Schema is null ||
                    memberSchema.Schema.Validate(defaultValue.Value).Count != 0 ||
                    AgentWireTypes.Validate(wrapped, [new(input.Name, input.Type.Alias, input.Type.CollectionKind, input.IsRequired, input.IsNullable)]).Count != 0)
                    diagnostics.Add(Problem("agent.contract.default", "An input default must be a literal value satisfying its schema and native type.", subject));
            }
        if (diagnostics.Count != 0) return new(null, diagnostics);

        var owners = modelFactories.Where(x => x.ProviderKey == manifest.Model.ProviderKey).ToArray();
        if (owners.Length != 1)
        {
            diagnostics.Add(Problem("agent.model.provider", "The selected model provider must have exactly one registered owner.", subject, "/model/providerKey"));
            return new(null, diagnostics);
        }
        AgentModelCapabilities capabilities;
        bool supportsResult;
        try
        {
            capabilities = await owners[0].GetCapabilitiesAsync(manifest.Model, cancellationToken);
            supportsResult = await owners[0].SupportsResultSchemaAsync(manifest.Model, schema.Result!.Schema, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Problem("agent.model.unavailable", "Current authorized model metadata is unavailable.", subject, "/model"));
            return new(null, diagnostics);
        }
        if (string.IsNullOrWhiteSpace(capabilities.Fingerprint) || !capabilities.SupportsJsonSchema || !supportsResult || capabilities.MaximumOutputTokens < 1 ||
            manifest.Limits.MaximumOutputTokens is { } tokens && tokens > capabilities.MaximumOutputTokens)
            diagnostics.Add(Problem("agent.model.capability", "The selected model cannot satisfy the published JSON result or token limit.", subject, "/model"));
        var settings = validator.Prepare(capabilities.SettingsSchema);
        if (settings.Schema is null || settings.Schema.Validate(manifest.Settings).Count != 0)
            diagnostics.Add(Problem("agent.model.settings", "Model settings do not satisfy the selected provider's current schema.", subject, "/settings"));
        if (diagnostics.Count != 0) return new(null, diagnostics);
        return new(new(manifest.Instructions, manifest.Model, manifest.Settings.Clone(), schema.Inputs!.Schema.Clone(), schema.Result!.Schema.Clone(),
            capabilities.Fingerprint, AgentRuntimeIds.SchemaProfile, manifest.Limits, manifest.AllowContinuity)
            { InvocationControlsProfile = controls ? AgentInvocationControls.Profile : null }, diagnostics);
    }

    private static AgentManifest? Read(ActivityProviderManifest source, ActivityDiagnosticSubject subject, List<ActivityDiagnostic> diagnostics)
    {
        if (source.ProviderKey != AgentRuntimeIds.ProviderKey || source.SchemaVersion is not (AgentRuntimeIds.ManifestSchema or AgentRuntimeIds.ContinuityManifestSchema))
        {
            diagnostics.Add(Problem("agent.manifest.schema", "The agent provider or manifest schema is unsupported.", subject));
            return null;
        }
        try { AgentJsonBounds.Check(source.Payload, 65_536, 2_048); }
        catch (AgentSchemaException)
        {
            diagnostics.Add(Problem("agent.manifest.bounds", "The agent manifest exceeds the bounded JSON profile.", subject));
            return null;
        }
        AgentManifest? manifest;
        try { manifest = source.Payload.Deserialize<AgentManifest>(AgentJson.ManifestOptions); }
        catch (JsonException) { manifest = null; }
        if (manifest is null)
        {
            diagnostics.Add(Problem("agent.manifest.invalid", "The saved agent configuration cannot be read.", subject));
            return null;
        }
        var invalid = false;
        void Refuse(string code)
        {
            invalid = true;
            var guidance = PublicDiagnostics.Single(x => x.Code == code);
            diagnostics.Add(Problem(code, guidance.Message, subject, guidance.JsonPointer));
        }
        if (string.IsNullOrWhiteSpace(manifest.Instructions)) Refuse("agent.manifest.instructions");
        else if (manifest.Instructions.Length > 32_768) Refuse("agent.manifest.instructions-length");
        if (manifest.Model is null || string.IsNullOrWhiteSpace(manifest.Model.ProviderKey) || string.IsNullOrWhiteSpace(manifest.Model.ConnectionName) || string.IsNullOrWhiteSpace(manifest.Model.ModelId) ||
            manifest.Model.ProviderKey.Length > 128 || manifest.Model.ConnectionName.Length > 256 || manifest.Model.ModelId.Length > 256)
            Refuse("agent.manifest.model");
        if (manifest.Settings.ValueKind != JsonValueKind.Object) Refuse("agent.manifest.settings");
        if (manifest.InputConstraints is null || manifest.ResultConstraints is null) Refuse("agent.manifest.constraints");
        if (manifest.Limits is null) Refuse("agent.manifest.limits");
        else
        {
            if (manifest.Limits.MaximumModelCalls is < 1 or > 32) Refuse("agent.manifest.model-calls");
            if (manifest.Limits.TimeoutMilliseconds is < 1_000 or > 1_800_000) Refuse("agent.manifest.timeout");
            if (manifest.Limits.MaximumOutputTokens is <= 0) Refuse("agent.manifest.output-tokens");
        }
        if (invalid) return null;
        return manifest;
    }

    private static RuntimeContract BuildRuntimeContract(ActivityTemplateCompilationRequest request, JsonElement payload)
    {
        var resultPolicy = ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result };
        return new(request.ActivityTypeKey, request.CandidateVersion, AgentRuntimeIds.ConsumerKey, payload,
            request.Contract.Inputs.Select(x => new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(x.ReferenceKey, x.Name,
                new(x.Type.Alias, x.Type.CollectionKind), x.IsRequired, !x.IsRequired || x.IsNullable,
                x.Default is not null, x.Default?.Value, ActivityValuePolicy.Default)),
            new(new("System.Text.Json.JsonElement"), true, resultPolicy,
                request.Contract.Outputs.Select(x => new ActivityResultProjectionContract(x.ReferenceKey, x.Name, new(x.Type.Alias, x.Type.CollectionKind), x.IsRequired && !x.IsNullable, resultPolicy, x.SourceRepresentation)),
                ValueRepresentation.StructuredValue), ["Done"], new(AgentRuntimeIds.ConsumerKey, AgentRuntimeIds.FacadeCapability));
    }

    private async ValueTask<IReadOnlyList<ActivityVersionChange>> CompatibilityChangesAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken)
    {
        // Read only through this publication operation's selected scope. The generic publisher owns head CAS,
        // reviewed-content invalidation and all writes; an unknown baseline can never weaken compatibility.
        var unknownBaseline = authoring is null || publications is null;
        if (!unknownBaseline)
        {
            var state = await authoring!.FindAsync(request.DefinitionId, cancellationToken);
            if (state is { HeadVersionId: null } && state.DefinitionId == request.DefinitionId) return [];
            var previous = state?.HeadVersionId is { } head ? await publications!.FindAsync(head, cancellationToken) : null;
            if (previous is not null && previous.DefinitionId == request.DefinitionId && previous.DefinitionVersionId == state!.HeadVersionId &&
                previous.Provider is { } source && source.ProviderKey == ProviderKey && SupportedManifestSchemas.Contains(source.SchemaVersion))
            {
                var before = Read(previous.Provider, Subject(), []);
                var after = Read(request.Provider, Subject(), []);
                if (before is not null && after is not null &&
                    (source.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema && before.AllowContinuity) !=
                    (request.Provider.SchemaVersion == AgentRuntimeIds.ContinuityManifestSchema && after.AllowContinuity))
                    return [new("agent-invocation-controls", ActivityVersionChangeArea.Contract, "AgentInvocationContractChanged", new(), null, null,
                        ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major, "The agent's native invocation controls or whole-result metadata changed.")];
                if (before is not null && after is not null &&
                    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.InputConstraints), JsonSerializer.SerializeToElement(after.InputConstraints)) &&
                    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.ResultConstraints), JsonSerializer.SerializeToElement(after.ResultConstraints))) return [];
            }
        }
        return [new("agent-json-constraints", ActivityVersionChangeArea.Contract, "AgentJsonConstraintsChanged", new(), null, null,
            ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
            unknownBaseline ? "The published agent JSON constraint baseline is unavailable; a breaking increment is required." : "Agent business JSON constraints changed; a breaking increment is required.")];
    }

    private static ActivityDiagnosticSubject Subject() => new("ActivityDefinition", AgentRuntimeIds.ProviderKey);
    private static bool IsDone(ActivityOutcomeContract? outcome) => outcome is { ReferenceKey: "done", Name: "Done", IsEmitted: true };
    private static ActivityDiagnostic Problem(string code, string message, ActivityDiagnosticSubject subject, string? path = null) =>
        new(code, ActivityDiagnosticSeverity.Error, message, subject, new(AgentRuntimeIds.ProviderKey, path));
    private sealed record Preparation(AgentRuntimeDescriptor? Descriptor, IReadOnlyList<ActivityDiagnostic> Diagnostics);
}
