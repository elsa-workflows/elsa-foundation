import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { planVariableRename } from "../api/variableRename";
import { applyVariableRenameEdits, withVariableName, type VariableRenameEdit, type VariableRenameFlag } from "../variableRename";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import type { WorkflowDefinitionState } from "../workflowTypes";
import "./variableRenameNotice.css";

/** What the last rename did to the draft's scripts: how many reads followed it, and what to repair by hand. */
export interface VariableRenameOutcome {
  oldName: string;
  newName: string;
  updated: number;
  flags: VariableRenameFlag[];
  error?: string;
}

/**
 * Plans and applies the script follow-up of a variable rename (spec 1222 FR-021, FR-022). The planner reads the draft
 * as it was before the rename, so the variable is restored to its old name in the state it is sent. Resolvable literal
 * reads are edited in the draft (through the normal edit path, so autosave and undo pick them up); every reference the
 * planner cannot update safely, and every edit whose script changed while the plan was made, is listed for repair.
 */
export function useVariableRename({ context, state, catalog, updateState }: {
  context: StudioEndpointContext;
  state: WorkflowDefinitionState | null | undefined;
  catalog?: ActivityCatalogLookup;
  updateState(producer: (state: WorkflowDefinitionState) => WorkflowDefinitionState): void;
}) {
  const [outcome, setOutcome] = useState<VariableRenameOutcome | null>(null);
  const latest = useRef(state);
  latest.current = state;
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);

  const onVariableRenamed = useCallback((scopeId: string, referenceKey: string, oldName: string, newName: string) => {
    const current = latest.current;
    if (!current || !oldName || !newName.trim() || oldName === newName) return;
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    const planned = withVariableName(current, scopeId, referenceKey, oldName, catalog);
    planVariableRename(context, planned, scopeId, referenceKey, newName, controller.signal)
      .then(plan => {
        if (controller.signal.aborted || !plan) return;
        if (plan.edits.length === 0 && plan.flags.length === 0) {
          setOutcome(null);
          return;
        }
        const target = latest.current ?? current;
        const { skipped } = applyVariableRenameEdits(target, plan.edits, catalog);
        if (plan.edits.length > skipped.length)
          updateState(draftState => applyVariableRenameEdits(draftState, plan.edits, catalog).state);
        setOutcome({
          oldName,
          newName,
          updated: plan.edits.length - skipped.length,
          flags: [...plan.flags, ...skipped.map(changedFlag(oldName, newName))]
        });
      })
      .catch(error => {
        if (controller.signal.aborted) return;
        setOutcome({
          oldName,
          newName,
          updated: 0,
          flags: [],
          error: `Scripts that read '${oldName}' were not updated: ${error instanceof Error ? error.message : "the rename could not be planned"}. Check them by hand.`
        });
      });
  }, [catalog, context, updateState]);

  return { outcome, dismiss: () => setOutcome(null), onVariableRenamed };
}

function changedFlag(oldName: string, newName: string) {
  return (edit: VariableRenameEdit): VariableRenameFlag => ({
    nodeId: edit.nodeId,
    inputKey: edit.inputKey,
    reason: "Changed",
    message: `The script changed while the rename was planned; change '${oldName}' to '${newName}' in it by hand.`
  });
}

/** Reports a rename's script follow-up: the reads updated, and each reference to repair, with a way to go to it. */
export function VariableRenameNotice({ outcome, labelFor, onSelectNode, onDismiss }: {
  outcome: VariableRenameOutcome | null;
  labelFor(nodeId: string): string;
  onSelectNode?(nodeId: string): void;
  onDismiss(): void;
}) {
  if (!outcome) return null;
  const { oldName, newName, updated, flags, error } = outcome;
  const summary = error
    ?? `Renamed '${oldName}' to '${newName}'. ${updated === 1 ? "1 script read was" : `${updated} script reads were`} updated` +
      (flags.length > 0 ? `; ${flags.length === 1 ? "1 reference needs" : `${flags.length} references need`} your attention.` : ".");
  return (
    <section className={`wf-variable-rename-notice${flags.length > 0 || error ? " wf-variable-rename-notice-attention" : ""}`} aria-label="Variable rename" role="status">
      <div className="wf-variable-rename-notice-heading">
        <p>{summary}</p>
        <button type="button" onClick={onDismiss} aria-label="Dismiss variable rename notice">Dismiss</button>
      </div>
      {flags.length > 0 ? (
        <ul>
          {flags.map((flag, index) => (
            <li key={`${flag.nodeId}:${flag.inputKey}:${index}`}>
              {onSelectNode
                ? <button type="button" className="wf-variable-rename-notice-target" onClick={() => onSelectNode(flag.nodeId)}>{labelFor(flag.nodeId)} · {flag.inputKey}</button>
                : <strong>{labelFor(flag.nodeId)} · {flag.inputKey}</strong>}
              <span>{flag.message}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
