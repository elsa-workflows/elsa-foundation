import { useId } from "react";
import type { StudioActivityInputDescriptor, StudioExpressionSymbol, StudioExpressionToolingState } from "@elsa-workflows/studio-sdk";
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

interface ActivityResultBinding { kind: "ActivityResult"; producerNodeId: string; projectionKey: string; isOptional: boolean }

function readActivityResultBinding(value: unknown): ActivityResultBinding | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const binding = value as Record<string, unknown>;
  if (typeof binding.kind !== "string" || binding.kind.toLowerCase() !== "activityresult" ||
      typeof binding.producerNodeId !== "string" || !binding.producerNodeId.trim() ||
      typeof binding.projectionKey !== "string" || !binding.projectionKey.trim() ||
      (binding.isOptional !== undefined && typeof binding.isOptional !== "boolean")) return null;
  return { kind: "ActivityResult", producerNodeId: binding.producerNodeId, projectionKey: binding.projectionKey, isOptional: binding.isOptional === true };
}

function activityResultChoiceKey(symbol: StudioExpressionSymbol): string {
  const binding = readActivityResultBinding(symbol.parameterBinding);
  return binding ? JSON.stringify([binding.producerNodeId, binding.projectionKey, binding.isOptional]) : "";
}

export function withActivityResultExpressionArgument(
  value: unknown, expressionType: string, activityInput: StudioActivityInputDescriptor, symbol: StudioExpressionSymbol
): PortableExpressionDefinitionValue | null {
  const binding = readActivityResultBinding(symbol.parameterBinding);
  if (!binding) return null;
  const existing = readPortableExpressionDefinition(value, expressionType);
  if (!existing && value != null && typeof value === "object") return null;
  const definition = existing ?? { language: expressionType, source: typeof value === "string" ? value : value == null ? "" : String(value),
    resultType: portableResultType(activityInput), parameters: {}, options: {}, capabilityProfile: "binding-pure-v1" };
  let name = "result", suffix = 1;
  while (Object.prototype.hasOwnProperty.call(definition.parameters, name)) name = `result${++suffix}`;
  return { ...definition, parameters: { ...definition.parameters, [name]: binding } };
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
  const fullName = stripAssemblyQualifiers(input.typeName);
  const alias = fullName.replace(/^System\.(String|Object|Boolean|Byte|SByte|Int16|UInt16|Int32|UInt32|Int64|UInt64|Single|Double|Decimal|Char|DateTime|DateTimeOffset|TimeSpan|Guid)$/, "$1");
  return { alias: alias || "Elsa.Any", collectionKind };
}

function stripAssemblyQualifiers(typeName: string): string {
  const trimmed = typeName.trim();
  let result = "";
  let depth = 0;

  for (let index = 0; index < trimmed.length; index++) {
    const char = trimmed[index];
    if (char === "[") {
      depth++;
      result += char;
    } else if (char === "]") {
      depth--;
      result += char;
    } else if (char === "," && depth === 0) {
      break;
    } else {
      result += char;
    }
  }

  return stripGenericArgumentAssemblyQualifiers(result.trim());
}

function stripGenericArgumentAssemblyQualifiers(typeName: string): string {
  let result = "";

  for (let index = 0; index < typeName.length; index++) {
    if (typeName[index] !== "`") {
      result += typeName[index];
      continue;
    }

    let open = index + 1;
    while (open < typeName.length && /\d/.test(typeName[open]!)) open++;
    if (open === index + 1 || typeName[open] !== "[") {
      result += typeName[index];
      continue;
    }

    const close = findMatchingBracket(typeName, open);
    if (close < 0) {
      result += typeName.slice(index);
      break;
    }

    const arguments_ = splitGenericArguments(typeName.slice(open + 1, close)).map(argument => {
      const trimmed = argument.trim();
      if (isBracketedTypeArgument(trimmed)) {
        return `[${stripAssemblyQualifiers(trimmed.slice(1, -1))}]`;
      }
      return stripAssemblyQualifiers(trimmed);
    });
    result += `${typeName.slice(index, open + 1)}${arguments_.join(",")}]`;
    index = close;
  }

  return result;
}

