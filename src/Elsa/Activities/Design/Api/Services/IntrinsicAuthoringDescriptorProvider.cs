using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Surfaces the engine's variable- and output-authoring intrinsics — Set Variable and Set Output — as
/// built-in authoring-catalog descriptors so the designer palette can offer the minimum authoring loop
/// (#929). The runtime already assigns variables and workflow outputs through engine intrinsics
/// (<c>WorkflowIntrinsicExecutor</c>); per ADR 0045 these operations are engine-owned and are never CLR
/// activities. These descriptors carry an <see cref="ActivityAuthoringIntrinsicView"/> so the authoring
/// client materializes an <c>AuthoredWorkflowIntrinsic</c> node rather than a catalog activity reference.
/// </summary>
public sealed class IntrinsicAuthoringDescriptorProvider : IBuiltInAuthoringDescriptorProvider
{
    // Stable authoring ids, mirroring Elsa.Workflows.Design.Core.Authoring.WorkflowIntrinsicAuthoringIds
    // (internal there). A placed node reuses these as its ActivityVersionId, matching the code-first builder.
    private const string SetVariableVersionId = "elsa.intrinsic.set@1";
    private const string SetOutputVersionId = "elsa.intrinsic.set-output@1";

    // Intrinsic input keys, mirroring WorkflowIntrinsicAuthoringInputKeys.
    private const string ValueKey = "value";
    private const string NameKey = "name";

    // A descriptor-only key naming the variable target. The target is authored as a VariableReference on the
    // intrinsic node, not as a runtime value binding, so the client renders it with a variable picker.
    private const string VariableKey = "variable";

    private const string PrimitivesCategory = "Primitives";
    private const string AnyType = "Elsa.Any";
    private const string StringType = "String";

    public IReadOnlyCollection<ActivityAuthoringDescriptorView> GetDescriptors() =>
    [
        SetVariableDescriptor(),
        SetOutputDescriptor()
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
