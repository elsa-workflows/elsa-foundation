using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using RuntimeContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;

namespace Elsa.Agent.Specialists.Design.Services;

// All durable writes and reviewed-head concurrency remain with the generic publisher.
internal sealed class SpecialistActivityProvider(
    SpecialistSchemaCompiler schemas,
    SpecialistSchemaValidator validator,
    IEnumerable<ISpecialistModelClientFactory> modelFactories,
    IActivityDefinitionAuthoringStore? authoring = null,
    IActivityDefinitionVersionPublicationStore? publications = null) : IActivityProvider, IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
{
    private static readonly ActivityOutcomeContract Done = new("done", "Done", true);
    public string ProviderKey => SpecialistRuntimeIds.ProviderKey;
    public string CompilerFingerprint => "elsa.specialist/compiler/1.0.0";
    public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string>(StringComparer.Ordinal) { SpecialistRuntimeIds.ManifestSchema };
    public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Specialist",
        [new(SpecialistRuntimeIds.ManifestSchema, true, new HashSet<string>(StringComparer.Ordinal) { SpecialistRuntimeIds.ManifestSchema })], new([Done]));

    public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        _ = Read(request.Manifest, Subject(), diagnostics);
        var current = request.Contract.Outcomes.FirstOrDefault(x => x.ReferenceKey == Done.ReferenceKey);
        return ValueTask.FromResult(new ActivityContractProposal(IsDone(current) ? [] :
            [new("outcome:done", current is null ? ActivityContractProposalOperation.Add : ActivityContractProposalOperation.Replace,
                ActivityContractMemberKind.Outcome, Done.ReferenceKey, Outcome: Done)], diagnostics));
    }

    public async ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, DesignContract contract, CancellationToken cancellationToken = default) =>
        (await PrepareAsync(manifest, contract, Subject(), cancellationToken)).Diagnostics;

    public async ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request.Provider, request.Contract, new("ActivityDraft", request.DraftId, request.DefinitionId, Revision: request.Revision), cancellationToken);
        if (prepared.Descriptor is null)
            return new(null, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], new(0, 0, 0, 0, 0, 0, 0), request.ProviderFingerprint, [], prepared.Diagnostics);

        var payload = JsonSerializer.SerializeToElement(prepared.Descriptor, SpecialistJson.Options);
        var workerPayload = JsonSerializer.SerializeToElement(new { role = "model-turn" });
        var workerContract = new RuntimeContract(SpecialistRuntimeIds.TurnCapability, "1", SpecialistRuntimeIds.ConsumerKey, workerPayload, [],
            new(new("String"), true, ActivityValuePolicy.Default, [new("Result", "$", new("String"), true, ActivityValuePolicy.Default)]),
            ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.TurnCapability), SideEffectProfile.External, supportsBackgroundExecution: true);
        var worker = new ExecutableNode("model-turn", "model-turn", SpecialistRuntimeIds.TurnCapability, "1",
            new RuntimeActivityDescriptor(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.DescriptorSchema, workerPayload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), ActivityExecutionPlacement.Background,
            activityContract: workerContract);
        var contract = BuildRuntimeContract(request, payload);
        var root = new ExecutableNode("specialist", "specialist", request.ActivityTypeKey, request.CandidateVersion,
            new RuntimeActivityDescriptor(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.DescriptorSchema, payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("model-turn", [worker])], activityContract: contract);
        return new(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [],
            [new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.DescriptorSchema)],
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
        if (request.TargetSchemaVersion != SpecialistRuntimeIds.ManifestSchema)
            diagnostics.Add(Problem("specialist.manifest.migration", "This specialist manifest migration is unsupported.", Subject()));
        return ValueTask.FromResult(new ActivityManifestMigration(manifest is not null && diagnostics.Count == 0 ? request.Source with { Payload = request.Source.Payload.Clone() } : null, diagnostics));
    }

    private async ValueTask<Preparation> PrepareAsync(ActivityProviderManifest source, DesignContract contract, ActivityDiagnosticSubject subject, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<ActivityDiagnostic>();
        var manifest = Read(source, subject, diagnostics);
        if (manifest is null) return new(null, diagnostics);
        if (contract.Outcomes.Count != 1 || !IsDone(contract.Outcomes[0]))
            diagnostics.Add(Problem("specialist.contract.outcome", "A specialist exposes the single Done outcome.", subject));
        if (contract.Outputs.Any(x => x.Name == "$" || x.ReferenceKey == "$result"))
            diagnostics.Add(Problem("specialist.contract.reserved", "A specialist output cannot use the reserved whole-result selector.", subject));
        var schema = schemas.Compile(contract, manifest);
        diagnostics.AddRange(schema.Problems.Select(x => Problem(x.Code, x.Message, subject, x.Path)));
        if (schema.Inputs is not null)
            foreach (var input in contract.Inputs.Where(x => x.Default is not null))
            {
                var defaultValue = input.Default!;
                var memberSchema = validator.Prepare(schema.Inputs.Schema.GetProperty("properties").GetProperty(input.Name));
                var wrapped = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [input.Name] = defaultValue.Value });
                if (!StringComparer.OrdinalIgnoreCase.Equals(defaultValue.Syntax, "Literal") || memberSchema.Schema is null ||
                    memberSchema.Schema.Validate(defaultValue.Value).Count != 0 ||
                    SpecialistWireTypes.Validate(wrapped, [new(input.Name, input.Type.Alias, input.Type.CollectionKind, input.IsRequired, input.IsNullable)]).Count != 0)
                    diagnostics.Add(Problem("specialist.contract.default", "An input default must be a literal value satisfying its schema and native type.", subject));
            }
        if (diagnostics.Count != 0) return new(null, diagnostics);

        var owners = modelFactories.Where(x => x.ProviderKey == manifest.Model.ProviderKey).ToArray();
        if (owners.Length != 1)
        {
            diagnostics.Add(Problem("specialist.model.provider", "The selected model provider must have exactly one registered owner.", subject, "/model/providerKey"));
            return new(null, diagnostics);
        }
        SpecialistModelCapabilities capabilities;
        try { capabilities = await owners[0].GetCapabilitiesAsync(manifest.Model, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(Problem("specialist.model.unavailable", "Current authorized model metadata is unavailable.", subject, "/model"));
            return new(null, diagnostics);
        }
        if (string.IsNullOrWhiteSpace(capabilities.Fingerprint) || !capabilities.SupportsJsonSchema || capabilities.MaximumOutputTokens < 1 ||
            manifest.Limits.MaximumOutputTokens is { } tokens && tokens > capabilities.MaximumOutputTokens)
            diagnostics.Add(Problem("specialist.model.capability", "The selected model cannot satisfy the published JSON result or token limit.", subject, "/model"));
        var settings = validator.Prepare(capabilities.SettingsSchema);
        if (settings.Schema is null || settings.Schema.Validate(manifest.Settings).Count != 0)
            diagnostics.Add(Problem("specialist.model.settings", "Model settings do not satisfy the selected provider's current schema.", subject, "/settings"));
        if (diagnostics.Count != 0) return new(null, diagnostics);
        return new(new(manifest.Instructions, manifest.Model, manifest.Settings.Clone(), schema.Inputs!.Schema.Clone(), schema.Result!.Schema.Clone(),
            capabilities.Fingerprint, SpecialistRuntimeIds.SchemaProfile, manifest.Limits, manifest.AllowContinuity), diagnostics);
    }

    private static SpecialistManifest? Read(ActivityProviderManifest source, ActivityDiagnosticSubject subject, List<ActivityDiagnostic> diagnostics)
    {
        if (source.ProviderKey != SpecialistRuntimeIds.ProviderKey || source.SchemaVersion != SpecialistRuntimeIds.ManifestSchema)
        {
            diagnostics.Add(Problem("specialist.manifest.schema", "The specialist provider or manifest schema is unsupported.", subject));
            return null;
        }
        try { SpecialistJsonBounds.Check(source.Payload, 65_536, 2_048); }
        catch (SpecialistSchemaException)
        {
            diagnostics.Add(Problem("specialist.manifest.bounds", "The specialist manifest exceeds the bounded JSON profile.", subject));
            return null;
        }
        SpecialistManifest? manifest;
        try { manifest = source.Payload.Deserialize<SpecialistManifest>(SpecialistJson.ManifestOptions); }
        catch (JsonException) { manifest = null; }
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Instructions) || manifest.Instructions.Length > 32_768 ||
            manifest.Model is null || string.IsNullOrWhiteSpace(manifest.Model.ProviderKey) || string.IsNullOrWhiteSpace(manifest.Model.ConnectionName) || string.IsNullOrWhiteSpace(manifest.Model.ModelId) ||
            manifest.Model.ProviderKey.Length > 128 || manifest.Model.ConnectionName.Length > 256 || manifest.Model.ModelId.Length > 256 ||
            manifest.Settings.ValueKind != JsonValueKind.Object || manifest.InputConstraints is null || manifest.ResultConstraints is null || manifest.Limits is null ||
            manifest.Limits.MaximumModelCalls is < 1 or > 32 || manifest.Limits.TimeoutMilliseconds is < 1_000 or > 1_800_000 || manifest.Limits.MaximumOutputTokens is <= 0)
        {
            diagnostics.Add(Problem("specialist.manifest.invalid", "A specialist requires bounded instructions, a logical model reference, object settings, constraints and positive execution limits.", subject));
            return null;
        }
        return manifest;
    }

    private static RuntimeContract BuildRuntimeContract(ActivityTemplateCompilationRequest request, JsonElement payload)
    {
        var resultPolicy = ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result };
        return new(request.ActivityTypeKey, request.CandidateVersion, SpecialistRuntimeIds.ConsumerKey, payload,
            request.Contract.Inputs.Select(x => new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(x.ReferenceKey, x.Name,
                new(x.Type.Alias, x.Type.CollectionKind), x.IsRequired, x.IsNullable, x.Default is not null, x.Default?.Value, ActivityValuePolicy.Default)),
            new(new("System.Text.Json.JsonElement"), true, resultPolicy,
                request.Contract.Outputs.Select(x => new ActivityResultProjectionContract(x.ReferenceKey, x.Name, new(x.Type.Alias, x.Type.CollectionKind), x.IsRequired, resultPolicy, x.SourceRepresentation)),
                ValueRepresentation.StructuredValue), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
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
                previous.Provider is { } source && source.ProviderKey == ProviderKey && source.SchemaVersion == SpecialistRuntimeIds.ManifestSchema)
            {
                var before = Read(previous.Provider, Subject(), []);
                var after = Read(request.Provider, Subject(), []);
                if (before is not null && after is not null &&
                    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.InputConstraints), JsonSerializer.SerializeToElement(after.InputConstraints)) &&
                    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.ResultConstraints), JsonSerializer.SerializeToElement(after.ResultConstraints))) return [];
            }
        }
        return [new("specialist-json-constraints", ActivityVersionChangeArea.Contract, "SpecialistJsonConstraintsChanged", new(), null, null,
            ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major,
            unknownBaseline ? "The published specialist JSON constraint baseline is unavailable; a breaking increment is required." : "Specialist business JSON constraints changed; a breaking increment is required.")];
    }

    private static ActivityDiagnosticSubject Subject() => new("ActivityDefinition", SpecialistRuntimeIds.ProviderKey);
    private static bool IsDone(ActivityOutcomeContract? outcome) => outcome is { ReferenceKey: "done", Name: "Done", IsEmitted: true };
    private static ActivityDiagnostic Problem(string code, string message, ActivityDiagnosticSubject subject, string? path = null) =>
        new(code, ActivityDiagnosticSeverity.Error, message, subject, new(SpecialistRuntimeIds.ProviderKey, path));
    private sealed record Preparation(SpecialistRuntimeDescriptor? Descriptor, IReadOnlyList<ActivityDiagnostic> Diagnostics);
}
