import type {
  ElsaStudioModuleApi,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContext,
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
    id: "studio.property.multiselect",
    order: 90,
    // Collection-scoped: owns the whole collection as a checklist when the input declares a fixed
    // option set (so an enum/option collection prevents duplicates instead of repeating dropdowns).
    supports: (descriptor, context) => isCollectionScope(context) && getOptions(descriptor).length > 0,
    component: MultiSelectEditor
  },
  {
    id: "studio.property.dropdown",
    order: 100,
    supports: (descriptor, context) => isElementScope(context) && (hasUiHint(descriptor, "dropdown") || getOptions(descriptor).length > 0),
    component: DropdownEditor
  },
  {
    id: "studio.property.checkbox",
    order: 120,
    supports: (descriptor, context) => isElementScope(context) && (hasUiHint(descriptor, "checkbox") || isBooleanDescriptor(descriptor)),
    component: CheckboxEditor
  },
  {
    id: "studio.property.multiline",
    order: 140,
    supports: (descriptor, context) => isElementScope(context) && hasUiHint(descriptor, "multiline"),
    component: MultilineEditor
  },
  {
    id: "studio.property.singleline",
    order: 160,
    supports: (descriptor, context) => isElementScope(context) && (hasUiHint(descriptor, "singleline") || isTextDescriptor(descriptor)),
    component: SinglelineEditor
  },
  {
    id: "studio.property.text-fallback",
    order: 10_000,
    // Element-scoped only: in collection scope, declining here lets the panel fall back to its repeater.
    supports: (_descriptor, context) => isElementScope(context),
    component: SinglelineEditor
  }
];

function isCollectionScope(context: StudioActivityPropertyEditorContext) {
  return context.scope === "collection";
}

function isElementScope(context: StudioActivityPropertyEditorContext) {
  return context.scope !== "collection";
}

function MultiSelectEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  const options = getOptions(descriptor);
  const selected = toValueArray(value);
  const toggle = (optionValue: unknown, checked: boolean) => {
    const next = checked
      ? [...selected.filter(item => !sameOptionValue(item, optionValue)), optionValue]
      : selected.filter(item => !sameOptionValue(item, optionValue));
    onChange(next);
  };

  if (options.length === 0) {
    return <p className="studio-property-multiselect-empty">No options available.</p>;
  }

  return (
    <div className="studio-property-multiselect" role="group">
      {options.map(option => (
        <label key={String(option.value)} className="studio-property-multiselect-option">
          <input
            type="checkbox"
            checked={selected.some(item => sameOptionValue(item, option.value))}
            disabled={disabled}
            onChange={event => toggle(option.value, event.target.checked)}
          />
          <span>{option.label}</span>
        </label>
      ))}
    </div>
  );
}

// Parallel to `toLiteralCollection` in the workflows module's activityProperties.ts; see the note there
// for why this coercion is duplicated rather than shared across the type-only studio-sdk boundary.
function toValueArray(value: unknown): unknown[] {
  if (Array.isArray(value)) return value;
  if (value == null) return [];
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) return [];
    if (trimmed.startsWith("[")) {
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) return parsed;
      } catch {
        // Not JSON — treat the whole string as a single selected value.
      }
    }
    return [value];
  }
  return [value];
}

function sameOptionValue(left: unknown, right: unknown) {
  return left === right || String(left) === String(right);
}

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

function CheckboxEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  // Scalar literals are serialized as strings on the backend wire and come back that way after
  // autosave. Treat the canonical persisted value as on so reconciliation does not reset the switch.
  const checked = value === true || (typeof value === "string" && value.trim().toLowerCase() === "true");
  const label = descriptor.displayName?.trim() || descriptor.name;

  return (
    <label className="studio-property-switch">
      <input
        type="checkbox"
        role="switch"
        aria-label={label}
        checked={checked}
        disabled={disabled}
        onChange={event => onChange(event.target.checked)}
      />
      <span className="studio-property-switch-state">{checked ? "Enabled" : "Disabled"}</span>
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
