import { graphBoundaryCompleteTypeKey } from "../workflowAdapter";
import "@xyflow/react/dist/style.css";
import "./graphCanvas.css";
import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { BaseEdge, EdgeLabelRenderer, Handle, NodeResizeControl, ResizeControlVariant, getBezierPath, getSmoothStepPath, useStore, type EdgeProps, type NodeProps, type OnResizeEnd, type OnResizeStart, type ReactFlowState } from "@xyflow/react";
import { useStudioThemeLayout, type StudioThemeLayout } from "@elsa-workflows/studio-sdk";
import { AlertTriangle, Flag, Plus, Trash2 } from "lucide-react";
import type { ActivityCatalogItem } from "../workflowTypes";
import { renderActivityIcon } from "../activityIconView";
import { getActivityDisplay, type WorkflowNodeData } from "../workflowAdapter";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { truncate } from "../workflowFormatting";
import { groupActivityPalette, isActivityBrowsable } from "./editorHelpers";
import { emptyGraphElements, mergeGraphPaletteGroups, type GraphPaletteElement } from "../graph-authoring/graphPaletteElements";
import { WorkflowEdgeActionsContext, WorkflowNodeAvailabilityContext, WorkflowNodeResizeContext } from "./contexts";
import { defaultNodeWidth, maxNodeWidth, minNodeWidth } from "../graph-authoring/nodeWidth";
import { NodeSlotBadges } from "./NodeSlotBadges";
import { WorkflowNodeContextToolbar } from "./WorkflowNodeContextToolbar";
import { WorkflowNodeRuntimeBadges } from "./WorkflowNodeRuntimeBadges";
import { BpmnElementNode } from "../bpmn/BpmnElementNode";
import { httpEndpointResponseBadge } from "../httpEndpointResponseMode";
import type { WorkflowEdge } from "./editorTypes";
import { resolveNodeActivityVisual } from "./workflowNodeVisual";
import { ExecutionActivityPulse } from "./ExecutionMotionPulse";
import { useGraphPortLayout } from "../graph-authoring/useGraphPortLayout";
import { portPlacementStyle } from "../graph-authoring/graphPortLayout";

export const nodeTypes = { workflowActivity: WorkflowActivityNode, bpmnElement: BpmnElementNode };
export const edgeTypes = { workflow: WorkflowFlowEdge };

