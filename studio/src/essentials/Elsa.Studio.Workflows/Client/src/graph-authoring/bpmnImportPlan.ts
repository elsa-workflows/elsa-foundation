import type { ActivityNode } from "../workflowTypes";
import { getChildSlots } from "../workflowAdapter";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { assertLosslessBpmnImport, layoutFromBpmnDiagram, presentationFromBpmn, type BpmnImportResult } from "../api/bpmnInterchange";
import { nativeBlockPlanning, type NativeBlockContext, type NativeBlockPlan } from "./nativeBlocks";
import { readBpmnBlockScope } from "./bpmnBlockRegion";

const { guarded, prepareDocument, edited, ready, refuse } = nativeBlockPlanning;
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);

/** Replaces only the current native body. The resource's activity identity, contract and invocation remain owned here. */
export function planBpmnBodyImport(context: NativeBlockContext, result: BpmnImportResult): NativeBlockPlan {
  return guarded(() => {
    const { owner } = prepareDocument(context);
    readBpmnBlockScope(owner, refuse);
    readBpmnBlockScope(result.processNode, refuse);
    try { assertLosslessBpmnImport(result.analysis); }
    catch (error) { refuse("lossy-bpmn-import", "analysis", (error as Error).message); }
    if (result.analysis.hostInventoryVerified !== true || result.analysis.canExecute !== true
      || !result.analysis.capabilities || result.analysis.capabilities.some(item => ![1, "available"].includes(typeof item.availability === "string" ? item.availability.toLowerCase() : item.availability)))
      refuse("unverified-bpmn-import", "analysis", "Resolve every binding against this host's catalog before replacing the body.");

    const oldIds = new Set<string>();
    const outsideIds = new Set<string>();
    const importedIds = new Set<string>([owner.nodeId]);
    const collect = (node: ActivityNode, ids: Set<string>, strict = false, includeNode = true) => {
      const add = (id: unknown) => {
        if (typeof id !== "string" || !id.trim() || strict && ids.has(id))
          refuse("duplicate-import-identity", "processNode", "Imported activity, element and routing identities must be nonempty and unique.");
        ids.add(id as string);
      };
      if (includeNode) add(node.nodeId);
      if (strict && includeNode) {
        const versions = context.catalog.filter(item => item.activityVersionId === node.activityVersionId && item.available !== false);
        if (versions.length !== 1) refuse("unavailable-import-version", `nodes.${node.nodeId}`, "An imported exact activity version is no longer available in this editor's catalog.");
      }
      if (node.structure?.kind === bpmnStructureKind) {
        const scope = readBpmnBlockScope(node, refuse);
        for (const element of scope.elements) add(element.elementId);
        for (const flow of scope.flows) add(flow.flowId);
        for (const [field, key] of [["lanes", "laneId"], ["pools", "poolId"]])
          if (Array.isArray(scope.payload[field])) for (const item of scope.payload[field]) add(record(item) ? item[key] : undefined);
      } else if (strict && node.structure != null)
        refuse("unsupported-import-structure", `nodes.${node.nodeId}`, "Only native BPMN bodies and catalog-bound actions can replace this body.");
      for (const slot of getChildSlots(node, context.catalog)) for (const child of slot.activities) collect(child, ids, strict);
    };
    collect(owner, oldIds, false, false);
    const collectOutside = (node: ActivityNode) => {
      if (node === owner) { outsideIds.add(node.nodeId); return; }
      // Collect this level without its children, then respect the replaced-body boundary below.
      outsideIds.add(node.nodeId);
      if (node.structure?.kind === bpmnStructureKind) {
        const scope = readBpmnBlockScope(node, refuse);
        scope.elements.forEach(element => outsideIds.add(element.elementId));
        scope.flows.forEach(flow => outsideIds.add(flow.flowId));
        for (const [field, key] of [["lanes", "laneId"], ["pools", "poolId"]])
          if (Array.isArray(scope.payload[field])) for (const item of scope.payload[field]) if (record(item) && typeof item[key] === "string") outsideIds.add(item[key]);
      }
      for (const slot of getChildSlots(node, context.catalog)) slot.activities.forEach(collectOutside);
    };
    collectOutside(context.document.rootActivity);
    for (const item of [...context.document.layout, ...context.document.activityPresentation]) if (!oldIds.has(item.nodeId)) outsideIds.add(item.nodeId);
    collect(result.processNode, importedIds, true, false);
    for (const id of importedIds) if (id !== owner.nodeId && outsideIds.has(id))
      refuse("occupied-import-identity", `identities.${id}`, "An imported identity belongs to another part of this resource. Keep the original file and correct its identities.");

    const checkReferences = (value: unknown, path: string, opaque = false) => {
      if (Array.isArray(value)) { value.forEach((item, index) => checkReferences(item, `${path}[${index}]`, opaque)); return; }
      if (record(value)) {
        const literal = String(value.expressionType ?? value.syntax ?? "").toLowerCase() === "literal";
        for (const [key, child] of Object.entries(value)) {
          if (value === owner && key === "structure") continue;
          if (literal && key === "value" && (child === null || typeof child !== "object")) continue;
          checkReferences(child, `${path}.${key}`, opaque || /expression|script|code/i.test(key) || typeof value.expressionType === "string" && !literal);
        }
      } else if (typeof value === "string" && [...oldIds].some(id => value === id || opaque && value.includes(id)))
        refuse("referenced-import-body", path, "A retained part of this resource refers to the body being replaced. Remove that reference or keep the current body.");
    };
    checkReferences(context.document.rootActivity, "rootActivity");
    checkReferences(context.referenceContext, "referenceContext");
    const nextOwner = { ...owner, structure: structuredClone(result.processNode.structure) };
    const layout = [...context.document.layout.filter(item => !oldIds.has(item.nodeId)), ...layoutFromBpmnDiagram(result.processNode)];
    const presentation = [...context.document.activityPresentation.filter(item => !oldIds.has(item.nodeId)), ...presentationFromBpmn(result.processNode)];
    return ready(context, edited(context, nextOwner, layout, presentation), []);
  });
}
