import { useId, type Ref } from "react";
import { Link2 } from "lucide-react";
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

export function readWorkflowRequestMemberKey(value: unknown, expressionType: string): string | null {
  if (expressionType.toLowerCase() !== "workflowrequest" || !value || typeof value !== "object" || Array.isArray(value)) return null;
  const memberKey = (value as Record<string, unknown>).memberKey;
  return typeof memberKey === "string" && memberKey.trim() ? memberKey : null;
}

export function withWorkflowInputExpressionArgument(
  value: unknown,
  expressionType: string,
  activityInput: StudioActivityInputDescriptor,
  workflowInput: WorkflowInput,
  inputBindingTarget: "name" | "referenceKey" = "name"
): PortableExpressionDefinitionValue | null {
  const definition = createPortableExpressionDefinition(value, expressionType, portableResultType(activityInput));
  if (!definition) return null;
  const hasExisting = Object.prototype.hasOwnProperty.call(definition.parameters, workflowInput.name);
  const existing = hasExisting ? definition.parameters[workflowInput.name] : null;
  return existing != null && !isWorkflowRequestBinding(existing)
    ? null
    : { ...definition, parameters: { ...definition.parameters, [workflowInput.name]: { kind: "WorkflowRequest", memberKey: workflowInput[inputBindingTarget] } } };
}

/** Common carrier preservation; callers decide the authoritative result type and admission. */
export function createPortableExpressionDefinition(value: unknown, expressionType: string, resultType: Record<string, unknown>): PortableExpressionDefinitionValue | null {
  const existing = readPortableExpressionDefinition(value, expressionType);
  if (existing) return existing;
  if (value != null && typeof value === "object") return null;
  return { language: expressionType, source: typeof value === "string" ? value : value == null ? "" : String(value),
    resultType, parameters: {}, options: {}, capabilityProfile: "binding-pure-v1" };
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
  inputBindingTarget: "name" | "referenceKey";
  bindingMode: "arguments" | "direct";
  value: unknown;
  syntax: string;
  activityInput: StudioActivityInputDescriptor;
  wrapped: WrappedActivityInputValue;
  disabled: boolean;
  expanded: boolean;
  bindingsId: string;
  onChange(value: WrappedActivityInputValue): void;
  onDirectInputSelect?(input: WorkflowInput): void;
  onExpandedChange(expanded: boolean): void;
  onEscape(): void;
}

interface WorkflowInputArgumentBindingsToggleProps {
  inputBindingTarget: "name" | "referenceKey";
  count: number;
  expanded: boolean;
  controls: string;
  onClick(): void;
  buttonRef: Ref<HTMLButtonElement>;
}

export function WorkflowInputArgumentBindingsToggle({ inputBindingTarget, count, expanded, controls, onClick, buttonRef }: WorkflowInputArgumentBindingsToggleProps) {
  const label = `${inputBindingTarget === "referenceKey" ? "Activity" : "Workflow"} input bindings`;
  return (
    <button
      ref={buttonRef}
      type="button"
      className={`wf-input-bindings-toggle${expanded || count > 0 ? " authored" : ""}`}
      aria-label={`${label}${count > 0 ? ` (${count})` : ""}`}
      title={label}
      aria-expanded={expanded}
      aria-controls={controls}
      onClick={onClick}
    >
      <Link2 size={13} />
    </button>
  );
}

export function WorkflowInputArgumentBindings({
  inputs,
  inputBindingTarget,
  bindingMode,
  value,
  syntax,
  activityInput,
  wrapped,
  disabled,
  expanded,
  bindingsId,
  onChange,
  onDirectInputSelect,
  onExpandedChange,
  onEscape
}: WorkflowInputArgumentBindingsProps) {
  const inputKind = inputBindingTarget === "referenceKey" ? "activity" : "workflow";
  const selectId = useId();
  const arguments_ = readWorkflowInputExpressionArguments(value, syntax);
  const bindingsByArgumentName = new Map(arguments_.map(binding => [binding.argumentName, binding]));
  const directMemberKey = bindingMode === "direct" ? readWorkflowRequestMemberKey(value, syntax) : null;
  const directInput = inputs.find(input => input.referenceKey === directMemberKey);

  return (
    <div
      id={bindingsId}
      className="wf-js-input-bindings wf-js-input-bindings-content"
      hidden={!expanded}
      onKeyDown={event => {
        if (event.key !== "Escape" || event.defaultPrevented) return;
        event.preventDefault();
        event.stopPropagation();
        onExpandedChange(false);
        onEscape();
      }}
    >
      {expanded ? <>
          <div className="wf-js-input-binding-add">
            <label htmlFor={selectId}>Add {inputKind} input</label>
            <select
              id={selectId}
              value={bindingMode === "direct" ? directMemberKey ?? "" : ""}
              disabled={disabled || inputs.length === 0}
              onChange={event => {
                const selected = inputs.find(workflowInput => workflowInput[inputBindingTarget] === event.currentTarget.value);
                if (!selected) return;
                if (bindingMode === "direct") {
                  onDirectInputSelect?.(selected);
                  return;
                }
                const definition = withWorkflowInputExpressionArgument(value, syntax, activityInput, selected, inputBindingTarget);
                if (definition) onChange(withLiteralValue(wrapped, definition));
              }}
            >
              <option value="">Select an input…</option>
              {bindingMode === "direct" && directMemberKey && !directInput ? (
                <option value={directMemberKey} disabled>{directMemberKey} — missing activity input</option>
              ) : null}
              {inputs.map(workflowInput => {
                if (bindingMode === "direct") {
                  return <option key={workflowInput.referenceKey} value={workflowInput.referenceKey}>
                    {workflowInput.displayName?.trim() || workflowInput.name}
                  </option>;
                }
                const alreadyBound = bindingsByArgumentName.has(workflowInput.name);
                const occupied = valueHasParameter(value, syntax, workflowInput.name) && !alreadyBound;
                return <option key={workflowInput.referenceKey} value={workflowInput.name} disabled={alreadyBound || occupied}>{workflowInput.name}{occupied ? " — argument name already used" : ""}</option>;
              })}
            </select>
          </div>
          {arguments_.length > 0 ? (
            <ul className="wf-reference-list wf-js-input-binding-list" aria-label={`Bound ${inputKind} inputs`}>
              {arguments_.map(binding => {
                const declaration = inputs.find(input => input[inputBindingTarget] === binding.memberKey);
                const label = declaration?.name || binding.memberKey || `Unknown ${inputKind} input`;
                const missing = !binding.memberKey || !declaration;
                return (
                  <li className="wf-reference-meta" key={binding.argumentName}>
                    <span>{label}{missing ? (binding.memberKey ? ` (missing ${inputKind} input)` : ` (invalid ${inputKind} input binding)`) : ""} <span aria-hidden="true">→</span> <code>{formatArgumentReference(binding.argumentName)}</code></span>
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
