import { useEffect, useRef, useState, type DragEvent, type KeyboardEvent, type MouseEvent, type ReactNode, type RefObject } from "react";
import type { Node } from "@xyflow/react";
import { AlertTriangle, ArrowDown, ArrowUp, ChevronRight, Copy, Plus, Power, Trash2 } from "lucide-react";
import { renderActivityIcon } from "../activityIconView";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { activityDragDataType } from "../workflow-editor/constants";
import type { WorkflowNodeAvailabilityLookup } from "../workflow-editor/editorTypes";
import { WorkflowNodeRuntimeBadges } from "../workflow-editor/WorkflowNodeRuntimeBadges";
import { ExecutionActivityPulse, ExecutionSequencePulse } from "../workflow-editor/ExecutionMotionPulse";
import { resolveNodeActivityVisual } from "../workflow-editor/workflowNodeVisual";
import type { ChildSlot, WorkflowNodeData } from "../workflowAdapter";
import "./sequenceDesigner.css";
import { NodeCollectionActions } from "../workflow-editor/NodeSlotBadges";
import type { CollectionSlotAction } from "../workflow-editor/contexts";

export const sequenceNodeDragDataType = "application/x-elsa-sequence-node";

interface SequenceDesignerBaseProps {
  nodes: Node<WorkflowNodeData>[];
  selectedNodeId: string | null;
  canvasRef: RefObject<HTMLDivElement | null>;
  onSelect(id: string | null): void;
  onOpenSlot?(ownerNodeId: string, ownerLabel: string, slot: ChildSlot): void;
  onAddCollectionItem?: CollectionSlotAction;
  availabilityLookup?: WorkflowNodeAvailabilityLookup;
  children?: ReactNode;
}

export interface ReadOnlySequenceDesignerProps extends SequenceDesignerBaseProps {
  readOnly: true;
  onInsert?: never;
  onDropActivity?: never;
  onMove?: never;
  onDelete?: never;
  onDuplicate?: never;
  onSetWorkflowStart?: never;
}

interface SequenceDesignerEditProps {
  onInsert(index: number, anchor: { clientX: number; clientY: number }): void;
  onDropActivity(activityVersionId: string, index: number): void;
  onMove(nodeId: string, boundaryIndex: number): void;
  onDelete(nodeId: string): void;
  onDuplicate?(nodeId: string): void;
  onSetWorkflowStart?(nodeId: string, enabled: boolean): void;
}

export type EditableSequenceDesignerProps = SequenceDesignerBaseProps & SequenceDesignerEditProps & { readOnly?: false };
export type DynamicReadOnlySequenceDesignerProps = SequenceDesignerBaseProps & SequenceDesignerEditProps & { readOnly: boolean };

export type SequenceDesignerProps = ReadOnlySequenceDesignerProps | EditableSequenceDesignerProps | DynamicReadOnlySequenceDesignerProps;

