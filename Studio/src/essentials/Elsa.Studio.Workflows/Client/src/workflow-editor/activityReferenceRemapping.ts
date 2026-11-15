import { getInputPropertyName, readPortableExpressionDefinition } from "../activityProperties";
import { getOutputPropertyName } from "../activityOutputCapture";
import { readActivityResultReference } from "../activityDataSelection";
import { readActivityDataMemberReference } from "../activityDataPickerModel";
import { readDeclaredActivityInputs } from "../inputInspectionRows";
import { collectionSlotItems, getCollectionSlots } from "../collectionSlots";
import { readVariableReference, WORKFLOW_SCOPE_ID } from "../scopedVariables";
import type { ActivityCatalogItem, ActivityNode, VariableReference } from "../workflowTypes";
import type { StudioActivityOutputDescriptor } from "@elsa-workflows/studio-sdk";

export type ActivityReferenceResourceKind = "workflow" | "activity-definition";

type Fields = Record<string, unknown>;
const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;

/** Remaps only the identities owned by authored value roles, never arbitrary JSON or source text. */
export function remapActivityReferences(node: ActivityNode, catalog: ActivityCatalogItem | undefined,
  activityIds: ReadonlyMap<string, string>, resourceKind: ActivityReferenceResourceKind): ActivityNode {
  const scope = (value: unknown) => typeof value === "string" && value !== WORKFLOW_SCOPE_ID && !(resourceKind === "activity-definition" && value === "activity-graph")
    ? activityIds.get(value) ?? value : value;
  const variable = (value: unknown): unknown => record(value) && readVariableReference(value)
    ? { ...value, ...(typeof value.declaringScopeId === "string" ? { declaringScopeId: scope(value.declaringScopeId) } : {}) } : value;
  const definition = (type: string, value: unknown): unknown => {
    if (!record(value)) return value;
    switch (type.toLowerCase()) {
      case "activityresult":
        if (!readActivityResultReference(value) || value.memberPath != null || value.path != null
          || value.producerScopeId != null && !text(value.producerScopeId)
          || value.isOptional != null && typeof value.isOptional !== "boolean") return value;
        return { ...value, producerNodeId: activityIds.get(value.producerNodeId as string) ?? value.producerNodeId,
          ...(typeof value.producerScopeId === "string" ? { producerScopeId: scope(value.producerScopeId) } : {}) };
      case "activityresultmember":
        return readActivityDataMemberReference({ expressionType: "ActivityResultMember", value })
          ? { ...value, producerNodeId: activityIds.get(value.producerNodeId as string) ?? value.producerNodeId } : value;
      case "variable":
        return value.memberPath == null && value.path == null ? variable(value) : value;
      case "variablemember":
        return readActivityDataMemberReference({ expressionType: "VariableMember", value }) ? variable(value) : value;
    }
    // Closed non-expression roles never gain portable semantics because their business value looks like one.
    if (nonPortableRoles.has(type.toLowerCase())) return value;
    const portable = readPortableExpressionDefinition(value, type);
    if (!portable) return value;
    return { ...portable, parameters: Object.fromEntries(Object.entries(portable.parameters).map(([alias, parameter]) => {
      if (!record(parameter)) return [alias, parameter];
      switch (parameter.kind) {
        case "ActivityResult":
          return [alias, text(parameter.producerNodeId) && text(parameter.projectionKey)
            && (parameter.isOptional == null || typeof parameter.isOptional === "boolean")
            ? { ...parameter, producerNodeId: activityIds.get(parameter.producerNodeId) ?? parameter.producerNodeId } : parameter];
        case "Variable":
          return [alias, text(parameter.variableKey) && text(parameter.declaringScopeNodeId)
            ? { ...parameter, declaringScopeNodeId: scope(parameter.declaringScopeNodeId) } : parameter];
        default: return [alias, parameter];
      }
    })) };
  };
  const argument = (value: unknown): unknown => {
    if (!record(value) || !record(value.value) || typeof value.value.expressionType !== "string") return value;
    return { ...value, value: { ...value.value, value: definition(value.value.expressionType, value.value.value) } };
  };
  const next: ActivityNode = { ...node,
    inputs: node.inputs.map(argument),
    // Outputs are write destinations: only a whole Variable capture owns a lexical scope here.
    outputs: node.outputs.map(value => record(value) && record(value.value) && typeof value.value.expressionType === "string" && value.value.expressionType.toLowerCase() === "variable"
      ? { ...value, value: { ...value.value, value: variable(value.value.value) } } : value) };

  // Expanded property locations are owned by the catalog, just as in ActivityPropertiesPanel. Unknown
  // top-level objects, including metadata and missing-provider payloads, remain opaque.
  for (const input of readDeclaredActivityInputs(catalog?.inputs)) {
    if (input.isWrapped === false) continue;
    const key = getInputPropertyName(input), wrapped = node[key];
    if (record(wrapped) && record(wrapped.expression) && typeof wrapped.expression.type === "string")
      next[key] = { ...wrapped, expression: { ...wrapped.expression, value: definition(wrapped.expression.type, wrapped.expression.value) } };
  }
  for (const output of catalog?.outputs ?? []) {
    if (!record(output) || typeof output.name !== "string") continue;
    const key = getOutputPropertyName(output as unknown as StudioActivityOutputDescriptor);
    for (const property of new Set([key, output.name])) {
      const capture = node[property];
      if (record(capture) && record(capture.target)) next[property] = { ...capture, target: variable(capture.target) };
    }
  }
  // Intrinsic variable assignment is distinct from the SetOutput public output name.
  if (typeof node.intrinsic?.kind === "string" && variableWriteIntrinsicKinds.has(node.intrinsic.kind.toLowerCase()) && node.intrinsic.variable)
    next.intrinsic = { ...node.intrinsic, variable: variable(node.intrinsic.variable) as VariableReference };

  // CaseConditionEditor stores this ArgumentState on the collection item, never in node.inputs.
  for (const collection of getCollectionSlots(next, catalog)) {
    if (!collection.conditionProperty) continue;
    const items = collectionSlotItems(next, collection);
    if (!items || !next.structure) continue;
    const conditionProperty = collection.conditionProperty;
    next.structure = { ...next.structure, payload: { ...next.structure.payload,
      [collection.property]: items.map(item => conditionProperty in item
        ? { ...item, [conditionProperty]: argument(item[conditionProperty]) } : item) } };
  }
  return next;
}

const nonPortableRoles = new Set(["literal", "object", "default", "secret", "connection", "input", "inputmember",
  "workflowrequest", "workflowrequestmember", "variable", "variablemember", "activityresult", "activityresultmember"]);

const variableWriteIntrinsicKinds = new Set(["set", "merge", "reduce"]);
