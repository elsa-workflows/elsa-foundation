import { canonicalizeStateForWire } from "../activityInputWire";
import { activityGraphLayoutFromDesign } from "../activityGraphDocumentAdapter";
import { activatedStorageDrivers } from "../activityContractAuthoring";
import { getActivityGraphOutcomeMappingOptions } from "../activityGraphOutcomeMappingOptions";
import { getChildSlots, replaceSlotActivities } from "../workflowAdapter";
import type { ActivityCatalogItem, ActivityNode, CollectionKind } from "../workflowTypes";
import type { ActivityContract, ActivityTypeReference } from "../activityDefinitionTypes";
import type { NativeBlockDiagnostic } from "./nativeBlocks";
import { promotionBody, promotionNodes, insertPromotionSteps, orderedPromotionChildren } from "./promotionBody";
import type { PromotionNodeLocation } from "./promotionBody";
import { rewritePromotionArgument, promotionInputRead, promotionResultRead } from "./promotionBindings";
import type { PromotionReference } from "./promotionBindings";
import { PromotionRefusal, promotionRecord as record, refusePromotion as refuse } from "./blockPromotionTypes";
import type { BlockPromotionContext, BlockPromotionPlan, ReadyBlockPromotion, BlockPromotionBinding } from "./blockPromotionTypes";

type Fields = Record<string, unknown>;
const clone = <T,>(value: T): T => structuredClone(value);
const safeActivities = new Set([
  "Elsa.Activities.Sequence.Activities.Sequence", "Elsa.Activities.Flowchart.Activities.Flowchart", "Elsa.Activities.Bpmn.Activities.BpmnProcess",
  "Elsa.Activities.Do.Activities.Do", "Elsa.Activities.For.Activities.For", "Elsa.Activities.ForEach.Activities.ForEach", "Elsa.Activities.While.Activities.While",
  "Elsa.Activities.If.Activities.If", "Elsa.Activities.Switch.Activities.Switch", "Elsa.Activities.Parallel.Activities.Parallel",
  "Elsa.Activities.Primitives.Activities.WriteLine", "Elsa.Activities.Primitives.Activities.WriteLines", "Elsa.Activities.Primitives.Activities.Fault",
  "Elsa.Activities.Primitives.Activities.Inline", "Elsa.Activities.Primitives.Activities.ReadLine", "Elsa.Activities.Primitives.Activities.Event", "Elsa.Activities.Scheduling.Activities.Delay"
]);
const loopKinds = new Set(["elsa.do.structure", "elsa.for.structure", "elsa.foreach.structure", "elsa.while.structure"]);

export function blockPromotionSource(context: BlockPromotionContext) {
  return JSON.stringify([context.resourceKind, context.sourceKey, context.canEdit, context.scope, context.document, context.referenceContext,
    context.catalog, context.publicInputs, context.capabilities, context.expressionBoundary?.revision]);
}

function identity(plan: Omit<ReadyBlockPromotion, "candidateIdentity">) {
  return JSON.stringify([plan.source, plan.blockNodeId, plan.name, plan.candidate, plan.callSite, plan.rewrittenSource, plan.referenceContext, plan.inputs, plan.outputs]);
}

/** Analysis is detached and has no HTTP, persistence, publication or history side effects. */
export function planBlockPromotion(context: BlockPromotionContext, blockNodeId: string, requestedName?: string): BlockPromotionPlan {
  try { return analyze(context, blockNodeId, requestedName); }
  catch (error) { if (error instanceof PromotionRefusal) return { status: "refused", diagnostics: [error.diagnostic] }; throw error; }
}

