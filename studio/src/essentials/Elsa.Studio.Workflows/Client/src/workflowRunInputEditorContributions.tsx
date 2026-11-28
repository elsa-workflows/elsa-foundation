import type {
  StudioWorkflowRunInputDescriptor,
  StudioWorkflowRunInputEditorContribution,
  StudioWorkflowRunInputEditorProps,
  StudioWorkflowRunInputEnumOption
} from "@elsa-workflows/studio-sdk";

interface EnumOption {
  value: string;
  label: string;
  wireValue: unknown;
  hasWireValue: boolean;
}

export interface EnumWorkflowRunInputEditorOptions {
  id: string;
  order?: number;
  supports(input: StudioWorkflowRunInputDescriptor): boolean;
  options: StudioWorkflowRunInputEnumOption[] | ((input: StudioWorkflowRunInputDescriptor) => StudioWorkflowRunInputEnumOption[]);
}

/** Creates a picker contribution whose type match and options are owned by the registering module. */
export function createEnumWorkflowRunInputEditorContribution(
  config: EnumWorkflowRunInputEditorOptions
): StudioWorkflowRunInputEditorContribution {
  const optionsFor = (input: StudioWorkflowRunInputDescriptor) => readEnumOptions(
    typeof config.options === "function" ? config.options(input) : config.options
  );

  return {
    id: config.id,
    order: config.order,
    supports: input => input.type.collectionKind === "Single" && config.supports(input) && optionsFor(input).length > 0,
    component: props => <EnumWorkflowRunInputEditor {...props} options={optionsFor(props.input)} />,
    validate: ({ input, draft }) => optionsFor(input).some(option => option.value === draft)
      ? undefined
      : `Choose an available ${input.displayName || input.name} value.`,
    serialize: ({ input, draft }) => {
      const option = optionsFor(input).find(candidate => candidate.value === draft);
      return option?.hasWireValue ? option.wireValue : option?.value;
    }
  };
}

function EnumWorkflowRunInputEditor({ draft, disabled, controlProps, onChange, options }: StudioWorkflowRunInputEditorProps & { options: EnumOption[] }) {
  return (
    <select {...controlProps} value={draft} disabled={disabled} onChange={event => onChange(event.currentTarget.value)}>
      <option value="">Not set</option>
      {options.map(option => (
        <option key={option.value} value={option.value}>{option.label}</option>
      ))}
    </select>
  );
}

function readEnumOptions(candidates: StudioWorkflowRunInputEnumOption[]): EnumOption[] {
  return candidates.flatMap(candidate => {
    if (!candidate || typeof candidate !== "object" || Array.isArray(candidate)) return [];
    const option = candidate as Record<string, unknown>;
    const value = typeof option.value === "string" ? option.value : "";
    if (!value) return [];
    return [{
      value,
      label: typeof option.label === "string" && option.label.trim() ? option.label : value,
      wireValue: option.wireValue,
      hasWireValue: Object.prototype.hasOwnProperty.call(option, "wireValue") && option.wireValue !== undefined
    }];
  });
}