export function WorkflowActivityNode({ id, data, selected }: NodeProps) {
  const nodeData = data as WorkflowNodeData;
  const { icon, fallback, tone } = resolveNodeActivityVisual(nodeData);
  const runtime = nodeData.runtime;
  const showFlowPorts = !nodeData.suppressFlowPorts;
  const sourcePorts = showFlowPorts && nodeData.activityTypeKey !== graphBoundaryCompleteTypeKey ? nodeData.sourcePorts : [];
  const subtitle = formatNodeSubtitle(nodeData);
  const httpResponseBadge = httpEndpointResponseBadge(nodeData.httpEndpointResponseMode ?? null);
  const availabilityLookup = React.useContext(WorkflowNodeAvailabilityContext);
  const availability = availabilityLookup?.({ activityVersionId: nodeData.activityVersionId, activityTypeKey: nodeData.activityTypeKey }) ?? null;
  const resizeNode = React.useContext(WorkflowNodeResizeContext);
  // The node's explicit (saved or resized) width. NodeProps.width is the measured size instead, so
  // applying it would pin every unresized node to whatever it measured on first render.
  const width = useStore(useCallback((state: ReactFlowState) => state.nodeLookup.get(id)?.width, [id]));
  const currentWidth = width ?? defaultNodeWidth;
  // NodeResizeControl tears down its drag listeners whenever a callback prop changes, and each width
  // change during a drag re-renders this node. Stable callbacks that read the latest handler through a
  // ref keep a touch drag's end listener alive, so the final width is still committed.
  const resizeNodeRef = useRef(resizeNode);
  const resizeStartWidthRef = useRef(0);
  useLayoutEffect(() => {
    resizeNodeRef.current = resizeNode;
  });
  const onResizeStart = useCallback<OnResizeStart>((_event, params) => {
    resizeStartWidthRef.current = params.width;
  }, []);
  const onResizeEnd = useCallback<OnResizeEnd>((_event, params) => {
    resizeNodeRef.current?.(id, params.width, resizeStartWidthRef.current);
  }, [id]);
  // Keyboard equivalent of dragging the strip (ARIA window-splitter pattern). Keys are kept from the
  // canvas, whose own arrow-key handling would otherwise move the node as well.
  const resizeFromKeyboard = (event: React.KeyboardEvent) => {
    const step = event.shiftKey ? 64 : 16;
    const target = event.key === "ArrowRight" ? currentWidth + step
      : event.key === "ArrowLeft" ? currentWidth - step
        : event.key === "Home" ? minNodeWidth
          : event.key === "End" ? maxNodeWidth
            : null;
    if (target === null) return;
    event.preventDefault();
    event.stopPropagation();
    resizeNode?.(id, Math.min(maxNodeWidth, Math.max(minNodeWidth, target)), currentWidth);
  };
  const ports = useGraphPortLayout(id, sourcePorts.map(port => port.name), showFlowPorts && nodeData.acceptsInbound);
  return (
    <div
      className={["wf-node", selected ? "selected" : "", runtime ? "wf-node-runtime" : "", runtime?.hasBlockingIncident ? "faulted" : "", availability ? "wf-node-unavailable" : "", nodeData.ghost ? "wf-node-ghost" : "", nodeData.isStartNode ? "wf-node-start" : ""].filter(Boolean).join(" ")}
      data-start-node={nodeData.isStartNode ? "true" : undefined}
      data-trigger-enabled={nodeData.canStartWorkflow ? "true" : undefined}
      data-icon={icon}
      data-tone={tone}
      data-runtime-node-id={nodeData.runtimeNodeId ?? id}
      title={[nodeData.label, nodeData.description].filter(Boolean).join(" — ")}
      aria-description={nodeData.description}
      style={{ minHeight: ports.minHeight, width }}
    >
      {resizeNode && selected ? (
        <NodeResizeControl
          nodeId={id}
          className="wf-node-resize-control nodrag"
          variant={ResizeControlVariant.Line}
          position="right"
          resizeDirection="horizontal"
          minWidth={minNodeWidth}
          maxWidth={maxNodeWidth}
          onResizeStart={onResizeStart}
          onResizeEnd={onResizeEnd}
        >
          <span
            className="wf-node-resize-handle nokey"
            role="separator"
            tabIndex={0}
            aria-orientation="vertical"
            aria-label={`Resize ${nodeData.label}`}
            aria-valuenow={Math.round(currentWidth)}
            aria-valuemin={minNodeWidth}
            aria-valuemax={maxNodeWidth}
            onKeyDown={resizeFromKeyboard}
          />
        </NodeResizeControl>
      ) : null}
      <ExecutionActivityPulse pulse={nodeData.executionPulse} status={nodeData.runtime?.status} />
      {ports.target ? <Handle type="target" position={ports.target.side} style={portPlacementStyle(ports.target)} /> : null}
      {nodeData.isStartNode ? (
        <span className="wf-node-start-badge" title="Execution starts here">
          <Flag size={11} aria-hidden="true" /> Start
        </span>
      ) : null}
      <WorkflowNodeContextToolbar nodeId={id} data={nodeData} selected={!!selected} position={ports.toolbarPosition} offset={ports.toolbarOffset} />
      {availability ? (
        <span className="wf-node-availability" title={`No longer available for new use · ${getAvailabilityStateLabel(availability.state)}`}>
          <AlertTriangle size={13} />
        </span>
      ) : null}
      <div className="wf-node-content">
        <span className="wf-node-icon" aria-hidden="true">{renderActivityIcon(icon, fallback)}</span>
        <span className="wf-node-copy">
          {nodeData.activityTypeKey === graphBoundaryCompleteTypeKey ? <small title="Completes the nearest reusable activity and cancels the rest of its body. No outgoing continuation runs.">Ends reusable activity</small> : null}
          <strong title={nodeData.label}>{nodeData.label}</strong>
          {nodeData.ghost ? <small className="wf-node-ghost-note">Not available in this environment</small> : subtitle.text ? <small title={subtitle.title}>{subtitle.text}</small> : null}
          {httpResponseBadge ? (
            <span
              className="wf-node-http-response-badge"
              data-mode={nodeData.httpEndpointResponseMode?.toLowerCase()}
              title={httpResponseBadge}
              aria-label={`HTTP endpoint behavior: ${httpResponseBadge}`}
            >
              {httpResponseBadge}
            </span>
          ) : null}
        </span>
      </div>
      <NodeSlotBadges
        ownerNodeId={id}
        ownerLabel={nodeData.label}
        slots={nodeData.childSlots}
        collections={nodeData.collectionSlots}
        onEnterSlot={nodeData.onEnterSlot}
      />
      <WorkflowNodeRuntimeBadges data={nodeData} />
      {sourcePorts.map((port, index) => {
        const placement = ports.sources[index];
        return (
          <React.Fragment key={port.name}>
            <span className={port.missing ? "wf-node-port-label wf-node-port-missing" : "wf-node-port-label"} data-side={placement.side}
              title={port.missing ? `${port.displayName}: no longer an outcome of this activity; reconnect or remove its connection` : port.displayName}
              style={{ ...portPlacementStyle(placement), maxWidth: placement.labelWidth }}>{port.displayName}</span>
            <Handle type="source" position={placement.side} id={port.name} style={portPlacementStyle(placement)} isConnectableStart={!port.missing} />
          </React.Fragment>
        );
      })}
    </div>
  );
}

