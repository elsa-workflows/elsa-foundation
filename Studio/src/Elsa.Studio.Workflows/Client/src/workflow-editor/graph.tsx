import React, { useEffect, useMemo, useRef, useState } from "react";
import { BaseEdge, EdgeLabelRenderer, Handle, Position, getSmoothStepPath, type EdgeProps, type NodeProps } from "@xyflow/react";
import { AlertTriangle, Plus, Trash2 } from "lucide-react";
import type { ActivityCatalogItem } from "../workflowTypes";
import { getActivityDisplay, type WorkflowNodeData } from "../workflowAdapter";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { renderActivityIcon } from "../workflowFormatting";
import { groupActivityPalette, isActivityBrowsable } from "./editorHelpers";
import { WorkflowEdgeActionsContext, WorkflowNodeAvailabilityContext } from "./contexts";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import type { WorkflowEdge } from "./editorTypes";

export const nodeTypes = { workflowActivity: WorkflowActivityNode };
export const edgeTypes = { workflow: WorkflowFlowEdge };

export function WorkflowActivityNode({ data, selected }: NodeProps) {
  const nodeData = data as WorkflowNodeData;
  const runtime = nodeData.runtime;
  const showFlowPorts = !nodeData.suppressFlowPorts;
  const sourcePorts = showFlowPorts
    ? nodeData.sourcePorts.length > 0 ? nodeData.sourcePorts : [{ name: "Done", displayName: "Done" }]
    : [];
  const subtitle = formatNodeSubtitle(nodeData);
  const availabilityLookup = React.useContext(WorkflowNodeAvailabilityContext);
  const availability = availabilityLookup?.({ activityVersionId: nodeData.activityVersionId, activityTypeKey: nodeData.activityTypeKey }) ?? null;
  return (
    <div
      className={["wf-node", selected ? "selected" : "", runtime ? "wf-node-runtime" : "", runtime?.hasBlockingIncident ? "faulted" : "", availability ? "wf-node-unavailable" : ""].filter(Boolean).join(" ")}
      data-icon={nodeData.icon ?? "activity"}
    >
      {showFlowPorts && nodeData.acceptsInbound ? <Handle type="target" position={Position.Left} /> : null}
      {availability ? (
        <span className="wf-node-availability" title={`No longer available for new use · ${getAvailabilityStateLabel(availability.state)}`}>
          <AlertTriangle size={13} />
        </span>
      ) : null}
      <div className="wf-node-content">
        <span className="wf-node-icon" aria-hidden="true">{renderActivityIcon(nodeData.icon)}</span>
        <span className="wf-node-copy">
          <strong>{nodeData.label}</strong>
          {subtitle ? <small>{subtitle}</small> : null}
        </span>
      </div>
      {nodeData.childSlots.length > 0 ? (
        <span className="wf-node-slot-badge">{nodeData.childSlots.length} slot{nodeData.childSlots.length === 1 ? "" : "s"}</span>
      ) : null}
      {runtime ? (
        <div className="wf-node-runtime-strip">
          {runtime.status ? <WorkflowStatusBadge status={runtime.status} subStatus={runtime.subStatus} /> : null}
          {runtime.incidentCount > 0 ? <span className="wf-node-runtime-count">{runtime.incidentCount} incident{runtime.incidentCount === 1 ? "" : "s"}</span> : null}
          {runtime.faultCount > 0 ? <span className="wf-node-runtime-count">{runtime.faultCount} faults</span> : null}
        </div>
      ) : null}
      {sourcePorts.map((port, index) => {
        const top = `${((index + 1) / (sourcePorts.length + 1)) * 100}%`;
        return (
          <React.Fragment key={port.name}>
            <span className="wf-node-port-label" style={{ top }}>{port.displayName}</span>
            <Handle type="source" position={Position.Right} id={port.name} style={{ top }} />
          </React.Fragment>
        );
      })}
    </div>
  );
}

function formatNodeSubtitle(nodeData: WorkflowNodeData) {
  const category = nodeData.category?.trim();
  const executionType = nodeData.executionType?.trim();
  const parts = [category, executionType].filter((part): part is string => !!part);
  return parts.join(" · ");
}

