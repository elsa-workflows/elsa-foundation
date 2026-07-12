import { createContext, useContext, type ReactNode } from "react";
import type {
  StudioContributionRegistry,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";
import type { ScopedVariableAnalysisStatus } from "./api/workflows";
import { describeCollectionType, formatTypeName } from "./activityProperties";
import { getChildSlots } from "./workflowAdapter";
import { readArgumentType } from "./workflowProperties";
import {
  makeVariableReference,
  readContainerVariables,
  readVariableReference,
  WORKFLOW_SCOPE_ID
} from "./scopedVariables";
import type {
  ArgumentType,
  VariableDefinition,
  VisibleVariableView,
  WorkflowDefinitionState
} from "./workflowTypes";

const variableSyntax = "Variable";
const variableOptionSeparator = "::";

interface VariableReferenceAuthoringState {
  workflowState: WorkflowDefinitionState;
  visibleVariables: VisibleVariableView[];
  status: ScopedVariableAnalysisStatus;
  retry?: () => void;
}

const VariableReferenceAuthoringContext = createContext<VariableReferenceAuthoringState | null>(null);

export function VariableReferenceAuthoringProvider({
  workflowState,
  visibleVariables,
  status,
  retry,
  children
}: VariableReferenceAuthoringState & { children: ReactNode }) {
  return (
    <VariableReferenceAuthoringContext.Provider value={{ workflowState, visibleVariables, status, retry }}>
      {children}
    </VariableReferenceAuthoringContext.Provider>
  );
}

export const variableReferenceContribution: StudioExpressionEditorContribution = {
  id: "studio.workflows.variable-reference",
  order: 100,
  supports: context => context.syntax === variableSyntax,
  surfaces: { inline: VariableReferenceEditor },
  createDefaultValue: () => null
};

export function registerVariableReferenceContribution(
  registry: StudioContributionRegistry<StudioExpressionEditorContribution>
) {
  registry.add(variableReferenceContribution);
}

function VariableReferenceEditor({ descriptor, value, disabled, onChange }: StudioExpressionEditorProps) {
  const authoring = useContext(VariableReferenceAuthoringContext);
  if (!authoring) {
    return <p className="wf-variable-picker-note">Variable authoring context is unavailable. The current reference is preserved.</p>;
  }

  const declarations = indexVariableDeclarations(authoring.workflowState);
  const options = authoring.visibleVariables.map(variable => {
    const declaration = declarations.get(variableOptionKey(variable.referenceKey, variable.scopeId));
    const type = declaration ? readArgumentType(declaration) : null;
    return { variable, type, compatible: type ? isVariableTypeCompatible(type, descriptor.typeName) : false };
  });
  const compatible = options.filter(option => option.compatible);
  const incompatible = options.filter(option => !option.compatible);
  const current = readVariableReference(value);
  const currentKey = current ? variableOptionKey(current.referenceKey, current.declaringScopeId) : "";
  const currentIsVisible = currentKey !== "" && options.some(option =>
    variableOptionKey(option.variable.referenceKey, option.variable.scopeId) === currentKey);
  const optionsUnavailable = authoring.status !== "ready";

  return (
    <div className="wf-variable-picker">
      <select
        aria-label="Variable reference"
        value={currentKey}
        disabled={disabled || optionsUnavailable}
        onChange={event => {
          const selected = parseVariableOptionKey(event.target.value);
          if (selected) onChange(makeVariableReference(selected.referenceKey, selected.scopeId));
        }}
      >
        <option value="">Select a variable…</option>
        {current && !currentIsVisible ? (
          <option value={currentKey} disabled>{current.referenceKey} (not visible from this scope)</option>
        ) : null}
        {compatible.length > 0 ? (
          <optgroup label="Compatible variables">
            {compatible.map(({ variable, type }) => (
              <VariableOption key={variableOptionKey(variable.referenceKey, variable.scopeId)} variable={variable} type={type} />
            ))}
          </optgroup>
        ) : null}
        {incompatible.length > 0 ? (
          <optgroup label="Other variables">
            {incompatible.map(({ variable, type }) => (
              <VariableOption
                key={variableOptionKey(variable.referenceKey, variable.scopeId)}
                variable={variable}
                type={type}
                disabled
              />
            ))}
          </optgroup>
        ) : null}
      </select>
      {authoring.status === "loading" ? (
        <p className="wf-variable-picker-note" role="status">Loading visible variables… The current reference is preserved.</p>
      ) : authoring.status === "error" ? (
        <div className="wf-variable-picker-error" role="alert">
          <span>Variable scope information could not be loaded. The current reference is preserved.</span>
          <button type="button" onClick={authoring.retry}>Retry</button>
        </div>
      ) : authoring.status === "unavailable" ? (
        <p className="wf-variable-picker-note">Variable scope information is unavailable. The current reference is preserved.</p>
      ) : authoring.visibleVariables.length === 0 ? (
        <p className="wf-variable-picker-note">No variables are visible here. Declare one on the workflow or a container scope.</p>
      ) : null}
    </div>
  );
}

function VariableOption({
  variable,
  type,
  disabled = false
}: {
  variable: VisibleVariableView;
  type: ArgumentType | null;
  disabled?: boolean;
}) {
  const scope = variable.isWorkflowScope ? "workflow" : "container";
  const typeLabel = type ? formatArgumentType(type) : "type unavailable";
  return (
    <option value={variableOptionKey(variable.referenceKey, variable.scopeId)} disabled={disabled}>
      {variable.name} · {scope} · {typeLabel}{disabled ? " (incompatible)" : ""}
    </option>
  );
}

function indexVariableDeclarations(state: WorkflowDefinitionState) {
  const declarations = new Map<string, VariableDefinition>();
  for (const variable of state.variables ?? []) {
    declarations.set(variableOptionKey(variable.referenceKey, WORKFLOW_SCOPE_ID), variable);
  }

  const visit = (node: NonNullable<WorkflowDefinitionState["rootActivity"]>) => {
    for (const variable of readContainerVariables(node)) {
      declarations.set(variableOptionKey(variable.referenceKey, node.nodeId), variable);
    }
    getChildSlots(node).forEach(slot => slot.activities.forEach(visit));
  };
  if (state.rootActivity) visit(state.rootActivity);
  return declarations;
}

function isVariableTypeCompatible(variableType: ArgumentType, propertyTypeName: string) {
  const propertyCollection = describeCollectionType(propertyTypeName);
  const propertyAlias = canonicalTypeAlias(propertyCollection?.elementTypeName ?? propertyTypeName);
  const variableAlias = canonicalTypeAlias(variableType.alias);
  if (propertyAlias === "system.object") return true;
  if (!propertyAlias || !variableAlias || propertyAlias !== variableAlias) return false;
  return propertyCollection ? variableType.collectionKind !== "Single" : variableType.collectionKind === "Single";
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

function formatArgumentType(type: ArgumentType) {
  const alias = formatTypeName(type.alias);
  return type.collectionKind === "Single" ? alias : `${type.collectionKind}<${alias}>`;
}

function normalizeScopeId(scopeId: string | null | undefined) {
  return !scopeId || scopeId === WORKFLOW_SCOPE_ID ? WORKFLOW_SCOPE_ID : scopeId;
}

function variableOptionKey(referenceKey: string, scopeId: string | null | undefined) {
  return `${normalizeScopeId(scopeId)}${variableOptionSeparator}${referenceKey}`;
}

function parseVariableOptionKey(key: string): { scopeId: string; referenceKey: string } | null {
  const index = key.indexOf(variableOptionSeparator);
  if (index < 0) return null;
  const referenceKey = key.slice(index + variableOptionSeparator.length);
  return referenceKey ? { scopeId: key.slice(0, index), referenceKey } : null;
}
