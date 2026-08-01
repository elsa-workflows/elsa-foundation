using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Surfaces the engine's author-facing intrinsics as built-in authoring-catalog descriptors so the designer
/// palette can offer them (#929, #1113). The runtime already performs these operations through engine
/// intrinsics (<c>WorkflowIntrinsicExecutor</c>); per ADR 0045 they are engine-owned and are never CLR
/// activities. Each descriptor carries an <see cref="ActivityAuthoringIntrinsicView"/> so the authoring
/// client materializes an <c>AuthoredWorkflowIntrinsic</c> node rather than a catalog activity reference.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which of the nine engine intrinsic kinds are published, and why (#1113).</b> Five are: <c>Set</c> and
/// <c>SetOutput</c> (the minimum authoring loop, #929), plus <c>Finish</c>, <c>SetCorrelationId</c> and
/// <c>SetInstanceName</c> — each the Elsa 4 replacement for a first-class Elsa 3 activity (<c>Finish</c>/
/// <c>Complete</c>, <c>Correlate</c>, <c>SetName</c>), so without a descriptor those three had no
/// <em>discoverable</em> Elsa 4 equivalent even though the engine implemented them and the REST API accepted them.
/// </para>
/// <para>
/// Four are deliberately withheld. <c>Merge</c> and <c>Reduce</c> execute identically to <c>Set</c> today —
/// <c>WorkflowIntrinsicExecutor.ExecuteVariableWriteAsync</c> handles all three with the same frame write — so
/// offering them would advertise three different palette entries for one behaviour. <c>Control</c> and
/// <c>Return</c> are compiler/engine seams (a literal outcome-key emitter and a node result carrier) with no
/// Elsa 3 counterpart and no meaning an author would place by hand. Both groups stay authorable through the
/// code-first builder and the REST API; they are simply not offered in the palette.
/// </para>
/// </remarks>
public sealed class IntrinsicAuthoringDescriptorProvider : IBuiltInAuthoringDescriptorProvider
{
    // Stable authoring ids, mirroring Elsa.Workflows.Design.Core.Authoring.WorkflowIntrinsicAuthoringIds
    // (internal there). A placed node reuses these as its ActivityVersionId, matching the code-first builder.
    private const string SetVariableVersionId = "elsa.intrinsic.set@1";
    private const string SetOutputVersionId = "elsa.intrinsic.set-output@1";
    private const string SetCorrelationIdVersionId = "elsa.intrinsic.set-correlation-id@1";
    private const string SetInstanceNameVersionId = "elsa.intrinsic.set-instance-name@1";
    private const string FinishVersionId = "elsa.intrinsic.finish@1";

    // Intrinsic input keys, mirroring WorkflowIntrinsicAuthoringInputKeys.
    private const string ValueKey = "value";
    private const string NameKey = "name";
    private const string OutcomeKey = "outcome";

    // A descriptor-only key naming the variable target. The target is authored as a VariableReference on the
    // intrinsic node, not as a runtime value binding, so the client renders it with a variable picker.
    private const string VariableKey = "variable";

    private const string PrimitivesCategory = "Primitives";
    private const string AnyType = "Elsa.Any";
    private const string StringType = "String";

    public IReadOnlyCollection<ActivityAuthoringDescriptorView> GetDescriptors() =>
    [
        SetVariableDescriptor(),
        SetOutputDescriptor(),
        SetCorrelationIdDescriptor(),
        SetInstanceNameDescriptor(),
        FinishDescriptor()
    ];

    private static ActivityAuthoringDescriptorView SetVariableDescriptor() =>
        new(
            SetVariableVersionId,
            ActivityTypeKey: "Elsa.SetVariable",
            Version: "1",
            DisplayName: "Set Variable",
            Category: PrimitivesCategory,
            Description: "Assigns a value to a declared workflow variable, resolved to its nearest visible scope.",
            ExecutionType: ActivityExecutionType.Action.ToString(),
            Available: true,
            AvailabilityReason: null,
            Inputs:
            [
                Input(VariableKey, "Variable", StringType, order: 0, required: true, nullable: false,
                    "The declared variable to assign.", uiHint: "variable-picker"),
                Input(ValueKey, "Value", AnyType, order: 1, required: true, nullable: true,
                    "The value to assign to the variable.", uiHint: "single-line")
            ],
            Outputs: [],
            Ports: [DonePort],
            ContainerStructure: null,
            AuthoringTemplate: IntrinsicTemplate(SetVariableVersionId),
            Intrinsic: new ActivityAuthoringIntrinsicView("Set", ValueKey, VariableKey, OutputNameInputKey: null));

    private static ActivityAuthoringDescriptorView SetOutputDescriptor() =>
        new(
            SetOutputVersionId,
            ActivityTypeKey: "Elsa.SetOutput",
            Version: "1",
            DisplayName: "Set Output",
            Category: PrimitivesCategory,
            Description: "Assigns a value to a named workflow output.",
            ExecutionType: ActivityExecutionType.Action.ToString(),
            Available: true,
            AvailabilityReason: null,
            Inputs:
            [
                Input(NameKey, "Output Name", StringType, order: 0, required: true, nullable: false,
                    "The name of the workflow output to assign.", uiHint: "single-line"),
                Input(ValueKey, "Value", AnyType, order: 1, required: true, nullable: true,
                    "The value to assign to the output.", uiHint: "single-line")
            ],
            Outputs: [],
            Ports: [DonePort],
            ContainerStructure: null,
            AuthoringTemplate: IntrinsicTemplate(SetOutputVersionId),
            Intrinsic: new ActivityAuthoringIntrinsicView("SetOutput", ValueKey, VariableInputKey: null, OutputNameInputKey: NameKey));

