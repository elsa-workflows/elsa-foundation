import { createContext, useContext, type ReactNode } from "react";
import type { ScopedVariableAnalysisStatus } from "./api/workflows";
import { describeCollectionType, formatTypeName } from "./activityProperties";
import type { ArgumentType, VisibleVariableView, WorkflowDefinitionState, WorkflowInput } from "./workflowTypes";

interface WorkflowReferenceAuthoringState {
  workflowState: WorkflowDefinitionState;
  workflowInputs: WorkflowInput[];
  visibleVariables: VisibleVariableView[];
  status: ScopedVariableAnalysisStatus;
  retry?: () => void;
}

const WorkflowReferenceAuthoringContext = createContext<WorkflowReferenceAuthoringState | null>(null);

export function WorkflowReferenceAuthoringProvider({
  workflowState,
  workflowInputs,
  visibleVariables,
  status,
  retry,
  children
}: WorkflowReferenceAuthoringState & { children: ReactNode }) {
  return (
    <WorkflowReferenceAuthoringContext.Provider value={{ workflowState, workflowInputs, visibleVariables, status, retry }}>
      {children}
    </WorkflowReferenceAuthoringContext.Provider>
  );
}

export function readWorkflowInputs(source: unknown[] | undefined): WorkflowInput[] {
  return (source ?? []).filter(isWorkflowInput);
}

function isWorkflowInput(value: unknown): value is WorkflowInput {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const input = value as Record<string, unknown>;
  return typeof input.referenceKey === "string"
    && typeof input.name === "string"
    && typeof input.displayName === "string";
}

export function useWorkflowReferenceAuthoring() {
  return useContext(WorkflowReferenceAuthoringContext);
}

export function isArgumentTypeCompatible(argumentType: ArgumentType, propertyTypeName: string) {
  const propertyCollection = describeCollectionType(propertyTypeName);
  const propertyAlias = canonicalTypeAlias(propertyCollection?.elementTypeName ?? propertyTypeName);
  const argumentAlias = canonicalTypeAlias(argumentType.alias);
  if (propertyAlias === "system.object") return true;
  if (!propertyAlias || !argumentAlias || propertyAlias !== argumentAlias) return false;
  return propertyCollection ? argumentType.collectionKind !== "Single" : argumentType.collectionKind === "Single";
}

export function formatArgumentType(type: ArgumentType) {
  const alias = formatTypeName(type.alias);
  return type.collectionKind === "Single" ? alias : `${type.collectionKind}<${alias}>`;
}

const clrPrimitiveAliases = new Set([
  "boolean", "byte", "sbyte", "int16", "uint16", "int32", "uint32", "int64", "uint64",
  "single", "double", "decimal", "char", "string", "object", "datetime", "datetimeoffset",
  "timespan", "guid", "uri"
]);

function canonicalTypeAlias(typeName: string | null | undefined) {
  const unqualified = stripTopLevelAssemblyQualifier((typeName ?? "").trim().replace(/^global::/, ""));
  if (!unqualified) return "";
  const lower = unqualified.toLowerCase();
  const primitive = lower.startsWith("system.") ? lower.slice("system.".length) : lower;
  return clrPrimitiveAliases.has(primitive) ? `system.${primitive}` : lower;
}

function stripTopLevelAssemblyQualifier(typeName: string) {
  let depth = 0;
  for (let index = 0; index < typeName.length; index++) {
    const character = typeName[index];
    if (character === "[") depth += 1;
    else if (character === "]") depth = Math.max(0, depth - 1);
    else if (character === "," && depth === 0) return typeName.slice(0, index).trim();
  }
  return typeName;
}
