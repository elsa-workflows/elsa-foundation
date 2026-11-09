import { useEffect, useId } from "react";
import { AlertTriangle } from "lucide-react";
import type { StudioActivityPropertyEditorContribution, StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import {
  compensateTargetUiHint,
  compensationTargetProblem,
  confirmTargetUiHint,
  enclosingCompensationScopes
} from "./compensationTargets";
import { useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";

// The `scope` input of Confirm and Compensate (spec 258 T321): blank reaches the nearest enclosing compensation scope;
// a value names an enclosing scope, so the picker lists only those. The validator's message explains anything else.
export function CompensationTargetEditor({ descriptor, value, disabled, context, onChange, onValidationChange }: StudioActivityPropertyEditorProps) {
  const workflowState = useWorkflowReferenceAuthoring()?.workflowState;
  const messageId = useId();
  const activityName = descriptor.uiHint === compensateTargetUiHint ? "Compensate" : "Confirm";
  const nodeId = context.activityId ?? "";
  const scopes = enclosingCompensationScopes(workflowState?.rootActivity, nodeId);
  const target = typeof value === "string" ? value.trim() : "";
  const problem = scopes ? compensationTargetProblem(activityName, nodeId, scopes, target) : null;
  // A null scope list means the graph is not known (yet), so a stored target is shown as is, not as refused.
  const known = !target || !scopes || scopes.some(scope => scope.nodeId === target);

  // The validator's errors block publishing, not saving, so the editor reports none of its own.
  useEffect(() => onValidationChange?.(false), [onValidationChange]);

  return (
    <div className="wf-compensation-target">
      <select
        aria-label={descriptor.displayName || descriptor.name}
        aria-describedby={problem ? messageId : undefined}
        aria-invalid={problem ? true : undefined}
        value={target}
        disabled={disabled}
        onChange={event => onChange(event.target.value || null)}
      >
        <option value="">Nearest enclosing scope{scopes?.[0] ? ` (${scopes[0].label})` : ""}</option>
        {(scopes ?? []).map(scope => (
          <option key={scope.nodeId} value={scope.nodeId}>
            {scope.label === scope.nodeId ? scope.nodeId : `${scope.label} (${scope.nodeId})`}
          </option>
        ))}
        {!scopes && target ? <option value={target}>{target}</option> : null}
        {!known ? <option value={target}>{target} (not an enclosing scope)</option> : null}
      </select>
      {problem ? (
        <p className="wf-compensation-target-problem" id={messageId} role="status">
          <AlertTriangle size={12} aria-hidden="true" /> {problem}
        </p>
      ) : null}
    </div>
  );
}

export const compensationTargetPropertyEditor: StudioActivityPropertyEditorContribution = {
  id: "workflows.property.compensation-target",
  order: 50,
  supportsValidationReporting: true,
  supports: (descriptor, context) => context.scope !== "collection"
    && (descriptor.uiHint === confirmTargetUiHint || descriptor.uiHint === compensateTargetUiHint),
  component: CompensationTargetEditor
};
