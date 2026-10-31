import { useId } from "react";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import { readPortableExpressionDefinition, withLiteralValue, type PortableExpressionDefinitionValue, type WrappedActivityInputValue } from "./activityProperties";
import type { WorkflowInput } from "./workflowTypes";

export interface WorkflowInputExpressionArgument {
  argumentName: string;
  memberKey: string | null;
}

export function readWorkflowInputExpressionArguments(value: unknown, expressionType: string): WorkflowInputExpressionArgument[] {
  const definition = readPortableExpressionDefinition(value, expressionType);
  return definition ? Object.entries(definition.parameters).flatMap(([argumentName, binding]) =>
    isWorkflowRequestBinding(binding)
      ? [{ argumentName, memberKey: typeof binding.memberKey === "string" ? binding.memberKey : null }]
      : []) : [];
}

export function withWorkflowInputExpressionArgument(
  value: unknown,
  expressionType: string,
  activityInput: StudioActivityInputDescriptor,
  workflowInput: WorkflowInput
): PortableExpressionDefinitionValue | null {
  const portableDefinition = readPortableExpressionDefinition(value, expressionType);
  if (!portableDefinition && value != null && typeof value === "object") return null;
  const definition = portableDefinition ?? {
    language: expressionType,
    source: typeof value === "string" ? value : value == null ? "" : String(value),
    resultType: portableResultType(activityInput),
    parameters: {},
    options: {},
    capabilityProfile: "binding-pure-v1"
  };
  const hasExisting = Object.prototype.hasOwnProperty.call(definition.parameters, workflowInput.name);
  const existing = hasExisting ? definition.parameters[workflowInput.name] : null;
  return existing != null && !isWorkflowRequestBinding(existing)
    ? null
    : { ...definition, parameters: { ...definition.parameters, [workflowInput.name]: { kind: "WorkflowRequest", memberKey: workflowInput.name } } };
}

export function withoutPortableExpressionParameter(
  value: unknown,
  expressionType: string,
  parameterName: string
): PortableExpressionDefinitionValue | null {
  const definition = readPortableExpressionDefinition(value, expressionType);
  if (!definition || !Object.prototype.hasOwnProperty.call(definition.parameters, parameterName)) return null;
  const parameters = { ...definition.parameters };
  delete parameters[parameterName];
  return { ...definition, parameters };
}

function isWorkflowRequestBinding(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value) &&
    typeof (value as Record<string, unknown>).kind === "string" &&
    ((value as Record<string, unknown>).kind as string).toLowerCase() === "workflowrequest";
}

function portableResultType(input: StudioActivityInputDescriptor): Record<string, unknown> {
  const collectionKind = ["Single", "Array", "List", "HashSet", "Dictionary"].find(
    kind => kind.toLowerCase() === input.collectionKind?.toLowerCase()
  ) ?? "Single";
  const fullName = input.typeName.split(",", 1)[0]!.trim();
  const alias = fullName.replace(/^System\.(String|Object|Boolean|Byte|SByte|Int16|UInt16|Int32|UInt32|Int64|UInt64|Single|Double|Decimal|Char|DateTime|DateTimeOffset|TimeSpan|Guid)$/i, "$1");
  return { alias: alias || "Elsa.Any", collectionKind };
}

interface WorkflowInputArgumentBindingsProps {
  inputs: WorkflowInput[];
  value: unknown;
  syntax: string;
  activityInput: StudioActivityInputDescriptor;
  wrapped: WrappedActivityInputValue;
  disabled: boolean;
  onChange(value: WrappedActivityInputValue): void;
}

export function WorkflowInputArgumentBindings({
  inputs,
  value,
  syntax,
  activityInput,
  wrapped,
  disabled,
  onChange
}: WorkflowInputArgumentBindingsProps) {
  const selectId = useId();
  const arguments_ = readWorkflowInputExpressionArguments(value, syntax);
  const bindingsByArgumentName = new Map(arguments_.map(binding => [binding.argumentName, binding]));

  return (
    <div className="wf-js-input-bindings">
      <label className="wf-section-label" htmlFor={selectId}>Workflow input bindings</label>
      <select
        id={selectId}
        value=""
        disabled={disabled || inputs.length === 0}
        onChange={event => {
          const selected = inputs.find(workflowInput => workflowInput.name === event.currentTarget.value);
          if (!selected) return;
          const definition = withWorkflowInputExpressionArgument(value, syntax, activityInput, selected);
          if (definition) onChange(withLiteralValue(wrapped, definition));
        }}
      >
        <option value="">Add workflow input…</option>
        {inputs.map(workflowInput => {
          const alreadyBound = bindingsByArgumentName.has(workflowInput.name);
          const occupied = valueHasParameter(value, syntax, workflowInput.name) && !alreadyBound;
          return <option key={workflowInput.referenceKey} value={workflowInput.name} disabled={alreadyBound || occupied}>{workflowInput.name}{occupied ? " — argument name already used" : ""}</option>;
        })}
      </select>
      {arguments_.length > 0 ? (
        <ul className="wf-reference-list" aria-label="Bound workflow inputs">
          {arguments_.map(binding => {
            const label = binding.memberKey || "Unknown workflow input";
            const missing = !binding.memberKey || !inputs.some(input => input.name === binding.memberKey);
            return (
              <li className="wf-reference-meta" key={binding.argumentName}>
                <span>{label}{missing ? (binding.memberKey ? " (missing workflow input)" : " (invalid workflow input binding)") : ""} <span aria-hidden="true">→</span> <code>{formatArgumentReference(binding.argumentName)}</code></span>
                <button
                  type="button"
                  className="wf-reference-toggle"
                  aria-label={`Remove ${label} workflow input binding`}
                  disabled={disabled}
                  onClick={() => {
                    const definition = withoutPortableExpressionParameter(value, syntax, binding.argumentName);
                    if (definition) onChange(withLiteralValue(wrapped, definition));
                  }}
                >
                  Remove
                </button>
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
  );
}

export default WorkflowInputArgumentBindings;

function valueHasParameter(value: unknown, syntax: string, name: string) {
  const parameters = readPortableExpressionDefinition(value, syntax)?.parameters;
  return !!parameters && Object.prototype.hasOwnProperty.call(parameters, name);
}

function formatArgumentReference(name: string) {
  return /^[A-Za-z_$][\w$]*$/.test(name) ? `args.${name}` : `args[${JSON.stringify(name)}]`;
}