function splitGenericArguments(text: string): string[] {
  const arguments_: string[] = [];
  let depth = 0;
  let start = 0;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (char === "[") depth++;
    else if (char === "]") depth--;
    else if (char === "," && depth === 0) {
      arguments_.push(text.slice(start, index));
      start = index + 1;
    }
  }
  arguments_.push(text.slice(start));
  return arguments_.filter(argument => argument.trim().length > 0);
}

function findMatchingBracket(text: string, open: number): number {
  let depth = 0;
  for (let index = open; index < text.length; index++) {
    if (text[index] === "[") depth++;
    else if (text[index] === "]" && --depth === 0) return index;
  }
  return -1;
}

function isBracketedTypeArgument(text: string): boolean {
  return text.startsWith("[") && findMatchingBracket(text, 0) === text.length - 1;
}

interface WorkflowInputArgumentBindingsProps {
  inputs: WorkflowInput[];
  value: unknown;
  syntax: string;
  activityInput: StudioActivityInputDescriptor;
  wrapped: WrappedActivityInputValue;
  disabled: boolean;
  resultSymbols?: StudioExpressionSymbol[];
  resultState?: StudioExpressionToolingState | "loading";
  onRequestResults?(): void;
  onChange(value: WrappedActivityInputValue): void;
}

