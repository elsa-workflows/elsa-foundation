import { useId } from "react";
import type { ActivityNode } from "../workflowTypes";
import {
  isBackgroundPlacement,
  readDisableBackgroundInputRefresh,
  readLostUpdateMode,
  writeDisableBackgroundInputRefresh,
  writeLostUpdateMode
} from "../variableLifecycle";
import "./variableLifecycle.css";

/**
 * The two per-node settings of spec 1221 in the designer: the lost-update mode (FR-012) and, for a background step, the
 * switch that turns off the admission refresh of its inputs (FR-019). Defaults are omitted from the authored node.
 */
export function VariableLifecycleNodeSettings({ node, readOnly = false, onChange }: {
  node: ActivityNode;
  readOnly?: boolean;
  onChange(node: ActivityNode): void;
}) {
  const id = useId();
  const mode = readLostUpdateMode(node);
  const background = isBackgroundPlacement(node);
  const refreshDisabled = readDisableBackgroundInputRefresh(node);
  return (
    <section className="wf-node-lifecycle-settings" aria-label="Variable writes">
      <h4>Variable writes</h4>
      <label htmlFor={`${id}-mode`}>
        <span>Lost-update mode</span>
        <select
          id={`${id}-mode`}
          value={mode}
          disabled={readOnly}
          aria-describedby={`${id}-mode-help`}
          onChange={event => onChange(writeLostUpdateMode(node, event.target.value === "Fault" ? "Fault" : "Default"))}
        >
          <option value="Default">Default (warn)</option>
          <option value="Fault">Fault (refuse)</option>
        </select>
        <small id={`${id}-mode-help`} className="wf-muted">
          {mode === "Fault"
            ? "If a variable this step reads and writes changed before its write commits, the write is refused and the step faults (VariableLostUpdate). An operator can then retry, re-execute, or accept the recorded completion."
            : "If a variable this step reads and writes changed before its write commits, the write still applies and the run records a lost-update warning with the value it replaced. A variable it only reads never causes one. Some activity types always use Fault, whatever this setting says."}
        </small>
      </label>
      <div className="wf-node-lifecycle-checkbox">
        <input
          id={`${id}-refresh`}
          type="checkbox"
          checked={refreshDisabled}
          disabled={readOnly || (!background && !refreshDisabled)}
          aria-describedby={`${id}-refresh-help`}
          onChange={event => onChange(writeDisableBackgroundInputRefresh(node, event.target.checked))}
        />
        <label htmlFor={`${id}-refresh`}>
          <span>Run on the inputs read at start</span>
          <small id={`${id}-refresh-help`} className="wf-muted">
            {background
              ? "Turns off the background input refresh: when a variable the inputs read changed before the worker picks up the step, it runs on the inputs read at start instead of evaluating them again. A write to that variable then meets the lost-update mode: a warning by default, a fault under Fault."
              : "Applies to background placement only; this step runs inline."}
          </small>
        </label>
      </div>
    </section>
  );
}