export function WorkflowFlowEdge(props: EdgeProps<WorkflowEdge>) {
  const {
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    markerEnd,
    style,
    label,
    labelStyle
  } = props;
  const actions = React.useContext(WorkflowEdgeActionsContext);
  const [hovered, setHovered] = useState(false);
  const [path, labelX, labelY] = getSmoothStepPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
  const isHighlighted = actions?.highlightedEdgeId === id;

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={markerEnd}
        style={{
          ...style,
          strokeWidth: isHighlighted ? 2.5 : style?.strokeWidth
        }}
        label={label}
        labelX={labelX}
        labelY={labelY}
        labelStyle={labelStyle}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      />
      {actions ? (
        <EdgeLabelRenderer>
          <div
            className={["wf-edge-actions", hovered ? "visible" : "", isHighlighted ? "highlighted" : ""].filter(Boolean).join(" ")}
            style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
          >
            <button type="button" aria-label="Insert activity into connection" title="Insert activity" onClick={event => actions.requestInsertActivity(id, event.clientX, event.clientY)}>
              <Plus size={12} />
            </button>
            <button type="button" aria-label="Delete connection" title="Delete connection" onClick={() => actions.deleteEdge(id)}>
              <Trash2 size={12} />
            </button>
          </div>
        </EdgeLabelRenderer>
      ) : null}
    </>
  );
}

export function ConnectMenu({ clientX, clientY, activities, onPick, onClose }: { clientX: number; clientY: number; activities: ActivityCatalogItem[]; onPick(activity: ActivityCatalogItem): void; onClose(): void }) {
  const [search, setSearch] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    const browsable = activities.filter(isActivityBrowsable);
    if (!term) return browsable;
    return browsable.filter(activity =>
      getActivityDisplay(activity).toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      (activity.category ?? "").toLowerCase().includes(term) ||
      (activity.description ?? "").toLowerCase().includes(term));
  }, [activities, search]);

  const groups = useMemo(() => groupActivityPalette(filtered), [filtered]);
  const flatActivities = useMemo(() => groups.flatMap(group => group.activities), [groups]);

  useEffect(() => {
    requestAnimationFrame(() => inputRef.current?.focus());
  }, []);

  useEffect(() => {
    const onMouseDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as globalThis.Node)) onClose();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };

    document.addEventListener("mousedown", onMouseDown, true);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onMouseDown, true);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [onClose]);

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveIndex(index => Math.min(index + 1, flatActivities.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex(index => Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      const activity = flatActivities[activeIndex];
      if (activity) onPick(activity);
    }
  };

  const left = Math.max(8, Math.min(clientX + 4, window.innerWidth - 328));
  const top = Math.max(8, Math.min(clientY + 4, window.innerHeight - 360));
  let itemIndex = -1;

  return (
    <div ref={containerRef} className="wf-connect-menu" style={{ left, top }} onMouseDown={event => event.stopPropagation()} onClick={event => event.stopPropagation()}>
      <input
        ref={inputRef}
        type="search"
        value={search}
        placeholder="Search activities..."
        aria-label="Search activities"
        onChange={event => {
          setSearch(event.target.value);
          setActiveIndex(0);
        }}
        onKeyDown={onInputKeyDown}
      />
      <div className="wf-connect-menu-list" role="listbox" aria-label="Activity picker">
        {groups.length === 0 ? <p>No matching activities.</p> : groups.map(group => (
          <section key={group.category}>
            <h4>{group.category}</h4>
            {group.activities.map(activity => {
              itemIndex += 1;
              const currentIndex = itemIndex;
              const active = currentIndex === activeIndex;
              return (
                <button
                  type="button"
                  role="option"
                  aria-selected={active}
                  className={active ? "active" : ""}
                  key={activity.activityVersionId}
                  onMouseEnter={() => setActiveIndex(currentIndex)}
                  onClick={() => onPick(activity)}
                >
                  <strong>{getActivityDisplay(activity)}</strong>
                  <small>{activity.category || activity.activityTypeKey}</small>
                </button>
              );
            })}
          </section>
        ))}
      </div>
    </div>
  );
}
