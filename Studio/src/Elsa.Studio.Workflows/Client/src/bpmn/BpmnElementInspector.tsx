import React from "react";
import { bpmnElementTypeLabel, type BpmnElement, type BpmnSequenceFlow } from "./bpmnTypes";

interface BpmnElementInspectorProps {
  element: BpmnElement;
  outboundFlows: BpmnSequenceFlow[];
  onChangeElement(elementId: string, patch: Partial<BpmnElement>): void;
  onChangeFlow(flowId: string, patch: Partial<BpmnSequenceFlow>): void;
  onSetDefaultFlow(sourceElementId: string, flowId: string | null): void;
  targetLabelFor(flow: BpmnSequenceFlow): string;
}

// Inspector for a pure BPMN structure element (event/gateway/unbound task) — these have no underlying
// Elsa activity, so the ordinary activity inspector does not apply. Activity-bearing elements route to
// the activity inspector via their bound child instead. Outbound sequence-flow conditions are edited
// here: a conditional flow matches the completing activity's outcome name, and the default flow is
// taken when nothing else matched.
export function BpmnElementInspector({
  element,
  outboundFlows,
  onChangeElement,
  onChangeFlow,
  onSetDefaultFlow,
  targetLabelFor
}: BpmnElementInspectorProps) {
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
      {outboundFlows.length > 0 ? (
        <div className="wf-bpmn-flow-list">
          <span>Outbound sequence flows</span>
          {outboundFlows.map(flow => {
            const isDefault = flow.isDefault === true;
            return (
              <div className="wf-bpmn-flow-row" key={flow.flowId}>
                <span className="wf-bpmn-flow-target" title={flow.flowId}>→ {targetLabelFor(flow)}</span>
                <input
                  type="text"
                  value={flow.conditionOutcome ?? ""}
                  placeholder="Any outcome"
                  aria-label={`Condition outcome for flow to ${targetLabelFor(flow)}`}
                  disabled={isDefault}
                  onChange={event => onChangeFlow(flow.flowId, { conditionOutcome: event.target.value || null })}
                />
                <label className="wf-bpmn-flow-default">
                  <input
                    type="checkbox"
                    checked={isDefault}
                    aria-label={`Default flow to ${targetLabelFor(flow)}`}
                    onChange={event => onSetDefaultFlow(element.elementId, event.target.checked ? flow.flowId : null)}
                  />
                  <span>Default</span>
                </label>
              </div>
            );
          })}
          <p className="wf-muted">
            A flow with a condition outcome is taken when the completing activity reports that outcome
            name; a blank condition means the flow is always taken. The default flow fires only when no
            other flow matched.
          </p>
        </div>
      ) : (
        <p className="wf-muted">
          Gateway routing follows the sequence flows on the canvas: conditional flows match the
          completing activity's outcome names; the default flow is taken when nothing else matches.
        </p>
      )}
    </div>
  );
}
