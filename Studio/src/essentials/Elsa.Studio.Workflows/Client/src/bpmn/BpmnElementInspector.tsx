import React from "react";
import { bpmnElementTypeLabel, isDecisionGatewayType, type BpmnElement, type BpmnSequenceFlow } from "./bpmnTypes";

interface BpmnElementInspectorProps {
  element: BpmnElement;
  outboundFlows: BpmnSequenceFlow[];
  onChangeElement(elementId: string, patch: Partial<BpmnElement>): void;
  onChangeFlow(flowId: string, patch: Partial<BpmnSequenceFlow>): void;
  onSetDefaultFlow(sourceElementId: string, flowId: string | null): void;
  targetLabelFor(flow: BpmnSequenceFlow): string;
  readOnly?: boolean;
  isDecision?: boolean;
  onAddDecision?(): void;
  conversionBlockedReason?: string;
}

// Inspector for a pure BPMN structure element (event/gateway/unbound task) — these have no underlying
// Elsa activity, so the ordinary activity inspector does not apply. Activity-bearing elements route to
// the activity inspector via their bound child instead. Outbound sequence-flow conditions are edited
// here: a conditional flow matches the completing activity's outcome name, and the default flow is
// taken when nothing else matched.
export function BpmnElementInspector(props: BpmnElementInspectorProps) {
  const { element } = props;
  return (
    <div className="wf-inspector-content">
      <div className="wf-inspector-context">
        <h3>{element.name?.trim() || bpmnElementTypeLabel(element)}</h3>
        <dl>
          <dt>Element ID</dt><dd>{element.elementId}</dd>
          <dt>BPMN element</dt><dd>{bpmnElementTypeLabel(element)}</dd>
        </dl>
      </div>
      <div className="wf-inspector-tab-panels">
        <div className="wf-inspector-tab-panel"><BpmnElementConfiguration {...props} /></div>
      </div>
    </div>
  );
}

// Shared with the activity inspector so bound gateways retain both expression and routing editors.
export function BpmnElementConfiguration({
  element, outboundFlows, onChangeElement, onChangeFlow, onSetDefaultFlow, targetLabelFor,
  readOnly = false, isDecision = false, onAddDecision, conversionBlockedReason
}: BpmnElementInspectorProps) {
  const gateway = isDecisionGatewayType(element.elementType);
  return (
    <section aria-label="BPMN routing">
      <label className="wf-form-field">
        <span>Name</span>
        <input type="text" value={element.name ?? ""} placeholder={bpmnElementTypeLabel(element)}
          disabled={readOnly} onChange={event => onChangeElement(element.elementId, { name: event.target.value || null })} />
      </label>
      {gateway || isDecision ? <>
        <label className="wf-form-field">
          <span>Gateway type</span>
          <select value={element.elementType} disabled={readOnly || !!conversionBlockedReason}
            onChange={event => onChangeElement(element.elementId, { elementType: event.target.value })}>
            {!gateway ? <option value={element.elementType}>{bpmnElementTypeLabel(element)} (existing)</option> : null}
            <option value="exclusiveGateway">Exclusive gateway — one path</option>
            <option value="inclusiveGateway">Inclusive gateway — matching paths</option>
          </select>
        </label>
        {conversionBlockedReason ? <p className="wf-muted" role="note">{conversionBlockedReason}</p> : null}
        {isDecision ? <p className="wf-muted">
          Outcome returns a path name, such as High or Low. Choose an expression syntax for rules based on
          workflow data. Expressions must return a configured outcome name, or Done for the fallback path.
          A blank Outcome also returns Done. Mark one outgoing path as the default.
          {!gateway ? " This existing Decision is a task. Choose a gateway type to use gateway routing." : ""}
        </p> : !element.childNodeId ? <div>
          <p className="wf-muted">Add a BPMN Decision to evaluate an Outcome expression for this gateway.</p>
          <button type="button" disabled={readOnly || !onAddDecision} onClick={onAddDecision}>Add decision</button>
          {!onAddDecision ? <p className="wf-muted">BPMN Decision is not available in the activity catalog.</p> : null}
        </div> : null}
      </> : null}
      {outboundFlows.length > 0 ? (
        <div className="wf-bpmn-flow-list">
          <h4>Outgoing paths</h4>
          {outboundFlows.map(flow => {
            const isDefault = flow.isDefault === true || element.defaultFlowId === flow.flowId;
            return (
              <div className="wf-bpmn-flow-row wf-property-row" key={flow.flowId}>
                <span className="wf-bpmn-flow-target" title={flow.flowId}>→ {targetLabelFor(flow)}</span>
                <input type="text" value={flow.conditionOutcome ?? ""} placeholder="Any outcome"
                  aria-label={`Condition outcome for flow to ${targetLabelFor(flow)}`}
                  disabled={readOnly || isDefault}
                  onChange={event => onChangeFlow(flow.flowId, { conditionOutcome: event.target.value || null })} />
                <label className="wf-bpmn-flow-default">
                  <input type="checkbox" checked={isDefault} aria-label={`Default flow to ${targetLabelFor(flow)}`}
                    disabled={readOnly}
                    onChange={event => onSetDefaultFlow(element.elementId, event.target.checked ? flow.flowId : null)} />
                  <span>Default</span>
                </label>
              </div>
            );
          })}
          <p className="wf-muted">A condition matches the completing activity's outcome name. A blank condition is
            unconditional. The default flow is taken only when no other flow matches.</p>
          {isDecision && outboundFlows.every(flow => !!flow.conditionOutcome?.trim() && !flow.isDefault && element.defaultFlowId !== flow.flowId)
            ? <p className="wf-muted" role="note">No default path is configured. If no path matches, gateway routing faults.</p> : null}
        </div>
      ) : <p className="wf-muted">Connect outgoing paths on the canvas, then configure their outcome names here.</p>}
    </section>
  );
}