export function SequenceDesigner({
  nodes,
  selectedNodeId,
  readOnly,
  canvasRef,
  onSelect,
  onInsert,
  onDropActivity,
  onMove,
  onDelete,
  onDuplicate,
  onSetWorkflowStart,
  onOpenSlot,
  onAddCollectionItem,
  availabilityLookup,
  children
}: SequenceDesignerProps) {
  const [rovingNodeId, setRovingNodeId] = useState(() => selectedNodeId ?? nodes[0]?.id ?? null);
  const previousSelectedNodeId = useRef(selectedNodeId);

  useEffect(() => {
    const selectionChanged = previousSelectedNodeId.current !== selectedNodeId;
    previousSelectedNodeId.current = selectedNodeId;
    const selectedExists = selectedNodeId !== null && nodes.some(node => node.id === selectedNodeId);
    const rovingExists = nodes.some(node => node.id === rovingNodeId);
    const next = selectionChanged && selectedExists
      ? selectedNodeId
      : rovingExists ? rovingNodeId : selectedExists ? selectedNodeId : nodes[0]?.id ?? null;
    if (next !== rovingNodeId) setRovingNodeId(next);
  }, [nodes, rovingNodeId, selectedNodeId]);

  const insertAt = (index: number, event: MouseEvent<HTMLButtonElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onInsert?.(index, {
      clientX: event.clientX || rect.left + rect.width / 2,
      clientY: event.clientY || rect.top + rect.height / 2
    });
  };

  const handleBoundaryDragOver = (event: DragEvent<HTMLButtonElement>) => {
    if (readOnly) {
      event.dataTransfer.dropEffect = "none";
      return;
    }
    event.preventDefault();
    event.dataTransfer.dropEffect = Array.from(event.dataTransfer.types).includes(sequenceNodeDragDataType) ? "move" : "copy";
  };

  const handleBoundaryDrop = (index: number, event: DragEvent<HTMLButtonElement>) => {
    event.preventDefault();
    if (readOnly) return;

    const draggedNodeId = event.dataTransfer.getData(sequenceNodeDragDataType);
    if (draggedNodeId) {
      if (nodes.some(node => node.id === draggedNodeId)) onMove?.(draggedNodeId, index);
      return;
    }

    const activityVersionId = event.dataTransfer.getData(activityDragDataType) ||
      (Array.from(event.dataTransfer.types).includes("text/plain") ? event.dataTransfer.getData("text/plain") : "");
    if (activityVersionId) onDropActivity?.(activityVersionId, index);
  };

  const boundary = (index: number, label: string, isEmpty = false, isEnd = false) => !readOnly ? (
    <button
      key={`boundary-${index}`}
      type="button"
      className={`wf-sequence-insert${isEmpty ? " wf-sequence-insert-empty" : ""}${isEnd ? " wf-sequence-insert-end" : ""}`}
      data-sequence-insert-index={index}
      aria-label={label}
      title={label}
      onClick={event => insertAt(index, event)}
      onDragOver={handleBoundaryDragOver}
      onDrop={event => handleBoundaryDrop(index, event)}
    >
      <Plus size={14} aria-hidden="true" />
      {isEmpty ? <span>Add first step</span> : isEnd ? <span>Add step</span> : null}
    </button>
  ) : null;

  return (
    <div ref={canvasRef} className="wf-sequence-designer" tabIndex={-1} aria-label="Sequence canvas" onClick={event => {
      if (readOnly && event.target instanceof Element && !event.target.closest(".wf-sequence-item, button")) onSelect(null);
    }}>
      <header className="wf-sequence-heading">
        <span className="wf-sequence-mode-pill">Sequence · runs top to bottom</span>
      </header>

      <div className="wf-sequence-content">
        {nodes.length === 0 ? (
          <section className="wf-sequence-empty" aria-label="Empty sequence">
            <span className="wf-sequence-empty-icon" aria-hidden="true"><Plus size={22} /></span>
            <h3>No steps yet</h3>
            <p>{readOnly ? "This sequence has no steps." : "Add an activity to start this sequence."}</p>
            {boundary(0, "Add the first step", true)}
          </section>
        ) : (
          <div className="wf-sequence-list" role="list" aria-label="Sequence steps">
            {nodes.map((node, index) => (
              <div className="wf-sequence-group" key={node.id} role="presentation">
                {boundary(index, insertionLabel(nodes, index))}
                <SequenceStep
                  node={node}
                  index={index}
                  selected={selectedNodeId === node.id}
                  readOnly={!!readOnly}
                  tabIndex={rovingNodeId === node.id ? 0 : -1}
                  listSize={nodes.length}
                  onSelect={() => {
                    setRovingNodeId(node.id);
                    onSelect(node.id);
                  }}
                  onFocus={() => setRovingNodeId(node.id)}
                  onMove={boundaryIndex => onMove?.(node.id, boundaryIndex)}
                  onDelete={() => onDelete?.(node.id)}
                  onDuplicate={onDuplicate ? () => onDuplicate(node.id) : undefined}
                  onSetWorkflowStart={onSetWorkflowStart ? enabled => onSetWorkflowStart(node.id, enabled) : undefined}
                  onOpenSlot={onOpenSlot}
                  onAddCollectionItem={readOnly ? undefined : onAddCollectionItem}
                  availabilityLookup={availabilityLookup}
                />
                {index < nodes.length - 1 ? (
                  <div className="wf-sequence-connector" aria-hidden="true"><ArrowDown size={15} /><ExecutionSequencePulse pulse={nodes[index + 1].data.incomingExecutionPulse} /></div>
                ) : null}
              </div>
            ))}
            <div className="wf-sequence-group" role="presentation">
              {boundary(nodes.length, insertionLabel(nodes, nodes.length), false, true)}
            </div>
          </div>
        )}
      </div>
      {children}
    </div>
  );
}

