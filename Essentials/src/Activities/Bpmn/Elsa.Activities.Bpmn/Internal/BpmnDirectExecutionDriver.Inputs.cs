using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed partial class BpmnDirectExecutionDriver
{
    internal const string AuthoredKind = "start-authored-v1";
    internal const string TriggerKind = "start-trigger-v1";
    private IWorkflowExecutableInputValidator InputsValidator => inputValidator
        ?? throw new NotSupportedException("Input-capable BPMN requires the shared declared-input validator.");

    private Start ReadStart(RuntimeModelExecutionContext context)
    {
        var start = context.Command.Payload.Deserialize<Start>(Strict) ?? throw new BpmnModelValidationException("BPMN start input is missing.");
        var inputs = start.Inputs ?? new Dictionary<string, JsonElement>();
        if (context.Material.Model!.Profile == BpmnModelProtocol.InputsProfile)
        {
            var validation = InputsValidator.Validate(context.Material.Model.InputContract, inputs);
            BpmnInputSupport.Check(validation);
            // Accepted commands contain the normalized bag; recovery cannot invent a different default snapshot.
            if (!SameInputs(inputs, validation.NormalizedInputs)) throw new BpmnModelValidationException("The model command inputs were not normalized at admission.");
        }
        else if (inputs.Count != 0) throw new BpmnModelValidationException("This direct profile has no declared workflow inputs.");
        if (context.Command.InputKind == AuthoredKind)
        {
            if (start.Authored is not { FormatVersion: 1, Slot: not null, Source: not null } proof || start.SubmittedInputs is null || start.Origin is not null || start.Trigger is not null ||
                proof.Slot.ActiveActivationId is null || proof.Slot.Revision <= 0 ||
                proof.Slot.Source?.IsSameOwnerAs(WorkflowActivationSource.Publishing) != true ||
                proof.Slot.SlotId != WorkflowActivationSlotIdentity.Create(proof.Slot.WorkflowDefinitionId, proof.Slot.SlotName) ||
                proof.Source.DefinitionId != proof.Slot.WorkflowDefinitionId || proof.Source.SlotId != proof.Slot.SlotId ||
                proof.Source.ActivationId != proof.Slot.ActiveActivationId || proof.Source.ArtifactId != context.Material.Identity.ArtifactId ||
                proof.Source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(proof.Slot.ActiveActivationId) ||
                proof.Source.SourceReferenceId != start.SourceReferenceId || proof.Source.Scope != WorkflowExecutableReferenceScope.Published ||
                proof.Source.SourceKind != "BpmnModel" || proof.Source.PublishedAt is null ||
                proof.Source.DeletedAt is not null || proof.Source.TenantId != context.Command.TenantId)
                throw new BpmnModelValidationException("The authored model start lacks its exact protected publication provenance.");
        }
        else if (context.Command.InputKind == TriggerKind)
        {
            if (start.Authored is not null || start.SubmittedInputs is not null || start.Origin is null || start.Trigger is null)
                throw new BpmnModelValidationException("The trigger start lacks its exact accepted capture and physical seal origin.");
        }
        else if (start.Authored is not null || start.SubmittedInputs is not null || start.Origin is not null || start.Trigger is not null)
            throw new BpmnModelValidationException("A raw model start cannot assert authored admission provenance.");
        return start with { Inputs = inputs };
    }

    private static JsonElement WriteDocument(Document document) => document.Entry is { } entry
        ? JsonSerializer.SerializeToElement(new InputFrame(16, entry.Profile, document with { Entry = null }, entry.Kind,
            entry.Payload, entry.Inputs), Web)
        : JsonSerializer.SerializeToElement(document, Web);

    private static Document ReadDocument(RuntimeModelExecutionState state)
    {
        if (state.Payload.TryGetProperty("formatVersion", out var version) && version.TryGetInt32(out var number) && number == 16)
        {
            var frame = state.Payload.Deserialize<InputFrame>(Strict) ?? throw new BpmnModelValidationException("BPMN input frame is missing.");
            if (frame.FormatVersion != 16 || frame.Document is null || frame.Inputs is null || frame.Payload.ValueKind != JsonValueKind.Object ||
                frame.Profile is not (BpmnModelProtocol.InputsProfile or BpmnModelProtocol.Profile or BpmnModelProtocol.NativeWorkProfile or BpmnModelProtocol.ClaimsProfile) ||
                frame.Kind is not ("start" or AuthoredKind or TriggerKind) || frame.Profile != BpmnModelProtocol.InputsProfile && frame.Kind is not (AuthoredKind or TriggerKind) ||
                frame.Document.NativeParent is not null || frame.Document.FormatVersion == 16 || frame.Document.Entry is not null)
                throw new BpmnModelValidationException("The BPMN input frame and its base profile differ.");
            return frame.Document with { Entry = new(frame.Profile, frame.Kind, frame.Payload, frame.Inputs) };
        }
        return state.Payload.Deserialize<Document>(Web) ?? throw new BpmnModelValidationException("BPMN model state is missing.");
    }

    private static void ValidateEntry(Document document, RuntimeModelExecutionState state)
    {
        if (document.Entry is not { } entry)
        {
            if (state.IngressOrigin is not null || NativeOrigins(document).Any(origin => origin.FormatVersion != 1 || origin.IngressOrigin is not null))
                throw new BpmnModelValidationException("Input-capable native work requires its versioned model input frame.");
            return;
        }
        var start = entry.Payload.Deserialize<Start>(Strict) ?? throw new BpmnModelValidationException("The retained model start is missing.");
        if (!SameInputs(start.Inputs ?? new Dictionary<string, JsonElement>(), entry.Inputs) || start.SourceReferenceId != document.SourceReferenceId ||
            entry.Profile != BpmnModelProtocol.InputsProfile && entry.Inputs.Count != 0 ||
            entry.Kind == AuthoredKind && (start.Authored is not { FormatVersion: 1, Slot: not null, Source: not null } proof ||
                proof.Source.SourceReferenceId != document.SourceReferenceId || proof.Source.ArtifactId != state.Executable.ArtifactId ||
                proof.Slot.ActiveActivationId is null ||
                proof.Source.TenantId != state.TenantId || proof.Slot.WorkflowDefinitionId != proof.Source.DefinitionId ||
                proof.Source.ActivationId != proof.Slot.ActiveActivationId || proof.Source.SlotId != proof.Slot.SlotId ||
                proof.Source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(proof.Slot.ActiveActivationId!) ||
                proof.Slot.Source?.IsSameOwnerAs(WorkflowActivationSource.Publishing) != true || proof.Slot.Revision <= 0 ||
                proof.Source.Scope != WorkflowExecutableReferenceScope.Published || proof.Source.SourceKind != "BpmnModel" ||
                proof.Source.PublishedAt is null || proof.Source.DeletedAt is not null || start.SubmittedInputs is null))
            throw new BpmnModelValidationException("The retained model input/provenance snapshot differs from its original entry.");
        if (entry.Profile != BpmnModelProtocol.InputsProfile && NativeOrigins(document).Any(origin => origin.FormatVersion != 1))
            throw new BpmnModelValidationException("Closed model material cannot retain input-capable native work.");
        if (entry.Kind == TriggerKind)
        {
            var capture = start.Trigger; var origin = start.Origin;
            if (capture is null || origin is null || !origin.IsEquivalentTo(state.IngressOrigin) || capture.ModelKey != "bpmn" || capture.StartIdentity.WorkflowExecutionId != state.ExecutionId ||
                capture.StartIdentity.Scope != state.Scope || origin.Scope != state.Scope || origin.RecipientId != capture.RecipientId ||
                origin.SourceOutboxItemId != capture.StartIdentity.SourceOutboxItemId || capture.TenantId != state.TenantId || capture.Partition != state.Partition ||
                capture.MaterialIncarnation != state.MaterialIncarnationId || capture.ModelProtocol != state.Protocol || capture.ModelProfile != entry.Profile ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(capture.Material, state.Executable) ||
                !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, capture.Authority.SystemIdentity, capture.Authority.RootInitiator, capture.Authority.Metadata) ||
                capture.SourceProvenance.SourceReferenceId != document.SourceReferenceId || !SameInputs(capture.Inputs, entry.Inputs))
                throw new BpmnModelValidationException("The retained trigger model entry differs from its immutable accepted capture.");
        }
        else if (state.IngressOrigin is not null)
            throw new BpmnModelValidationException("Only a trigger entry may retain sealed ingress model authority.");
        if (NativeOrigins(document).Any(origin => origin.IngressOrigin is null ? state.IngressOrigin is not null : !origin.IngressOrigin.IsEquivalentTo(state.IngressOrigin)))
            throw new BpmnModelValidationException("The retained native worker differs from its admitted model root authority.");
    }

    private static IEnumerable<RuntimeModelNativeWorkOrigin> NativeOrigins(Document document) =>
        document.Work.Where(item => item.NativeWork is not null).Select(item => item.NativeWork!)
            .Concat((document.NativeResults ?? []).Select(item => item.Origin))
            .Concat((document.Cancellations ?? []).Select(item => item.Origin));

    internal static bool SameInputs(IReadOnlyDictionary<string, JsonElement> left, IReadOnlyDictionary<string, JsonElement> right) =>
        left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) && JsonElement.DeepEquals(item.Value, value));

    internal sealed record InputEntry(string Profile, string Kind, JsonElement Payload, IReadOnlyDictionary<string, JsonElement> Inputs);
    private sealed record InputFrame(int FormatVersion, string Profile, Document Document, string Kind, JsonElement Payload,
        IReadOnlyDictionary<string, JsonElement> Inputs);
}