/**
 * The node subtitle. Prefers a per-instance summary of the primary authored input (what this node
 * actually does) over the static `category · kind`, which duplicates the palette grouping and never
 * varies between instances. Falls back to the bare kind when nothing is authored yet. The full summary
 * and the type metadata are preserved in `title` so a hover still surfaces them.
 */
function formatNodeSubtitle(nodeData: WorkflowNodeData): { text: string; title?: string } {
  const summary = nodeData.summary?.trim();
  const typeMeta = [nodeData.category?.trim(), nodeData.executionType?.trim()]
    .filter((part): part is string => !!part)
    .join(" · ");
  if (summary) {
    return { text: truncate(summary, 40), title: [summary, typeMeta].filter(Boolean).join(" — ") };
  }
  return { text: nodeData.executionType?.trim() ?? "", title: typeMeta || undefined };
}

/** Floating and editorial layouts draw soft curves; classic and workbench keep squared wiring. */
const curvedEdgeLayouts: readonly StudioThemeLayout[] = ["floating", "editorial"];

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
  const getPath = curvedEdgeLayouts.includes(useStudioThemeLayout()) ? getBezierPath : getSmoothStepPath;
  const [path, labelX, labelY] = getPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
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
        label={!actions && typeof label === "string" && label === props.sourceHandleId ? undefined : label}
        labelX={labelX}
        labelY={labelY}
        labelStyle={labelStyle}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      />
      {props.data?.executionPulse ? <path key={props.data.executionPulse} className="wf-execution-edge-pulse" data-execution-pulse="connection"
        d={path} pathLength={1} vectorEffect="non-scaling-stroke" aria-hidden="true" /> : null}
      {actions ? (
        <EdgeLabelRenderer>
          <div
            className={["wf-edge-actions", hovered ? "visible" : "", isHighlighted ? "highlighted" : ""].filter(Boolean).join(" ")}
            style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
          >
            <button
              type="button"
              aria-label="Insert activity into connection"
              title="Insert activity"
              onClick={event => {
                const rect = event.currentTarget.getBoundingClientRect();
                const keyboardActivation = event.detail === 0;
                actions.requestInsertActivity(
                  id,
                  keyboardActivation ? rect.left + rect.width / 2 : event.clientX,
                  keyboardActivation ? rect.top + rect.height / 2 : event.clientY
                );
              }}
            >
              <Plus size={12} />
            </button>
            <button type="button" className="wf-edge-delete" aria-label="Delete connection" title="Delete connection" onClick={() => actions.deleteEdge(id)}>
              <Trash2 size={12} />
            </button>
          </div>
        </EdgeLabelRenderer>
      ) : null}
    </>
  );
}

