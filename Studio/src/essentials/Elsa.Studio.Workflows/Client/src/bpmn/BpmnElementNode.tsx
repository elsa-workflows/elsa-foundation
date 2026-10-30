import React from "react";
import { Handle, type NodeProps } from "@xyflow/react";
import { renderActivityIcon } from "../activityIconView";
import { NodeSlotBadges } from "../workflow-editor/NodeSlotBadges";
import { RuntimeIncidentAction } from "../workflow-editor/RuntimeIncidentAction";
import { ExecutionActivityPulse } from "../workflow-editor/ExecutionMotionPulse";
import { bpmnElementTypes, bpmnElementTypeLabel, isEventElementType, isGatewayElementType, isTerminateEndEvent } from "./bpmnTypes";
import type { BpmnNodeData } from "./bpmnAdapter";
import { useGraphPortLayout } from "../graph-authoring/useGraphPortLayout";
import { portPlacementStyle } from "../graph-authoring/graphPortLayout";

// Renders one BPMN element with its standard notation shape: thin circle (start event), thick circle
// (end event, filled when terminate), diamond with an X / + / O marker (exclusive / parallel /
// inclusive gateway), and a rounded task/subprocess frame showing the bound Elsa activity. Start
// events have no inbound handle; end events no outbound handle.
export function BpmnElementNode({ id, data, selected }: NodeProps) {
  const nodeData = data as BpmnNodeData;
  const element = nodeData.element;
  const acceptsInbound = element.elementType !== bpmnElementTypes.startEvent;
  const producesOutbound = element.elementType !== bpmnElementTypes.endEvent;
  const typeLabel = bpmnElementTypeLabel(element);
  const ports = useGraphPortLayout(id, producesOutbound ? ["flow"] : [], acceptsInbound);

  return (
    <div
      className={["wf-bpmn-node", selected ? "selected" : "", nodeData.runtime ? "wf-bpmn-node-runtime" : "", nodeData.runtime?.hasBlockingIncident ? "faulted" : ""].filter(Boolean).join(" ")}
      data-element-type={element.elementType}
      data-runtime-node-id={nodeData.runtimeNodeId ?? nodeData.boundActivity?.nodeId ?? element.elementId}
      title={typeLabel}
    >
      <ExecutionActivityPulse pulse={nodeData.executionPulse} status={nodeData.runtime?.status} />
      {ports.target ? <Handle type="target" position={ports.target.side} style={portPlacementStyle(ports.target)} /> : null}
      {renderBpmnShape(nodeData)}
      {nodeData.runtime ? <RuntimeIncidentAction runtime={nodeData.runtime} onOpenIncident={nodeData.onIncidentClick} activityLabel={element.name?.trim() || nodeData.boundActivity?.label || typeLabel} className="wf-bpmn-incident-action" /> : null}
      {ports.sources.map(port => <Handle key={port.id} type="source" position={port.side} id={port.id} style={portPlacementStyle(port)} />)}
    </div>
  );
}

function renderBpmnShape(nodeData: BpmnNodeData) {
  const element = nodeData.element;

  if (isEventElementType(element.elementType)) {
    const isEnd = element.elementType === bpmnElementTypes.endEvent;
    const isTerminate = isTerminateEndEvent(element);
    return (
      <span className="wf-bpmn-event-wrap">
        <span className={["wf-bpmn-event", isEnd ? "wf-bpmn-event-end" : "wf-bpmn-event-start"].join(" ")} aria-hidden="true">
          {isTerminate ? <span className="wf-bpmn-event-terminate" /> : null}
        </span>
        <small className="wf-bpmn-caption">{element.name?.trim() || bpmnElementTypeLabel(element)}</small>
      </span>
    );
  }

  if (isGatewayElementType(element.elementType)) {
    const marker = element.elementType === bpmnElementTypes.parallelGateway
      ? "+"
      : element.elementType === bpmnElementTypes.inclusiveGateway
        ? "O"
        : "×";
    return (
      <span className="wf-bpmn-gateway-wrap">
        <span className="wf-bpmn-gateway" aria-hidden="true">
          <span className="wf-bpmn-gateway-marker">{marker}</span>
        </span>
        <small className="wf-bpmn-caption">{element.name?.trim() || nodeData.boundActivity?.label || bpmnElementTypeLabel(element)}</small>
      </span>
    );
  }

  // Task family and subprocess: a rounded frame; a bound Elsa activity renders its icon + label, plus a
  // slot badge per child slot when it is a container (the affordance for descending into a subprocess).
  const bound = nodeData.boundActivity;
  return (
    <span className={["wf-bpmn-task", element.elementType === bpmnElementTypes.subProcess ? "wf-bpmn-subprocess" : ""].filter(Boolean).join(" ")}>
      {bound ? (
        <>
          <span className="wf-node-icon" data-tone={bound.iconTone ?? "accent"} aria-hidden="true">{renderActivityIcon(bound.icon, "activity")}</span>
          <span className="wf-bpmn-task-copy">
            <strong>{element.name?.trim() || bound.label}</strong>
            <small>{bpmnElementTypeLabel(element)}</small>
            {/* Slot entry addresses the bound ACTIVITY node; the element id resolves to nothing. */}
            <NodeSlotBadges
              ownerNodeId={bound.nodeId}
              ownerLabel={element.name?.trim() || bound.label}
              slots={nodeData.childSlots}
              onEnterSlot={nodeData.onEnterSlot}
            />
          </span>
        </>
      ) : (
        <span className="wf-bpmn-task-copy">
          <strong>{element.name?.trim() || bpmnElementTypeLabel(element)}</strong>
          <small>No activity bound</small>
        </span>
      )}
      {element.elementType === bpmnElementTypes.subProcess ? <span className="wf-bpmn-subprocess-marker" aria-hidden="true">⊞</span> : null}
    </span>
  );
}
