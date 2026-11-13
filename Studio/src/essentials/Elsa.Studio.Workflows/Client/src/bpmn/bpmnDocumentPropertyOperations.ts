import type { ActivityNode } from "../workflowTypes";
import { bpmnNamespaces as ns, resolveBpmnDocumentReference, type BpmnBounds, type BpmnDocumentCommand, type BpmnDocumentSelection, type BpmnScopeAddress, type BpmnXmlAddress } from "./bpmnDocument";
import { bpmnAppearanceNamespaces, isBpmnAppearanceColor, type BpmnLabelFont } from "./bpmnDocumentAppearance";
import { bpmnBoolean } from "./bpmnDocumentPolicy";
import { isActivityBearingElementType } from "./bpmnNotationTypes";
import { bpmnAttributePrefix, bpmnChildren, bpmnFragment, bpmnOperationContext, bpmnSourceAddress, cloneBpmnPropertyFragment, insertBpmnFragment, isBpmnOwnedSyntax, refuseBpmnOperation, resolveBpmnOperationAddress } from "./bpmnDocumentOperationXml";
import type { BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";

export type BpmnStandardPropertyIntent =
  | { kind: "attribute"; target: BpmnXmlAddress; name: string; namespaceURI?: string; value: string | null }
  | { kind: "text"; target: BpmnXmlAddress; value: string }
  | { kind: "appendProperty"; parent: BpmnXmlAddress; xml: string }
  | { kind: "removeProperty"; target: BpmnXmlAddress };

const structuralTypes = new Set(["definitions", "process", "collaboration", "participant", "laneSet", "childLaneSet", "lane", "task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask", "callActivity", "subProcess", "transaction", "adHocSubProcess", "startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent", "exclusiveGateway", "inclusiveGateway", "parallelGateway", "eventBasedGateway", "complexGateway", "sequenceFlow", "messageFlow", "dataInputAssociation", "dataOutputAssociation", "association", "dataObject", "dataObjectReference", "dataStoreReference", "dataInput", "dataOutput", "textAnnotation", "group"]);
const identityAttributes = new Set(["id", "sourceRef", "targetRef", "attachedToRef", "default", "processRef", "bpmnElement"]);
export function canRemoveBpmnStandardProperty(node: Pick<BpmnStandardPropertyNode, "namespaceURI" | "type">) { return node.namespaceURI === ns.model && !structuralTypes.has(node.type); }

/** Exact primitive XML property editing, including idless members. Structural/reference operations have typed planners. */
export function planBpmnStandardProperty(owner: ActivityNode, scope: BpmnScopeAddress, intent: BpmnStandardPropertyIntent): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope);
  const address = intent.kind === "appendProperty" ? intent.parent : intent.target, element = resolveBpmnOperationAddress(context, address);
  let command: BpmnDocumentCommand; const declarations: BpmnDocumentCommand[] = [];
  if (intent.kind === "attribute") {
    if ((!intent.namespaceURI && identityAttributes.has(intent.name)) || intent.name === "xmlns" || intent.name.startsWith("xmlns:")) refuseBpmnOperation("identity, containment and connection references use their dedicated operation");
    command = { ...intent };
    if (intent.namespaceURI && intent.value !== null) {
      const prefix = bpmnAttributePrefix(context, element, intent.namespaceURI, intent.name.includes(":") ? intent.name.split(":")[0] : "attributeNs", declarations);
      command = { ...intent, name: `${prefix}:${intent.name.split(":").at(-1)!}` };
    }
  } else if (intent.kind === "text") {
    if (element.namespaceURI !== ns.model || structuralTypes.has(element.localName)) refuseBpmnOperation("edit the actual text property rather than replacing a construct's content");
    if (element.children.length) refuseBpmnOperation("editing this mixed-content property would discard retained child configuration");
    command = { ...intent };
  } else if (intent.kind === "removeProperty") {
    if (element.namespaceURI !== ns.model || structuralTypes.has(element.localName)) refuseBpmnOperation("deleting a construct requires its ownership-aware delete operation");
    command = { kind: "remove", target: intent.target };
  } else {
    if (/<!\s*(?:DOCTYPE|ENTITY)\b/i.test(intent.xml)) refuseBpmnOperation("DTD and entity declarations are not supported");
    const fragment = new DOMParser().parseFromString(intent.xml, "application/xml").documentElement;
    if (fragment.namespaceURI !== ns.model || structuralTypes.has(fragment.localName) || fragment.localName === "parsererror") refuseBpmnOperation("add a standard configuration member here; constructs use the toolbox");
    command = insertBpmnFragment(context, element, fragment);
  }
  return { transaction: { commands: [...declarations, command] }, selection: null };
}

