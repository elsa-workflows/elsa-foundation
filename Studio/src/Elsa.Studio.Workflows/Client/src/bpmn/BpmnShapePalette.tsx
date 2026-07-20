import React from "react";
import { bpmnShapePalette, type BpmnShapeDescriptor } from "./bpmnTypes";

interface BpmnShapePaletteProps {
  onAddShape(shape: BpmnShapeDescriptor): void;
}

// The BPMN shape section shown above the activity palette in bpmn designer mode: stamps pure structure
// elements (events, gateways, unbound tasks) onto the canvas. Activity-bearing tasks come from the
// ordinary activity palette below (a catalog drop becomes a bound task/subProcess element).
export function BpmnShapePalette({ onAddShape }: BpmnShapePaletteProps) {
  return (
    <div className="wf-bpmn-shape-palette">
      <span className="wf-bpmn-shape-palette-title">BPMN shapes</span>
      <div className="wf-bpmn-shape-palette-list" role="list" aria-label="BPMN shapes">
        {bpmnShapePalette.map(shape => (
          <button
            key={shape.key}
            type="button"
            role="listitem"
            className="wf-bpmn-shape-palette-item"
            title={`Add ${shape.label.toLowerCase()} to the canvas`}
            onClick={() => onAddShape(shape)}
          >
            {shape.label}
          </button>
        ))}
      </div>
    </div>
  );
}
