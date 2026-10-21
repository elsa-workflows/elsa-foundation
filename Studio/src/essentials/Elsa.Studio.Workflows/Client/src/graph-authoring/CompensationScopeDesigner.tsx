import { useId, useRef, type KeyboardEvent, type MouseEvent, type ReactNode, type RefObject } from "react";
import type { Node } from "@xyflow/react";
import { AlertTriangle, ArrowDown, ArrowUp, BadgeCheck, Ban, Check, ChevronRight, CircleDashed, CircleDot, CircleSlash, CornerUpLeft, Hand, Hourglass, Loader, Minus, OctagonX, Plus, RotateCw, Trash2, Undo2, UserCheck, X } from "lucide-react";
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
  compensationDecisions,
  forwardStateLabel,
  operatorHistoryLabel,
  ordinalLabel,
  recoveryLabel,
  scopePhaseLabel,
  type CompensationForwardState,
  type CompensationOperatorHistoryEntry,
  type CompensationRecoveryState,
  type CompensationRowRuntime,
  type CompensationScopeReference,
  type CompensationScopeRuntime
} from "../compensationRuntime";
import { resolveNodeActivityVisual } from "../workflow-editor/workflowNodeVisual";
import {
  abandonActionModel,
  abandonableRow,
  resolveActionModel,
  retryActionModel,
  type OperatorActionModel,
  type OperatorActionScopeContext
} from "../operatorActions";
import { formatDate } from "../workflowFormatting";
import type { ChildSlot, WorkflowNodeData } from "../workflowAdapter";
import type { ActivityNode } from "../workflowTypes";
import "./compensationScopeDesigner.css";

/** Where a picked activity goes: a new step, a step's forward activity, or a step's compensation handler. */
export type CompensationActivityTarget =
  | { kind: "new-step"; index: number }
  | { kind: "step-activity"; index: number }
  | { kind: "compensation"; index: number };