export function planBpmnDefaultFlow(owner: ActivityNode, scope: BpmnScopeAddress, sourceId: string, flowId: string | null): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), source = resolveBpmnOperationAddress(context, { elementId: sourceId });
  if (!isBpmnOwnedSyntax(source) || !["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask", "callActivity", "subProcess", "transaction", "adHocSubProcess", "exclusiveGateway", "inclusiveGateway", "complexGateway"].includes(source.localName)) refuseBpmnOperation("this notation does not own a default sequence flow");
  if (flowId !== null) {
    const flow = context.parsed.byId.get(flowId);
    if (flow?.namespaceURI !== ns.model || flow.localName !== "sequenceFlow" || resolveBpmnDocumentReference(context.parsed, flow, flow.getAttribute("sourceRef")) !== source) refuseBpmnOperation("the default must be an actual outgoing sequence flow");
  }
  return { transaction: { commands: [{ kind: "attribute", target: { elementId: sourceId }, name: "default", value: flowId }] }, selection: { elementId: sourceId } };
}

export function planBpmnCondition(owner: ActivityNode, scope: BpmnScopeAddress, flowId: string, value: string | null, language?: string): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), flow = resolveBpmnOperationAddress(context, { elementId: flowId });
  if (!isBpmnOwnedSyntax(flow) || flow.namespaceURI !== ns.model || flow.localName !== "sequenceFlow") refuseBpmnOperation("a sequence-flow condition needs its actual flow");
  const conditions = bpmnChildren(flow, "conditionExpression");
  if (conditions.length > 1) refuseBpmnOperation("choose the actual condition before editing an ambiguous draft");
  const condition = conditions[0], commands: BpmnDocumentCommand[] = [];
  if (value === null) { if (condition) commands.push({ kind: "remove", target: bpmnSourceAddress(condition, context.parsed) }); }
  else if (condition) {
    if (condition.children.length) refuseBpmnOperation("the condition has retained structured content; edit its exact properties");
    commands.push({ kind: "text", target: bpmnSourceAddress(condition, context.parsed), value });
    if (language !== undefined) {
      const xsi = "http://www.w3.org/2001/XMLSchema-instance", currentType = condition.getAttributeNS(xsi, "type");
      if (language && currentType && !(currentType.split(":").at(-1) === "tFormalExpression" && condition.lookupNamespaceURI(currentType.includes(":") ? currentType.split(":")[0] : null) === ns.model)) refuseBpmnOperation("the condition has a different explicit expression type");
      if (language && !currentType) commands.push({ kind: "attribute", target: bpmnSourceAddress(condition, context.parsed), namespaceURI: xsi, name: `${bpmnAttributePrefix(context, condition, xsi, "xsiNs", commands)}:type`, value: `${condition.prefix ? `${condition.prefix}:` : ""}tFormalExpression` });
      commands.push({ kind: "attribute", target: bpmnSourceAddress(condition, context.parsed), name: "language", value: language || null });
    }
  } else {
    const created = bpmnFragment(context, "conditionExpression", language ? { language } : {}); created.textContent = value;
    if (language) created.setAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "xsi:type", `${created.prefix ? `${created.prefix}:` : ""}tFormalExpression`);
    commands.push(insertBpmnFragment(context, flow, created));
  }
  return { transaction: { commands }, selection: { elementId: flowId } };
}

export type BpmnLoopMode = "none" | "standard" | "parallel" | "sequential";
export function planBpmnLoop(owner: ActivityNode, scope: BpmnScopeAddress, elementId: string, mode: BpmnLoopMode, replaceConfiguredLoop = false): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), element = resolveBpmnOperationAddress(context, { elementId });
  if (!isBpmnOwnedSyntax(element) || !isActivityBearingElementType(element.localName) || bpmnBoolean(element, "triggeredByEvent")) refuseBpmnOperation("this notation does not support activity loop characteristics");
  const loops = [...element.children].filter(child => child.namespaceURI === ns.model && ["standardLoopCharacteristics", "multiInstanceLoopCharacteristics"].includes(child.localName));
  if (loops.length > 1) refuseBpmnOperation("the draft has multiple loop authorities");
  const desired = mode === "none" ? null : mode === "standard" ? "standardLoopCharacteristics" : "multiInstanceLoopCharacteristics", old = loops[0];
  if (old && old.localName === desired) return { transaction: { commands: mode === "standard" ? [] : [{ kind: "attribute", target: bpmnSourceAddress(old, context.parsed), name: "isSequential", value: String(mode === "sequential") }] }, selection: { elementId } };
  if (old && !replaceConfiguredLoop) refuseBpmnOperation("replacing this loop requires an explicit configuration replacement choice");
  const commands: BpmnDocumentCommand[] = old ? [{ kind: "remove", target: bpmnSourceAddress(old, context.parsed) }] : [];
  if (desired) commands.push(insertBpmnFragment(context, element, bpmnFragment(context, desired, mode === "standard" ? {} : { isSequential: String(mode === "sequential") })));
  return { transaction: { commands }, selection: { elementId } };
}

