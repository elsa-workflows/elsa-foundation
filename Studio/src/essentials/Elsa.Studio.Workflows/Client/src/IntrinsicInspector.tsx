import { useId } from "react";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAuthoringIntrinsic, VisibleVariableView } from "./workflowTypes";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import { ActivityPropertiesPanel, type ActivityPropertiesPanelProps } from "./ActivityPropertiesPanel";
import { readIntrinsicVariable, writeIntrinsicVariable } from "./intrinsicActivities";
import { makeVariableReference } from "./scopedVariables";
import { selectedOptionKey, VariableTargetSelect } from "./variableTargetPicker";

export type IntrinsicInspectorProps = ActivityPropertiesPanelProps & {
  intrinsic: ActivityAuthoringIntrinsic;
};

/**
 * Inspector body for an engine-intrinsic node (Set Variable / Set Output — foundation #929).
 *
 * A Set Variable intrinsic authors its write target as a {@link ActivityAuthoringIntrinsic} variable
 * reference (not a runtime input binding), so that key is rendered as a dedicated variable-target picker
 * writing `intrinsic.variable`. Every remaining input (the `value`, and for Set Output the literal output
 * `name`) is delegated to the standard {@link ActivityPropertiesPanel} so authors get the full
 * expression-syntax control, conversion controls, and editors unchanged.
 */
export function IntrinsicInspector({ intrinsic, ...panelProps }: IntrinsicInspectorProps) {
  const { activity, descriptor, visibleVariables, scopeStatus, scopeRetry, onChange } = panelProps;
  const variableInputKey = intrinsic.variableInputKey?.trim() || null;

  // The variable target is authored separately from the runtime inputs, so drop its pseudo-input from the
  // descriptor the standard panel renders; the value (and output name) inputs flow through untouched.
  const panelDescriptor = variableInputKey && descriptor
    ? { ...descriptor, inputs: descriptor.inputs.filter(input => input.referenceKey !== variableInputKey) }
    : descriptor;

  const variableInput = variableInputKey && descriptor
    ? descriptor.inputs.find(input => input.referenceKey === variableInputKey) ?? null
    : null;

  return (
    <div className="wf-intrinsic-inspector">
      {variableInputKey ? (
        <IntrinsicVariablePicker
          input={variableInput}
          visibleVariables={visibleVariables}
          status={scopeStatus}
          retry={scopeRetry}
          selected={selectedOptionKey(readIntrinsicVariable(activity))}
          onSelect={selection =>
            onChange(writeIntrinsicVariable(activity, selection ? makeVariableReference(selection.referenceKey, selection.scopeId) : null))
          }
        />
      ) : null}
      <ActivityPropertiesPanel {...panelProps} descriptor={panelDescriptor} />
    </div>
  );
}

function IntrinsicVariablePicker({
  input,
  visibleVariables,
  status,
  retry,
  selected,
  onSelect
}: {
  input: StudioActivityInputDescriptor | null;
  visibleVariables: VisibleVariableView[];
  status: ScopedVariableAnalysisStatus;
  retry?: () => void;
  selected: string;
  onSelect(selection: { referenceKey: string; scopeId: string } | null): void;
}) {
  const labelId = useId();
  const label = input?.displayName || input?.name || "Variable";
  const description = input?.description;

  return (
    <div className="wf-property-row wf-intrinsic-variable">
      <div className="wf-property-row-header">
        <label id={labelId}>{label}</label>
      </div>
      {description ? <p>{description}</p> : null}
      <VariableTargetSelect
        ariaLabelledBy={labelId}
        visibleVariables={visibleVariables}
        status={status}
        retry={retry}
        selected={selected}
        onSelect={onSelect}
      />
    </div>
  );
}
