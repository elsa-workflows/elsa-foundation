import { useId } from "react";
import { BaseEdge, Handle, NodeResizeControl, Position, type Edge, type EdgeProps, type Node, type NodeProps } from "@xyflow/react";
import { renderActivityIcon } from "../activityIconView";
import { NodeSlotBadges } from "../workflow-editor/NodeSlotBadges";
import { bpmnAppearanceLabelStyle } from "./bpmnDocumentAppearance";
import { BpmnNotation } from "./bpmnNotation";
import { isActivityBearingElementType } from "./bpmnTypes";
import type { BpmnBounds, BpmnPoint, BpmnScopeAddress } from "./bpmnDocument";
import type { BpmnDocumentEdgeData, BpmnDocumentNodeData } from "./bpmnDocumentCanvas";
import type { BpmnDocumentCanvasViewProps, BpmnDocumentNativePresentation } from "./bpmnDocumentCanvasPort";

export interface BpmnDocumentViewNodeData extends BpmnDocumentNodeData {
  presentation?: BpmnDocumentNativePresentation | null;
  standardScope?: BpmnScopeAddress | null;
  canResize: boolean;
  blocked: boolean;
  parentAbsolutePosition: BpmnPoint;
  beginResize(): void;
  previewResize?(diId: string, bounds: BpmnBounds, direction: string): void;
  finishResize(diId: string, bounds: BpmnBounds, direction: string): void;
  navigate: BpmnDocumentCanvasViewProps["onNavigateStandard"];
  openNative: BpmnDocumentCanvasViewProps["onOpenNativeSlot"];
}

export function BpmnDocumentCanvasElement({ id, data, selected, width, height }: NodeProps<Node<BpmnDocumentViewNodeData>>) {
  const descriptor = { ...data.notation, width: width ?? data.absoluteBounds.width, height: height ?? data.absoluteBounds.height };
  const activity = isActivityBearingElementType(data.semanticType), native = data.presentation;
  const ownsLabel = ["participant", "lane", "textAnnotation", "group"].includes(data.semanticType);
  const label = descriptor.name || native?.activity.label;
  const labelBounds = data.labelBounds;
  const labelPosition = labelBounds ? { left: labelBounds.x - data.absoluteBounds.x, top: labelBounds.y - data.absoluteBounds.y,
    width: labelBounds.width, height: labelBounds.height } : undefined;
  const resizePositions = data.semanticType === "textAnnotation" ? ["left", "right"] as const : ["bottom-right"] as const;
  return <div className="wf-bpmn-document-element" data-bpmn-element-id={data.semanticElementId} data-bpmn-di-id={id}>
    <Handle type="target" position={Position.Left} isConnectable={false} className="wf-bpmn-document-route-handle" />
    <BpmnNotation descriptor={descriptor} selected={selected} />
    {!ownsLabel && label ? <span className={`wf-bpmn-document-label ${!labelBounds && activity ? "embedded" : "external"}`}
      style={{ ...labelPosition, ...bpmnAppearanceLabelStyle(descriptor.appearance) }}>{label}</span> : null}
    {native && activity ? <span className="wf-node-icon wf-bpmn-document-binding" data-tone={native.activity.iconTone ?? "accent"} aria-hidden="true">
      {renderActivityIcon(native.activity.icon, "activity")}</span> : null}
    {native?.childSlots.length ? <span className="wf-bpmn-document-slots">{data.blocked || !data.openNative ? native.childSlots.map(slot => <button key={slot.id} type="button" className="wf-node-slot-badge nodrag nopan" disabled>{slot.label}</button>) : <NodeSlotBadges ownerNodeId={native.activity.nodeId}
      ownerLabel={label || native.activity.label} slots={native.childSlots}
      onEnterSlot={!data.openNative ? undefined : slot => { void data.openNative!(native.activity.nodeId, slot); }} />}</span> : null}
    {data.standardScope && data.navigate ? <button type="button" className="wf-bpmn-document-open nodrag nopan" disabled={data.blocked}
      onClick={event => { event.stopPropagation(); void data.navigate!(data.standardScope!); }}>Open BPMN body</button> : null}
    {selected && data.canResize && !data.blocked ? resizePositions.map(position => <NodeResizeControl key={position} position={position}
      minWidth={data.semanticType === "subProcess" ? 100 : 20} minHeight={data.semanticType === "subProcess" ? 80 : 20}
      onResizeStart={data.beginResize} onResize={(_event, parameters) => {
        data.previewResize?.(id, { x: data.parentAbsolutePosition.x + parameters.x, y: data.parentAbsolutePosition.y + parameters.y, width: parameters.width, height: parameters.height }, position === "left" ? "w" : position === "right" ? "e" : "se");
      }} onResizeEnd={(_event, parameters) => {
        const parentX = data.parentAbsolutePosition.x;
        const parentY = data.parentAbsolutePosition.y;
        data.finishResize(id, { x: parentX + parameters.x, y: parentY + parameters.y, width: parameters.width, height: parameters.height },
          position === "left" ? "w" : position === "right" ? "e" : "se");
      }} />) : null}
    <Handle type="source" position={Position.Right} isConnectable={false} className="wf-bpmn-document-route-handle" />
  </div>;
}