export function planBpmnLabel(owner: ActivityNode, scope: BpmnScopeAddress, selection: BpmnDocumentSelection, value: string, labelBounds?: BpmnBounds | null): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), element = resolveBpmnOperationAddress(context, { elementId: selection.elementId }), commands: BpmnDocumentCommand[] = [];
  if (["association", "dataInputAssociation", "dataOutputAssociation"].includes(element.localName)) refuseBpmnOperation("this relationship has no standard name label");
  if (element.localName === "textAnnotation") {
    const texts = bpmnChildren(element, "text");
    if (texts.length > 1) refuseBpmnOperation("choose the actual annotation text before editing an ambiguous draft");
    if (texts[0]) { if (texts[0].children.length) refuseBpmnOperation("the annotation contains retained markup; edit its exact text member"); commands.push({ kind: "text", target: bpmnSourceAddress(texts[0], context.parsed), value }); }
    else { const text = bpmnFragment(context, "text"); text.textContent = value; commands.push(insertBpmnFragment(context, element, text)); }
  } else if (element.localName === "group") {
    const category = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("categoryValueRef"));
    if (!category || category.namespaceURI !== ns.model || category.localName !== "categoryValue") refuseBpmnOperation("the group needs its actual category-value label");
    commands.push({ kind: "attribute", target: bpmnSourceAddress(category, context.parsed), name: "value", value });
  } else commands.push({ kind: "attribute", target: { elementId: selection.elementId }, name: "name", value });
  if (labelBounds !== undefined) {
    const di = selection.diId && context.parsed.byId.get(selection.diId);
    if (!di || di.parentElement !== context.plane || resolveBpmnDocumentReference(context.parsed, di, di.getAttribute("bpmnElement")) !== element) refuseBpmnOperation("label geometry needs the actual selected DI instance");
    commands.push({ kind: "labelBounds", diId: selection.diId!, bounds: labelBounds });
  }
  return { transaction: { commands }, selection };
}

