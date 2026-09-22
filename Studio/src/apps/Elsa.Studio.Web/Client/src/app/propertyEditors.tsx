import { useEffect, useId, useState } from "react";
import {
  readActivityInputOptionsProvider,
  type ElsaStudioModuleApi,
  type StudioActivityInputDescriptor,
  type StudioActivityPropertyEditorContext,
  type StudioActivityPropertyEditorContribution,
  type StudioActivityPropertyEditorProps
} from "../sdk";

const textLikeTypes = new Set(["string", "system.string", "text"]);

// CLR numeric primitives (short simple name after the last dot). Integer families are separated so the
// editor can reject fractional input for integer types while allowing it for real/decimal types.
const integerTypeNames = new Set([
  "int", "int16", "int32", "int64", "short", "long", "byte", "sbyte",
  "uint", "uint16", "uint32", "uint64", "ushort", "ulong", "nint", "nuint",
  "biginteger"
]);
const realTypeNames = new Set(["decimal", "double", "single", "float"]);
const timeSpanTypeNames = new Set(["timespan", "system.timespan"]);

// Accepts the common .NET TimeSpan literals: "hh:mm:ss", "d.hh:mm:ss", either with an optional
// fractional-seconds tail, plus the bare "hh:mm" shorthand. Deliberately lenient — the authoritative
// parse is server-side; this only stops obviously malformed text from being accepted silently.
const timeSpanPattern = /^-?(\d+\.)?\d{1,2}:\d{2}(:\d{2}(\.\d{1,7})?)?$/;

export function registerBuiltInPropertyEditors(api: ElsaStudioModuleApi) {
  for (const editor of builtInPropertyEditors) {
    api.propertyEditors.add(editor);
  }
}

