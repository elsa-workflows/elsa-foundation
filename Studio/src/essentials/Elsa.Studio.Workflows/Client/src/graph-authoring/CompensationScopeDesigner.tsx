import { useId, type KeyboardEvent, type MouseEvent, type ReactNode, type RefObject } from "react";
import type { Node } from "@xyflow/react";
import { AlertTriangle, ArrowDown, ArrowUp, Check, ChevronRight, CircleDashed, CircleDot, Hand, Hourglass, Minus, Plus, Trash2, Undo2, X } from "lucide-react";
import { renderActivityIcon } from "../activityIconView";
import {
  compensationStepProblems,
  moveCompensationStep,
  readCompensationSteps,
  removeCompensationStep,
  updateCompensationStep,
  type CompensationStep
} from "../compensationScope";
import {
  forwardStateLabel,
  ordinalLabel,
  recoveryStateLabel,
  scopePhaseLabel,
  type CompensationForwardState,
  type CompensationRecoveryState,
  type CompensationScopeRuntime
} from "../compensationRuntime";
import { resolveNodeActivityVisual } from "../workflow-editor/workflowNodeVisual";
import type { ChildSlot, WorkflowNodeData } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";
import "./compensationScopeDesigner.css";

/** Where a picked activity goes: a new step, a step's forward activity, or a step's compensation handler. */
export type CompensationActivityTarget =
  | { kind: "new-step"; index: number }
  | { kind: "step-activity"; index: number }
  | { kind: "compensation"; index: number };

export interface CompensationScopeDesignerProps {
  owner: ActivityNode;
  /** Canvas nodes for the scope's forward and handler activities: labels, icons, slots and run overlays. */
  nodes: Node<WorkflowNodeData>[];
  selectedNodeId: string | null;
  readOnly: boolean;
  canvasRef?: RefObject<HTMLDivElement | null>;
  onSelect(nodeId: string | null): void;
  /**
   * Applies a step edit. The edit is a function of the current steps, so the host applies it to the latest draft
   * and never overwrites a concurrent edit to a step's activities with a stale copy.
   */
  onChangeSteps?(edit: (steps: CompensationStep[]) => CompensationStep[], status: string): void;
  onRequestActivity?(target: CompensationActivityTarget, anchor: { clientX: number; clientY: number }): void;
  onOpenSlot?(ownerNodeId: string, ownerLabel: string, slot: ChildSlot): void;
  /** Run inspection only: what the run says about each step. */
  runtime?: CompensationScopeRuntime;
  /** Run inspection only: selects an occurrence by activity execution id. */
  onSelectExecution?(activityExecutionId: string): void;
  /** Run inspection only: opens the blocking incident a row waits on (spec 251 FR-217). */
  onOpenIncident?(incidentId: string): void;
  children?: ReactNode;
}

/**
 * The paired step list for a compensation scope (spec 250, OD4): each row shows a forward step and the handler that
 * undoes it, side by side. Authoring edits the scope's steps directly; run inspection shows each side's state.
 */
