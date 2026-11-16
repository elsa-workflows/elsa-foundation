import { useId } from "react";
import { argumentNameKeys, isPlainRecord, readArgumentType, readStringField } from "./workflowProperties";
import { getLiteralEditorValue, readWrappedInput, withExpression, writeInputValue } from "./activityProperties";
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
 * writing `intrinsic.variable`. Set Output selects a declaration from the current draft. The `value`
 * editor follows the selected variable/output type and is delegated to the standard {@link ActivityPropertiesPanel} so authors get the full
 * expression-syntax control, conversion controls, and editors unchanged.
 */
export function IntrinsicInspector({ intrinsic, ...panelProps }: IntrinsicInspectorProps) {
  const { activity, descriptor, visibleVariables, scopeStatus, scopeRetry, onChange, workflowState, readOnly } = panelProps;
  const outputInput = intrinsic.outputNameInputKey ? descriptor?.inputs.find(input => input.referenceKey === intrinsic.outputNameInputKey) : undefined;
  const variableInputKey = intrinsic.variableInputKey?.trim() || null;
  const selectedTarget = selectedOptionKey(readIntrinsicVariable(activity));
  const outputName = outputInput ? getLiteralEditorValue(activity, outputInput) : null;
  const selectedOutput = Array.isArray(workflowState?.outputs) ? workflowState.outputs.find(item => isPlainRecord(item) && readStringField(item, argumentNameKeys) === outputName) : null;
  const outputType = isPlainRecord(selectedOutput) ? readArgumentType(selectedOutput) : null;
  const targetType = variableInputKey && scopeStatus === "ready"
    ? visibleVariables.find(variable => selectedOptionKey(makeVariableReference(variable.referenceKey, variable.scopeId)) === selectedTarget)?.type
    : outputType;

  // The variable target is authored separately from the runtime inputs, so drop its pseudo-input from the
  // descriptor the standard panel renders. Derive the value editor from the current declaration on
  // every render, so a target or declaration change cannot leave an old type on the editor.
  const panelDescriptor = descriptor
    ? {
        ...descriptor,
        inputs: descriptor.inputs
          .filter(input => !variableInputKey || input.referenceKey !== variableInputKey)
          .map(input => input.referenceKey === intrinsic.valueInputKey && targetType
            ? { ...input, typeName: targetType.alias, collectionKind: targetType.collectionKind, uiHint: undefined }
            : input)
      }
    : descriptor;
  const visibleDescriptor = outputInput && panelDescriptor ? { ...panelDescriptor, inputs: panelDescriptor.inputs.filter(input => input !== outputInput) } : panelDescriptor;

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
          selected={selectedTarget}
          onSelect={selection =>
            onChange(writeIntrinsicVariable(activity, selection ? makeVariableReference(selection.referenceKey, selection.scopeId) : null))
          }
        />
      ) : null}
      {outputInput ? <WorkflowOutputPicker input={outputInput} outputs={workflowState?.outputs} value={getLiteralEditorValue(activity, outputInput)} disabled={readOnly}
        onSelect={name => onChange(writeInputValue(activity, outputInput, withExpression(readWrappedInput(activity, outputInput), "Literal", name)))} /> : null}
      <ActivityPropertiesPanel {...panelProps} descriptor={visibleDescriptor} />
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

function WorkflowOutputPicker({ input, outputs, value, disabled, onSelect }: {
  input: StudioActivityInputDescriptor; outputs: unknown; value: unknown; disabled?: boolean; onSelect(name: string): void;
}) {
  const id = useId();
  const names = [...new Set((Array.isArray(outputs) ? outputs : []).flatMap(item =>
    item && typeof item === "object" && readStringField(item, argumentNameKeys).trim() ? [readStringField(item, argumentNameKeys)] : []))];
  const selected = typeof value === "string" ? value : "";
  const missing = Boolean(selected && !names.includes(selected));
  return <div className="wf-property-row">
    <div className="wf-property-row-header"><label htmlFor={id}>{input.displayName || "Output Name"}</label></div>
    <select id={id} className="studio-input" disabled={disabled || !names.length} value={selected} aria-invalid={missing} aria-describedby={`${id}-help`} onChange={event => onSelect(event.target.value)}>
      <option value="">Select an output</option>
      {missing && <option value={selected} disabled>{selected} (renamed or removed)</option>}
      {names.map(name => <option key={name} value={name}>{name}</option>)}
    </select>
    <p id={`${id}-help`} role={missing ? "alert" : undefined}>{missing ? "This output is no longer defined. " : ""}{!names.length ? "Add a workflow output in the editor's Outputs tab, then select it here." : missing ? "Select its current name." : "Assign a value to an output declared in this draft."}</p>
  </div>;
}
