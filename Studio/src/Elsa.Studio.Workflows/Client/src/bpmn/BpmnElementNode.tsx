import React from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { renderActivityIcon } from "../workflowFormatting";
import { bpmnElementTypes, bpmnElementTypeLabel, isEventElementType, isGatewayElementType, isTerminateEndEvent } from "./bpmnTypes";
import type { BpmnNodeData } from "./bpmnAdapter";

// Renders one BPMN element with its standard notation shape: thin circle (start event), thick circle
// (end event, filled when terminate), diamond with an X / + / O marker (exclusive / parallel /
// inclusive gateway), and a rounded task/subprocess frame showing the bound Elsa activity. Start
// events have no inbound handle; end events no outbound handle.
export function BpmnElementNode({ data, selected }: NodeProps) {
  const nodeData = data as BpmnNodeData;
  const element = nodeData.element;
  const acceptsInbound = element.elementType !== bpmnElementTypes.startEvent;
  const producesOutbound = element.elementType !== bpmnElementTypes.endEvent;
  const typeLabel = bpmnElementTypeLabel(element);

  return (
    <div
      className={["wf-bpmn-node", `wf-bpmn-${element.elementType}`, selected ? "selected" : ""].filter(Boolean).join(" ")}
      data-element-type={element.elementType}
      title={typeLabel}
    >
      {acceptsInbound ? <Handle type="target" position={Position.Left} /> : null}
      {renderBpmnShape(nodeData)}
      {producesOutbound ? <Handle type="source" position={Position.Right} id="flow" /> : null}
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
        <small className="wf-bpmn-caption">{element.name?.trim() || bpmnElementTypeLabel(element)}</small>
      </span>
    );
  }

  // Task family and subprocess: a rounded frame; a bound Elsa activity renders its icon + label.
  const bound = nodeData.boundActivity;
  return (
    <span className={["wf-bpmn-task", element.elementType === bpmnElementTypes.subProcess ? "wf-bpmn-subprocess" : ""].filter(Boolean).join(" ")}>
      {bound ? (
        <>
          <span className="wf-node-icon" aria-hidden="true">{renderActivityIcon(bound.icon)}</span>
          <span className="wf-bpmn-task-copy">
            <strong>{element.name?.trim() || bound.label}</strong>
            <small>{bpmnElementTypeLabel(element)}</small>
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
