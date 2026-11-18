import { useId } from "react";
import type { StudioActivityDescriptor, StudioActivityInputDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { formatTypeName, readWrappedInput, withExpression, writeInputValue } from "./activityProperties";
import { makeVariableReference, readVariableReference } from "./scopedVariables";
import { optionKey, selectedOptionKey, VariableTargetSelect } from "./variableTargetPicker";
import { indexVariableDeclarations, variableOptionKey } from "./variableReferenceContribution";
import { readArgumentType } from "./workflowProperties";
import { formatArgumentType, useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";
import type { ActivityNode, ArgumentType, VariableReference, VisibleVariableView, WorkflowDefinitionState } from "./workflowTypes";

/**
 * Variable-reference inputs (spec 1231 FR-022, FR-036): an activity input of CLR type `VariableReference<T>` or
 * `ReadOnlyVariableReference<T>` is bound to a declared variable in scope with the `Variable` syntax, and nothing
 * else. The node's may-write set is the variables its `ReadWrite` references are bound to; an activity marked
 * `[AccessesUndeclaredVariables]` may write any visible variable.
 */

const variableSyntax = "Variable";

export function isVariableReferenceInput(input: StudioActivityInputDescriptor) {
  return typeof input.variableAccess === "string" && input.variableAccess.length > 0;
}

export function describeVariableAccess(input: StudioActivityInputDescriptor) {
  return input.variableAccess === "ReadWrite" ? "reads and writes" : "reads";
}

/** The variable a variable-reference input is bound to, or null when it is unbound or bound with another syntax. */
export function readBoundVariable(activity: ActivityNode, input: StudioActivityInputDescriptor): VariableReference | null {
  const wrapped = readWrappedInput(activity, input);
  return wrapped.expression.type === variableSyntax ? readVariableReference(wrapped.expression.value) : null;
}

/** The names of the variables the node's bound `ReadWrite` reference inputs name, in input order. */
export function readMayWriteSet(
  activity: ActivityNode,
  descriptor: StudioActivityDescriptor,
  visibleVariables: VisibleVariableView[],
  scopeReady = true,
  workflowState?: WorkflowDefinitionState
): string[] {
  const names: string[] = [];
  for (const input of descriptor.inputs) {
    if (input.variableAccess !== "ReadWrite") continue;
    const bound = readBoundVariable(activity, input);
    if (!bound) continue;
    const key = selectedOptionKey(bound);
    const visible = visibleVariables.find(variable => optionKey(variable.referenceKey, variable.scopeId) === key);
    // A binding to a variable that was deleted or moved out of scope keeps its stored name, flagged; publish refuses it.
    // Until the scope analysis is ready nothing is known to be out of scope, so the stored name shows unflagged.
    const stored = readStoredVariableName(activity, input) ?? bound.referenceKey;
    // A visible variable whose type no longer fits the input is refused by publish, so it is flagged too.
    const fits = !visible || compatibleVariables([visible], workflowState, input).length === 1;
    const name = visible
      ? fits ? visible.name : `${visible.name} (type does not fit)`
      : scopeReady ? `${stored} (not visible from this scope)` : stored;
    if (!names.includes(name)) names.push(name);
  }
  return names;
}

/** The declared name a Variable binding stored with its reference, if any. */
function readStoredVariableName(activity: ActivityNode, input: StudioActivityInputDescriptor): string | null {
  const value = readWrappedInput(activity, input).expression.value;
  if (value && typeof value === "object" && "name" in value) {
    const name = (value as { name?: unknown }).name;
    return typeof name === "string" && name.trim() !== "" ? name : null;
  }
  return null;
}

const openTypeAliases = new Set([
  "elsa.any", "object", "system.object", "jsonnode", "system.text.json.nodes.jsonnode", "jsonelement", "system.text.json.jsonelement"
]);

/**
 * Whether a variable of `variableType` fits a reference input of value type `inputTypeName`: the rule publish applies
 * (StepVariableWriteValidation.TypeFits). An Any, Object or JSON type on either side takes any value; otherwise the alias
 * and the collection kind must be the same.
 */
export function referenceTypeFits(variableType: ArgumentType, inputTypeName: string, inputCollectionKind?: string | null): boolean {
  const variableAlias = variableType.alias.trim().toLowerCase();
  const inputAlias = inputTypeName.trim().toLowerCase();
  if (openTypeAliases.has(variableAlias) || openTypeAliases.has(inputAlias)) return true;
  const variableKind = (variableType.collectionKind || "Single").toLowerCase();
  const inputKind = (inputCollectionKind || "Single").toLowerCase();
  return variableAlias === inputAlias && variableKind === inputKind;
}

/**
 * The visible variables a reference input can be bound to: those whose declared type fits the input's value type
 * (`referenceTypeFits`). A variable whose declaration is not known here is kept, since its type cannot be judged.
 */
export function compatibleVariables(
  visibleVariables: VisibleVariableView[],
  workflowState: WorkflowDefinitionState | undefined,
  input: StudioActivityInputDescriptor
): VisibleVariableView[] {
  if (!workflowState) return visibleVariables;
  const declarations = indexVariableDeclarations(workflowState);
  return visibleVariables.filter(variable => {
    const declaration = declarations.get(variableOptionKey(variable.referenceKey, variable.scopeId));
    return !declaration || referenceTypeFits(readArgumentType(declaration), input.typeName, input.collectionKind);
  });
}

function declaredType(workflowState: WorkflowDefinitionState | undefined, variable: VisibleVariableView): string | null {
  if (!workflowState) return null;
  const declaration = indexVariableDeclarations(workflowState).get(variableOptionKey(variable.referenceKey, variable.scopeId));
  return declaration ? formatArgumentType(readArgumentType(declaration)) : null;
}

/** The display name of the other syntax a variable-reference input is stored with, or null when it is unbound or a Variable binding. */
export function readForeignBindingSyntax(
  activity: ActivityNode,
  input: StudioActivityInputDescriptor,
  expressionDescriptors: StudioExpressionDescriptor[] = []
): string | null {
  const { expression } = readWrappedInput(activity, input);
  if (expression.type === variableSyntax) return null;
  const value = expression.value;
  if (value === null || value === undefined || (typeof value === "string" && value.trim() === "")) return null;
  return expressionDescriptors.find(descriptor => descriptor.type === expression.type)?.displayName || expression.type;
}

export function VariableReferenceInputRow({
  activity,
  input,
  readOnly,
  expressionDescriptors = [],
  onChange
}: {
  activity: ActivityNode;
  input: StudioActivityInputDescriptor;
  readOnly: boolean;
  expressionDescriptors?: StudioExpressionDescriptor[];
  onChange(activity: ActivityNode, input: StudioActivityInputDescriptor): void;
}) {
  const labelId = useId();
  const accessId = useId();
  const noteId = useId();
  const typeNoteId = useId();
  const authoring = useWorkflowReferenceAuthoring();
  const label = input.displayName || input.name;
  const access = describeVariableAccess(input);
  // Validation/recovery state: a stored binding of another syntax is kept until the author picks a variable.
  const foreignSyntax = readForeignBindingSyntax(activity, input, expressionDescriptors);
  const bound = readBoundVariable(activity, input);
  const selected = selectedOptionKey(bound);
  const offered = authoring ? compatibleVariables(authoring.visibleVariables, authoring.workflowState, input) : [];
  // A bound variable that is in scope but whose type no longer fits, or one that is no longer visible: say which, by name.
  const boundVisible = authoring && bound
    ? authoring.visibleVariables.find(variable => optionKey(variable.referenceKey, variable.scopeId) === selected)
    : undefined;
  const typeMismatch = !!boundVisible && !offered.some(variable => optionKey(variable.referenceKey, variable.scopeId) === selected);
  const boundType = typeMismatch && authoring ? declaredType(authoring.workflowState, boundVisible!) : null;
  // Until the scope analysis is ready nothing is known to be out of scope: the stored name shows unflagged, as in the
  // may-write summary.
  const scopeReady = authoring?.status === "ready";
  const storedName = bound ? readStoredVariableName(activity, input) ?? bound.referenceKey : undefined;
  const unlistedLabel = typeMismatch
    ? `${boundVisible!.name} (type does not fit)`
    : storedName ? scopeReady ? `${storedName} (not visible from this scope)` : storedName : undefined;
  const describedBy = [accessId, foreignSyntax ? noteId : null, typeMismatch ? typeNoteId : null].filter(Boolean).join(" ");

  return (
    <div className="wf-property-row">
      <div className="wf-property-row-header">
        <label id={labelId}>{label}</label>
        <div className="wf-property-row-header-meta">
          <span>{formatTypeName(input.typeName)}</span>
          <span id={accessId} className="wf-variable-access">{access}</span>
        </div>
      </div>
      {input.description ? <p>{input.description}</p> : null}
      {foreignSyntax ? (
        <p id={noteId} className="wf-variable-picker-note" role="note">
          Bound with {foreignSyntax}; this input accepts only a declared variable. Pick one to replace it.
        </p>
      ) : null}
      {typeMismatch ? (
        <p id={typeNoteId} className="wf-variable-picker-note" role="note">
          {boundVisible!.name}{boundType ? ` (${boundType})` : ""} does not fit {formatTypeName(input.typeName)}. Pick a compatible variable.
        </p>
      ) : null}
      {authoring ? (
        <VariableTargetSelect
          ariaLabelledBy={labelId}
          ariaDescribedBy={describedBy}
          visibleVariables={offered}
          unlistedLabel={unlistedLabel}
          emptyLabel={authoring.visibleVariables.length > 0 && offered.length === 0
            ? `No visible variable fits ${formatTypeName(input.typeName)}. Declare one of that type.`
            : undefined}
          status={authoring.status}
          retry={authoring.retry}
          disabled={readOnly || input.isReadOnly === true}
          selected={selected}
          onSelect={selection => {
            // Publish reads the declared name alongside the key (RuntimeInputBindingCompiler.ReadVariableName).
            const name = selection
              ? authoring.visibleVariables.find(variable =>
                optionKey(variable.referenceKey, variable.scopeId) === optionKey(selection.referenceKey, selection.scopeId))?.name
              : undefined;
            const reference = selection
              ? { ...makeVariableReference(selection.referenceKey, selection.scopeId), ...(name ? { name } : {}) }
              : null;
            onChange(writeInputValue(activity, input, withExpression(readWrappedInput(activity, input), variableSyntax, reference)), input);
          }}
        />
      ) : (
        <p className="wf-variable-picker-note">Variable authoring context is unavailable. The current reference is preserved.</p>
      )}
    </div>
  );
}

/** The node's may-write set, or the marker for an activity that may write any visible variable. */
export function VariableWritesSummary({
  activity,
  descriptor,
  visibleVariables,
  scopeReady = true,
  workflowState
}: {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor;
  visibleVariables: VisibleVariableView[];
  scopeReady?: boolean;
  workflowState?: WorkflowDefinitionState;
}) {
  if (descriptor.accessesUndeclaredVariables === true) {
    return <p className="wf-variable-writes" role="note">May write any visible variable</p>;
  }
  const names = readMayWriteSet(activity, descriptor, visibleVariables, scopeReady, workflowState);
  if (names.length === 0) return null;
  return <p className="wf-variable-writes" role="note">May write: {names.join(", ")}</p>;
}
