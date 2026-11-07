import type { BpmnNotationDescriptor } from "./bpmnNotation";
import type { BpmnDocumentConnectionKind } from "./bpmnDocumentPolicy";

export type BpmnToolboxGroup = "Tasks" | "Gateways" | "Events" | "Bodies" | "Collaboration" | "Data" | "Artifacts";
export type BpmnPropertySection = "name" | "routing" | "event" | "loop" | "script" | "call" | "body" | "participant" | "lane" | "data" | "text" | "category" | "appearance" | "geometry";
export interface BpmnToolboxShape {
  id: string;
  label: string;
  group: BpmnToolboxGroup;
  type: string;
  width: number;
  height: number;
  attributes?: Readonly<Record<string, string>>;
  eventDefinitions?: readonly string[];
  expanded?: boolean;
  configuration: readonly BpmnPropertySection[];
  /** Standard ownership, distinct from an Elsa activity's native Slots. */
  placement: "flowScope" | "activityBoundary" | "collaboration" | "laneContainer";
  eventSubprocessOnly?: boolean;
  transactionOnly?: boolean;
}

const standard: readonly BpmnPropertySection[] = ["name", "appearance", "geometry"];
function shape(id: string, label: string, type: string, group: BpmnToolboxGroup, configuration: readonly BpmnPropertySection[], extra: Partial<BpmnToolboxShape> = {}): BpmnToolboxShape {
  return { id, label, type, group, width: 100, height: 80, placement: "flowScope", configuration: [...standard, ...configuration], ...extra };
}
const tasks = [
  ["task", "Task"], ["userTask", "User task"], ["serviceTask", "Service task"], ["manualTask", "Manual task"],
  ["scriptTask", "Script task"], ["businessRuleTask", "Business rule task"], ["sendTask", "Send task"], ["receiveTask", "Receive task"]
] as const;
const events: BpmnToolboxShape[] = [];
function event(type: string, label: string, definitions: readonly string[], options: Partial<BpmnToolboxShape> = {}) {
  const kinds = definitions.map(value => value.replace("EventDefinition", ""));
  const id = [type, ...kinds, options.eventSubprocessOnly ? "event-body" : "", options.attributes?.cancelActivity === "false" || options.attributes?.isInterrupting === "false" ? "non-interrupting" : "",
    options.attributes?.parallelMultiple === "true" ? "parallel" : ""].filter(Boolean).join("-");
  events.push(shape(id, label, type, "Events", ["event", "routing"], { width: 36, height: 36, eventDefinitions: definitions, ...options }));
}
const definition = (kind: string) => `${kind}EventDefinition`;
const eventNames: Readonly<Record<string, string>> = { message: "Message", timer: "Timer", conditional: "Conditional", signal: "Signal", error: "Error", escalation: "Escalation", compensate: "Compensation", cancel: "Cancel", link: "Link", terminate: "Terminate" };
event("startEvent", "Start event", []);
event("intermediateThrowEvent", "Intermediate event", []);
event("endEvent", "End event", []);
for (const kind of ["message", "timer", "conditional", "signal"]) event("startEvent", `${eventNames[kind]} start`, [definition(kind)]);
for (const kind of ["message", "timer", "conditional", "signal", "link"]) event("intermediateCatchEvent", `${eventNames[kind]} catch`, [definition(kind)]);
for (const kind of ["message", "escalation", "signal", "compensate", "link"]) event("intermediateThrowEvent", `${eventNames[kind]} throw`, [definition(kind)]);
for (const kind of ["message", "escalation", "error", "cancel", "compensate", "signal", "terminate"])
  event("endEvent", `${eventNames[kind]} end`, [definition(kind)], { transactionOnly: kind === "cancel" });
for (const kind of ["message", "timer", "escalation", "conditional", "error", "cancel", "signal", "compensate"])
  event("boundaryEvent", `${eventNames[kind]} boundary`, [definition(kind)], { placement: "activityBoundary", transactionOnly: kind === "cancel", attributes: { cancelActivity: "true" } });
for (const kind of ["message", "timer", "escalation", "conditional", "signal"])
  event("boundaryEvent", `${eventNames[kind]} boundary (non-interrupting)`, [definition(kind)], { placement: "activityBoundary", attributes: { cancelActivity: "false" } });