    /// <summary>Elsa 3's <c>Correlate</c>: stamps the running instance's correlation id.</summary>
    private static ActivityAuthoringDescriptorView SetCorrelationIdDescriptor() =>
        ValueEffectDescriptor(
            SetCorrelationIdVersionId,
            activityTypeKey: "Elsa.SetCorrelationId",
            displayName: "Set Correlation Id",
            description: "Assigns the correlation id of the running workflow instance.",
            valueDescription: "The correlation id to assign to this instance.",
            intrinsicKind: "SetCorrelationId");

    /// <summary>Elsa 3's <c>SetName</c>: stamps the running instance's display name.</summary>
    private static ActivityAuthoringDescriptorView SetInstanceNameDescriptor() =>
        ValueEffectDescriptor(
            SetInstanceNameVersionId,
            activityTypeKey: "Elsa.SetInstanceName",
            displayName: "Set Instance Name",
            description: "Assigns the display name of the running workflow instance.",
            valueDescription: "The name to assign to this instance.",
            intrinsicKind: "SetInstanceName");

    /// <summary>
    /// Elsa 3's <c>Finish</c>/<c>Complete</c>: ends the workflow with a named outcome. The single authored input is
    /// the outcome key, so it rides the intrinsic view's value-input slot — that slot names whichever input carries
    /// the intrinsic's argument, which for Finish is <c>outcome</c> rather than <c>value</c>.
    /// </summary>
    private static ActivityAuthoringDescriptorView FinishDescriptor() =>
        new(
            FinishVersionId,
            ActivityTypeKey: "Elsa.Finish",
            Version: "1",
            DisplayName: "Finish",
            Category: PrimitivesCategory,
            Description: "Completes the workflow with a named outcome.",
            ExecutionType: ActivityExecutionType.Action.ToString(),
            Available: true,
            AvailabilityReason: null,
            Inputs:
            [
                Input(OutcomeKey, "Outcome", StringType, order: 0, required: true, nullable: false,
                    "The outcome the workflow completes with.", uiHint: "single-line")
            ],
            Outputs: [],
            // Finish terminates the run: there is no continuation to wire, so it declares no outgoing port.
            Ports: [],
            ContainerStructure: null,
            AuthoringTemplate: IntrinsicTemplate(FinishVersionId),
            Intrinsic: new ActivityAuthoringIntrinsicView("Finish", OutcomeKey, VariableInputKey: null, OutputNameInputKey: null));

    /// <summary>
    /// Shared shape for the identity-effect intrinsics: one required string <c>value</c> input, one Done port, no
    /// variable target and no output name.
    /// </summary>
    private static ActivityAuthoringDescriptorView ValueEffectDescriptor(
        string versionId,
        string activityTypeKey,
        string displayName,
        string description,
        string valueDescription,
        string intrinsicKind) =>
        new(
            versionId,
            ActivityTypeKey: activityTypeKey,
            Version: "1",
            DisplayName: displayName,
            Category: PrimitivesCategory,
            Description: description,
            ExecutionType: ActivityExecutionType.Action.ToString(),
            Available: true,
            AvailabilityReason: null,
            Inputs:
            [
                Input(ValueKey, "Value", StringType, order: 0, required: true, nullable: false,
                    valueDescription, uiHint: "single-line")
            ],
            Outputs: [],
            Ports: [DonePort],
            ContainerStructure: null,
            AuthoringTemplate: IntrinsicTemplate(versionId),
            Intrinsic: new ActivityAuthoringIntrinsicView(intrinsicKind, ValueKey, VariableInputKey: null, OutputNameInputKey: null));

    private static ActivityPortDescriptorView DonePort => new("Done", "Done", null, true);

    private static ActivityInputDescriptorView Input(
        string referenceKey,
        string name,
        string type,
        float order,
        bool required,
        bool nullable,
        string description,
        string uiHint) =>
        new(
            referenceKey,
            name,
            type,
            // The Set Variable / Set Output intrinsic inputs are scalar (single value / any).
            CollectionKind: CollectionKind.Single,
            DisplayName: name,
            Description: description,
            Order: order,
            Category: null,
            IsBrowsable: true,
            IsRequired: required,
            IsNullable: nullable,
            UiHint: uiHint,
            DefaultValue: null,
            DefaultSyntax: null,
            UiSpecifications: null);

    private static ActivityAuthoringTemplateView IntrinsicTemplate(string versionId) =>
        new(
            "intrinsic",
            versionId,
            new Dictionary<string, ActivityArgumentValue>(StringComparer.Ordinal),
            new Dictionary<string, ActivityArgumentValue>(StringComparer.Ordinal),
            Structure: null);
}
