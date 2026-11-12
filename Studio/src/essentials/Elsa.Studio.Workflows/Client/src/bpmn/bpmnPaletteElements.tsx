import React from "react";
import type { GraphPaletteElement } from "../graph-authoring/graphPaletteElements";
import { createBpmnShapeNode } from "./bpmnAdapter";
import { bpmnShapePalette, bpmnElementTypes, isEventElementType, isGatewayElementType, type BpmnShapeDescriptor } from "./bpmnTypes";
import { directBindingProperty, directHumanBinding } from "./bpmnDirectBinding";

/** BPMN owns its notation and payload; the shared Elements palette only consumes contributions. */
export const bpmnPaletteElements: readonly GraphPaletteElement[] = bpmnShapePalette.map(shape => ({
  id: `bpmn:${shape.key}`,
  label: shape.label,
  category: "BPMN",
  description: shape.key === "task" ? "Task with no activity bound yet." : undefined,
  keywords: [shape.elementType, shape.key === "exclusiveGateway" ? "decision xor" : shape.key === "parallelGateway" ? "and fork join" : shape.key === "inclusiveGateway" ? "or" : ""],
  icon: <BpmnPaletteIcon shape={shape} />,
  createNode: position => createBpmnShapeNode(shape, position)
}));

/** Offered only by an editable root workflow with an available direct publisher. */
export const directHumanBpmnElement: GraphPaletteElement = {
  id: "bpmn:humanWait", label: "Human wait", category: "BPMN",
  description: "Pause a direct BPMN process for a person to complete.", keywords: ["human", "person", "user task", "wait"],
  icon: <BpmnPaletteIcon shape={{ key: "humanWait", label: "Human wait", elementType: "userTask" }} />,
  createNode: position => createBpmnShapeNode({ key: "humanWait", label: "Human wait", name: "Human wait", elementType: "userTask",
    properties: { [directBindingProperty]: directHumanBinding } }, position)
};

function BpmnPaletteIcon({ shape }: { shape: BpmnShapeDescriptor }) {
  const isEnd = shape.elementType === bpmnElementTypes.endEvent;
  const terminate = shape.eventDefinitions?.some(definition => definition.type === "terminate");
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true" data-element-icon={shape.key}>
      {isEventElementType(shape.elementType) ? <>
        <circle cx="12" cy="12" r="8" strokeWidth={isEnd ? 3 : 1.5} />
        {terminate ? <circle cx="12" cy="12" r="4.5" fill="currentColor" stroke="none" /> : null}
      </> : isGatewayElementType(shape.elementType) ? <>
        <path d="M12 2 22 12 12 22 2 12Z" />
        {shape.elementType === bpmnElementTypes.exclusiveGateway ? <path d="m8.5 8.5 7 7m-7 0 7-7" strokeWidth="2" />
          : shape.elementType === bpmnElementTypes.parallelGateway ? <path d="M7 12h10m-5-5v10" strokeWidth="2" />
            : <circle cx="12" cy="12" r="4.5" strokeWidth="2" />}
      </> : <rect x="2.5" y="4.5" width="19" height="15" rx="4" />}
    </svg>
  );
}