for (const kind of ["message", "timer", "conditional", "signal", "error", "escalation", "compensate"])
  event("startEvent", `${eventNames[kind]} event-body start`, [definition(kind)], { eventSubprocessOnly: true, attributes: { isInterrupting: "true" } });
for (const kind of ["message", "timer", "conditional", "signal", "escalation"])
  event("startEvent", `${eventNames[kind]} event-body start (non-interrupting)`, [definition(kind)], { eventSubprocessOnly: true, attributes: { isInterrupting: "false" } });
for (const type of ["startEvent", "intermediateCatchEvent", "boundaryEvent", "intermediateThrowEvent", "endEvent"]) {
  event(type, `Multiple ${type.replace(/Event$/, "").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase()} event`, [definition("message"), definition("signal")],
    { placement: type === "boundaryEvent" ? "activityBoundary" : "flowScope" });
  if (["startEvent", "intermediateCatchEvent", "boundaryEvent"].includes(type))
    event(type, `Parallel multiple ${type.replace(/Event$/, "").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase()} event`, [definition("message"), definition("signal")],
      { placement: type === "boundaryEvent" ? "activityBoundary" : "flowScope", attributes: { parallelMultiple: "true" } });
}
for (const parallel of [false, true]) for (const interrupting of [true, false]) event("startEvent", `${parallel ? "Parallel multiple" : "Multiple"} event-body start${interrupting ? "" : " (non-interrupting)"}`, [definition("message"), definition("signal")],
  { eventSubprocessOnly: true, attributes: { isInterrupting: String(interrupting), ...(parallel ? { parallelMultiple: "true" } : {}) } });

/** Independent standard facts checked against pinned bpmn-js18.31.1 create/replace families; no optional runtime import. */
export const bpmnDocumentToolboxShapes: readonly BpmnToolboxShape[] = [
  ...tasks.map(([type, label]) => shape(type, label, type, "Tasks", ["routing", "loop", ...(type === "scriptTask" ? ["script" as const] : [])])),
  shape("callActivity", "Call activity", "callActivity", "Tasks", ["call", "loop"]),
  ...[["exclusiveGateway", "Exclusive gateway"], ["inclusiveGateway", "Inclusive gateway"], ["parallelGateway", "Parallel gateway"],
    ["complexGateway", "Complex gateway"], ["eventBasedGateway", "Event-based gateway"]].map(([type, label]) => shape(type, label, type, "Gateways", ["routing"], { width: 50, height: 50 })),
  ...events,
  shape("subProcess-collapsed", "Subprocess (collapsed)", "subProcess", "Bodies", ["body", "loop"], { expanded: false }),
  shape("subProcess-expanded", "Subprocess (expanded)", "subProcess", "Bodies", ["body", "loop"], { expanded: true, width: 300, height: 180 }),
  shape("eventSubProcess", "Event subprocess", "subProcess", "Bodies", ["body"], { attributes: { triggeredByEvent: "true" }, expanded: true, width: 300, height: 180 }),
  shape("transaction", "Transaction", "transaction", "Bodies", ["body", "loop"], { expanded: true, width: 300, height: 180 }),
  shape("adHocSubProcess-collapsed", "Ad-hoc subprocess (collapsed)", "adHocSubProcess", "Bodies", ["body", "loop"], { expanded: false }),
  shape("adHocSubProcess-expanded", "Ad-hoc subprocess (expanded)", "adHocSubProcess", "Bodies", ["body", "loop"], { expanded: true, width: 300, height: 180 }),
  shape("participant-expanded", "Pool with process", "participant", "Collaboration", ["participant"], { placement: "collaboration", expanded: true, width: 600, height: 250 }),
  shape("participant-collapsed", "Empty pool", "participant", "Collaboration", ["participant"], { placement: "collaboration", expanded: false, width: 600, height: 80 }),
  shape("lane", "Lane", "lane", "Collaboration", ["lane"], { placement: "laneContainer", width: 570, height: 120 }),
  shape("dataObjectReference", "Data object", "dataObjectReference", "Data", ["data"], { width: 50, height: 64 }),
  shape("dataStoreReference", "Data store", "dataStoreReference", "Data", ["data"], { width: 50, height: 50 }),
  shape("dataInput", "Data input", "dataInput", "Data", ["data"], { width: 50, height: 64 }),
  shape("dataOutput", "Data output", "dataOutput", "Data", ["data"], { width: 50, height: 64 }),
  shape("textAnnotation", "Text annotation", "textAnnotation", "Artifacts", ["text"], { width: 140, height: 70 }),
  shape("group", "Group", "group", "Artifacts", ["category"], { width: 240, height: 160 })
];

