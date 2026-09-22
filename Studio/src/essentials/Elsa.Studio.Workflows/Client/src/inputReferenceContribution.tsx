import type {
  StudioContributionRegistry,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";
import { readArgumentType } from "./workflowProperties";
import {
  formatArgumentType,
  isArgumentTypeCompatible,
  useWorkflowReferenceAuthoring
} from "./workflowReferenceAuthoring";
import type { InputReference, WorkflowInput } from "./workflowTypes";

const inputSyntax = "Input";

export const inputReferenceContribution: StudioExpressionEditorContribution = {
  id: "studio.workflows.input-reference",
  order: 110,
  supports: context => context.syntax === inputSyntax,
  surfaces: { inline: InputReferenceEditor },
  createDefaultValue: () => null
};

export function registerInputReferenceContribution(
  registry: StudioContributionRegistry<StudioExpressionEditorContribution>
) {
  registry.add(inputReferenceContribution);
}

function InputReferenceEditor({ descriptor, value, disabled, onChange }: StudioExpressionEditorProps) {
  const authoring = useWorkflowReferenceAuthoring();
  if (!authoring) {
    return <p className="wf-variable-picker-note">Input authoring context is unavailable. The current reference is preserved.</p>;
  }

  const options = authoring.workflowInputs.map(input => ({
    input,
    type: readArgumentType(input),
    compatible: isArgumentTypeCompatible(readArgumentType(input), descriptor.typeName)
  }));
  const compatible = options.filter(option => option.compatible);
  const incompatible = options.filter(option => !option.compatible);
  const current = readInputReference(value);
  const currentKey = current?.referenceKey ?? "";
  const currentIsAvailable = currentKey !== "" && options.some(option => option.input.referenceKey === currentKey);

  return (
    <div className="wf-variable-picker">
      <select
        aria-label="Input reference"
        value={currentKey}
        disabled={disabled}
        onChange={event => {
          if (event.target.value) onChange({ referenceKey: event.target.value } satisfies InputReference);
        }}
      >
        <option value="">Select an input…</option>
        {current && !currentIsAvailable ? (
          <option value={currentKey} disabled>{current.referenceKey} (not available)</option>
        ) : null}
        {compatible.length > 0 ? (
          <optgroup label="Compatible inputs">
            {compatible.map(option => <InputOption key={option.input.referenceKey} {...option} />)}
          </optgroup>
        ) : null}
        {incompatible.length > 0 ? (
          <optgroup label="Other inputs">
            {incompatible.map(option => <InputOption key={option.input.referenceKey} {...option} disabled />)}
          </optgroup>
        ) : null}
      </select>
      {options.length === 0 ? (
        <p className="wf-variable-picker-note">No workflow inputs are available. Add one in workflow properties.</p>
      ) : null}
    </div>
  );
}

function InputOption({ input, type, disabled = false }: {
  input: WorkflowInput;
  type: ReturnType<typeof readArgumentType>;
  disabled?: boolean;
}) {
  const label = input.displayName?.trim() || input.name;
  return (
    <option value={input.referenceKey} disabled={disabled}>
      {label} · {formatArgumentType(type)}{disabled ? " (incompatible)" : ""}
    </option>
  );
}

export function readInputReference(value: unknown): InputReference | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const referenceKey = (value as Record<string, unknown>).referenceKey;
  return typeof referenceKey === "string" && referenceKey.trim() ? { referenceKey } : null;
}