/** Run inspection only (spec 260): what the run view lets an operator do about a waiting recovery. */
export interface CompensationOperatorControls {
  /** The run says the caller may manage it, and the host advertises the alteration routes. */
  canAct: boolean;
  workflowExecutionId: string;
  /** A submitted action Studio is still following, shown on the row it is about ("Retry submitted", "Applying…"). */
  pending?: { incidentId: string; label: string } | null;
  /**
   * Opens the confirmation dialog. `focusFallback` moves focus to the row's state chip when the button that opened the
   * dialog is gone after the action.
   */
  onAction(model: OperatorActionModel, focusFallback: () => void): void;
}

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
  /** Run inspection only: selects an occurrence by activity execution id (a step, a handler, or a scope occurrence). */
  onSelectExecution?(activityExecutionId: string): void;
  /** Run inspection only: opens the blocking incident a row waits on (spec 251 FR-217). */
  onOpenIncident?(incidentId: string): void;
  /** Run inspection only: retry, resolve and abandon (spec 260). Without it the view offers no action. */
  operator?: CompensationOperatorControls;
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
  operator,
  children
}: CompensationScopeDesignerProps) {
  const steps = readCompensationSteps(owner);
  const problems = readOnly ? new Map<number, string>() : compensationStepProblems(steps);
  const nodesById = new Map(nodes.map(node => [node.id, node]));
  const headingId = useId();
  const rootRef = useRef<HTMLDivElement | null>(null);
  const setRoot = (element: HTMLDivElement | null) => {
    rootRef.current = element;
    if (canvasRef) (canvasRef as { current: HTMLDivElement | null }).current = element;
  };

  // Spec 260: actions show only when the caller may manage the run, the routes exist, the scope is not cancelled and no
  // other action on this run is still being followed.
  const canAct = !!runtime && !!operator?.canAct && runtime.phase !== "Cancelled" && !operator.pending;
  const actionContext: OperatorActionScopeContext | undefined = runtime && operator ? {
    workflowExecutionId: operator.workflowExecutionId,
    runtime,
    labelOf: nodeId => (nodeId ? nodesById.get(nodeId)?.data.label : undefined)
  } : undefined;
  const focusChip = (selector: string) => () => rootRef.current?.querySelector<HTMLElement>(selector)?.focus();
  const rowActions = (row: CompensationRowRuntime, name: string, chipSelector: string): ReactNode => {
    const actions = row.actions;
    if (!canAct || !actions || !actionContext) return null;
    const fallback = focusChip(chipSelector);
    const canResolve = actions.decisions.includes(compensationDecisions.resolveEffectAbsent) || actions.decisions.includes(compensationDecisions.resolveEffectAccepted);
    return (
      <span className="wf-compensation-operator-actions">
        {actions.sourceActivityExecutionId ? (
          <button
            type="button"
            className="wf-compensation-operator-action"
            aria-label={actions.target === "handler" ? `Retry the compensation of ${name}` : `Retry ${name}`}
            onClick={() => operator!.onAction(retryActionModel(row, actionContext), fallback)}
          >
            <RotateCw size={12} aria-hidden="true" /> Retry
          </button>
        ) : null}
        {canResolve ? (
          <button
            type="button"
            className="wf-compensation-operator-action"
            aria-label={`Resolve ${name}`}
            onClick={() => operator!.onAction(resolveActionModel(row, actionContext), fallback)}
          >
            <UserCheck size={12} aria-hidden="true" /> Resolve…
          </button>
        ) : null}
      </span>
    );
  };
  const pendingChip = (row: CompensationRowRuntime) => {
    const pending = operator?.pending;
    if (!pending || !row.actions || row.actions.incidentId !== pending.incidentId) return null;
    return (
      <span className="wf-compensation-state" data-kind="recovery" data-state="Submitted" aria-label={`Recovery: ${pending.label}`} tabIndex={-1}>
        <Loader size={12} aria-hidden="true" /> <span>{pending.label}</span>
      </span>
    );
  };
  const abandonRow = runtime ? abandonableRow(runtime) : undefined;
  const scopeName = runtime?.scopeLabel || "this scope";

  const change = (edit: (current: CompensationStep[]) => CompensationStep[], status: string) => onChangeSteps?.(edit, status);
  const request = (target: CompensationActivityTarget, event: MouseEvent<HTMLButtonElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onRequestActivity?.(target, {
      clientX: event.clientX || rect.left + rect.width / 2,
      clientY: event.clientY || rect.top + rect.height / 2
    });
  };

  return (
    <div ref={setRoot} className="wf-compensation-designer" tabIndex={-1} aria-labelledby={headingId}>
      <header className="wf-compensation-heading">
        <h3 id={headingId}>Compensation scope</h3>
        <p>Steps run top to bottom. If a step fails, the steps that completed are undone bottom to top.</p>
        {runtime ? (
          <span className="wf-compensation-phase" data-phase={runtime.phase} tabIndex={-1}>
            <PhaseIcon phase={runtime.phase} /> {phaseLabel(runtime)}
          </span>
        ) : null}
        {runtime && canAct && abandonRow && actionContext ? (
          <button
            type="button"
            className="wf-compensation-operator-action"
            data-tone="danger"
            aria-label={`Abandon recovery of ${scopeName}`}
            onClick={() => operator!.onAction(abandonActionModel(actionContext), focusChip(".wf-compensation-phase"))}
          >
            <OctagonX size={12} aria-hidden="true" /> Abandon recovery…
          </button>
        ) : null}
        {runtime?.occurrences && onSelectExecution ? (
          <div className="wf-compensation-occurrence">
            <label htmlFor={`${headingId}-occurrence`}>Occurrence</label>
            <select
              id={`${headingId}-occurrence`}
              value={runtime.scopeActivityExecutionId ?? ""}
              aria-describedby={`${headingId}-occurrences`}
              onChange={event => onSelectExecution(event.target.value)}
            >
              {runtime.occurrences.map(occurrence => (
                <option key={occurrence.activityExecutionId} value={occurrence.activityExecutionId}>
                  {occurrence.label} · {occurrence.status}
                </option>
              ))}
            </select>
            <span id={`${headingId}-occurrences`} className="wf-visually-hidden">
              This scope ran {runtime.occurrences.length} times. Choose the occurrence to show.
            </span>
          </div>
        ) : null}
      </header>

      {runtime ? <SourceNote runtime={runtime} /> : null}

      {runtime?.incomplete ? (
        <p className="wf-instance-note" role="note">
          This run has more activity data than Studio loaded. A state shown as Not loaded may be on a later page.
        </p>
      ) : null}

      {runtime?.cause ? (
        <p className="wf-compensation-cause" role="note">
          <AlertTriangle size={14} aria-hidden="true" />
          {runtime.cause.kind === "explicit" ? (
            <span>
              Recovery started because a Compensate activity asked for it
              {runtime.cause.step ? <> while <strong>{runtime.cause.step}</strong> was running</> : null}.
            </span>
          ) : (
            <span>
              Recovery started because <strong>{runtime.cause.step}</strong> {runtime.cause.kind === "uncertain" ? "could not confirm its outcome" : "failed"}
              {runtime.cause.failureType ? <>: <code>{runtime.cause.failureType}</code></> : null}
              {runtime.cause.message ? <> — {runtime.cause.message.replace(/[.\s]+$/, "")}</> : runtime.cause.messageWithheld ? <> (message withheld)</> : null}.
            </span>
          )}
          {onSelectExecution ? (
            <button type="button" className="wf-compensation-link" onClick={() => onSelectExecution(runtime.cause!.activityExecutionId)}>
              {runtime.cause.kind === "explicit" ? "Show request" : "Show failed step"}
            </button>
          ) : null}
          {runtime.cause.incidentId && onOpenIncident && runtime.cause.kind !== "explicit" ? (
            <button type="button" className="wf-compensation-link" aria-label="Show the incident that started recovery" onClick={() => onOpenIncident(runtime.cause!.incidentId!)}>
              Show incident
            </button>
          ) : null}
        </p>
      ) : null}

      {runtime?.remaining?.length && (runtime.phase === "Compensating" || runtime.phase === "AwaitingIntervention") ? (
        <p className="wf-compensation-remaining" role="note">
          <Hourglass size={14} aria-hidden="true" />
          <span>Still to undo, in this order: {runtime.remaining.map(item => item.step).join(", ")}.</span>
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
                  {row?.actions?.target === "step"
                    ? rowActions(row, name, `[data-step-index="${index}"] .wf-compensation-state[data-kind="recovery"]`)
                    : null}
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
                  {row ? <RecoveryState row={row} name={name} onSelectExecution={onSelectExecution} onOpenIncident={onOpenIncident} /> : null}
                  {row ? pendingChip(row) : null}
                  {row?.actions?.target === "handler"
                    ? rowActions(row, name, `[data-step-index="${index}"] .wf-compensation-state[data-kind="recovery"]`)
                    : null}
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
          {runtime?.received?.length ? (
            <section className="wf-compensation-received" aria-labelledby={`${headingId}-received`}>
              <h4 id={`${headingId}-received`}>Handed over by inner scopes</h4>
              <p>Work that completed inner scopes left for this scope to undo if it recovers.</p>
              <div role="list" aria-label="Handed-over steps">
                {runtime.received.map((row, index) => (
                  <article
                    key={`${row.origin?.activityExecutionId ?? ""}:${row.forward.activityExecutionId ?? index}`}
                    className="wf-compensation-row wf-compensation-received-row"
                    role="listitem"
                    aria-label={`${row.step}, from ${originLabel(row.origin)}`}
                    data-received-index={index}
                  >
                    <span className="wf-compensation-ordinal" aria-hidden="true"><CornerUpLeft size={12} /></span>
                    <div className="wf-compensation-step">
                      <span className="wf-compensation-name">{row.step}</span>
                      {row.origin ? (
                        onSelectExecution && row.origin.activityExecutionId ? (
                          <button
                            type="button"
                            className="wf-compensation-link"
                            aria-label={`Show ${originLabel(row.origin)}, where ${row.step} ran`}
                            onClick={() => onSelectExecution(row.origin!.activityExecutionId!)}
                          >
                            From {originLabel(row.origin)}
                          </button>
                        ) : <span className="wf-compensation-origin">From {originLabel(row.origin)}</span>
                      ) : null}
                      <StateChip
                        kind="forward"
                        state={row.forward.state}
                        label={forwardStateLabel(row.forward.state)}
                        onSelect={row.forward.activityExecutionId && onSelectExecution ? () => onSelectExecution(row.forward.activityExecutionId!) : undefined}
                      />
                    </div>
                    <div className="wf-compensation-handler">
                      <RecoveryState row={row} name={row.step} onSelectExecution={onSelectExecution} onOpenIncident={onOpenIncident} />
                      {pendingChip(row)}
                      {rowActions(row, row.step, `[data-received-index="${index}"] .wf-compensation-state[data-kind="recovery"]`)}
                    </div>
                  </article>
                ))}
              </div>
            </section>
          ) : null}
          {runtime?.history?.length ? (
            <OperatorHistory headingId={`${headingId}-history`} history={runtime.history} onOpenIncident={onOpenIncident} />
          ) : null}
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

function RecoveryState({
  row,
  name,
  onSelectExecution,
  onOpenIncident
}: {
  row: CompensationRowRuntime;
  name: string;
  onSelectExecution?(activityExecutionId: string): void;
  onOpenIncident?(incidentId: string): void;
}) {
  const { recovery } = row;
  const target = recovery.activityExecutionId ?? recovery.handedOverTo?.activityExecutionId;
  return (
    <>
      <StateChip
        kind="recovery"
        state={recovery.state}
        label={`${recoveryLabel(recovery)}${recovery.order ? ` (${ordinalLabel(recovery.order)})` : ""}${attemptLabel(recovery.attempts)}`}
        onSelect={target && onSelectExecution ? () => onSelectExecution(target) : undefined}
      />
      {recovery.confirmedBy && onSelectExecution ? (
        <button type="button" className="wf-compensation-link" aria-label={`Show what confirmed ${name}`} onClick={() => onSelectExecution(recovery.confirmedBy!)}>
          Show confirmation
        </button>
      ) : null}
      {recovery.incidentId && onOpenIncident ? (
        <button type="button" className="wf-compensation-link" aria-label={`Show the incident ${name} is waiting on`} onClick={() => onOpenIncident(recovery.incidentId!)}>
          Show incident
        </button>
      ) : null}
    </>
  );
}

// A cancelled scope never compensates (spec 258 OD10): say so, unless a handler that was already running finished.
function phaseLabel(runtime: CompensationScopeRuntime): string {
  if (runtime.phase !== "Cancelled") return scopePhaseLabel(runtime.phase);
  const undone = [...runtime.rows, ...(runtime.received ?? [])].some(row => row.recovery.state === "Compensated");
  return undone ? "Cancelled" : "Cancelled — nothing undone";
}

function originLabel(origin: CompensationScopeReference | undefined): string {
  if (!origin) return "an inner scope";
  return origin.iterationId ? `${origin.label} (iteration ${origin.iterationId})` : origin.label;
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
    <span className="wf-compensation-state" data-kind={kind} data-state={state} aria-label={`${prefix}: ${label}`} tabIndex={-1}>{content}</span>
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
    case "Confirmed": return <BadgeCheck size={12} aria-hidden="true" />;
    case "HandedOver": return <CornerUpLeft size={12} aria-hidden="true" />;
    case "Cancelled": return <Ban size={12} aria-hidden="true" />;
    case "NotRun":
    case "Registered": return <CircleDashed size={12} aria-hidden="true" />;
    case "Retrying": return <RotateCw size={12} aria-hidden="true" />;
    case "ResolvedAbsent":
    case "ResolvedKept": return <UserCheck size={12} aria-hidden="true" />;
    case "NotUndone": return <CircleSlash size={12} aria-hidden="true" />;
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
    case "Cancelled": return <Ban size={14} aria-hidden="true" />;
    case "Abandoned": return <OctagonX size={14} aria-hidden="true" />;
    default: return <CircleDot size={14} aria-hidden="true" />;
  }
}

// Spec 260: where the rows come from. A live scope's rows come from its journal; a completed scope's journal is no longer
// kept, so its rows, like those of a host without the journal, are derived from run data.
function SourceNote({ runtime }: { runtime: CompensationScopeRuntime }) {
  if (runtime.source === "journal") return <p className="wf-compensation-source" role="note">From the scope's journal.</p>;
  if (runtime.source !== "derived" || !runtime.journalUnavailable) return null;
  return (
    <p className="wf-compensation-source" role="note">
      {runtime.journalUnavailable === "StateCleared"
        ? "Derived from run data: a scope keeps its journal only until it completes."
        : "Derived from run data: the scope's journal is not available."}
    </p>
  );
}

function OperatorHistory({
  headingId,
  history,
  onOpenIncident
}: {
  headingId: string;
  history: CompensationOperatorHistoryEntry[];
  onOpenIncident?(incidentId: string): void;
}) {
  return (
    <section className="wf-compensation-history" aria-labelledby={headingId}>
      <h4 id={headingId}>Operator actions</h4>
      <ul>
        {history.map(entry => (
          <li key={entry.incidentId} data-kind={entry.kind}>
            <span className="wf-compensation-history-kind">
              <HistoryIcon kind={entry.kind} /> {operatorHistoryLabel(entry)}{entry.step ? ` (${entry.step})` : ""}
            </span>
            <span>{entry.actor ?? "Unknown operator"}</span>
            {entry.appliedAt ? <span>{formatDate(entry.appliedAt)}</span> : null}
            <span className={entry.reason ? "wf-compensation-history-reason" : "wf-compensation-history-none"}>
              {entry.reason ? `"${entry.reason}"` : "No reason given"}
            </span>
            {entry.notApplied ? <span className="wf-compensation-history-note">Decision not applied: the scope was cancelled first</span> : null}
            {onOpenIncident ? (
              <button type="button" className="wf-compensation-link" aria-label={`Show the incident for ${operatorHistoryLabel(entry)}${entry.step ? ` on ${entry.step}` : ""}`} onClick={() => onOpenIncident(entry.incidentId)}>
                Show incident
              </button>
            ) : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

function HistoryIcon({ kind }: { kind: CompensationOperatorHistoryEntry["kind"] }) {
  switch (kind) {
    case "Retry": return <RotateCw size={12} aria-hidden="true" />;
    case "Abandoned": return <OctagonX size={12} aria-hidden="true" />;
    default: return <UserCheck size={12} aria-hidden="true" />;
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
