import type { ActivityNode } from "../workflowTypes";
import { bpmnNamespaces as ns, resolveBpmnDocumentReference, type BpmnDocumentCommand, type BpmnDocumentConfigurationChange, type BpmnScopeAddress } from "./bpmnDocument";
import { bpmnIdentity, bpmnOperationContext, isBpmnOwnedSyntax, refuseBpmnOperation } from "./bpmnDocumentOperationXml";
import { prepareBpmnDeleteCore, type BpmnDeletePreparation, type BpmnNativeCleanupRequirement } from "./bpmnDocumentTransformOperations";

export interface BpmnPoolDeleteDecision {
  /** Default false: remove the participant while retaining its process, native configuration and DI. */
  deleteProcess?: boolean;
  nextScope?: BpmnScopeAddress;
  nextProcessId?: string;
  configuration?: BpmnDocumentConfigurationChange;
}
export interface BpmnPoolTransitionRequirement {
  nextScope: BpmnScopeAddress;
  selectedProcess?: { from: string; requested?: string; available: string[]; ownerConfigurationRequired: true };
  cleanup: BpmnNativeCleanupRequirement;
}
export type BpmnPoolDeletePreparation = (BpmnDeletePreparation & { retainedProcessId?: string; deletedProcessId?: string })
  | { kind: "requiresHostTransition"; commands: readonly BpmnDocumentCommand[]; transition: BpmnPoolTransitionRequirement;
    retainedProcessId?: string; deletedProcessId?: string; reason: string };

/** Pure decision preparation; no implicit deletion of process content or selected-owner configuration. */
export function prepareBpmnPoolDelete(owner: ActivityNode, scope: BpmnScopeAddress, poolId: string,
  decision: BpmnPoolDeleteDecision = {}): BpmnPoolDeletePreparation {
  const context = bpmnOperationContext(owner, scope), pool = context.parsed.byId.get(poolId);
  if (!pool || pool.namespaceURI !== ns.model || pool.localName !== "participant" || !isBpmnOwnedSyntax(pool)
    || pool.parentElement?.localName !== "collaboration") refuseBpmnOperation("choose an actual standard pool");
  const collaboration = pool.parentElement;
  if (resolveBpmnDocumentReference(context.parsed, context.plane, context.plane.getAttribute("bpmnElement")) !== collaboration
    || !context.scene.shapes.some(shape => shape.elementId === poolId)) refuseBpmnOperation("the pool must have actual DI in this collaboration plane");
  const process = resolveBpmnDocumentReference(context.parsed, pool, pool.getAttribute("processRef"));
  if (pool.hasAttribute("processRef") && (process?.namespaceURI !== ns.model || process.localName !== "process")) refuseBpmnOperation("repair the pool's actual process reference before deletion");
  const deleting = decision.deleteProcess === true && process !== undefined;
  if (deleting && [...context.parsed.document.getElementsByTagNameNS(ns.model, "participant")].some(other => other !== pool && isBpmnOwnedSyntax(other)
    && resolveBpmnDocumentReference(context.parsed, other, other.getAttribute("processRef")) === process)) refuseBpmnOperation("a shared process must be retained while another participant references it");
  const deleted = (element: Element) => deleting && (process === element || process!.contains(element));
  const bindings = context.parsed.payload.bindings.filter(binding => deleted(context.parsed.byId.get(binding.elementId)!));
  const headers = (context.parsed.payload.scopeHeaders ?? []).filter(header => deleted(context.parsed.byId.get(header.scopeElementId)!));
  const cleanup: BpmnNativeCleanupRequirement = { bindingElementIds: bindings.map(binding => binding.elementId),
    activityRootIds: bindings.flatMap(binding => [binding.childNodeId, binding.listenerNodeId]).filter((id): id is string => typeof id === "string"), headerScopeIds: headers.map(header => header.scopeElementId) };
  const collaborationScope = { ...scope, scopeElementId: bpmnIdentity(collaboration) };
  const nextScope = decision.nextScope ?? collaborationScope;
  if (nextScope.documentOwnerNodeId !== scope.documentOwnerNodeId || deleted(context.parsed.byId.get(nextScope.scopeElementId)!)
    || nextScope.scopeElementId === poolId) refuseBpmnOperation("navigation must retain an actual scope outside the deleted process");
  bpmnOperationContext(owner, nextScope);
  const selected = deleting && process === context.parsed.process;
  const alternatives = [...context.parsed.document.documentElement.children].filter(element => element.namespaceURI === ns.model
    && element.localName === "process" && element !== process).map(bpmnIdentity);
  if (decision.nextProcessId && (!selected || !alternatives.includes(decision.nextProcessId))) refuseBpmnOperation("choose an actual retained replacement process");
  const transition: BpmnPoolTransitionRequirement = { nextScope, cleanup, ...(selected ? {
    selectedProcess: { from: bpmnIdentity(process!), requested: decision.nextProcessId, available: alternatives, ownerConfigurationRequired: true as const }
  } : {}) };
  const receipt = { ...(deleting ? { deletedProcessId: bpmnIdentity(process!) } : process ? { retainedProcessId: bpmnIdentity(process) } : {}) };
  if (selected && !alternatives.length) return { kind: "requiresHostTransition", commands: [], transition, ...receipt,
    reason: "The resource host must create/select a replacement process and preserve its exact invocation configuration before preparing this deletion." };
  // A syntax-only candidate checks selected-process deletion. It is never returned as an admitted owner.
  const planningOwner = selected ? { ...owner, structure: { ...owner.structure!, payload: { ...owner.structure!.payload,
    processId: decision.nextProcessId ?? alternatives[0], activities: [], bindings: [], scopeHeaders: [] } } } : owner;
  const prepared = prepareBpmnDeleteCore(planningOwner, collaborationScope, [poolId, ...(deleting ? [bpmnIdentity(process!)] : [])],
    selected ? undefined : decision.configuration, true);
  const commands = prepared.kind === "ready" ? prepared.plan.transaction.commands ?? [] : prepared.commands;
  if (selected || scope.scopeElementId !== bpmnIdentity(collaboration) && !decision.nextScope) return {
    kind: "requiresHostTransition", commands, transition, ...receipt,
    reason: "The host must admit navigation, selected-process invocation configuration and native cleanup together; apply these commands only to that accepted owner." };
  if (prepared.kind === "ready") prepared.plan.nextScope = nextScope;
  return { ...prepared, ...receipt };
}