export const bpmnDocumentToolboxConnections: readonly { id: BpmnDocumentConnectionKind; label: string; semanticType: string; configuration: readonly BpmnPropertySection[] }[] = [
  { id: "sequenceFlow", label: "Sequence flow", semanticType: "sequenceFlow", configuration: ["name", "routing", "geometry", "appearance"] },
  { id: "messageFlow", label: "Message flow", semanticType: "messageFlow", configuration: ["name", "event", "geometry", "appearance"] },
  { id: "dataInputAssociation", label: "Data input association", semanticType: "dataInputAssociation", configuration: ["data", "geometry", "appearance"] },
  { id: "dataOutputAssociation", label: "Data output association", semanticType: "dataOutputAssociation", configuration: ["data", "geometry", "appearance"] },
  { id: "association", label: "Association", semanticType: "association", configuration: ["geometry", "appearance"] },
  { id: "compensationAssociation", label: "Compensation association", semanticType: "association", configuration: ["geometry", "appearance"] }
];

export function bpmnToolboxNotation(entry: BpmnToolboxShape): BpmnNotationDescriptor {
  return { type: entry.type, width: entry.width, height: entry.height, eventDefinitions: entry.eventDefinitions, isExpanded: entry.expanded,
    eventSubProcess: entry.attributes?.triggeredByEvent === "true", interrupting: entry.attributes?.isInterrupting !== "false",
    cancelActivity: entry.attributes?.cancelActivity !== "false", parallelMultiple: entry.attributes?.parallelMultiple === "true" };
}

/** Construct/configuration/DI and operation matrix, used by the integrated controls rather than persisted. */
export const bpmnDocumentToolboxMatrix = {
  shapes: bpmnDocumentToolboxShapes,
  connections: bpmnDocumentToolboxConnections,
  operations: ["create", "configure", "replace", "delete", "move", "resize", "reparent", "reconnect", "label", "route"] as const,
  nativeConfiguration: "existing Elsa inspector and exact native Slots",
  standardBodyAuthority: "documentXml",
  geometryAuthority: "BPMNDI",
  executionAdmission: "separate module-owned capability assessment"
};

/** Delivery map, not an assertion that an unenabled planner has composed-host interaction acceptance. */
export const bpmnDocumentOperationCoverage = [
  { operation: "create", source: "standard XML plus actual DI", controls: "palette choice; canvas placement/attachment integration pending" },
  { operation: "configure", source: "exact attributes/text/idless properties; routing/loops/appearance", controls: "controlled shared inspector foundation; native configuration supplied by host" },
  { operation: "replace", source: "same-family task/gateway/event/body; explicit event configuration replacement", controls: "cross-role incident and ownership transforms pending" },
  { operation: "delete", source: "syntax/incident refs/DI; native cleanup requirement returned before mutation", controls: "host native cleanup and pool process-retention/active-scope decisions pending" },
  { operation: "move", source: "fractional containment and incident geometry plans", controls: "canvas foundation; reparent gesture pending" },
  { operation: "resize", source: "pinned native resize policy; exact source DI commands", controls: "canvas foundation; full container interaction pending" },
  { operation: "reparent", source: "atomic namespace/context-preserving XML command", controls: "ownership, lane membership and scope-transition planner pending" },
  { operation: "reconnect", source: "typed ordinary/data refs; real I/O; actual relationship/DI IDs retained", controls: "connection gestures and cross-scope containment transform pending" },
  { operation: "label", source: "semantic/category/annotation text plus per-instance DI label bounds", controls: "inspector foundation; direct label gesture pending" },
  { operation: "route", source: "exact waypoint source mappings and retained vertex metadata", controls: "bend handles and routing UI pending" }
] as const;