function analyze(context: BlockPromotionContext, blockNodeId: string, requestedName?: string): ReadyBlockPromotion {
  if (!["workflow-definition", "activity-definition-graph"].includes(context.resourceKind))
    refuse("missing-resource-kind", "resource", "Reload the owning resource's reference contract before promotion.");
  if (!context.canEdit) refuse("read-only", "authority", "Open an editable draft before promoting a block.");
  const provider = context.capabilities.providers.find(item => item.providerKey === "elsa.activity-graph");
  if (!provider?.manifestSchemas.some(schema => schema.schemaVersion === "2" && schema.isAuthorable))
    refuse("graph-authoring-unavailable", "capabilities", "This host must support ordinary schema-2 reusable graph authoring.");
  if (!Array.isArray(context.referenceContext) || !context.capabilities.contractSchemaVersions[0])
    refuse("missing-context", "resource", "Reload the complete resource and authoring capabilities before promotion.");
  const document = clone(context.document);
  document.rootActivity = canonicalizeStateForWire({ rootActivity: document.rootActivity }).rootActivity!;
  const all = promotionNodes(document.rootActivity, context.catalog);
  const location = all.get(blockNodeId);
  if (!location) refuse("missing-block", "selection", "Select a current embedded block.");
  const root = clone(location.node);
  const body = promotionBody(root, context.catalog);
  const name = (requestedName ?? document.activityPresentation.find(item => item.nodeId === blockNodeId)?.displayName ?? "").trim();
  if (!name || name.length > 200) refuse("invalid-name", "name", "Give the reusable activity a name of at most 200 characters.");
  if (root.outputs.length) refuse("boundary-output-capture", `nodes.${blockNodeId}.outputs`, "Review this container's existing result captures before introducing a new reusable result contract.");
  const inputs: BlockPromotionBinding[] = [], outputs: BlockPromotionBinding[] = [];
  const contract: ActivityContract = { contractSchemaVersion: context.capabilities.contractSchemaVersions[0], inputs: [], outputs: [], outcomes: [] };
  const inputBySource = new Map<string, BlockPromotionBinding>(), outputBySource = new Map<string, BlockPromotionBinding>();
  const reserved = new Set([...all.keys(), ...document.layout.map(item => item.nodeId), ...document.activityPresentation.map(item => item.nodeId)]);
  // Structure identities can share the same canvas without being ActivityNodes.
  for (const { node } of all.values()) for (const value of Object.values(node.structure?.payload ?? {})) if (Array.isArray(value))
    for (const item of value) if (record(item)) for (const field of ["elementId", "flowId", "id"]) if (typeof item[field] === "string") reserved.add(item[field]);
  const allocate = (label: string) => { let index = 1; while (reserved.has(`${blockNodeId}-promotion-${label}-${index}`)) index++; const id = `${blockNodeId}-promotion-${label}-${index}`; reserved.add(id); return id; };
  const candidateLayout = clone(document.layout);
  const candidatePresentation = clone(document.activityPresentation);
  const argumentReference = (reference: PromotionReference) => {
    const { kind: _kind, ...value } = reference.authored;
    return value;
  };
  const captures = new Map<string, { owner: ActivityNode; afterId: string | null; steps: ActivityNode[] }>();
  const descriptor = (node: ActivityNode) => {
    const matches = context.catalog.filter(item => item.activityVersionId === node.activityVersionId && item.available !== false);
    if (matches.length !== 1) refuse("unavailable-activity", `nodes.${node.nodeId}`, "Every activity needs one exact available catalog version.");
    return matches[0];
  };
  const type = (value: unknown, path: string): ActivityTypeReference => {
    const source = record(value) ? value : {};
    const alias = typeof value === "string" ? value : source.alias;
    const collection = typeof source.collectionKind === "string" ? source.collectionKind : "Single";
    const entry = context.capabilities.types.find(item => typeof alias === "string" && item.alias.toLowerCase() === alias.toLowerCase());
    const collectionKind = entry?.supportedCollectionKinds.find(item => item.toLowerCase() === collection.toLowerCase());
    if (!entry?.supportsDurability || !collectionKind || !activatedStorageDrivers(entry, context.capabilities).length)
      refuse("unsupported-contract-type", path, "This value has no active durable reusable-contract type and storage driver.");
    return { alias: entry.alias, collectionKind };
  };
  const descriptorType = (item: Fields, path: string) => type(record(item.type) ? item.type
    : { alias: item.type ?? item.typeName, collectionKind: item.collectionKind ?? "Single" }, path);
  const driver = (value: ActivityTypeReference) => activatedStorageDrivers(context.capabilities.types.find(item => item.alias === value.alias), context.capabilities)[0];
  const publicInput = (reference: PromotionReference, path: string) => {
    let valueType: ActivityTypeReference;
    let label: string;
    let nullable = true;
    let sourceInputKey: string | undefined;
    if (reference.kind === "input") {
      const requestName = "memberKey" in reference.authored && context.resourceKind === "workflow-definition";
      const declaration = context.publicInputs.filter(item => (requestName ? item.name : item.referenceKey) === reference.key);
      if (declaration.length !== 1) refuse("unknown-input", path, "This read does not name one current public input declaration.");
      if (declaration[0].isRequired !== true && !record(declaration[0].default))
        refuse("unpreserved-input-availability", path, "This optional input may be absent until its original read. Use a required input or an ordinary input default before promotion.");
      valueType = type(declaration[0].type, path); label = declaration[0].name; nullable = declaration[0].isNullable === true;
      sourceInputKey = declaration[0].referenceKey;
    } else {
      const producer = all.get(reference.key);
      if (!producer || body.has(reference.key)) refuse("unknown-producer", path, "This external result producer is unavailable.");
      const order = location.parent && producer.parent === location.parent ? orderedPromotionChildren(location.parent, context.catalog) : null;
      if (!order || order.indexOf(reference.key) < 0 || order.indexOf(reference.key) >= order.indexOf(blockNodeId))
        refuse("unpreserved-result-timing", path, "This result is not guaranteed to exist before the new call-input snapshot.");
      const declaration = descriptor(producer.node).outputs.filter(item => record(item) && item.referenceKey === reference.projection);
      if (reference.projection !== "$result" && declaration.length !== 1) refuse("unknown-projection", path, "Choose an exact advertised result projection.");
      valueType = reference.projection === "$result" ? type("Elsa.Any", path) : descriptorType(declaration[0] as Fields, path);
      label = document.activityPresentation.find(item => item.nodeId === reference.key)?.displayName || `Result ${inputs.length + 1}`;
    }
    const key = JSON.stringify([reference.kind, reference.authored, valueType]);
    let binding = inputBySource.get(key);
    if (!binding) {
      const referenceKey = `input-${inputs.length + 1}`;
      const source = reference.kind === "input" ? { expressionType: "Input", value: { referenceKey: sourceInputKey } }
        : { expressionType: "ActivityResult", value: clone(argumentReference(reference)) };
      binding = { referenceKey, name: label, type: valueType, source, uses: [] };
      inputs.push(binding); inputBySource.set(key, binding);
      contract.inputs.push({ referenceKey, name: `Input${inputs.length}`, displayName: label, type: valueType, isRequired: true, isNullable: nullable,
        default: null, storageDriverKey: driver(valueType), durability: "Required" });
    }
    binding.uses.push(path);
    return binding.referenceKey;
  };
  const localDeclaration = (reference: PromotionReference, consumerId: string) => {
    let current: PromotionNodeLocation | undefined = body.get(consumerId);
    while (current) {
      const declarations = current.node.structure?.payload.variables;
      if ((!reference.scope || reference.scope === current.node.nodeId) && Array.isArray(declarations)
        && declarations.some(item => record(item) && item.referenceKey === reference.key)) return true;
      current = current.parent ? body.get(current.parent.nodeId) : undefined;
    }
    return false;
  };
  for (const { node, parent } of body.values()) {
    const entry = descriptor(node), at = `nodes.${node.nodeId}`;
    if (node.nodeId === "activity-graph" && Array.isArray(node.structure?.payload.variables) && node.structure.payload.variables.length)
      refuse("reserved-local-scope", at, "This local scope uses a reserved reusable identity. Keep the block embedded.");
    if (entry.activityTypeKey === "Elsa.Activities.Primitives.Activities.Event") {
      const startKey = entry.inputs.find(input => record(input) && input.name === "CanStartWorkflow") as Fields | undefined;
      const starts = startKey && node.inputs.find(input => record(input) && input.referenceKey === (startKey.referenceKey ?? startKey.name)) as Fields | undefined;
      const binding = starts && record(starts.value) ? starts.value : undefined;
      if (!binding || typeof binding.expressionType !== "string" || binding.expressionType.toLowerCase() !== "literal"
        || binding.value !== false || starts?.evaluatorType != null || starts?.autoEvaluate === false || starts?.conversion != null)
        refuse("start-trigger-boundary", `${at}.inputs.CanStartWorkflow`, "A starting Event cannot keep its trigger-entry path through a reusable graph. Turn off Start new workflows explicitly or keep the block embedded.");
    }
    if (entry.activityTypeKey === "Elsa.Activities.Graph.Runtime.Activities.Complete")
      refuse("changed-complete-target", at, "Complete would target the new reusable boundary. Keep this block embedded.");
    if (entry.activityTypeKey === "Elsa.Activities.Primitives.Activities.Break") {
      let ancestor = parent;
      while (ancestor && !loopKinds.has(ancestor.structure?.kind ?? "")) ancestor = body.get(ancestor.nodeId)?.parent ?? null;
      if (!ancestor) refuse("changed-break-target", at, "Break targets a loop outside this block.");
    } else if (!node.intrinsic && !safeActivities.has(entry.activityTypeKey))
      refuse("unsupported-activity-effects", at, "This activity needs a closed-boundary preservation profile before promotion.");
    if (node.intrinsic) {
      if (node.intrinsic.kind.toLowerCase() !== "set" || !node.intrinsic.variable)
        refuse("changed-write-target", at, "This operation writes outside the block or changes its target at a reusable boundary.");
      const target = node.intrinsic.variable;
      if (!localDeclaration({ kind: "variable", key: target.referenceKey, scope: target.declaringScopeId ?? undefined, authored: {} }, node.nodeId))
        refuse("external-write", `${at}.intrinsic.variable`, "Promotion cannot move this write into a closed reusable scope.");
    }
    const rewrite = (reference: PromotionReference, path: string, parameter: boolean) => {
      if (reference.kind === "variable") {
        if (!localDeclaration(reference, node.nodeId)) refuse("unpreserved-read-timing", path, "An external mutable variable read cannot become an earlier call-input snapshot. Pass an explicit immutable input or keep the block embedded.");
        return null;
      }
      if (reference.kind === "result" && body.has(reference.key)) return null;
      return promotionInputRead(publicInput(reference, path), parameter);
    };
    node.inputs.forEach((argument, index) => rewritePromotionArgument(argument, `${at}.inputs[${index}]`, context.expressionBoundary, rewrite));
    if (node.outputs.length) refuse("unsupported-body-capture", `${at}.outputs`, "This existing capture needs a reusable-body publication profile before promotion. Keep the block embedded.");
    const declarations = node.structure?.payload.variables;
    if (declarations != null && !Array.isArray(declarations)) refuse("malformed-variables", `${at}.variables`, "Repair the local variable declarations before promotion.");
    if (Array.isArray(declarations)) for (const [index, declaration] of declarations.entries()) {
      if (!record(declaration) || typeof declaration.referenceKey !== "string") refuse("malformed-variable", `${at}.variables[${index}]`, "Repair this local variable declaration.");
      if (declaration.default != null) {
        const argument = { referenceKey: declaration.referenceKey, value: declaration.default };
        rewritePromotionArgument(argument, `${at}.variables[${index}].default`, context.expressionBoundary, rewrite);
        declaration.default = argument.value;
      }
    }
  }
  const addOutput = (value: { expressionType: string; value: unknown }, valueType: ActivityTypeReference, label: string,
    path: string, owner: ActivityNode, afterId: string | null) => {
    const referenceKey = `output-${outputs.length + 1}`, outputName = `Output${outputs.length + 1}`;
    const binding = { referenceKey, name: label, type: valueType, source: clone(value), uses: [path] };
    outputs.push(binding);
    contract.outputs.push({ referenceKey, name: outputName, displayName: label, type: valueType, isRequired: true, isNullable: context.capabilities.types.find(item => item.alias === valueType.alias)?.supportsNull === true,
      storageDriverKey: driver(valueType), durability: "Required" });
    const setters = context.catalog.filter(item => item.available !== false && item.intrinsic?.kind.toLowerCase() === "setoutput");
    if (setters.length !== 1 || !setters[0].intrinsic?.outputNameInputKey) refuse("output-capture-unavailable", "catalog", "This host needs the ordinary Set Output intrinsic for a reviewed reusable output.");
    const setter = setters[0], intrinsic = setter.intrinsic!;
    const captureKinds: CollectionKind[] = ["Single", "Array", "List", "HashSet"];
    const captureKind = captureKinds.find(kind => kind === valueType.collectionKind);
    if (!captureKind) refuse("unsupported-output-collection", path, "This result collection needs an intrinsic capture type profile before promotion.");
    const capture: ActivityNode = { nodeId: allocate("output"), activityVersionId: setter.activityVersionId, inputs: [
      { referenceKey: intrinsic.outputNameInputKey!, value: { expressionType: "Literal", value: outputName } },
      { referenceKey: intrinsic.valueInputKey, value: clone(value) }
    ], outputs: [], intrinsic: { kind: "setoutput", valueType: { alias: valueType.alias, collectionKind: captureKind } } };
    const captureKey = `${owner.nodeId}:${afterId ?? "completion"}`;
    const group = captures.get(captureKey) ?? { owner, afterId, steps: [] };
    group.steps.push(capture); captures.set(captureKey, group);
    candidatePresentation.push({ nodeId: capture.nodeId, displayName: `Expose ${label}` });
    return binding;
  };
  const exposeResult = (reference: PromotionReference, path: string) => {
    const key = JSON.stringify([reference.key, reference.projection, reference.scope]);
    let binding = outputBySource.get(key);
    if (binding) { binding.uses.push(path); return binding.referenceKey; }
    const producer = body.get(reference.key)!;
    let current: PromotionNodeLocation | undefined = producer;
    while (current?.parent) {
      if (!orderedPromotionChildren(current.parent, context.catalog)) refuse("unproven-result-timing", path, "This output needs one complete normal path through its body.");
      current = body.get(current.parent.nodeId);
    }
    if (!producer.parent || reference.scope && body.has(reference.scope)) refuse("unproven-result-scope", path, "This result needs an explicit preserved execution-scope plan.");
    const declaration = descriptor(producer.node).outputs.filter(item => record(item) && item.referenceKey === reference.projection);
    if (reference.projection !== "$result" && declaration.length !== 1) refuse("unknown-projection", path, "This result projection is not advertised by the exact producer.");
    const valueType = reference.projection === "$result" ? type("Elsa.Any", path) : descriptorType(declaration[0] as Fields, path);
    binding = addOutput({ expressionType: "ActivityResult", value: clone(argumentReference(reference)) }, valueType,
      `Result ${outputs.length + 1}`, path, producer.parent, reference.key);
    outputBySource.set(key, binding);
    return binding.referenceKey;
  };
  const rewriteOutside = (reference: PromotionReference, path: string, parameter: boolean, consumerId?: string) => {
    if (reference.kind === "variable" && reference.scope && body.has(reference.scope))
      refuse("escaping-local-scope", path, "This caller references a local scope that would move behind the reusable boundary.");
    if (reference.kind !== "result") return null;
    if (reference.key === blockNodeId) refuse("changed-container-result", path, "This caller reads the old container result rather than a reviewed child projection.");
    if (!body.has(reference.key)) return null;
    if (location.parent) {
      const order = orderedPromotionChildren(location.parent, context.catalog);
      let consumer = consumerId ? all.get(consumerId) : undefined;
      while (consumer?.parent && consumer.parent !== location.parent) consumer = all.get(consumer.parent.nodeId);
      const resourceCompletion = !consumerId && location.parent === document.rootActivity;
      if (!order || !resourceCompletion && (!consumer || order.indexOf(consumer.node.nodeId) <= order.indexOf(blockNodeId)))
        refuse("unproven-caller-timing", path, "This result reader is not guaranteed to follow the promoted call's completion.");
    }
    const key = exposeResult(reference, path);
    return promotionResultRead(blockNodeId, key, parameter, reference.scope);
  };
  for (const { node } of all.values()) if (!body.has(node.nodeId)) {
    node.inputs.forEach((argument, index) => rewritePromotionArgument(argument, `nodes.${node.nodeId}.inputs[${index}]`, context.expressionBoundary, (reference, path, parameter) => rewriteOutside(reference, path, parameter, node.nodeId)));
  }
  const referenceContext = clone(context.referenceContext);
  const inspectResource = (value: unknown, path: string) => {
    if (Array.isArray(value)) { value.forEach((child, index) => inspectResource(child, `${path}[${index}]`)); return; }
    if (!record(value)) return;
    if (typeof value.syntax === "string" && "value" in value) {
      const argument = { referenceKey: "resource", value: { expressionType: value.syntax, value: value.value } };
      rewritePromotionArgument(argument, path, context.expressionBoundary, rewriteOutside);
      value.syntax = argument.value.expressionType; value.value = argument.value.value;
      return;
    }
    if (typeof value.expressionType === "string") {
      const argument = { referenceKey: "resource", value };
      rewritePromotionArgument(argument, path, context.expressionBoundary, rewriteOutside);
      if (argument.value !== value) {
        const next = clone(argument.value);
        Object.keys(value).forEach(key => delete value[key]); Object.assign(value, next);
      }
      return;
    }
    for (const [key, child] of Object.entries(value)) inspectResource(child, `${path}.${key}`);
  };
  inspectResource(referenceContext, "referenceContext");
  // Literal-initialized root locals can be reviewed as outputs without moving their native frame.
  // Capture while that frame is alive; ordinary Set Output addresses only this new graph invocation.
  const locals = root.structure?.payload.variables;
  if (Array.isArray(locals)) for (const [index, value] of locals.entries()) {
    if (!record(value) || typeof value.referenceKey !== "string" || typeof value.name !== "string" || !record(value.default)
      || typeof value.default.expressionType !== "string" || !["literal", "object"].includes(value.default.expressionType.toLowerCase())) continue;
    addOutput({ expressionType: "Variable", value: { referenceKey: value.referenceKey, declaringScopeId: root.nodeId } }, type(value.type, `nodes.${root.nodeId}.variables[${index}]`),
      value.name, `nodes.${root.nodeId}.variables[${index}]`, root, null);
  }
  for (const group of captures.values()) insertPromotionSteps(group.owner, group.afterId, group.steps, allocate, candidateLayout);
  const outcomes = getActivityGraphOutcomeMappingOptions(root, descriptor(root), { outcomes: [] }).sourceOutcomes;
  if (!outcomes.length) refuse("unknown-outcomes", `nodes.${blockNodeId}.outcomes`, "The block needs exact advertised outcome identities before promotion.");
  contract.outcomes = outcomes.map(outcome => ({ referenceKey: outcome.referenceKey, name: outcome.name, isEmitted: true }));
  const callSite = { nodeId: blockNodeId, inputs: inputs.map(input => ({ referenceKey: input.referenceKey, value: clone(input.source) })), outputs: [] };
  const candidateIds = promotionCanvasIds(promotionNodes(root, context.catalog));
  const source: Omit<ReadyBlockPromotion, "candidateIdentity"> = { status: "ready", source: blockPromotionSource(context), blockNodeId, name, inputs, outputs, callSite,
    candidate: { contract, provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: root, variables: [], outputMappings: [],
      outcomeMappings: outcomes.map(outcome => ({ sourceOutcomeReferenceKey: outcome.referenceKey, boundaryOutcomeReferenceKey: outcome.referenceKey })) } },
      layout: activityGraphLayoutFromDesign(candidateLayout.filter(item => candidateIds.has(item.nodeId))),
      activityPresentation: candidatePresentation.filter(item => candidateIds.has(item.nodeId)) },
    rewrittenSource: document, referenceContext };
  return { ...source, candidateIdentity: identity(source) };
}

