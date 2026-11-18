import { useId } from "react";
import type { StudioActivityDescriptor, StudioActivityInputDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { formatTypeName, readWrappedInput, withExpression, writeInputValue } from "./activityProperties";
import { makeVariableReference, readVariableReference } from "./scopedVariables";
import { optionKey, selectedOptionKey, VariableTargetSelect } from "./variableTargetPicker";
import { indexVariableDeclarations, variableOptionKey } from "./variableReferenceContribution";
import { readArgumentType } from "./workflowProperties";
import { isArgumentTypeCompatible, useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";
import type { ActivityNode, VariableReference, VisibleVariableView, WorkflowDefinitionState } from "./workflowTypes";

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
  scopeReady = true
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
    const name = visible ? visible.name : scopeReady ? `${stored} (not visible from this scope)` : stored;
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

/**
 * The visible variables a reference input can be bound to: those whose declared type fits the input's value type, by the
 * same rule as the Variable editor (`isArgumentTypeCompatible`). A variable whose declaration is not known here is kept,
 * since its type cannot be judged.
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
    return !declaration || isArgumentTypeCompatible(readArgumentType(declaration), input.typeName, input.collectionKind);
  });
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
  const authoring = useWorkflowReferenceAuthoring();
  const label = input.displayName || input.name;
  const access = describeVariableAccess(input);
  // Validation/recovery state: a stored binding of another syntax is kept until the author picks a variable.
  const foreignSyntax = readForeignBindingSyntax(activity, input, expressionDescriptors);

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
      {authoring ? (
        <VariableTargetSelect
          ariaLabelledBy={labelId}
          ariaDescribedBy={foreignSyntax ? `${accessId} ${noteId}` : accessId}
          visibleVariables={compatibleVariables(authoring.visibleVariables, authoring.workflowState, input)}
          status={authoring.status}
          retry={authoring.retry}
          disabled={readOnly || input.isReadOnly === true}
          selected={selectedOptionKey(readBoundVariable(activity, input))}
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
  scopeReady = true
}: {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor;
  visibleVariables: VisibleVariableView[];
  scopeReady?: boolean;
}) {
  if (descriptor.accessesUndeclaredVariables === true) {
    return <p className="wf-variable-writes" role="note">May write any visible variable</p>;
  }
  const names = readMayWriteSet(activity, descriptor, visibleVariables, scopeReady);
  if (names.length === 0) return null;
  return <p className="wf-variable-writes" role="note">May write: {names.join(", ")}</p>;
}
