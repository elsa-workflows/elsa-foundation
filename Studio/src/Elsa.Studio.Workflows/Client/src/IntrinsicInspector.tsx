import { useId } from "react";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityAuthoringIntrinsic, VisibleVariableView } from "./workflowTypes";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import { ActivityPropertiesPanel, type ActivityPropertiesPanelProps } from "./ActivityPropertiesPanel";
import { readIntrinsicVariable, writeIntrinsicVariable } from "./intrinsicActivities";
import { makeVariableReference, WORKFLOW_SCOPE_ID } from "./scopedVariables";

const optionSeparator = "::";

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
  const optionsUnavailable = status !== "ready";
  const selectedIsVisible = selected !== "" && visibleVariables.some(variable => optionKey(variable.referenceKey, variable.scopeId) === selected);

  return (
    <div className="wf-property-row wf-intrinsic-variable">
      <div className="wf-property-row-header">
        <label id={labelId}>{label}</label>
      </div>
      {description ? <p>{description}</p> : null}
      <div className="wf-variable-picker">
        <select
          aria-labelledby={labelId}
          value={selected}
          disabled={optionsUnavailable}
          onChange={event => onSelect(parseOptionKey(event.target.value))}
        >
          <option value="">Select a variable…</option>
          {selected !== "" && !selectedIsVisible ? (
            <option value={selected} disabled>{parseOptionKey(selected)?.referenceKey ?? selected} (not visible from this scope)</option>
          ) : null}
          {visibleVariables.map(variable => (
            <option key={optionKey(variable.referenceKey, variable.scopeId)} value={optionKey(variable.referenceKey, variable.scopeId)}>
              {variable.name} · {variable.isWorkflowScope ? "workflow" : "container"}
            </option>
          ))}
        </select>
        {status === "loading" ? (
          <p className="wf-variable-picker-note" role="status">Loading visible variables… The current target is preserved.</p>
        ) : status === "error" ? (
          <div className="wf-variable-picker-error" role="alert">
            <span>Variable scope information could not be loaded. The current target is preserved.</span>
            {retry ? <button type="button" onClick={retry}>Retry</button> : null}
          </div>
        ) : status === "unavailable" ? (
          <p className="wf-variable-picker-note">Variable scope information is unavailable. The current target is preserved.</p>
        ) : visibleVariables.length === 0 ? (
          <p className="wf-variable-picker-note">No variables are visible here. Declare one on the workflow or a container scope.</p>
        ) : null}
      </div>
    </div>
  );
}

function normalizeScopeId(scopeId: string | null | undefined) {
  return !scopeId || scopeId === WORKFLOW_SCOPE_ID ? WORKFLOW_SCOPE_ID : scopeId;
}

function optionKey(referenceKey: string, scopeId: string | null | undefined) {
  return `${normalizeScopeId(scopeId)}${optionSeparator}${referenceKey}`;
}

function selectedOptionKey(reference: { referenceKey: string; declaringScopeId?: string | null } | null) {
  return reference ? optionKey(reference.referenceKey, reference.declaringScopeId) : "";
}

function parseOptionKey(key: string): { referenceKey: string; scopeId: string } | null {
  const index = key.indexOf(optionSeparator);
  if (index < 0) return null;
  const referenceKey = key.slice(index + optionSeparator.length);
  return referenceKey ? { scopeId: key.slice(0, index), referenceKey } : null;
}