/** T006 must verify the ordinary target's publication, authority and reviewed material before calling. */
export function bindBlockPromotionVersion(context: BlockPromotionContext, plan: ReadyBlockPromotion, activityVersionId: string):
  { status: "ready"; document: ReadyBlockPromotion["rewrittenSource"]; referenceContext: unknown[]; selection: string[] } | { status: "refused"; diagnostics: NativeBlockDiagnostic[] } {
  if (!context.canEdit || blockPromotionSource(context) !== plan.source || identity(plan) !== plan.candidateIdentity)
    return { status: "refused", diagnostics: [{ code: "stale-promotion", path: "source", message: "The source, proposal, catalog or capability snapshot changed. Analyze and review again." }] };
  const target = context.catalog.filter(item => item.activityVersionId === activityVersionId && item.available !== false);
  if (!activityVersionId || target.length !== 1) return { status: "refused", diagnostics: [{ code: "unavailable-target", path: "target", message: "Select one exact available reviewed published version." }] };
  const document = clone(plan.rewrittenSource), all = promotionNodes(document.rootActivity, context.catalog);
  const selected = all.get(plan.blockNodeId);
  if (!selected) return { status: "refused", diagnostics: [{ code: "missing-block", path: "selection", message: "The original block is no longer present." }] };
  const removed = promotionNodes(selected.node, context.catalog);
  const replacement: ActivityNode = { ...clone(plan.callSite), activityVersionId, structure: null };
  const replace = (node: ActivityNode): ActivityNode => {
    if (node.nodeId === plan.blockNodeId) return replacement;
    let result = node;
    for (const slot of getChildSlots(node, context.catalog)) {
      const children = slot.activities.map(replace);
      if (children.some((child, index) => child !== slot.activities[index])) result = replaceSlotActivities(result, slot, children, false);
    }
    return result;
  };
  document.rootActivity = replace(document.rootActivity);
  const removedIds = promotionCanvasIds(removed);
  document.layout = document.layout.filter(item => item.nodeId === plan.blockNodeId || !removedIds.has(item.nodeId));
  document.activityPresentation = document.activityPresentation.filter(item => item.nodeId === plan.blockNodeId || !removedIds.has(item.nodeId));
  return { status: "ready", document, referenceContext: clone(plan.referenceContext), selection: [plan.blockNodeId] };
}

function promotionCanvasIds(nodes: Map<string, PromotionNodeLocation>) {
  const ids = new Set(nodes.keys());
  for (const { node } of nodes.values()) for (const value of Object.values(node.structure?.payload ?? {})) if (Array.isArray(value))
    for (const item of value) if (record(item)) for (const field of ["elementId", "flowId", "id"])
      if (typeof item[field] === "string") ids.add(item[field]);
  return ids;
}