export function BpmnDocumentCanvasConnection({ id, data, selected }: EdgeProps<Edge<BpmnDocumentEdgeData>>) {
  const markerId = useId().replace(/:/g, "_"), points = data?.points ?? [];
  if (points.length < 2) return null;
  const path = points.map((point, index) => `${index ? "L" : "M"}${point.x},${point.y}`).join(" ");
  const type = data!.semanticType, association = type === "association", message = type === "messageFlow";
  const dataAssociation = type === "dataInputAssociation" || type === "dataOutputAssociation";
  const direction = data!.element.getAttribute("associationDirection");
  const arrow = !association || direction === "One" || direction === "Both";
  const paint = data!.appearance.stroke ?? "var(--studio-text)";
  const openArrow = message || association || dataAssociation;
  const name = data!.element.getAttribute("name"), labelBounds = data!.labelBounds;
  const midpoint = points[Math.floor(points.length / 2)];
  const labelX = labelBounds ? labelBounds.x + labelBounds.width / 2 : midpoint.x;
  const labelY = labelBounds ? labelBounds.y + labelBounds.height / 2 : midpoint.y - 8;
  return <>
    <defs><marker id={`${markerId}-end`} markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto-start-reverse" markerUnits="userSpaceOnUse">
      <path d={openArrow ? "M1,1 L10,6 L1,11" : "M1,1 L10,6 L1,11 Z"} fill={openArrow ? "none" : paint} stroke={paint} strokeWidth="1.5" /></marker>
    <marker id={`${markerId}-start`} markerWidth="12" markerHeight="12" refX="6" refY="6" orient="auto" markerUnits="userSpaceOnUse">
      <circle cx="6" cy="6" r="4" fill="var(--studio-surface)" stroke={paint} strokeWidth="1.5" /></marker>
    <marker id={`${markerId}-default`} markerWidth="18" markerHeight="18" refX="0" refY="9" orient="auto" markerUnits="userSpaceOnUse">
      <path data-bpmn-marker="default-flow" d="M6,3 L12,15" stroke={paint} strokeWidth="1.5" /></marker>
    <marker id={`${markerId}-conditional`} markerWidth="14" markerHeight="14" refX="1" refY="7" orient="auto" markerUnits="userSpaceOnUse">
      <path data-bpmn-marker="conditional-flow" d="M1,7 L7,1 L13,7 L7,13 Z" fill="var(--studio-surface)" stroke={paint} strokeWidth="1.5" /></marker></defs>
    {selected ? <path data-bpmn-overlay="selection" d={path} className="wf-bpmn-document-edge-selection" /> : null}
    <BaseEdge id={id} path={path} markerStart={message ? `url(#${markerId}-start)` : association && direction === "Both" ? `url(#${markerId}-end)`
      : data!.isDefault ? `url(#${markerId}-default)` : data!.isConditional ? `url(#${markerId}-conditional)` : undefined}
      markerEnd={arrow ? `url(#${markerId}-end)` : undefined} style={{ stroke: paint, strokeWidth: 1.5,
        strokeDasharray: message ? "8 5" : association || dataAssociation ? "2 4" : undefined }} />
    {name ? <text className="wf-bpmn-document-edge-label" x={labelX} y={labelY} textAnchor="middle" dominantBaseline="central"
      style={bpmnAppearanceLabelStyle(data!.appearance)}>{name}</text> : null}
  </>;
}
