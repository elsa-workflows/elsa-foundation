import type { ActivityNode } from "../workflowTypes";
import { getChildSlots, sequenceStructureKind, flowchartStructureKind } from "../workflowAdapter";
import { compensationScopeStructureKind, compensationStepsSlotId } from "../compensationScope";
import type { NativeBlockContext, NativeBlockDocument } from "./nativeBlocks";

type Fields = Record<string, unknown>;
type Refuse = (code: string, path: string, message: string) => never;
const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);

/** Explicit ordinary Set/variable transfers; the server still admits the complete proposed document. */
export function transferNativeBlockResults(context: NativeBlockContext, proposed: NativeBlockDocument,
  ownerId: string, blockId: string, selected: Set<string>, refuse: Refuse, flowchartStart?: string): NativeBlockDocument {
  const locations: { node: ActivityNode; parent: ActivityNode | null; moved: boolean }[] = [];
  const visit = (node: ActivityNode, parent: ActivityNode | null = null, moved = false) => {
    moved ||= selected.has(node.nodeId);
    locations.push({ node, parent, moved });
    for (const slot of getChildSlots(node, context.catalog)) {
      if (node.structure?.kind === compensationScopeStructureKind && slot.id === compensationStepsSlotId) continue;
      slot.activities.forEach(child => visit(child, node, moved));
    }
  };
  visit(context.document.rootActivity);
  const moved = new Set(locations.filter(item => item.moved).map(item => item.node.nodeId));
  const owner = locations.find(item => item.node.nodeId === ownerId)!.node;
  const originalChildren = owner.structure!.payload.activities as ActivityNode[];
  const finalSelected = Math.max(...originalChildren.map((node, index) => selected.has(node.nodeId) ? index : -1));
  const scopeKind = owner.structure!.kind;
  const references: { argument: Fields; binding: Fields; consumerId: string; path: string; parameter: boolean }[] = [];
  const inspect = (argument: unknown, consumerId: string, path: string) => {
    if (!record(argument) || typeof argument.expressionType !== "string") return;
    if (argument.expressionType.toLowerCase() === "activityresult" && record(argument.value)) {
      if (typeof argument.value.producerNodeId === "string" && moved.has(argument.value.producerNodeId))
        references.push({ argument, binding: argument.value, consumerId, path, parameter: false });
      return;
    }
    if (["literal", "object", "default", "secret", "connection"].includes(argument.expressionType.toLowerCase())) return;
    const value = argument.value;
    if (!record(value) || typeof value.language !== "string" || typeof value.source !== "string" || !record(value.parameters)) return;
    for (const [key, parameter] of Object.entries(value.parameters))
      if (record(parameter) && parameter.kind === "ActivityResult" && typeof parameter.producerNodeId === "string" && moved.has(parameter.producerNodeId))
        references.push({ argument: value.parameters, binding: parameter, consumerId, path: `${path}.value.parameters.${key}`, parameter: true });
  };
  locations.filter(item => !item.moved).forEach(({ node }) => node.inputs.forEach((input, index) => {
    if (!record(input)) return;
    const count = references.length;
    inspect(input.value, node.nodeId, `nodes.${node.nodeId}.inputs[${index}]`);
    if (references.length !== count && input.conversion != null)
      refuse("unproven-result-conversion", `nodes.${node.nodeId}.inputs[${index}].conversion`, "Keep this producer and reader together: transferring an authored conversion requires a separately proved conversion plan.");
  }));
  // Resource-owned output mappings cannot be rewritten by a graph-document-only edit.
  const inspectOutside = (value: unknown, path: string) => {
    if (Array.isArray(value)) { value.forEach((item, index) => inspectOutside(item, `${path}[${index}]`)); return; }
    if (!record(value)) return;
    if (typeof value.expressionType === "string") {
      const count = references.length;
      inspect(value, "", path);
      if (references.length !== count) refuse("resource-result-crossing", path, "A resource-owned result mapping needs an explicit resource-level rewrite before this extraction.");
      return;
    }
    Object.entries(value).forEach(([key, child]) => inspectOutside(child, `${path}.${key}`));
  };
  inspectOutside(context.referenceContext, "referenceContext");
  if (!references.length) return proposed;
  const inspectScopeReads = (value: unknown, path: string) => {
    if (Array.isArray(value)) { value.forEach((item, index) => inspectScopeReads(item, `${path}[${index}]`)); return; }
    if (!record(value)) return;
    if (typeof value.expressionType === "string") {
      if (["literal", "object", "default", "secret", "connection", "input", "variable", "variablemember", "activityresult", "activityresultmember", "workflowrequest"].includes(value.expressionType.toLowerCase())) return;
      const expression = value.value;
      if (!record(expression) || typeof expression.language !== "string" || typeof expression.source !== "string"
        || !record(expression.resultType) || !record(expression.options) || !record(expression.parameters)
        || expression.capabilityProfile !== "binding-pure-v1"
        || (expression.language.toLowerCase() === "javascript" && /\bvariables\b|\bgetVariable\b|\bget[A-Z_$][\w$]*\s*\(/.test(expression.source)))
        refuse("opaque-result-scope", path, "Adding a result variable cannot preserve an opaque or ambient scope read. Keep the producer and reader together or use explicit expression parameters.");
      return;
    }
    Object.entries(value).forEach(([key, child]) => inspectScopeReads(child, `${path}.${key}`));
  };
  inspectScopeReads(owner, `nodes.${ownerId}`);
  const setDescriptors = context.catalog.filter(item => item.available !== false && item.activityVersionId === "elsa.intrinsic.set@1"
    && typeof item.intrinsic?.kind === "string" && item.intrinsic.kind.toLowerCase() === "set" && item.intrinsic.valueInputKey === "value");
  if (setDescriptors.length !== 1)
    refuse("unavailable-result-transfer", "catalog", "The current catalog must expose Set variable before a child result can be transferred out of this block.");
  const declarations = owner.structure!.payload.variables;
  if (declarations != null && (!Array.isArray(declarations) || declarations.some(item => !record(item) || typeof item.referenceKey !== "string")))
    refuse("malformed-variables", `nodes.${ownerId}.structure.payload.variables`, "Repair the enclosing variable declarations before transferring a result.");
  const transfers = new Map<string, { producerId: string; projection: string; scope?: string; key: string; nodeId: string }>();
  const occupied = new Set([...locations.map(item => item.node.nodeId), ...context.document.layout.map(item => item.nodeId),
    ...context.document.activityPresentation.map(item => item.nodeId), ...(Array.isArray(declarations) ? declarations.map(item => (item as Fields).referenceKey as string) : [])]);
  for (const { node } of locations) if (node.structure?.kind === flowchartStructureKind) {
    const payload = node.structure.payload;
    for (const field of [payload.nodeMetadata, payload.connectionMetadata]) if (record(field)) Object.keys(field).forEach(id => occupied.add(id));
    if (Array.isArray(payload.connections)) for (const edge of payload.connections) if (record(edge)) {
      if (typeof edge.id === "string" && edge.id.trim()) occupied.add(edge.id.trim());
      else if (record(edge.source) && record(edge.target) && typeof edge.source.nodeId === "string" && typeof edge.target.nodeId === "string")
        occupied.add(`${edge.source.nodeId}:${typeof edge.source.port === "string" && edge.source.port.trim() || "Done"}->${edge.target.nodeId}`);
    }
  }
  const allocate = (suffix: string) => {
    let index = 1;
    while (occupied.has(`${blockId}-${suffix}-${index}`)) index++;
    const id = `${blockId}-${suffix}-${index}`; occupied.add(id); return id;
  };
  const occupiedNames = new Set<string>();
  const reserveNames = (value: unknown) => {
    if (Array.isArray(value)) { value.forEach(reserveNames); return; }
    if (!record(value)) return;
    if (typeof value.referenceKey === "string" && typeof value.name === "string" && record(value.type)) occupiedNames.add(value.name.toUpperCase());
    Object.values(value).forEach(reserveNames);
  };
  reserveNames(context.document.rootActivity);
  reserveNames(context.referenceContext);
  const allocateName = () => {
    let index = 1;
    while (occupiedNames.has(`BLOCK RESULT ${index}`)) index++;
    const name = `Block result ${index}`; occupiedNames.add(name.toUpperCase()); return name;
  };
  for (const reference of references) {
    const producerId = reference.binding.producerNodeId as string;
    const producer = locations.find(item => item.node.nodeId === producerId)!;
    const consumer = locations.find(item => item.node.nodeId === reference.consumerId)!;
    if (!selected.has(producerId) || producer.parent !== owner || consumer.parent !== owner || reference.binding.isOptional === true
      || typeof reference.binding.projectionKey !== "string" || !reference.binding.projectionKey.trim())
      refuse("unproven-result-transfer", reference.path, "This result transfer cannot be preserved by the supported sibling extraction. Keep its producer and reader together.");
    const consumerIndex = originalChildren.findIndex(node => node.nodeId === consumer.node.nodeId);
    if (scopeKind === sequenceStructureKind && consumerIndex <= finalSelected)
      refuse("unproven-result-transfer", reference.path, "The result reader must follow the selected Sequence range.");
    if (scopeKind === flowchartStructureKind) {
      const connections = owner.structure!.payload.connections as Fields[];
      const exits = connections.filter(edge => record(edge.source) && selected.has(edge.source.nodeId as string) && record(edge.target) && !selected.has(edge.target.nodeId as string));
      if (exits.length !== 1 || (exits[0].target as Fields).nodeId !== consumer.node.nodeId)
        refuse("unproven-result-transfer", reference.path, "The result reader must be the selected Flowchart region's normal continuation.");
    }
    const declaredScope = typeof reference.binding.producerScopeId === "string" ? reference.binding.producerScopeId : undefined;
    if (!declaredScope) {
      // A new direct read without a declared execution scope must not invent a loop's occurrence.
      // Root Sequences and whole linear Flowcharts establish the single-run profile themselves.
      if (owner !== context.document.rootActivity)
        refuse("unproven-result-transfer", reference.path, "This nested result transfer needs an existing explicit producer scope.");
      if (scopeKind === flowchartStructureKind) {
        const edges = owner.structure!.payload.connections as { source: { nodeId: string; port?: string }; target: { nodeId: string } }[];
        const visited = new Set<string>();
        let next = flowchartStart;
        while (typeof next === "string" && !visited.has(next)) {
          visited.add(next);
          const outgoing = edges.filter(edge => edge.source.nodeId === next);
          if (outgoing.length > 1 || outgoing.some(edge => (edge.source.port?.trim() || "Done") !== "Done")) break;
          next = outgoing[0]?.target.nodeId;
        }
        if (next !== undefined || visited.size !== originalChildren.length || edges.length !== originalChildren.length - 1)
          refuse("unproven-result-transfer", reference.path, "A result without an explicit scope requires a complete linear Flowchart.");
      }
    }
    const identity = JSON.stringify([producerId, reference.binding.projectionKey]);
    const existing = transfers.get(identity);
    if (existing?.scope && declaredScope && existing.scope !== declaredScope)
      refuse("unproven-result-transfer", reference.path, "These readers declare different producer scopes; review an explicit transfer.");
    if (existing) existing.scope ??= declaredScope;
    else transfers.set(identity, { producerId, projection: reference.binding.projectionKey, scope: declaredScope, key: allocate("result"), nodeId: allocate("capture") });
  }
  const result = structuredClone(proposed);
  const nodes = new Map<string, ActivityNode>();
  const index = (node: ActivityNode) => {
    nodes.set(node.nodeId, node);
    for (const slot of getChildSlots(node, context.catalog)) {
      if (node.structure?.kind === compensationScopeStructureKind && slot.id === compensationStepsSlotId) continue;
      slot.activities.forEach(index);
    }
  };
  index(result.rootActivity);
  const nextOwner = nodes.get(ownerId)!;
  const block = nodes.get(blockId)!;
  const body = block.structure!.payload;
  const captures = new Map<string, ActivityNode[]>();
  const added = [...transfers.values()].map(transfer => {
    const variable = { referenceKey: transfer.key, name: allocateName(), type: { alias: "Elsa.Any", collectionKind: "single" }, storageDriverType: null, default: null };
    const capture: ActivityNode = { nodeId: transfer.nodeId, activityVersionId: setDescriptors[0].activityVersionId, inputs: [{ referenceKey: "value", value: {
      expressionType: "ActivityResult", value: { producerNodeId: transfer.producerId, projectionKey: transfer.projection, ...(transfer.scope ? { producerScopeId: transfer.scope } : {}) }
    } }], outputs: [], intrinsic: { kind: "set", valueType: variable.type, variable: { referenceKey: transfer.key, declaringScopeId: ownerId } } };
    captures.set(transfer.producerId, [...(captures.get(transfer.producerId) ?? []), capture]);
    result.activityPresentation.push({ nodeId: transfer.nodeId, displayName: "Store block result" });
    const position = result.layout.find(item => item.nodeId === transfer.producerId);
    if (position) result.layout.push({ nodeId: transfer.nodeId, x: position.x + 120, y: position.y + 100 * captures.get(transfer.producerId)!.length });
    return variable;
  });
  nextOwner.structure!.payload.variables = [...(Array.isArray(declarations) ? structuredClone(declarations) : []), ...added];
  body.activities = (body.activities as ActivityNode[]).flatMap(node => [node, ...(captures.get(node.nodeId) ?? [])]);
  if (scopeKind === flowchartStructureKind) {
    const connections = body.connections as Fields[];
    for (const [producerId, chain] of captures) {
      const last = chain[chain.length - 1].nodeId;
      for (const connection of connections) if (record(connection.source) && connection.source.nodeId === producerId)
        connection.source = { ...connection.source, nodeId: last };
      for (let position = 0; position < chain.length; position++) connections.push({
        id: chain[position].nodeId, source: { nodeId: position === 0 ? producerId : chain[position - 1].nodeId, port: "Done" }, target: { nodeId: chain[position].nodeId }
      });
    }
  }
  // Rewrite only owned typed reads; authored expression source and unrelated argument fields survive.
  for (const reference of references) {
    const transfer = transfers.get(JSON.stringify([reference.binding.producerNodeId, reference.binding.projectionKey]))!;
    const original = locations.find(item => item.node.nodeId === reference.consumerId)!.node;
    const next = nodes.get(reference.consumerId)!;
    next.inputs = next.inputs.map((input, position) => {
      const oldInput = original.inputs[position];
      if (!record(oldInput) || !record(oldInput.value)) return input;
      if (!reference.parameter && oldInput.value === reference.argument)
        return { ...(input as Fields), value: { ...oldInput.value, expressionType: "Variable", value: { referenceKey: transfer.key, declaringScopeId: ownerId } } };
      if (reference.parameter && record(oldInput.value.value) && oldInput.value.value.parameters === reference.argument && record(input) && record(input.value) && record(input.value.value) && record(input.value.value.parameters))
        for (const [key, binding] of Object.entries(reference.argument)) if (binding === reference.binding)
          input.value.value.parameters = { ...input.value.value.parameters, [key]: { kind: "Variable", declaringScopeNodeId: ownerId, variableKey: transfer.key } };
      return input;
    });
  }
  return result;
}