export function WorkflowInputArgumentBindings({
  inputs,
  value,
  syntax,
  activityInput,
  wrapped,
  disabled,
  resultSymbols,
  resultState,
  onRequestResults,
  onChange
}: WorkflowInputArgumentBindingsProps) {
  const selectId = useId();
  const arguments_ = readWorkflowInputExpressionArguments(value, syntax);
  const bindingsByArgumentName = new Map(arguments_.map(binding => [binding.argumentName, binding]));

  return (
    <div className="wf-js-input-bindings">
      {inputs.length > 0 || arguments_.length > 0 ? <>
      <label className="wf-section-label" htmlFor={selectId}>Public input bindings</label>
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
        <option value="">Add public input…</option>
        {inputs.map(workflowInput => {
          const alreadyBound = bindingsByArgumentName.has(workflowInput.name);
          const occupied = valueHasParameter(value, syntax, workflowInput.name) && !alreadyBound;
          return <option key={workflowInput.referenceKey} value={workflowInput.name} disabled={alreadyBound || occupied}>{workflowInput.name}{occupied ? " — argument name already used" : ""}</option>;
        })}
      </select>
      {arguments_.length > 0 ? (
        <ul className="wf-reference-list" aria-label="Bound public inputs">
          {arguments_.map(binding => {
            const label = binding.memberKey || "Unknown public input";
            const missing = !binding.memberKey || !inputs.some(input => input.name === binding.memberKey);
            return (
              <li className="wf-reference-meta" key={binding.argumentName}>
                <span>{label}{missing ? (binding.memberKey ? " (missing public input)" : " (invalid public input binding)") : ""} <span aria-hidden="true">→</span> <code>{formatArgumentReference(binding.argumentName)}</code></span>
                <button
                  type="button"
                  className="wf-reference-toggle"
                  aria-label={`Remove ${binding.argumentName} binding`}
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
      </> : null}
      {onRequestResults || hasActivityResultArguments(value, syntax) ? <ActivityResultArgumentBindings
        value={value} syntax={syntax} activityInput={activityInput} wrapped={wrapped} disabled={disabled}
        symbols={resultSymbols ?? []} state={resultState} onRequest={onRequestResults} onChange={onChange}
      /> : null}
    </div>
  );
}

function hasActivityResultArguments(value: unknown, syntax: string) {
  return Object.values(readPortableExpressionDefinition(value, syntax)?.parameters ?? {}).some(binding =>
    binding && typeof binding === "object" && typeof (binding as Record<string, unknown>).kind === "string" &&
    ((binding as Record<string, unknown>).kind as string).toLowerCase() === "activityresult");
}

function ActivityResultArgumentBindings({ value, syntax, activityInput, wrapped, disabled, symbols, state, onRequest, onChange }: {
  value: unknown; syntax: string; activityInput: StudioActivityInputDescriptor; wrapped: WrappedActivityInputValue;
  disabled: boolean; symbols: StudioExpressionSymbol[]; state?: StudioExpressionToolingState | "loading"; onRequest?(): void;
  onChange(value: WrappedActivityInputValue): void;
}) {
  const selectId = useId();
  const parameters = readPortableExpressionDefinition(value, syntax)?.parameters ?? {};
  const bound = Object.entries(parameters).filter(([, binding]) => binding && typeof binding === "object" &&
    typeof (binding as Record<string, unknown>).kind === "string" && ((binding as Record<string, unknown>).kind as string).toLowerCase() === "activityresult");
  const ready = state === "ready" || state === "supported-empty";
  const choices = ready ? symbols.filter(symbol => readActivityResultBinding(symbol.parameterBinding)) : [];
  const incomplete = ready && choices.length !== symbols.length;
  const same = (left: ActivityResultBinding | null, right: ActivityResultBinding | null) => left && right &&
    left.producerNodeId === right.producerNodeId && left.projectionKey === right.projectionKey && left.isOptional === right.isOptional;
  const placeholder = !state ? "Choose to load earlier results…" : state === "loading" ? "Loading earlier results…" : !ready
    ? "Earlier results unavailable" : choices.length === 0 ? incomplete ? "Earlier results unavailable" : "No earlier results" : "Add earlier result…";
  return <div className="wf-js-input-bindings">
    <label className="wf-section-label" htmlFor={selectId}>Earlier result bindings</label>
    <select id={selectId} value="" disabled={disabled} onFocus={onRequest} onChange={event => {
      const selected = choices.find(symbol => activityResultChoiceKey(symbol) === event.currentTarget.value);
      if (!selected) return;
      const definition = withActivityResultExpressionArgument(value, syntax, activityInput, selected);
      if (definition) onChange(withLiteralValue(wrapped, definition));
    }}>
      <option value="">{placeholder}</option>
      {choices.map(symbol => <option key={activityResultChoiceKey(symbol)} value={activityResultChoiceKey(symbol)}
        disabled={bound.some(([, binding]) => same(readActivityResultBinding(binding), readActivityResultBinding(symbol.parameterBinding)))}>{symbol.name}</option>)}
    </select>
    {incomplete && choices.length > 0 ? <p role="status">Some earlier results are unavailable.</p> : null}
    {incomplete || state && !ready && state !== "loading" ? <button type="button" className="wf-reference-toggle" disabled={disabled || !onRequest} onClick={onRequest}>Retry earlier results</button> : null}
    {bound.length > 0 ? <ul className="wf-reference-list" aria-label="Bound earlier results">{bound.map(([name, raw]) => {
      const binding = readActivityResultBinding(raw);
      const symbol = binding && symbols.find(candidate => same(binding, readActivityResultBinding(candidate.parameterBinding)));
      return <li className="wf-reference-meta" key={name}>
        <span>{symbol?.name ?? (binding ? `${binding.producerNodeId}.${binding.projectionKey}${ready ? " (unavailable)" : ""}` : "Invalid activity result binding")} <span aria-hidden="true">→</span> <code>{formatArgumentReference(name)}</code></span>
        <button type="button" className="wf-reference-toggle" aria-label={`Remove ${name} result binding`} disabled={disabled} onClick={() => {
          const definition = withoutPortableExpressionParameter(value, syntax, name);
          if (definition) onChange(withLiteralValue(wrapped, definition));
        }}>Remove</button>
      </li>;
    })}</ul> : null}
  </div>;
}

export default WorkflowInputArgumentBindings;

function valueHasParameter(value: unknown, syntax: string, name: string) {
  const parameters = readPortableExpressionDefinition(value, syntax)?.parameters;
  return !!parameters && Object.prototype.hasOwnProperty.call(parameters, name);
}

function formatArgumentReference(name: string) {
  return /^[A-Za-z_$][\w$]*$/.test(name) ? `args.${name}` : `args[${JSON.stringify(name)}]`;
}