export interface BpmnAppearanceIntent { fill?: string | null; stroke?: string | null; labelColor?: string | null; font?: BpmnLabelFont }
export function planBpmnAppearance(owner: ActivityNode, scope: BpmnScopeAddress, diId: string, intent: BpmnAppearanceIntent): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), di = resolveBpmnOperationAddress(context, { elementId: diId });
  if (di.parentElement !== context.plane || di.namespaceURI !== ns.diagram || !["BPMNShape", "BPMNEdge"].includes(di.localName)) refuseBpmnOperation("appearance belongs to an actual DI instance in this plane");
  const commands: BpmnDocumentCommand[] = [];
  const color = (target: BpmnXmlAddress, element: Element, name: string, value: string | null) => {
    if (value !== null && !isBpmnAppearanceColor(value)) refuseBpmnOperation("choose a supported literal color");
    const prefix = value === null ? element.lookupPrefix(bpmnAppearanceNamespaces.color) ?? "colorNs" : bpmnAttributePrefix(context, element, bpmnAppearanceNamespaces.color, "colorNs", commands);
    commands.push({ kind: "attribute", target, namespaceURI: bpmnAppearanceNamespaces.color, name: `${prefix}:${name}`, value });
    if (value === null && ["background-color", "border-color"].includes(name)) commands.push({ kind: "attribute", target, namespaceURI: bpmnAppearanceNamespaces.legacyColor, name: name === "background-color" ? "fill" : "stroke", value: null });
  };
  if (intent.fill !== undefined) { if (di.localName !== "BPMNShape") refuseBpmnOperation("a connection has no background fill"); color({ elementId: diId }, di, "background-color", intent.fill); }
  if (intent.stroke !== undefined) color({ elementId: diId }, di, "border-color", intent.stroke);
  const labels = [...di.children].filter(child => child.namespaceURI === ns.diagram && child.localName === "BPMNLabel");
  if (intent.font || intent.labelColor !== undefined && (intent.labelColor !== null || labels.length)) {
    if (labels.length > 1) refuseBpmnOperation("label appearance needs one unambiguous label");
    const label = labels[0] ?? bpmnFragment(context, "BPMNLabel", {}, ns.diagram);
    if (!labels.length) {
      commands.push(insertBpmnFragment(context, di, label));
      di.append(label); // Only the private planning DOM advances; subsequent commands address this exact new label.
    }
    const labelAddress = bpmnSourceAddress(label, context.parsed);
    if (intent.labelColor !== undefined) color(labelAddress, label, "color", intent.labelColor);
    if (intent.font) {
      const font = intent.font;
      if (font.size !== undefined && (!Number.isFinite(font.size) || font.size < 0) || font.name !== undefined && (!font.name.trim() || [...font.name].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127))) refuseBpmnOperation("font name and size must be valid diagram values");
      // Create a private label style: editing this instance must not restyle other labels sharing the old style.
      const diagram = context.parsed.byId.get(scope.diagramId)!;
      const priorStyleId = label.getAttribute("labelStyle"), priorStyle = priorStyleId ? resolveBpmnDocumentReference(context.parsed, label, priorStyleId) : undefined;
      if (priorStyleId && (!priorStyle || priorStyle.namespaceURI !== ns.diagram || priorStyle.localName !== "BPMNLabelStyle")) refuseBpmnOperation("the current label style is unresolved; repair that actual reference before changing font");
      const styleId = context.allocate("LabelStyle"), style = priorStyle ? cloneBpmnPropertyFragment(priorStyle, diagram) : bpmnFragment(context, "BPMNLabelStyle", {}, ns.diagram);
      style.setAttribute("id", styleId);
      if ([...style.getElementsByTagName("*")].some(child => child.getAttribute("id") && context.parsed.byId.has(child.getAttribute("id")!))) refuseBpmnOperation("cloning this shared label style needs an explicit nested identity remap");
      const fonts = [...style.children].filter(child => child.namespaceURI === ns.bounds && child.localName === "Font");
      if (priorStyle && fonts.length !== 1) refuseBpmnOperation("the current label style needs one actual font");
      const attributes: Record<string, string> = {};
      for (const [key, name] of [["name", "name"], ["size", "size"], ["bold", "isBold"], ["italic", "isItalic"], ["underline", "isUnderline"], ["strikeThrough", "isStrikeThrough"]] as const) if (font[key] !== undefined) attributes[name] = String(font[key]);
      const nextFont = fonts[0] ?? bpmnFragment(context, "Font", {}, ns.bounds);
      for (const [name, value] of Object.entries(attributes)) nextFont.setAttribute(name, value);
      if (!fonts.length) style.append(nextFont);
      commands.push(insertBpmnFragment(context, diagram, style), { kind: "attribute", target: labelAddress, name: "labelStyle", value: styleId });
    }
  }
  return { transaction: { commands }, selection: { elementId: resolveBpmnDocumentReference(context.parsed, di, di.getAttribute("bpmnElement"))!.getAttribute("id")!, diId } };
}

/** The tree keeps real property addresses; attributes/text never become a synthetic ActivityNode. */
export interface BpmnStandardPropertyNode { address: BpmnXmlAddress; namespaceURI: string; type: string; attributes: { name: string; namespaceURI: string | null; value: string; readOnly: boolean }[]; text?: string; children: BpmnStandardPropertyNode[] }
export function readBpmnStandardProperties(owner: ActivityNode, scope: BpmnScopeAddress, selection: BpmnDocumentSelection): BpmnStandardPropertyNode {
  const context = bpmnOperationContext(owner, scope), root = resolveBpmnOperationAddress(context, selection.property ?? { elementId: selection.elementId });
  const visit = (element: Element): BpmnStandardPropertyNode => ({ address: bpmnSourceAddress(element, context.parsed), namespaceURI: element.namespaceURI ?? "", type: element.localName,
    attributes: [...element.attributes].filter(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/").map(attribute => ({ name: attribute.name, namespaceURI: attribute.namespaceURI, value: attribute.value, readOnly: !attribute.namespaceURI && identityAttributes.has(attribute.name) })),
    ...(!element.children.length && element.namespaceURI === ns.model && !structuralTypes.has(element.localName) ? { text: element.textContent ?? "" } : {}), children: [...element.children].map(visit) });
  return visit(root);
}
