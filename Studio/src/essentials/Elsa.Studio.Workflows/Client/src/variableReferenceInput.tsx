import { useId } from "react";
import type { StudioActivityDescriptor, StudioActivityInputDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { formatTypeName, readWrappedInput, withExpression, writeInputValue } from "./activityProperties";
import { makeVariableReference, readVariableReference } from "./scopedVariables";
import { optionKey, selectedOptionKey, VariableTargetSelect } from "./variableTargetPicker";
import { useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";
import type { ActivityNode, VariableReference, VisibleVariableView } from "./workflowTypes";

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
  visibleVariables: VisibleVariableView[]
): string[] {
  const names: string[] = [];
  for (const input of descriptor.inputs) {
    if (input.variableAccess !== "ReadWrite") continue;
    const bound = readBoundVariable(activity, input);
    if (!bound) continue;
    const key = selectedOptionKey(bound);
    const name = visibleVariables.find(variable => optionKey(variable.referenceKey, variable.scopeId) === key)?.name
      ?? bound.referenceKey;
    if (!names.includes(name)) names.push(name);
  }
  return names;
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
        <p className="wf-variable-picker-note" role="note">
          Bound with {foreignSyntax}; this input accepts only a declared variable. Pick one to replace it.
        </p>
      ) : null}
      {authoring ? (
        <VariableTargetSelect
          ariaLabelledBy={labelId}
          ariaDescribedBy={accessId}
          visibleVariables={authoring.visibleVariables}
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
  visibleVariables
}: {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor;
  visibleVariables: VisibleVariableView[];
}) {
  if (descriptor.accessesUndeclaredVariables === true) {
    return <p className="wf-variable-writes" role="note">May write any visible variable</p>;
  }
  const names = readMayWriteSet(activity, descriptor, visibleVariables);
  if (names.length === 0) return null;
  return <p className="wf-variable-writes" role="note">May write: {names.join(", ")}</p>;
}
