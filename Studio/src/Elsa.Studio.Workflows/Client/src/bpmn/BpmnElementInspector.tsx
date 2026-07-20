import React from "react";
import { bpmnElementTypeLabel, type BpmnElement } from "./bpmnTypes";

interface BpmnElementInspectorProps {
  element: BpmnElement;
  onChangeElement(elementId: string, patch: Partial<BpmnElement>): void;
}

// Inspector for a pure BPMN structure element (event/gateway/unbound task) — these have no underlying
// Elsa activity, so the ordinary activity inspector does not apply. Activity-bearing elements route to
// the activity inspector via their bound child instead.
export function BpmnElementInspector({ element, onChangeElement }: BpmnElementInspectorProps) {
  return (
    <div className="wf-inspector-content">
      <h3>{element.name?.trim() || bpmnElementTypeLabel(element)}</h3>
      <dl>
        <dt>Element ID</dt>
        <dd>{element.elementId}</dd>
        <dt>BPMN element</dt>
        <dd>{bpmnElementTypeLabel(element)}</dd>
      </dl>
      <label className="wf-field">
        <span>Name</span>
        <input
          type="text"
          value={element.name ?? ""}
          placeholder={bpmnElementTypeLabel(element)}
          onChange={event => onChangeElement(element.elementId, { name: event.target.value || null })}
        />
      </label>
      <p className="wf-muted">
        Gateway routing follows the sequence flows on the canvas: conditional flows match the completing
        activity's outcome names; the default flow is taken when nothing else matches.
      </p>
    </div>
  );
}