function SequenceStep({
  node,
  index,
  selected,
  readOnly,
  tabIndex,
  listSize,
  onSelect,
  onFocus,
  onMove,
  onDelete,
  onDuplicate,
  onSetWorkflowStart,
  onOpenSlot,
  onAddCollectionItem,
  availabilityLookup
}: {
  node: Node<WorkflowNodeData>;
  index: number;
  selected: boolean;
  readOnly: boolean;
  tabIndex: number;
  listSize: number;
  onSelect(): void;
  onFocus(): void;
  onMove(boundaryIndex: number): void;
  onDelete(): void;
  onDuplicate?: () => void;
  onSetWorkflowStart?: (enabled: boolean) => void;
  onOpenSlot?: SequenceDesignerProps["onOpenSlot"];
  onAddCollectionItem?: CollectionSlotAction;
  availabilityLookup?: WorkflowNodeAvailabilityLookup;
}) {
  const { data } = node;
  const { icon, fallback, tone } = resolveNodeActivityVisual(data);
  const availability = availabilityLookup?.({ activityVersionId: data.activityVersionId, activityTypeKey: data.activityTypeKey }) ?? null;
  const availabilityLabel = availability ? `No longer available for new use · ${getAvailabilityStateLabel(availability.state)}` : null;
  const activityType = shortTypeName(data.activityTypeKey ?? data.activityVersionId);
  const summary = data.summary?.trim() || data.description?.trim();

  const onCardDragStart = (event: DragEvent<HTMLButtonElement>) => {
    if (readOnly) {
      event.preventDefault();
      return;
    }
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData(sequenceNodeDragDataType, node.id);
    event.dataTransfer.setData("text/plain", node.id);
  };

  const focusRelativeStep = (event: KeyboardEvent<HTMLButtonElement>, targetIndex: number) => {
    event.preventDefault();
    const steps = event.currentTarget.closest(".wf-sequence-list")?.querySelectorAll<HTMLButtonElement>(".wf-sequence-step");
    steps?.item(targetIndex)?.focus();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === "ArrowUp") return focusRelativeStep(event, Math.max(0, index - 1));
    if (event.key === "ArrowDown") return focusRelativeStep(event, Math.min(listSize - 1, index + 1));
    if (event.key === "Home") return focusRelativeStep(event, 0);
    if (event.key === "End") return focusRelativeStep(event, listSize - 1);
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onSelect();
      return;
    }
    if (!readOnly && (event.key === "Delete" || event.key === "Backspace")) {
      event.preventDefault();
      event.stopPropagation();
      onDelete();
    }
  };

  return (
    <article className={`wf-sequence-item${selected ? " is-selected" : ""}${availability ? " wf-sequence-unavailable" : ""}${data.runtime ? " wf-sequence-runtime" : ""}${data.runtime?.hasBlockingIncident ? " faulted" : ""}`} role="listitem" data-icon={icon} data-tone={tone} title={availabilityLabel ?? undefined}>
      <ExecutionActivityPulse pulse={data.executionPulse} status={data.runtime?.status} />
      <button
        type="button"
        className="wf-sequence-step"
        data-id={node.id}
        data-graph-node-id={node.id}
        data-runtime-node-id={data.runtimeNodeId ?? node.id}
        data-selected={String(selected)}
        data-index={index}
        aria-pressed={selected}
        aria-label={`${index + 1}. ${data.label}${availabilityLabel ? `, ${availabilityLabel}` : ""}`}
        tabIndex={tabIndex}
        draggable={!readOnly}
        onClick={onSelect}
        onFocus={onFocus}
        onKeyDown={handleKeyDown}
        onDragStart={onCardDragStart}
      >
        <span className="wf-sequence-icon wf-activity-icon" data-icon={icon} data-tone={tone} aria-hidden="true">
          {renderActivityIcon(icon, fallback)}
        </span>
        <span className="wf-sequence-step-copy">
          <span className="wf-sequence-title-row">
            <span className="wf-sequence-title">{data.label}</span>
            {availabilityLabel ? <span className="wf-sequence-availability" role="img" aria-label={availabilityLabel}><AlertTriangle size={14} aria-hidden="true" /></span> : null}
          </span>
          <span className="wf-sequence-type">{data.category || activityType}</span>
          {summary ? <span className="wf-sequence-summary">{summary}</span> : null}
        </span>
      </button>

      {data.childSlots.length || (!readOnly && data.collectionSlots?.length) ? (
        <div className="wf-sequence-slots" aria-label={`${data.label} child slots`}>
          {data.childSlots.map(slot => (
            <button
              key={slot.id}
              type="button"
              className="wf-sequence-slot"
              aria-label={`Open ${slot.label}, ${slotSummary(slot)}`}
              disabled={!onOpenSlot}
              onClick={() => onOpenSlot?.(node.id, data.label, slot)}
            >
              <span>{slot.label}</span>
              <span className="wf-sequence-slot-summary">{slotSummary(slot)}<ChevronRight size={14} aria-hidden="true" /></span>
            </button>
          ))}
          {!readOnly ? <NodeCollectionActions ownerNodeId={node.id} collections={data.collectionSlots} onAdd={onAddCollectionItem} className="wf-sequence-slot" /> : null}
        </div>
      ) : null}

      {data.runtime ? <WorkflowNodeRuntimeBadges data={data} className="wf-sequence-runtime-strip" /> : null}

      {selected && !readOnly ? (
        <div className="wf-sequence-actions" aria-label={`${data.label} actions`}>
          <button type="button" aria-label={`Move ${data.label} up`} title="Move up" disabled={index === 0} onClick={() => onMove(index - 1)}>
            <ArrowUp size={15} aria-hidden="true" />
          </button>
          <button type="button" aria-label={`Move ${data.label} down`} title="Move down" disabled={index === listSize - 1} onClick={() => onMove(index + 2)}>
            <ArrowDown size={15} aria-hidden="true" />
          </button>
          {onSetWorkflowStart && data.workflowStartEnabled !== undefined ? <button
            type="button"
            aria-label={`Start new workflows from ${data.label}`}
            aria-pressed={data.workflowStartEnabled}
            title={data.workflowStartEnabled ? "Disable workflow start" : "Enable workflow start"}
            onClick={() => onSetWorkflowStart(!data.workflowStartEnabled)}
          ><Power size={15} aria-hidden="true" /></button> : null}
          {onDuplicate ? <button type="button" aria-label={`Duplicate ${data.label}`} title="Duplicate" onClick={onDuplicate}><Copy size={15} aria-hidden="true" /></button> : null}
          <button type="button" aria-label={`Delete ${data.label}`} title="Delete" onClick={onDelete}><Trash2 size={15} aria-hidden="true" /></button>
        </div>
      ) : null}
    </article>
  );
}

function slotSummary(slot: ChildSlot) {
  const count = slot.activities.length;
  if (count === 0) return "Empty";
  const noun = slot.mode === "sequence" ? "step" : "activity";
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

function insertionLabel(nodes: Node<WorkflowNodeData>[], index: number) {
  if (index === 0) return `Insert step before ${nodes[0]?.data.label ?? "first step"}`;
  if (index === nodes.length) return `Insert step after ${nodes.at(-1)?.data.label ?? "last step"}`;
  return `Insert step between ${nodes[index - 1]?.data.label ?? "previous step"} and ${nodes[index]?.data.label ?? "next step"}`;
}

function shortTypeName(value: string) {
  return value.split(".").at(-1) || value;
}
