import type {
  ElsaStudioModuleApi,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorProps
} from "../sdk";

const textLikeTypes = new Set(["string", "system.string", "text"]);

export function registerBuiltInPropertyEditors(api: ElsaStudioModuleApi) {
  for (const editor of builtInPropertyEditors) {
    api.propertyEditors.add(editor);
  }
}

export const builtInPropertyEditors: StudioActivityPropertyEditorContribution[] = [
  {
    id: "studio.property.dropdown",
    order: 100,
    supports: descriptor => hasUiHint(descriptor, "dropdown") || getOptions(descriptor).length > 0,
    component: DropdownEditor
  },
  {
    id: "studio.property.checkbox",
    order: 120,
    supports: descriptor => hasUiHint(descriptor, "checkbox") || isBooleanDescriptor(descriptor),
    component: CheckboxEditor
  },
  {
    id: "studio.property.multiline",
    order: 140,
    supports: descriptor => hasUiHint(descriptor, "multiline"),
    component: MultilineEditor
  },
  {
    id: "studio.property.singleline",
    order: 160,
    supports: descriptor => hasUiHint(descriptor, "singleline") || isTextDescriptor(descriptor),
    component: SinglelineEditor
  },
  {
    id: "studio.property.text-fallback",
    order: 10_000,
    supports: () => true,
    component: SinglelineEditor
  }
];

function SinglelineEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  return (
    <input
      type="text"
      value={value == null ? "" : String(value)}
      disabled={disabled}
      placeholder={stringValue(descriptor.defaultValue)}
      onChange={event => onChange(event.target.value)}
    />
  );
}

function MultilineEditor({ value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  return (
    <textarea
      value={value == null ? "" : String(value)}
      disabled={disabled}
      rows={4}
      onChange={event => onChange(event.target.value)}
    />
  );
}

function CheckboxEditor({ value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  return (
    <label className="studio-property-checkbox">
      <input
        type="checkbox"
        checked={value === true}
        disabled={disabled}
        onChange={event => onChange(event.target.checked)}
      />
      <span>{value === true ? "Enabled" : "Disabled"}</span>
    </label>
  );
}

function DropdownEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  const options = getOptions(descriptor);
  return (
    <select value={value == null ? "" : String(value)} disabled={disabled} onChange={event => {
      const option = options.find(candidate => String(candidate.value) === event.target.value);
      onChange(option ? option.value : event.target.value);
    }}>
      <option value="">Select...</option>
      {options.map(option => (
        <option key={String(option.value)} value={String(option.value)}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

function getOptions(descriptor: StudioActivityInputDescriptor): Array<{ label: string; value: unknown }> {
  const specs = descriptor.uiSpecifications ?? {};
  const options = readOptionList(specs.options) ?? readOptionList(specs.items) ?? readOptionList(specs.values);
  if (options) return options;

  const direct = descriptor as StudioActivityInputDescriptor & { options?: unknown };
  return readOptionList(direct.options) ?? [];
}

function readOptionList(value: unknown): Array<{ label: string; value: unknown }> | null {
  if (!Array.isArray(value)) return null;
  return value.map(item => {
    if (item && typeof item === "object") {
      const record = item as Record<string, unknown>;
      const optionValue = record.value ?? record.name ?? record.id ?? record.label;
      const label = stringValue(record.label ?? record.displayName ?? record.name ?? optionValue);
      return { label: label || String(optionValue ?? ""), value: optionValue };
    }

    return { label: String(item), value: item };
  });
}

function hasUiHint(descriptor: StudioActivityInputDescriptor, hint: string) {
  return descriptor.uiHint?.toLowerCase() === hint;
}

function isBooleanDescriptor(descriptor: StudioActivityInputDescriptor) {
  const typeName = descriptor.typeName.toLowerCase();
  return typeName === "boolean" || typeName === "bool" || typeName === "system.boolean";
}

function isTextDescriptor(descriptor: StudioActivityInputDescriptor) {
  return textLikeTypes.has(descriptor.typeName.toLowerCase());
}

function stringValue(value: unknown) {
  return typeof value === "string" ? value : "";
}