export function CompensationScopeDesigner({
  owner,
  nodes,
  selectedNodeId,
  readOnly,
  canvasRef,
  onSelect,
  onChangeSteps,
  onRequestActivity,
  onOpenSlot,
  runtime,
  onSelectExecution,
  onOpenIncident,
  children
}: CompensationScopeDesignerProps) {
  const steps = readCompensationSteps(owner);
  const problems = readOnly ? new Map<number, string>() : compensationStepProblems(steps);
  const nodesById = new Map(nodes.map(node => [node.id, node]));
  const headingId = useId();

  const change = (edit: (current: CompensationStep[]) => CompensationStep[], status: string) => onChangeSteps?.(edit, status);
  const request = (target: CompensationActivityTarget, event: MouseEvent<HTMLButtonElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onRequestActivity?.(target, {
      clientX: event.clientX || rect.left + rect.width / 2,
      clientY: event.clientY || rect.top + rect.height / 2
    });
  };

  return (
    <div ref={canvasRef} className="wf-compensation-designer" tabIndex={-1} aria-labelledby={headingId}>
      <header className="wf-compensation-heading">
        <h3 id={headingId}>Compensation scope</h3>
        <p>Steps run top to bottom. If a step fails, the steps that completed are undone bottom to top.</p>
        {runtime ? (
          <span className="wf-compensation-phase" data-phase={runtime.phase}>
            <PhaseIcon phase={runtime.phase} /> {scopePhaseLabel(runtime.phase)}
          </span>
        ) : null}
      </header>

      {runtime?.incomplete ? (
        <p className="wf-instance-note" role="note">
          This run has more activity data than Studio loaded. A state shown as Not loaded may be on a later page.
        </p>
      ) : null}

      {runtime?.cause ? (
        <p className="wf-compensation-cause" role="note">
          <AlertTriangle size={14} aria-hidden="true" />
          <span>
            Recovery started because <strong>{runtime.cause.step}</strong> {runtime.cause.kind === "uncertain" ? "could not confirm its outcome" : "failed"}
            {runtime.cause.failureType ? <>: <code>{runtime.cause.failureType}</code></> : null}
            {runtime.cause.message ? <> — {runtime.cause.message}</> : null}.
          </span>
          {onSelectExecution ? (
            <button type="button" className="wf-compensation-link" onClick={() => onSelectExecution(runtime.cause!.activityExecutionId)}>
              Show failed step
            </button>
          ) : null}
        </p>
      ) : null}

      {steps.length === 0 ? (
        <section className="wf-compensation-empty" aria-label="Empty compensation scope">
          <h4>No steps yet</h4>
          <p>{readOnly ? "This scope has no steps." : "Add the first step, then pair it with the activity that undoes it."}</p>
          {!readOnly ? (
            <button type="button" className="wf-compensation-add" onClick={event => request({ kind: "new-step", index: 0 }, event)}>
              <Plus size={14} aria-hidden="true" /> Add first step
            </button>
          ) : null}
        </section>
      ) : (
        <div className="wf-compensation-table">
          <div className="wf-compensation-columns" aria-hidden="true">
            <span>#</span><span>Step</span><span>Compensation</span>
          </div>
          <div className="wf-compensation-rows" role="list" aria-label="Compensation steps">
          {steps.map((step, index) => {
            const name = step.name.trim() || `Step ${index + 1}`;
            const forwardNode = step.activity ? nodesById.get(step.activity.nodeId) : undefined;
            const handlerNode = step.compensation ? nodesById.get(step.compensation.nodeId) : undefined;
            const row = runtime?.rows[index];
            const problem = problems.get(index);
            const problemId = problem ? `${headingId}-problem-${index}` : undefined;
            const handlerLabel = handlerNode?.data.label;
            return (
              <article
                key={index}
                className={`wf-compensation-row${problem ? " has-problem" : ""}`}
                role="listitem"
                aria-label={`Step ${index + 1}, ${name}${handlerLabel ? `, compensated by ${handlerLabel}` : ", no compensation"}`}
                data-step-index={index}
              >
                <span className="wf-compensation-ordinal" aria-hidden="true">{index + 1}</span>

                <div className="wf-compensation-step">
                  {readOnly ? (
                    <span className="wf-compensation-name">{name}</span>
                  ) : (
                    <input
                      className="wf-compensation-name-input"
                      aria-label={`Name of step ${index + 1}`}
                      aria-describedby={problemId}
                      aria-invalid={problem ? true : undefined}
                      value={step.name}
                      onChange={event => {
                        const name = event.target.value;
                        change(current => updateCompensationStep(current, index, { name }), `Renamed step ${index + 1}`);
                      }}
                    />
                  )}
                  <ActivityCell
                    node={forwardNode}
                    role="step"
                    stepName={name}
                    stepIndex={index}
                    selected={!!step.activity && selectedNodeId === step.activity.nodeId}
                    readOnly={readOnly}
                    onSelect={() => step.activity && onSelect(step.activity.nodeId)}
                    onChoose={event => request({ kind: "step-activity", index }, event)}
                    onOpenSlot={onOpenSlot}
                  />
                  {row ? (
                    <StateChip
                      kind="forward"
                      state={row.forward.state}
                      label={forwardStateLabel(row.forward.state)}
                      onSelect={row.forward.activityExecutionId && onSelectExecution ? () => onSelectExecution(row.forward.activityExecutionId!) : undefined}
                    />
                  ) : null}
                </div>

                <div className="wf-compensation-handler">
                  {step.compensation ? (
                    <>
                      <ActivityCell
                        node={handlerNode}
                        role="compensation"
                        stepName={name}
                        stepIndex={index}
                        selected={selectedNodeId === step.compensation.nodeId}
                        readOnly={readOnly}
                        onSelect={() => onSelect(step.compensation!.nodeId)}
                        onOpenSlot={onOpenSlot}
                      />
                      <span className="wf-compensation-undoes"><Undo2 size={12} aria-hidden="true" /> Undoes: {name}</span>
                      {!readOnly ? (
                        <button
                          type="button"
                          className="wf-compensation-icon-button"
                          aria-label={`Remove compensation for ${name}`}
                          title="Remove compensation"
                          onClick={() => change(current => updateCompensationStep(current, index, { compensation: null }), `Removed the compensation for ${name}`)}
                        >
                          <X size={14} aria-hidden="true" />
                        </button>
                      ) : null}
                    </>
                  ) : readOnly ? (
                    <span className="wf-compensation-none">No compensation</span>
                  ) : (
                    <button type="button" className="wf-compensation-add" aria-label={`Add compensation for ${name}`} onClick={event => request({ kind: "compensation", index }, event)}>
                      <Plus size={14} aria-hidden="true" /> Add compensation
                    </button>
                  )}
                  {row ? (
                    <StateChip
                      kind="recovery"
                      state={row.recovery.state}
                      label={`${recoveryStateLabel(row.recovery.state)}${row.recovery.order ? ` (${ordinalLabel(row.recovery.order)})` : ""}${attemptLabel(row.recovery.attempts)}`}
                      onSelect={row.recovery.activityExecutionId && onSelectExecution ? () => onSelectExecution(row.recovery.activityExecutionId!) : undefined}
                    />
                  ) : null}
                  {row?.recovery.incidentId && onOpenIncident ? (
                    <button type="button" className="wf-compensation-link" aria-label={`Show the incident ${name} is waiting on`} onClick={() => onOpenIncident(row.recovery.incidentId!)}>
                      Show incident
                    </button>
                  ) : null}
                </div>

                {!readOnly ? (
                  <div className="wf-compensation-actions" aria-label={`${name} actions`}>
                    <button type="button" aria-label={`Move ${name} up`} title="Move up" disabled={index === 0}
                      onClick={() => change(current => moveCompensationStep(current, index, index - 1), `Moved ${name} up`)}>
                      <ArrowUp size={14} aria-hidden="true" />
                    </button>
                    <button type="button" aria-label={`Move ${name} down`} title="Move down" disabled={index === steps.length - 1}
                      onClick={() => change(current => moveCompensationStep(current, index, index + 1), `Moved ${name} down`)}>
                      <ArrowDown size={14} aria-hidden="true" />
                    </button>
                    <button type="button" aria-label={`Delete step ${name}`} title="Delete step"
                      onClick={() => change(current => removeCompensationStep(current, index), `Deleted step ${name}`)}>
                      <Trash2 size={14} aria-hidden="true" />
                    </button>
                  </div>
                ) : null}

                {problem ? <p className="wf-compensation-problem" id={problemId}><AlertTriangle size={12} aria-hidden="true" /> {problem}</p> : null}
              </article>
            );
          })}
          </div>
          {!readOnly ? (
            <div className="wf-compensation-footer">
              <button type="button" className="wf-compensation-add" onClick={event => request({ kind: "new-step", index: steps.length }, event)}>
                <Plus size={14} aria-hidden="true" /> Add step
              </button>
            </div>
          ) : null}
        </div>
      )}
      {children}
    </div>
  );
}

