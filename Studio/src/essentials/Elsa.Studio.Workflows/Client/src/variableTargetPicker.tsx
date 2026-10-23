import type { VisibleVariableView } from "./workflowTypes";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import { WORKFLOW_SCOPE_ID } from "./scopedVariables";

/**
 * Shared scope-aware variable-target picker (ADR-0027). A `<select>` over the variables visible from a
 * selected activity, keyed `scopeId::referenceKey`, with an empty "Select a variable…" option and the
 * loading/error/unavailable/empty status notes. A selected reference that is not in the visible set (or is
 * filtered out by `workflowScopeOnly`) is preserved as a disabled "(not visible from this scope)" option so
 * an authored target is never silently dropped. Used by the Set Variable intrinsic inspector and the
 * activity output-capture editor; the caller supplies its own label markup around the select.
 */

const optionSeparator = "::";

export function VariableTargetSelect({
  ariaLabelledBy,
  ariaLabel,
  visibleVariables,
  status,
  retry,
  selected,
  onSelect,
  workflowScopeOnly = false
}: {
  ariaLabelledBy?: string;
  ariaLabel?: string;
  visibleVariables: VisibleVariableView[];
  status: ScopedVariableAnalysisStatus;
  retry?: () => void;
  selected: string;
  onSelect(selection: { referenceKey: string; scopeId: string } | null): void;
  // v1 output captures only offer workflow-scope targets; the intrinsic picker offers every scope.
  workflowScopeOnly?: boolean;
}) {
  const options = workflowScopeOnly ? visibleVariables.filter(variable => variable.isWorkflowScope) : visibleVariables;
  const optionsUnavailable = status !== "ready";
  const selectedIsVisible = selected !== "" && options.some(variable => optionKey(variable.referenceKey, variable.scopeId) === selected);

  return (
    <div className="wf-variable-picker">
      <select
        aria-labelledby={ariaLabelledBy}
        aria-label={ariaLabel}
        value={selected}
        disabled={optionsUnavailable}
        onChange={event => onSelect(parseOptionKey(event.target.value))}
      >
        <option value="">Select a variable…</option>
        {selected !== "" && !selectedIsVisible ? (
          <option value={selected} disabled>{parseOptionKey(selected)?.referenceKey ?? selected} (not visible from this scope)</option>
        ) : null}
        {options.map(variable => (
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
      ) : options.length === 0 ? (
        <p className="wf-variable-picker-note">No variables are visible here. Declare one on the workflow or a container scope.</p>
      ) : null}
    </div>
  );
}

function normalizeScopeId(scopeId: string | null | undefined) {
  return !scopeId || scopeId === WORKFLOW_SCOPE_ID ? WORKFLOW_SCOPE_ID : scopeId;
}

export function optionKey(referenceKey: string, scopeId: string | null | undefined) {
  return `${normalizeScopeId(scopeId)}${optionSeparator}${referenceKey}`;
}

export function selectedOptionKey(reference: { referenceKey: string; declaringScopeId?: string | null } | null) {
  return reference ? optionKey(reference.referenceKey, reference.declaringScopeId) : "";
}

export function parseOptionKey(key: string): { referenceKey: string; scopeId: string } | null {
  const index = key.indexOf(optionSeparator);
  if (index < 0) return null;
  const referenceKey = key.slice(index + optionSeparator.length);
  return referenceKey ? { scopeId: key.slice(0, index), referenceKey } : null;
}