export const builtInPropertyEditors: StudioActivityPropertyEditorContribution[] = [
  {
    id: "studio.property.multiselect",
    order: 90,
    // Collection-scoped: owns the whole collection as a checklist when the input declares an option
    // source (so an enum/provider collection prevents duplicates instead of repeating dropdowns).
    supports: (descriptor, context) => isCollectionScope(context) && !hasUiHint(descriptor, "dropdown") && (hasUiHint(descriptor, "checklist") || hasOptionSource(descriptor)),
    component: MultiSelectEditor
  },
  {
    id: "studio.property.dropdown",
    order: 100,
    supports: (descriptor, context) => isElementScope(context) && (hasUiHint(descriptor, "dropdown") || hasOptionSource(descriptor)),
    component: DropdownEditor
  },
  {
    id: "studio.property.checkbox",
    order: 120,
    supports: (descriptor, context) => isElementScope(context) && (hasUiHint(descriptor, "checkbox") || isBooleanDescriptor(descriptor)),
    component: CheckboxEditor
  },
  {
    id: "studio.property.timespan",
    order: 128,
    // Element-scoped TimeSpan fields (scalar or collection element): typed input with a format hint and
    // inline validation, so an unparsable duration is flagged while authoring instead of at preflight.
    supports: (descriptor, context) => isElementScope(context) && !hasOptionSource(descriptor) && isTimeSpanDescriptor(descriptor),
    component: TimeSpanEditor
  },
  {
    id: "studio.property.number",
    order: 130,
    // Element-scoped numeric fields (scalar or collection element, e.g. an Int32[] item): a number
    // input that rejects non-numeric text inline rather than letting it reach the server as a literal.
    supports: (descriptor, context) => isElementScope(context) && !hasOptionSource(descriptor) && isNumericDescriptor(descriptor),
    component: NumericEditor
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
  const renderedOptions = withUnavailableOptions(options, selected);
  const toggle = (optionValue: unknown, checked: boolean) => {
    const next = checked
      ? [...selected.filter(item => !sameOptionValue(item, optionValue)), optionValue]
      : selected.filter(item => !sameOptionValue(item, optionValue));
    onChange(next);
  };

  if (renderedOptions.length === 0) {
    if (readActivityInputOptionsProvider(descriptor)) return null;
    return <p className="studio-property-multiselect-empty">No options available.</p>;
  }

  return (
    <div className="studio-property-multiselect" role="group">
      {renderedOptions.map(option => (
        <label key={optionKey(option.value)} className={option.unavailable ? "studio-property-multiselect-option unavailable" : "studio-property-multiselect-option"}>
          <input
            type="checkbox"
            checked={selected.some(item => sameOptionValue(item, option.value))}
            disabled={disabled}
            onChange={event => toggle(option.value, event.target.checked)}
          />
          <span>{option.unavailable ? `${option.label} (unavailable)` : option.label}</span>
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
  return optionKey(left) === optionKey(right);
}

function SinglelineEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  // An enum-typed input with no options payload can't offer a select; fall back to text but hint the
  // caller (via title) that a specific member name is expected instead of an arbitrary string.
  const enumHint = isEnumDescriptor(descriptor) && !hasOptionSource(descriptor)
    ? `Enter a valid ${formatSimpleTypeName(descriptor.typeName)} value (enum member name).`
    : undefined;
  return (
    <input
      type="text"
      aria-label={accessibleName(descriptor)}
      title={enumHint}
      value={value == null ? "" : String(value)}
      disabled={disabled}
      placeholder={stringValue(descriptor.defaultValue) || (enumHint ? formatSimpleTypeName(descriptor.typeName) : "")}
      onChange={event => onChange(event.target.value)}
    />
  );
}

function MultilineEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  return (
    <textarea
      aria-label={accessibleName(descriptor)}
      value={value == null ? "" : String(value)}
      disabled={disabled}
      rows={4}
      onChange={event => onChange(event.target.value)}
    />
  );
}

function NumericEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  const integer = isIntegerDescriptor(descriptor);
  const errorId = useId();
  const [text, setText] = useState(() => numericText(value));
  const [error, setError] = useState<string | null>(null);

  // Reconcile external value changes (autosave round-trips numbers back as strings) without clobbering
  // an in-progress invalid entry: only re-seed the field when the incoming value differs numerically.
  useEffect(() => {
    if (error) return;
    const incoming = numericText(value);
    setText(current => (Number(current) === Number(incoming) && current.trim() !== "" ? current : incoming));
  }, [value, error]);

  const commit = (next: string) => {
    setText(next);
    const trimmed = next.trim();
    if (trimmed === "") {
      setError(null);
      onChange("");
      return;
    }
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed)) {
      setError(`Enter a valid ${integer ? "whole " : ""}number.`);
      return;
    }
    if (integer && !Number.isInteger(parsed)) {
      setError("Enter a whole number without a decimal point.");
      return;
    }
    setError(null);
    onChange(parsed);
  };

  return (
    <>
      <input
        type="text"
        inputMode={integer ? "numeric" : "decimal"}
        aria-label={accessibleName(descriptor)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        value={text}
        disabled={disabled}
        spellCheck={false}
        autoComplete="off"
        placeholder={stringValue(descriptor.defaultValue)}
        onChange={event => commit(event.target.value)}
      />
      {error ? <p id={errorId} className="studio-property-error" role="alert">{error}</p> : null}
    </>
  );
}

function TimeSpanEditor({ descriptor, value, disabled, onChange }: StudioActivityPropertyEditorProps) {
  const errorId = useId();
  const hintId = useId();
  const [text, setText] = useState(() => (value == null ? "" : String(value)));
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (error) return;
    setText(value == null ? "" : String(value));
  }, [value, error]);

  const commit = (next: string) => {
    setText(next);
    const trimmed = next.trim();
    if (trimmed === "" || timeSpanPattern.test(trimmed)) {
      setError(null);
      onChange(next);
      return;
    }
    setError("Enter a duration as hh:mm:ss (optionally d.hh:mm:ss).");
  };

  return (
    <>
      <input
        type="text"
        aria-label={accessibleName(descriptor)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : hintId}
        value={text}
        disabled={disabled}
        spellCheck={false}
        autoComplete="off"
        placeholder="hh:mm:ss"
        onChange={event => commit(event.target.value)}
      />
      {error
        ? <p id={errorId} className="studio-property-error" role="alert">{error}</p>
        : <p id={hintId} className="studio-property-hint">Format: hh:mm:ss (e.g. 00:00:30), or d.hh:mm:ss for days.</p>}
    </>
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
  const renderedOptions = withUnavailableOptions(options, value == null || value === "" ? [] : [value]);
  return (
    <select aria-label={accessibleName(descriptor)} value={value == null || value === "" ? "" : optionKey(value)} disabled={disabled} onChange={event => {
      const option = renderedOptions.find(candidate => optionKey(candidate.value) === event.target.value);
      onChange(option ? option.value : event.target.value);
    }}>
      <option value="">Select...</option>
      {renderedOptions.map(option => (
        <option key={optionKey(option.value)} value={optionKey(option.value)}>
          {option.unavailable ? `${option.label} (unavailable)` : option.label}
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

function hasOptionSource(descriptor: StudioActivityInputDescriptor) {
  return getOptions(descriptor).length > 0 || !!readActivityInputOptionsProvider(descriptor);
}

function withUnavailableOptions(
  options: Array<{ label: string; value: unknown }>,
  selected: unknown[]
): Array<{ label: string; value: unknown; unavailable?: boolean }> {
  const result: Array<{ label: string; value: unknown; unavailable?: boolean }> = [...options];
  for (const value of selected) {
    if (result.some(option => sameOptionValue(option.value, value))) continue;
    result.push({ label: String(value), value, unavailable: true });
  }
  return result;
}

function optionKey(value: unknown) {
  if (value === null) return "null:null";
  if (typeof value === "object") {
    try { return `object:${JSON.stringify(value)}`; } catch { return `object:${String(value)}`; }
  }
  return `${typeof value}:${String(value)}`;
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

function isNumericDescriptor(descriptor: StudioActivityInputDescriptor) {
  const simple = simpleTypeName(descriptor.typeName);
  return integerTypeNames.has(simple) || realTypeNames.has(simple);
}

function isIntegerDescriptor(descriptor: StudioActivityInputDescriptor) {
  return integerTypeNames.has(simpleTypeName(descriptor.typeName));
}

function isTimeSpanDescriptor(descriptor: StudioActivityInputDescriptor) {
  return timeSpanTypeNames.has(descriptor.typeName.trim().toLowerCase());
}

// A descriptor is enum-like when the backend flags it as one. #924 will report enum members via
// uiSpecifications; until then honour an explicit `enum` marker or an `enum` ui hint. Keyed off
// descriptor metadata only — never off the activity type — so no activity-specific hacks leak in.
function isEnumDescriptor(descriptor: StudioActivityInputDescriptor) {
  if (hasUiHint(descriptor, "enum")) return true;
  const specs = descriptor.uiSpecifications as (Record<string, unknown> | null | undefined);
  const marker = specs?.enum ?? specs?.isEnum ?? specs?.enumType;
  return marker === true || (typeof marker === "string" && marker.trim().length > 0);
}

function accessibleName(descriptor: StudioActivityInputDescriptor) {
  return descriptor.displayName?.trim() || descriptor.name;
}

// Short simple name after the last "." (and nested-type "+"), lower-cased — e.g. "System.Int32" -> "int32".
function simpleTypeName(typeName: string): string {
  const core = typeName.split(",", 1)[0]?.trim() ?? "";
  const segment = core.split(".").filter(Boolean).at(-1) ?? core;
  return (segment.split("+").filter(Boolean).at(-1) ?? segment).toLowerCase();
}

function formatSimpleTypeName(typeName: string): string {
  const core = typeName.split(",", 1)[0]?.trim() ?? typeName;
  const segment = core.split(".").filter(Boolean).at(-1) ?? core;
  return segment.split("+").filter(Boolean).at(-1) ?? segment;
}

function numericText(value: unknown): string {
  if (value == null || value === "") return "";
  if (typeof value === "number") return Number.isFinite(value) ? String(value) : "";
  return String(value);
}

function stringValue(value: unknown) {
  return typeof value === "string" ? value : "";
}
