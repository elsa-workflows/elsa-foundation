import { useState, type ReactNode } from "react";
import { bpmnNamespaces as ns, readBpmnDocument, resolveBpmnDocumentReference, type BpmnDocumentSelection } from "./bpmnDocument";
import { readBpmnDocumentAppearance } from "./bpmnDocumentAppearance";
import type { BpmnDocumentCanvasSnapshot } from "./bpmnDocumentCanvasPort";
import type { BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";
import { canRemoveBpmnStandardProperty, planBpmnAppearance, planBpmnCondition, planBpmnDefaultFlow, planBpmnLabel, planBpmnLoop, planBpmnStandardProperty, readBpmnStandardProperties, type BpmnAppearanceIntent, type BpmnLoopMode, type BpmnStandardPropertyIntent, type BpmnStandardPropertyNode } from "./bpmnDocumentPropertyOperations";
import "./bpmnDocumentControls.css";

export type BpmnDocumentInspectorIntent = BpmnStandardPropertyIntent
  | { kind: "label"; value: string }
  | { kind: "condition"; value: string; language?: string }
  | { kind: "default"; flowId: string | null }
  | { kind: "loop"; mode: BpmnLoopMode; replaceConfiguredLoop: boolean }
  | { kind: "appearance"; value: BpmnAppearanceIntent };
export interface BpmnDocumentPropertyDraft {
  /** The host retains this baseline and edit across navigation/refusal until explicit acknowledgement/discard. */
  source: BpmnDocumentCanvasSnapshot;
  selection: BpmnDocumentSelection;
  intent: BpmnDocumentInspectorIntent;
}
export interface BpmnDocumentPropertiesProps {
  snapshot: BpmnDocumentCanvasSnapshot;
  selection: BpmnDocumentSelection | null;
  readOnly: boolean;
  pending?: boolean;
  message?: string | null;
  /** Controlled by the resource host. There is no hidden inspector save buffer or independent history. */
  draft: BpmnDocumentPropertyDraft | null;
  onDraftChange(draft: BpmnDocumentPropertyDraft | null): void;
  /** Host retains the plan and admits it through the same Promise acknowledgement/checkpoint port as canvas actions. */
  onApply(plan: BpmnDocumentOperationPlan, expected: BpmnDocumentCanvasSnapshot, label: string): void;
  renderNativeConfiguration?(selection: BpmnDocumentSelection, source: BpmnDocumentCanvasSnapshot): ReactNode;
}

export function planBpmnInspectorDraft(draft: BpmnDocumentPropertyDraft): BpmnDocumentOperationPlan {
  const { source: { owner, scope }, selection, intent } = draft;
  if (intent.kind === "label") return planBpmnLabel(owner, scope, selection, intent.value);
  if (intent.kind === "condition") return planBpmnCondition(owner, scope, selection.elementId, intent.value, intent.language);
  if (intent.kind === "default") return planBpmnDefaultFlow(owner, scope, selection.elementId, intent.flowId);
  if (intent.kind === "loop") return planBpmnLoop(owner, scope, selection.elementId, intent.mode, intent.replaceConfiguredLoop);
  if (intent.kind === "appearance") { if (!selection.diId) throw new Error("Select an actual DI instance to edit appearance."); return planBpmnAppearance(owner, scope, selection.diId, intent.value); }
  return planBpmnStandardProperty(owner, scope, intent);
}

/** Shared inspector foundation. Local text is controlled and source-pinned by the host, including rejected edits. */
export function BpmnDocumentProperties(props: BpmnDocumentPropertiesProps) {
  const { draft, pending = false } = props, [error, setError] = useState<string | null>(null);
  const source = draft?.source ?? props.snapshot, selection = draft?.selection ?? props.selection;
  if (!selection) return <section aria-label="BPMN properties"><p>Select a BPMN construct.</p></section>;
  let propertyTree: BpmnStandardPropertyNode, diTree: BpmnStandardPropertyNode | undefined;
  try {
    propertyTree = readBpmnStandardProperties(source.owner, source.scope, selection);
    if (selection.diId) diTree = readBpmnStandardProperties(source.owner, source.scope, { ...selection, property: { elementId: selection.diId } });
  } catch (reason) { return <section aria-label="BPMN properties"><p role="alert">{reason instanceof Error ? reason.message : String(reason)}</p></section>; }
  const parsed = readBpmnDocument(source.owner), element = parsed.byId.get(selection.elementId)!;
  const locked = props.readOnly || pending;
  const begin = (intent: BpmnDocumentInspectorIntent) => { setError(null); props.onDraftChange({ source, selection, intent }); };
  const change = (intent: BpmnDocumentInspectorIntent) => props.onDraftChange({ source, selection, intent });
  const apply = () => { if (!draft) return; try { setError(null); props.onApply(planBpmnInspectorDraft(draft), draft.source, "Edit BPMN property"); } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } };
  const outgoing = [...parsed.byId.values()].filter(flow => flow.namespaceURI === ns.model && flow.localName === "sequenceFlow" && resolveBpmnDocumentReference(parsed, flow, flow.getAttribute("sourceRef")) === element);
  const loop = [...element.children].find(child => child.namespaceURI === ns.model && ["standardLoopCharacteristics", "multiInstanceLoopCharacteristics"].includes(child.localName));
  const actualLoop: BpmnLoopMode = !loop ? "none" : loop.localName === "standardLoopCharacteristics" ? "standard" : ["true", "1"].includes(loop.getAttribute("isSequential") ?? "") ? "sequential" : "parallel";
  const activity = ["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask", "callActivity", "subProcess", "transaction", "adHocSubProcess"].includes(element.localName);
  const condition = [...element.children].find(child => child.namespaceURI === ns.model && child.localName === "conditionExpression");
  const input = (label: string, value: string, edit: (value: string) => void, multiline = false) => <label>{label}{multiline ? <textarea value={value} disabled={locked} onChange={event => edit(event.target.value)} /> : <input value={value} disabled={locked} onChange={event => edit(event.target.value)} />}</label>;
  const tree = (node: BpmnStandardPropertyNode): ReactNode => <details key={JSON.stringify(node.address)}><summary>{node.type}</summary>
    <dl>{node.attributes.map(attribute => <div key={`${attribute.namespaceURI}:${attribute.name}`}><dt>{attribute.name}</dt><dd><code>{attribute.value}</code>{!attribute.readOnly && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "attribute", target: node.address, name: attribute.name, ...(attribute.namespaceURI ? { namespaceURI: attribute.namespaceURI } : {}), value: attribute.value })}>Edit {attribute.name}</button>}</dd></div>)}</dl>
    {node.text !== undefined && <div><pre>{node.text}</pre><button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "text", target: node.address, value: node.text! })}>Edit {node.type} text</button></div>}
    {node.namespaceURI === ns.model && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "appendProperty", parent: node.address, xml: `<b:documentation xmlns:b="${ns.model}"></b:documentation>` })}>Add property to {node.type}</button>}
    {canRemoveBpmnStandardProperty(node) && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "removeProperty", target: node.address })}>Remove {node.type} property</button>}
    {node.children.map(tree)}
  </details>;
  const currentName = element.localName === "textAnnotation" ? [...element.children].find(child => child.localName === "text" && child.namespaceURI === ns.model)?.textContent ?? "" : element.localName === "group" ? resolveBpmnDocumentReference(parsed, element, element.getAttribute("categoryValueRef"))?.getAttribute("value") ?? "" : element.getAttribute("name") ?? "";
  return <section className="wf-bpmn-document-properties" aria-label="BPMN properties">
    <p><strong>{element.localName}</strong> <code>{selection.elementId}</code>{selection.diId && <> · DI <code>{selection.diId}</code></>}</p>
    {draft && <p role="status">Property edit retained at source revision {draft.source.token.revision}.</p>}
    {(props.message || error) && <p role="alert">{error ?? props.message}</p>}
    <div className="wf-bpmn-document-property-actions">
      {!["association", "dataInputAssociation", "dataOutputAssociation"].includes(element.localName) && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "label", value: currentName })}>Edit label</button>}
      {outgoing.length > 0 && (activity || ["exclusiveGateway", "inclusiveGateway", "complexGateway"].includes(element.localName)) && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "default", flowId: resolveBpmnDocumentReference(parsed, element, element.getAttribute("default"))?.id ?? null })}>Edit default flow</button>}
      {element.localName === "sequenceFlow" && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "condition", value: condition?.textContent ?? "", language: condition?.getAttribute("language") ?? undefined })}>Edit condition</button>}
      {activity && element.getAttribute("triggeredByEvent") !== "true" && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "loop", mode: actualLoop, replaceConfiguredLoop: false })}>Edit loop</button>}
      {selection.diId && <button type="button" disabled={locked || !!draft} onClick={() => begin({ kind: "appearance", value: {} })}>Edit appearance</button>}
    </div>
    {draft && <div className="wf-bpmn-document-property-edit">
      {draft.intent.kind === "label" && input("Label", draft.intent.value, value => change({ kind: "label", value }))}
      {draft.intent.kind === "condition" && <>{input("Condition expression", draft.intent.value, value => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "condition" }>, value }), true)}{input("Expression language", draft.intent.language ?? "", language => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "condition" }>, language }))}</>}
      {draft.intent.kind === "default" && <label>Default flow<select disabled={locked} value={draft.intent.flowId ?? ""} onChange={event => change({ kind: "default", flowId: event.target.value || null })}><option value="">No default</option>{outgoing.map(flow => <option key={flow.id} value={flow.id}>{flow.getAttribute("name") || flow.id}</option>)}</select></label>}
      {draft.intent.kind === "loop" && <><label>Loop<select disabled={locked} value={draft.intent.mode} onChange={event => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "loop" }>, mode: event.target.value as BpmnLoopMode })}>{["none", "standard", "parallel", "sequential"].map(mode => <option key={mode}>{mode}</option>)}</select></label><label><input type="checkbox" disabled={locked} checked={draft.intent.replaceConfiguredLoop} onChange={event => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "loop" }>, replaceConfiguredLoop: event.target.checked })} />Replace existing loop configuration</label></>}
      {draft.intent.kind === "attribute" && <>{input(`Attribute ${draft.intent.name}`, draft.intent.value ?? "", value => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "attribute" }>, value }))}<button type="button" disabled={locked} onClick={() => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "attribute" }>, value: null })}>Remove attribute</button>{draft.intent.value === null && <p>This attribute will be removed.</p>}</>}
      {draft.intent.kind === "text" && input("Property text", draft.intent.value, value => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "text" }>, value }), true)}
      {draft.intent.kind === "appendProperty" && input("Standard XML property", draft.intent.xml, xml => change({ ...draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "appendProperty" }>, xml }), true)}
      {draft.intent.kind === "removeProperty" && <p>This exact XML property and its contained configuration will be removed.</p>}
      {draft.intent.kind === "appearance" && (["fill", "stroke", "labelColor"] as const).map(name => {
        const intent = draft.intent as Extract<BpmnDocumentInspectorIntent, { kind: "appearance" }>, actual = selection.diId ? readBpmnDocumentAppearance(parsed.byId.get(selection.diId)!) : undefined;
        return <div key={name}>{input(name === "labelColor" ? "Label color" : name === "fill" ? "Fill" : "Stroke", intent.value[name] === undefined ? actual?.[name] ?? "" : intent.value[name] ?? "", value => change({ kind: "appearance", value: { ...intent.value, [name]: value || null } }))}</div>;
      })}
      <div className="wf-bpmn-document-property-actions"><button type="button" disabled={locked} onClick={apply}>Apply BPMN property</button><button type="button" disabled={pending} onClick={() => { setError(null); props.onDraftChange(null); }}>Discard BPMN property</button></div>
    </div>}
    <details><summary>Standard XML properties</summary>{tree(propertyTree)}</details>
    {diTree && <details><summary>Diagram properties</summary>{tree(diTree)}</details>}
    {props.renderNativeConfiguration && <section aria-label="Elsa activity configuration">{props.renderNativeConfiguration(selection, source)}</section>}
  </section>;
}