function ActivityCell({
  node,
  role,
  stepName,
  stepIndex,
  selected,
  readOnly,
  onSelect,
  onChoose,
  onOpenSlot
}: {
  node: Node<WorkflowNodeData> | undefined;
  role: "step" | "compensation";
  stepName: string;
  stepIndex: number;
  selected: boolean;
  readOnly: boolean;
  onSelect(): void;
  onChoose?(event: MouseEvent<HTMLButtonElement>): void;
  onOpenSlot?: CompensationScopeDesignerProps["onOpenSlot"];
}) {
  if (!node) {
    if (readOnly || !onChoose) return <span className="wf-compensation-none">No activity</span>;
    return (
      <button type="button" className="wf-compensation-add" aria-label={`Choose the activity for ${stepName}`} onClick={onChoose}>
        <Plus size={14} aria-hidden="true" /> Choose activity
      </button>
    );
  }

  const { icon, fallback, tone } = resolveNodeActivityVisual(node.data);
  const purpose = role === "step" ? `Step ${stepIndex + 1}, ${stepName}` : `Compensation for ${stepName}`;
  return (
    <div className={`wf-compensation-cell${selected ? " is-selected" : ""}`} data-role={role}>
      <button
        type="button"
        className="wf-compensation-activity"
        data-graph-node-id={node.id}
        data-cell={role}
        aria-pressed={selected}
        aria-label={`${purpose}: ${node.data.label}`}
        onClick={onSelect}
        onKeyDown={moveFocusBetweenRows}
      >
        <span className="wf-compensation-activity-icon wf-activity-icon" data-icon={icon} data-tone={tone} aria-hidden="true">
          {renderActivityIcon(icon, fallback)}
        </span>
        <span className="wf-compensation-activity-label">{node.data.label}</span>
      </button>
      {onOpenSlot ? node.data.childSlots.map(slot => (
        <button
          key={slot.id}
          type="button"
          className="wf-compensation-slot"
          aria-label={`Open ${slot.label} of ${node.data.label}`}
          onClick={() => onOpenSlot(node.id, node.data.label, slot)}
        >
          {slot.label} <ChevronRight size={12} aria-hidden="true" />
        </button>
      )) : null}
    </div>
  );
}