type ConnectMenuItem = { kind: "activity"; activity: ActivityCatalogItem } | { kind: "element"; element: GraphPaletteElement };

export function ConnectMenu({ clientX, clientY, activities, elements = emptyGraphElements, onPick, onPickElement, onClose }: {
  clientX: number;
  clientY: number;
  activities: ActivityCatalogItem[];
  elements?: readonly GraphPaletteElement[];
  onPick(activity: ActivityCatalogItem): void;
  onPickElement?(element: GraphPaletteElement): void;
  onClose(): void;
}) {
  const [search, setSearch] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const returnFocusRef = useRef<HTMLElement | null>(document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const restoreFocusOnCloseRef = useRef(true);
  const listboxId = React.useId();
  const hasElements = elements.length > 0 && !!onPickElement;

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

  const groups = useMemo(
    () => mergeGraphPaletteGroups(groupActivityPalette(filtered), hasElements ? elements : emptyGraphElements, search),
    [elements, filtered, hasElements, search]
  );
  const itemsByGroup = useMemo(() => groups.map(group => ({
    category: group.category,
    items: [
      ...group.elements.map(element => ({ kind: "element" as const, element })),
      ...group.activities.map(activity => ({ kind: "activity" as const, activity }))
    ]
  })), [groups]);
  const flatItems = useMemo(() => itemsByGroup.flatMap(group => group.items), [itemsByGroup]);
  const optionId = (item: ConnectMenuItem) => `${listboxId}-option-${item.kind === "activity" ? `activity-${item.activity.activityVersionId}` : `element-${item.element.id}`}`;

  useEffect(() => {
    const returnFocus = returnFocusRef.current;
    requestAnimationFrame(() => inputRef.current?.focus());
    return () => {
      if (!restoreFocusOnCloseRef.current) return;
      requestAnimationFrame(() => {
        if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
      });
    };
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
      setActiveIndex(index => Math.min(index + 1, Math.max(flatItems.length - 1, 0)));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex(index => Math.max(index - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      const item = flatItems[activeIndex];
      if (item) pick(item);
    }
  };

  const pick = (item: ConnectMenuItem) => {
    restoreFocusOnCloseRef.current = false;
    if (item.kind === "activity") onPick(item.activity);
    else onPickElement?.(item.element);
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
        placeholder={hasElements ? "Search activities and elements..." : "Search activities..."}
        aria-label={hasElements ? "Search activities and elements" : "Search activities"}
        aria-controls={listboxId}
        aria-activedescendant={flatItems[activeIndex] ? optionId(flatItems[activeIndex]) : undefined}
        onChange={event => {
          setSearch(event.target.value);
          setActiveIndex(0);
        }}
        onKeyDown={onInputKeyDown}
      />
      <div id={listboxId} className="wf-connect-menu-list" role="listbox" aria-label={hasElements ? "Activity and element picker" : "Activity picker"}>
        {groups.length === 0 ? <p>{hasElements ? "No matching activities or elements." : "No matching activities."}</p> : itemsByGroup.map(group => (
          <section key={group.category}>
            <h4>{group.category}</h4>
            {group.items.map(item => {
              itemIndex += 1;
              const currentIndex = itemIndex;
              const active = currentIndex === activeIndex;
              return (
                <button
                  type="button"
                  role="option"
                  id={optionId(item)}
                  aria-selected={active}
                  className={active ? "active" : ""}
                  key={optionId(item)}
                  onMouseEnter={() => setActiveIndex(currentIndex)}
                  onClick={() => pick(item)}
                >
                  <strong>{item.kind === "activity" ? getActivityDisplay(item.activity) : item.element.label}</strong>
                  <small>{item.kind === "activity" ? item.activity.category || item.activity.activityTypeKey : item.element.category}</small>
                </button>
              );
            })}
          </section>
        ))}
      </div>
    </div>
  );
}
