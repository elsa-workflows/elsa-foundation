import { useCallback, useEffect, useRef, useState } from "react";
import { X } from "lucide-react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { planVariableRename } from "../api/variableRename";
import { applyVariableRenameEdits, withVariableName, type VariableRenameEdit, type VariableRenameFlag } from "../variableRename";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { getNotifications } from "./notifications";
import "./variableRenameNotice.css";

/** What a rename left for the author: the reads it updated and the references to repair by hand. */
export interface VariableRenameOutcome {
  oldName: string;
  newName: string;
  updated: number;
  flags: VariableRenameFlag[];
  error?: string;
}

/** How a flagged reference is named to the author: the activity's label and the input's display name. */
export interface VariableRenameReferenceLabel {
  activity: string;
  input: string;
}

/**
 * Plans and applies the script follow-up of a variable rename (spec 1222 FR-022). The planner reads the draft as it was
 * before the rename, so the variable is restored to its old name in the state it is sent. Resolvable literal reads are
 * edited in the draft (through the normal edit path, so autosave and undo pick them up). A rename whose reads all
 * followed is reported as a notification; one that leaves references to repair by hand (FR-022), or that could not be
 * planned, keeps an inline notice that lists them. Renames are planned one after another, so a second rename made
 * while the first is planned starts from the scripts the first one updated.
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
  const queue = useRef<Promise<void>>(Promise.resolve());
  const disposed = useRef(false);
  useEffect(() => {
    disposed.current = false;
    return () => { disposed.current = true; };
  }, []);

  const follow = useCallback(async (scopeId: string, referenceKey: string, oldName: string, newName: string) => {
    const current = latest.current;
    if (!current || disposed.current) return;
    try {
      const plan = await planVariableRename(context, withVariableName(current, scopeId, referenceKey, oldName, catalog), scopeId, referenceKey, newName);
      if (disposed.current) return;
      if (!plan) {
        // FR-022: a rename never breaks a read silently, even on a host that cannot plan one.
        setOutcome({
          oldName,
          newName,
          updated: 0,
          flags: [],
          error: `Scripts that read '${oldName}' were not checked: this server does not plan variable renames. Check them by hand.`
        });
        return;
      }
      const target = latest.current ?? current;
      const { state: next, skipped } = applyVariableRenameEdits(target, plan.edits, catalog);
      if (plan.edits.length > skipped.length) {
        // The next queued rename plans from this state, before the editor re-renders with it.
        latest.current = next;
        updateState(draftState => applyVariableRenameEdits(draftState, plan.edits, catalog).state);
      }
      const updated = plan.edits.length - skipped.length;
      const flags = [...plan.flags, ...skipped.map(changedFlag(oldName, newName))];
      if (flags.length === 0) {
        setOutcome(null);
        if (updated > 0)
          getNotifications().notify({ level: "success", title: `Renamed '${oldName}' to '${newName}'`, message: `${readCount(updated)} updated.` });
        return;
      }
      setOutcome({ oldName, newName, updated, flags });
    } catch (error) {
      if (disposed.current) return;
      setOutcome({
        oldName,
        newName,
        updated: 0,
        flags: [],
        error: `Scripts that read '${oldName}' were not updated: ${error instanceof Error ? error.message : "the rename could not be planned"}. Check them by hand.`
      });
    }
  }, [catalog, context, updateState]);

  const onVariableRenamed = useCallback((scopeId: string, referenceKey: string, oldName: string, newName: string) => {
    if (!oldName || !newName.trim() || oldName === newName) return;
    queue.current = queue.current.then(() => follow(scopeId, referenceKey, oldName, newName));
  }, [follow]);

  return { outcome, dismiss: () => setOutcome(null), onVariableRenamed };
}

function readCount(count: number) {
  return count === 1 ? "1 script read was" : `${count} script reads were`;
}

function changedFlag(oldName: string, newName: string) {
  return (edit: VariableRenameEdit): VariableRenameFlag => ({
    nodeId: edit.nodeId,
    inputKey: edit.inputKey,
    reason: "Changed",
    message: `The script changed while the rename was planned; change '${oldName}' to '${newName}' in it by hand.`
  });
}

function summarize({ oldName, newName, updated, flags, error }: VariableRenameOutcome) {
  return error ?? `Renamed '${oldName}' to '${newName}'. ${readCount(updated)} updated` +
    `; ${flags.length === 1 ? "1 reference needs" : `${flags.length} references need`} your attention.`;
}

/**
 * Lists the references a rename left to repair by hand, each with a way to go to it (FR-022). The summary is announced
 * through a live region that stays mounted; the list itself is not live, so its buttons are not read out.
 */
export function VariableRenameNotice({ outcome, labelFor, onSelectReference, onDismiss }: {
  outcome: VariableRenameOutcome | null;
  labelFor(nodeId: string, inputKey: string): VariableRenameReferenceLabel;
  onSelectReference(nodeId: string, inputKey: string): void;
  onDismiss(): void;
}) {
  const summary = outcome ? summarize(outcome) : "";
  return (
    <>
      <p className="wf-visually-hidden" role="status">{summary}</p>
      {outcome ? (
        <section className="wf-variable-rename-notice" aria-label="Variable rename">
          <div className="wf-variable-rename-notice-heading">
            <p>{summary}</p>
            <StudioButton variant="ghost" size="sm" aria-label="Dismiss variable rename notice" onClick={onDismiss}>
              <X size={14} aria-hidden="true" /> Dismiss
            </StudioButton>
          </div>
          {outcome.flags.length > 0 ? (
            <ul>
              {outcome.flags.map((flag, index) => {
                const label = labelFor(flag.nodeId, flag.inputKey);
                return (
                  <li key={`${flag.nodeId}:${flag.inputKey}:${index}`}>
                    <StudioButton
                      variant="ghost"
                      size="sm"
                      className="wf-variable-rename-notice-target"
                      aria-label={`${label.activity} · ${label.input}, go to input`}
                      onClick={() => onSelectReference(flag.nodeId, flag.inputKey)}
                    >
                      {label.activity} · {label.input}
                    </StudioButton>
                    <span>{flag.message}</span>
                  </li>
                );
              })}
            </ul>
          ) : null}
        </section>
      ) : null}
    </>
  );
}