function StateChip({
  kind,
  state,
  label,
  onSelect
}: {
  kind: "forward" | "recovery";
  state: CompensationForwardState | CompensationRecoveryState;
  label: string;
  onSelect?(): void;
}) {
  const content = <><StateIcon state={state} /> <span>{label}</span></>;
  const prefix = kind === "forward" ? "Forward" : "Recovery";
  return onSelect ? (
    <button type="button" className="wf-compensation-state" data-kind={kind} data-state={state} aria-label={`${prefix}: ${label}. Show this occurrence`} onClick={onSelect}>
      {content}
    </button>
  ) : (
    <span className="wf-compensation-state" data-kind={kind} data-state={state} aria-label={`${prefix}: ${label}`}>{content}</span>
  );
}

function StateIcon({ state }: { state: CompensationForwardState | CompensationRecoveryState }) {
  switch (state) {
    case "Completed": return <Check size={12} aria-hidden="true" />;
    case "Faulted":
    case "Failed": return <X size={12} aria-hidden="true" />;
    case "Running": return <CircleDot size={12} aria-hidden="true" />;
    case "Compensated": return <Undo2 size={12} aria-hidden="true" />;
    case "Pending": return <Hourglass size={12} aria-hidden="true" />;
    case "Uncertain": return <AlertTriangle size={12} aria-hidden="true" />;
    case "NeedsAction": return <Hand size={12} aria-hidden="true" />;
    case "NotRun":
    case "Registered": return <CircleDashed size={12} aria-hidden="true" />;
    default: return <Minus size={12} aria-hidden="true" />;
  }
}

function PhaseIcon({ phase }: { phase: CompensationScopeRuntime["phase"] }) {
  switch (phase) {
    case "Compensated":
    case "Compensating": return <Undo2 size={14} aria-hidden="true" />;
    case "Completed": return <Check size={14} aria-hidden="true" />;
    case "Faulted": return <X size={14} aria-hidden="true" />;
    case "AwaitingIntervention": return <Hand size={14} aria-hidden="true" />;
    default: return <CircleDot size={14} aria-hidden="true" />;
  }
}

// "Attempt 2 of 3" once a handler ran more than once or its limit is known; nothing for a single run.
function attemptLabel(attempts: { count: number; max?: number } | undefined) {
  if (!attempts) return "";
  return attempts.max ? ` · Attempt ${attempts.count} of ${attempts.max}` : ` · Attempt ${attempts.count}`;
}

// Arrow keys move between the same cell of neighbouring rows; Home and End jump to the first and last row.
function moveFocusBetweenRows(event: KeyboardEvent<HTMLButtonElement>) {
  if (!["ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) return;
  const cell = event.currentTarget.dataset.cell;
  const table = event.currentTarget.closest(".wf-compensation-table");
  const cells = Array.from(table?.querySelectorAll<HTMLButtonElement>(`.wf-compensation-activity[data-cell="${cell}"]`) ?? []);
  const index = cells.indexOf(event.currentTarget);
  const target = event.key === "Home" ? 0 : event.key === "End" ? cells.length - 1 : event.key === "ArrowUp" ? index - 1 : index + 1;
  if (target < 0 || target >= cells.length) return;
  event.preventDefault();
  cells[target].focus();
}
