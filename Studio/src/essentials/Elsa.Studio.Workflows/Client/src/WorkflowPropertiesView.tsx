import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Plus, Trash2 } from "lucide-react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listVariableTypeDescriptors } from "./api/expressions";
import { getActivityAuthoringCapabilities } from "./api/activityDesign";
import type { ActivityAuthoringCapabilities } from "./activityDefinitionTypes";
import { IncidentStrategySection } from "./IncidentStrategySection";
import { PersistenceModeSection } from "./PersistenceModeSection";
import {
  argumentNameKeys,
  createInput,
  createOutput,
  createVariable,
  descriptorAlias,
  friendlyTypeLabel,
  generateUniqueName,
  isPlainRecord,
  literalDefault,
  readArgumentType,
  readStringField,
  readVariableDefault,
  referenceKeyKeys,
  storageDriverKeys,
  updateInput,
  updateIncidentStrategyReference,
  updateOutput,
  updatePersistenceMode,
  updateVariable,
  variableNameKeys
} from "./workflowProperties";
import { collectionKinds } from "./workflowTypes";
import type {
  CollectionKind,
  VariableDefinition,
  VariableTypeDescriptor,
  WorkflowDefinitionDetails,
  WorkflowDefinitionState,
  WorkflowDraft,
  WorkflowInput,
  WorkflowOutput
} from "./workflowTypes";

type StateProducer = (state: WorkflowDefinitionState) => WorkflowDefinitionState;

interface PickerOption {
  value: string;
  label: string;
  group?: string;
}

type StorageOptions = PickerOption[] | ((alias: string) => PickerOption[]) | null;
type StorageOptionsStatus = "loading" | "ready" | "unavailable";

function storageChoices(options: StorageOptions, alias: string) {
  return typeof options === "function" ? options(alias) : options;
}

interface DescriptorOptions {
  typeOptions: PickerOption[] | null;
  storageOptions: StorageOptions;
  storageOptionsStatus: StorageOptionsStatus;
  editorForAlias: EditorForAlias;
}

// Type editors come from Expressions; activated, type-compatible storage comes from authoring capabilities.
export function useDescriptorOptions(context: StudioEndpointContext): DescriptorOptions {
  const [variableTypes, setVariableTypes] = useState<VariableTypeDescriptor[] | null>(null);
  const [storage, setStorage] = useState<{ context: StudioEndpointContext; status: StorageOptionsStatus; capabilities?: ActivityAuthoringCapabilities }>();

  useEffect(() => {
    let cancelled = false;
    listVariableTypeDescriptors(context).then(
      descriptors => { if (!cancelled) setVariableTypes(descriptors); },
      () => { if (!cancelled) setVariableTypes([]); }
    );
    return () => { cancelled = true; };
  }, [context]);

  useEffect(() => {
    let cancelled = false;
    setStorage({ context, status: "loading" });
    void Promise.resolve().then(() => getActivityAuthoringCapabilities(context)).then(
      capabilities => { if (!cancelled) setStorage({ context, status: "ready", capabilities }); },
      () => { if (!cancelled) setStorage({ context, status: "unavailable" }); }
    );
    return () => { cancelled = true; };
  }, [context]);

  const storageOptions = useMemo(() => {
    const capabilities = storage?.context === context ? storage.capabilities : undefined;
    const activated = new Set(capabilities?.storageDriverKeys ?? []);
    const byAlias = new Map((capabilities?.types ?? []).map(type => [type.alias,
      type.supportsDurability ? [...new Set(type.compatibleStorageDriverKeys.filter(key => activated.has(key)))].sort().map(key => ({ value: key, label: key })) : []
    ]));
    return (alias: string) => byAlias.get(alias) ?? [];
  }, [context, storage]);

  const typeOptions = useMemo<PickerOption[] | null>(
    () => variableTypes && variableTypes.length > 0
      ? variableTypes.map(descriptor => {
          const alias = descriptorAlias(descriptor);
          return {
            value: alias,
            label: friendlyTypeLabel(descriptor.displayName, alias),
            group: descriptor.category?.trim() || "Other"
          };
        })
      : null,
    [variableTypes]
  );
  // Resolves a selected alias to its default-value editor hint (text/number/checkbox/date/none) in O(1)
  // via a prebuilt map, so per-row lookups don't rescan the descriptor list on every render.
  const editorForAlias = useMemo<EditorForAlias>(() => {
    const editors = new Map<string, string>();
    for (const descriptor of variableTypes ?? []) {
      const alias = descriptorAlias(descriptor);
      const editor = descriptor.defaultEditor?.trim();
      if (alias) editors.set(alias, editor && editor.length > 0 ? editor : "text");
    }
    return alias => editors.get(alias) ?? "text";
  }, [variableTypes]);

  // `null` is the PickerOrText contract for a free-text field, not a pending descriptor request.
  return { typeOptions, storageOptions, storageOptionsStatus: storage?.context === context ? storage.status : "loading", editorForAlias };
}

export type EditorForAlias = (alias: string) => string;

// Mirrors the designer's "prefer String" default when adding a new entry; falls back to the
// first available type, then to the module-level default when no descriptors loaded.
function pickDefaultType(options: PickerOption[] | null): string | undefined {
  if (!options || options.length === 0) return undefined;
  const stringLike = options.find(option => /(^|\.)String$/i.test(option.value) || option.label.toLowerCase() === "string");
  return (stringLike ?? options[0]).value;
}

// Shared add/update/remove wiring for the three collection editors. Only the row markup and the
// create/patch factories differ between Variables/Inputs/Outputs, so the control logic lives here.
function buildCollectionOps<T extends Record<string, unknown>>(
  items: Record<string, unknown>[],
  onChange: (next: unknown[]) => void,
  config: {
    namePrefix: string;
    nameKeys: string[];
    create: (name: string) => T;
    patch: (existing: T, patch: Partial<T>) => T;
  }
) {
  return {
    add: () => {
      const name = generateUniqueName(config.namePrefix, items.map(item => readStringField(item, config.nameKeys)));
      onChange([...items, config.create(name)]);
    },
    update: (index: number, patch: Partial<T>) =>
      onChange(items.map((item, current) => (current === index ? config.patch(item as T, patch) : item))),
    remove: (index: number) => onChange(items.filter((_, current) => current !== index))
  };
}

function PickerCell({ value, options, placeholder, allowEmpty, ariaLabel, disabled, onChange }: {
  value: string;
  options: PickerOption[] | null;
  placeholder?: string;
  allowEmpty?: boolean;
  ariaLabel: string;
  disabled?: boolean;
  onChange(value: string): void;
}) {
  // No descriptors available → free-text fallback so the field stays editable.
  if (!options) {
    return (
      <input
        type="text"
        aria-label={ariaLabel}
        disabled={disabled}
        value={value}
        placeholder={placeholder}
        onChange={event => onChange(event.target.value)}
      />
    );
  }

  // Surface a value the descriptor list doesn't know about so edits don't silently drop it.
  const hasValue = value === "" || options.some(option => option.value === value);
  const groups = Array.from(new Set(options.map(option => option.group).filter((group): group is string => !!group)));
  const grouped = groups.length > 0;

  return (
    <select aria-label={ariaLabel} value={value} disabled={disabled} onChange={event => onChange(event.target.value)}>
      {allowEmpty ? <option value="">{placeholder ?? "—"}</option> : null}
      {/* An alias the descriptor list doesn't know about is preserved but shown as an unresolved, disabled option. */}
      {!hasValue ? <option value={value} disabled>{value} (unresolved)</option> : null}
      {grouped
        ? groups.map(group => (
            <optgroup key={group} label={group}>
              {options.filter(option => option.group === group).map(option => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </optgroup>
          ))
        : options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
    </select>
  );
}

const collectionKindLabels: Record<CollectionKind, string> = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};

function CollectionKindCell({ value, ariaLabel, onChange }: {
  value: CollectionKind;
  ariaLabel: string;
  onChange(kind: CollectionKind): void;
}) {
  return (
    <select aria-label={ariaLabel} value={value} onChange={event => onChange(event.target.value as CollectionKind)}>
      {collectionKinds.map(kind => <option key={kind} value={kind}>{collectionKindLabels[kind]}</option>)}
    </select>
  );
}

// True when `value` is empty or already fits the specialized editor's expected format. A stored default
// that doesn't fit (e.g. an ISO datetime for a `date` input, or a non-boolean string for `checkbox`)
// must NOT be forced into the typed control — the browser would render it blank and silently clobber it
// on interaction — so we fall back to a plain text field that shows and round-trips the raw value.
function fitsEditor(editor: string, value: string): boolean {
  if (value === "") return true;
  if (editor === "checkbox") return value === "true" || value === "false" || value === "True" || value === "False";
  if (editor === "number") return Number.isFinite(Number(value.trim())) && value.trim() !== "";
  if (editor === "date") {
    const date = new Date(value);
    return /^\d{4}-\d{2}-\d{2}$/.test(value) && date.getUTCFullYear() > 0 && date.toISOString().slice(0, 10) === value;
  }
  return true;
}

// Type-aware default-value editor. The `editor` hint comes from the selected type's descriptor
// (checkbox for bool, number, date, else text). Unknown hints fall back to text; `none` renders a
// disabled field so a non-editable type's stored default is preserved but not edited. A stored value
// that doesn't fit the specialized control degrades to text so it stays visible and isn't clobbered.
function DefaultValueCell({ value, editor, ariaLabel, invalid, describedBy, onChange }: {
  value: string;
  editor: string;
  ariaLabel: string;
  invalid?: boolean;
  describedBy?: string;
  onChange(value: string): void;
}) {
  if (editor === "json") {
    let error: string | null = null;
    if (value.trim()) {
      try { JSON.parse(value); } catch { error = "Enter a valid JSON value."; }
    }
    return <div>
      <textarea aria-label={ariaLabel} value={value} rows={3} placeholder="(empty)"
        aria-invalid={error || invalid ? true : undefined} aria-describedby={describedBy}
        onChange={event => onChange(event.target.value)} />
      {error ? <span role="alert">{error}</span> : null}
    </div>;
  }
  const specialized = fitsEditor(editor, value);
  if (specialized && editor === "checkbox") {
    return (
      <input
        type="checkbox"
        aria-label={ariaLabel}
        aria-invalid={invalid || undefined} aria-describedby={describedBy}
        checked={value === "true" || value === "True"}
        onChange={event => onChange(event.target.checked ? "true" : "false")}
      />
    );
  }
  if (specialized && (editor === "number" || editor === "date")) {
    return (
      <input
        type={editor}
        aria-label={ariaLabel}
        aria-invalid={invalid || undefined} aria-describedby={describedBy}
        value={value}
        placeholder="(empty)"
        onChange={event => onChange(event.target.value)}
      />
    );
  }
  return (
    <input
      type="text"
      aria-label={ariaLabel}
      aria-invalid={invalid || undefined} aria-describedby={describedBy}
      value={value}
      placeholder="(empty)"
      disabled={editor === "none"}
      onChange={event => onChange(event.target.value)}
    />
  );
}

interface CollectionDefaultItem { id: number; token: string; text?: string; rawEditing?: boolean }

function CollectionDefaultValueCell({ value, editor, alias, ariaLabel, onChange }: {
  value: string;
  editor: string;
  alias: string;
  ariaLabel: string;
  onChange(value: string): void;
}) {
  const errorId = useId();
  const nextId = useRef(0);
  const container = useRef<HTMLDivElement>(null);
  const focusAfter = useRef<number | "add" | null>(null);
  const parsed = useMemo<CollectionDefaultItem[] | null>(() => {
    const tokens = collectionDefaultTokens(value);
    return tokens?.map(token => ({ id: ++nextId.current, token })) ?? null;
  }, [value]);
  // Keep an item's incomplete JSON draft in its own field while the parent retains the authored text.
  const [edited, setEdited] = useState<{ value: string; editor: string; alias: string; rows: CollectionDefaultItem[] } | null>(null);
  const rows = edited?.value === value && edited.editor === editor && edited.alias === alias ? edited.rows : parsed;
  useEffect(() => {
    if (focusAfter.current === null) return;
    const target = focusAfter.current === "add"
      ? container.current?.querySelector<HTMLButtonElement>(".wf-collection-add")
      : container.current?.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[data-default-item-id='${focusAfter.current}'] input, [data-default-item-id='${focusAfter.current}'] textarea`);
    focusAfter.current = null;
    target?.focus();
  }, [rows]);

  if (rows === null) {
    return <div>
      <textarea aria-label={ariaLabel} value={value} rows={3} aria-invalid="true"
        onChange={event => onChange(event.target.value)} />
      <span role="alert">Enter a JSON array. Comma-separated text is not a collection.</span>
    </div>;
  }
  const change = (next: CollectionDefaultItem[]) => {
    const text = `[${next.map(row => row.token).join(",")}]`;
    setEdited({ value: text, editor, alias, rows: next });
    onChange(text);
  };
  const add = () => {
    const row = { id: ++nextId.current, token: editor === "number" ? "0" : editor === "checkbox" ? "false" : editor === "json" ? "null" : '""' };
    focusAfter.current = row.id;
    change([...rows, row]);
  };
  const remove = (index: number) => {
    focusAfter.current = rows[index + 1]?.id ?? rows[index - 1]?.id ?? "add";
    change(rows.filter((_, position) => position !== index));
  };
  return <div ref={container} className="wf-collection-editor" role="group" aria-label={ariaLabel}>
    {rows.length === 0 ? <p className="wf-collection-empty">No items yet.</p> : <ul className="wf-collection-items">
      {rows.map((row, index) => {
        const error = collectionItemError(row.token, editor, alias);
        // A loaded token must fit its control before decoding it. In particular, "true" is not true.
        // Keep a raw correction in its textarea until blur, and an incomplete typed edit in its input.
        const raw = row.rawEditing || (row.text === undefined && collectionItemNeedsRaw(row.token, editor, error));
        const description = error ? `${errorId}-${row.id}` : undefined;
        return <li key={row.id} className="wf-collection-item" data-default-item-id={row.id}>
          <div className="wf-collection-item-editor">
            {raw ? <textarea aria-label={`${ariaLabel} item ${index + 1}`} value={row.token} rows={2}
              aria-invalid={error ? true : undefined} aria-describedby={description} disabled={editor === "none"}
              onChange={event => change(rows.map(current => current.id === row.id
                ? { ...current, token: event.target.value, rawEditing: true } : current))}
              onBlur={() => {
                if (!error) setEdited({ value, editor, alias, rows: rows.map(current => current.id === row.id
                  ? { ...current, rawEditing: false } : current) });
              }} /> : <DefaultValueCell value={row.text ?? collectionItemText(row.token, editor)} editor={editor}
              ariaLabel={`${ariaLabel} item ${index + 1}`}
              invalid={!!error} describedBy={description}
              onChange={text => change(rows.map(current => current.id === row.id
                ? { ...current, token: collectionItemToken(text, editor), text } : current))} />}
            {error ? <span id={description} role="alert">{error} Stored JSON: <code>{row.token}</code></span> : null}
          </div>
          <button type="button" className="wf-collection-item-button danger" disabled={editor === "none"}
            aria-label={`Remove ${ariaLabel} item ${index + 1}`} onClick={() => remove(index)}>
            <Trash2 size={13} aria-hidden="true" />
          </button>
        </li>;
      })}
    </ul>}
    <button type="button" className="wf-collection-add" aria-label={`Add ${ariaLabel} item`}
      disabled={editor === "none"} onClick={add}><Plus size={13} aria-hidden="true" /> Add item</button>
  </div>;
}

function collectionItemError(token: string, editor: string, alias: string): string | null {
  if (editor === "json") return null;
  let value: unknown;
  try { value = JSON.parse(token); } catch { return "Enter a valid JSON value."; }
  if (editor === "checkbox" && typeof value !== "boolean") return "Enter a JSON boolean (true or false).";
  if (editor === "number" && typeof value !== "number") return "Enter a JSON number.";
  // Null handling and custom type conversion remain the publisher's responsibility.
  if ((alias === "String" || editor === "date") && value !== null && typeof value !== "string") return "Enter a JSON string.";
  if (editor === "date" && typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) && !fitsEditor(editor, value))
    return "Enter a valid calendar date (YYYY-MM-DD).";
  return null;
}

function collectionItemNeedsRaw(token: string, editor: string, error: string | null): boolean {
  if (error) return true;
  if (editor === "json" || editor === "number" || editor === "checkbox") return false;
  try { return typeof JSON.parse(token) !== "string"; } catch { return true; }
}

// Validate once, then retain each top-level token's source. JSON.parse is never used to save numbers.
function collectionDefaultTokens(value: string): string[] | null {
  if (value === "") return [];
  const source = value.trim();
  try { if (!Array.isArray(JSON.parse(source))) return null; } catch { return null; }
  const items: string[] = [];
  let start = 1, depth = 0, quoted = false, escaped = false;
  for (let index = 1; index < source.length - 1; index += 1) {
    const character = source[index];
    if (quoted) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') quoted = false;
    } else if (character === '"') quoted = true;
    else if (character === "[" || character === "{") depth += 1;
    else if (character === "]" || character === "}") depth -= 1;
    else if (character === "," && depth === 0) { items.push(source.slice(start, index).trim()); start = index + 1; }
  }
  const last = source.slice(start, -1).trim();
  if (last) items.push(last);
  return items;
}

function collectionItemText(token: string, editor: string): string {
  if (editor === "json") return token;
  try {
    const value: unknown = JSON.parse(token);
    return typeof value === "string" ? value : token;
  } catch { return token; }
}

function collectionItemToken(text: string, editor: string): string {
  if (editor === "json" || editor === "checkbox") return text;
  if (editor === "number" && /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(text.trim())) return text.trim();
  return JSON.stringify(text);
}

function EditorSection({ title, addLabel, emptyLabel, headers, isEmpty, onAdd, children }: {
  title: string;
  addLabel: string;
  emptyLabel: string;
  headers: string[];
  isEmpty: boolean;
  onAdd(): void;
  children: ReactNode;
}) {
  return (
    <section className="wf-properties-section">
      <div className="wf-properties-section-head">
        <h3>{title}</h3>
        <StudioButton variant="default" size="md" className="wf-properties-add" onClick={onAdd}><Plus size={14} aria-hidden="true" />{addLabel}</StudioButton>
      </div>
      {isEmpty ? (
        <p className="wf-muted">{emptyLabel}</p>
      ) : (
        <table className="wf-properties-table">
          <thead>
            <tr>
              {headers.map(header => <th key={header}>{header}</th>)}
              <th aria-label="Actions" />
            </tr>
          </thead>
          <tbody>{children}</tbody>
        </table>
      )}
    </section>
  );
}

function RemoveCell({ label, onRemove }: { label: string; onRemove(): void }) {
  return (
    <td>
      <button type="button" className="wf-properties-remove" aria-label={label} title={label} onClick={onRemove}>
        <Trash2 size={14} />
      </button>
    </td>
  );
}

function RequiredCell({ checked, ariaLabel, onChange }: {
  checked: boolean;
  ariaLabel: string;
  onChange(value: boolean): void;
}) {
  return (
    <input type="checkbox" aria-label={ariaLabel} checked={checked} onChange={event => onChange(event.target.checked)} />
  );
}

// Which optional columns a collection renders. Only Variables carry a Default (an ArgumentValue); the
// backend Input/Output records have no default member. Inputs alone carry the `isRequired` flag.
interface ArgumentColumns {
  default: boolean;
  storage: boolean;
  required?: boolean;
}

// One row editor for all three collections. They share Name / Type / Collection-kind; Variables and
// Inputs add a type-aware Default and a Storage picker; Outputs are minimal (produced, not consumed).
function ArgumentsEditor({
  items, typeOptions, storageOptions, storageOptionsStatus, editorForAlias,
  namePrefix, nameKeys, title, addLabel, emptyLabel,
  create, patch, columns, warnings, onChange
}: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: StorageOptions;
  storageOptionsStatus?: StorageOptionsStatus;
  editorForAlias: EditorForAlias;
  namePrefix: string;
  nameKeys: string[];
  title: string;
  addLabel: string;
  emptyLabel: string;
  create(name: string, alias?: string): Record<string, unknown>;
  patch(existing: Record<string, unknown>, patch: Record<string, unknown>): Record<string, unknown>;
  columns: ArgumentColumns;
  warnings?: Map<string, string>;
  onChange(next: unknown[]): void;
}) {
  const { add, update, remove } = buildCollectionOps<Record<string, unknown>>(items, onChange, {
    namePrefix,
    nameKeys,
    create: name => create(name, pickDefaultType(typeOptions)),
    patch
  });

  const headers = ["Name", "Type", "Collection", ...(columns.default ? ["Default"] : []), ...(columns.storage ? ["Storage"] : []), ...(columns.required ? ["Required"] : [])];
  const noun = namePrefix.toLowerCase();

  return (
    <EditorSection
      title={title}
      addLabel={addLabel}
      emptyLabel={emptyLabel}
      headers={headers}
      isEmpty={items.length === 0}
      onAdd={add}
    >
      {items.map((item, index) => {
        const name = readStringField(item, nameKeys);
        const argType = readArgumentType(item);
        const rowStorageOptions = storageChoices(storageOptions, argType.alias);
        const referenceKey = readStringField(item, referenceKeyKeys);
        const warning = referenceKey ? warnings?.get(referenceKey) : undefined;
        const typeEditor = editorForAlias(argType.alias);
        const isCollection = argType.collectionKind !== "Single";
        return (
          <tr key={index}>
            <td>
              <input type="text" aria-label={`${namePrefix} name`} value={name} onChange={event => update(index, { name: event.target.value })} />
              {warning ? <span className="wf-properties-warning" role="note" title={warning}>{warning}</span> : null}
            </td>
            <td>
              <PickerCell
                ariaLabel={`${namePrefix} type`}
                value={argType.alias}
                options={typeOptions}
                placeholder="Type"
                onChange={value => update(index, { type: { alias: value, collectionKind: argType.collectionKind } })}
              />
            </td>
            <td>
              <CollectionKindCell
                ariaLabel={`${namePrefix} collection kind`}
                value={argType.collectionKind}
                onChange={kind => update(index, { type: { alias: argType.alias, collectionKind: kind } })}
              />
            </td>
            {columns.default ? (
              <td className={isCollection ? "wf-properties-collection-default" : undefined}>
                {/* Only Variables render this column; the value is their ArgumentValue default. */}
                {isCollection ? <CollectionDefaultValueCell
                  ariaLabel={`${namePrefix} default value`}
                  value={readVariableDefault((item as VariableDefinition).default)}
                  editor={typeEditor}
                  alias={argType.alias}
                  onChange={value => update(index, { default: literalDefault(value) })}
                /> : <DefaultValueCell
                  ariaLabel={`${namePrefix} default value`}
                  value={readVariableDefault((item as VariableDefinition).default)}
                  editor={typeEditor}
                  onChange={value => update(index, { default: typeEditor === "json" && value !== ""
                    ? { value, expressionType: "Object" }
                    : literalDefault(value) })}
                />}
              </td>
            ) : null}
            {columns.storage ? (
              <td>
                <PickerCell
                  ariaLabel={`${namePrefix} storage driver`}
                  value={readStringField(item, storageDriverKeys)}
                  options={rowStorageOptions}
                  placeholder={storageOptionsStatus === "loading" ? "Loading storage…" : storageOptionsStatus === "unavailable" ? "Storage unavailable" : rowStorageOptions?.length === 0 ? "No compatible storage" : "Select storage"}
                  disabled={storageOptionsStatus === "loading" || storageOptionsStatus === "unavailable" || rowStorageOptions?.length === 0}
                  allowEmpty
                  onChange={value => update(index, { storageDriverType: value || null })}
                />
              </td>
            ) : null}
            {columns.required ? (
              <td>
                <RequiredCell
                  ariaLabel={`${namePrefix} required`}
                  checked={item.isRequired === true}
                  onChange={value => update(index, { isRequired: value })}
                />
              </td>
            ) : null}
            <RemoveCell label={`Remove ${noun} ${name || index + 1}`} onRemove={() => remove(index)} />
          </tr>
        );
      })}
    </EditorSection>
  );
}

export function VariablesEditor({ items, typeOptions, storageOptions, storageOptionsStatus, editorForAlias, title = "Variables", addLabel = "Add variable", emptyLabel = "No variables defined.", warnings, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: StorageOptions;
  storageOptionsStatus?: StorageOptionsStatus;
  editorForAlias: EditorForAlias;
  title?: string;
  addLabel?: string;
  emptyLabel?: string;
  warnings?: Map<string, string>;
  onChange(next: unknown[]): void;
}) {
  return (
    <ArgumentsEditor
      items={items}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
      storageOptionsStatus={storageOptionsStatus}
      editorForAlias={editorForAlias}
      namePrefix="Variable"
      nameKeys={variableNameKeys}
      title={title}
      addLabel={addLabel}
      emptyLabel={emptyLabel}
      create={(name, alias) => {
        const variable = createVariable({ name, alias });
        const available = storageChoices(storageOptions, readArgumentType(variable).alias);
        return available?.length === 1 ? { ...variable, storageDriverType: available[0].value } : variable;
      }}
      patch={(existing, next) => updateVariable(existing as VariableDefinition, next)}
      columns={{ default: true, storage: true }}
      warnings={warnings}
      onChange={onChange}
    />
  );
}

function InputsEditor({ items, typeOptions, storageOptions, storageOptionsStatus, editorForAlias, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: StorageOptions;
  storageOptionsStatus?: StorageOptionsStatus;
  editorForAlias: EditorForAlias;
  onChange(next: unknown[]): void;
}) {
  return (
    <ArgumentsEditor
      items={items}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
      storageOptionsStatus={storageOptionsStatus}
      editorForAlias={editorForAlias}
      namePrefix="Input"
      nameKeys={argumentNameKeys}
      title="Inputs"
      addLabel="Add input"
      emptyLabel="No inputs defined."
      create={(name, alias) => createInput({ name, alias })}
      patch={(existing, next) => updateInput(existing as WorkflowInput, next)}
      columns={{ default: false, storage: true, required: true }}
      onChange={onChange}
    />
  );
}

function OutputsEditor({ items, typeOptions, storageOptions, editorForAlias, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: StorageOptions;
  editorForAlias: EditorForAlias;
  onChange(next: unknown[]): void;
}) {
  return (
    <ArgumentsEditor
      items={items}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
      editorForAlias={editorForAlias}
      namePrefix="Output"
      nameKeys={argumentNameKeys}
      title="Outputs"
      addLabel="Add output"
      emptyLabel="No outputs defined."
      create={(name, alias) => createOutput({ name, alias })}
      patch={(existing, next) => updateOutput(existing as WorkflowOutput, next)}
      columns={{ default: false, storage: false }}
      onChange={onChange}
    />
  );
}

export function toRecords(value: unknown[] | undefined): Record<string, unknown>[] {
  return (value ?? []).filter(isPlainRecord);
}

// Self-contained variable editor (loads its own type/storage descriptors) for use outside the
// workflow data tabs — e.g. the container-scoped variables section in the activity inspector. Workflow-
// and container-scoped declarations share this one editor, differing only by where they are stored.
export function ScopedVariablesEditor({ context, variables, title, addLabel, emptyLabel, warnings, onChange }: {
  context: StudioEndpointContext;
  variables: unknown[] | undefined;
  title?: string;
  addLabel?: string;
  emptyLabel?: string;
  warnings?: Map<string, string>;
  onChange(next: unknown[]): void;
}) {
  const { typeOptions, storageOptions, storageOptionsStatus, editorForAlias } = useDescriptorOptions(context);
  return (
    <VariablesEditor
      items={toRecords(variables)}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
      storageOptionsStatus={storageOptionsStatus}
      editorForAlias={editorForAlias}
      title={title}
      addLabel={addLabel}
      emptyLabel={emptyLabel}
      warnings={warnings}
      onChange={onChange}
    />
  );
}

// Editable name/description bound to the definition metadata. Local state keeps typing smooth and
// commits on blur (the metadata save path is separate from the draft/state autosave). It re-seeds when
// the definition changes underneath (e.g. after a reload or an accepted save).
function InformationSection({ definition, definitionId, onMetaChange }: {
  definition: WorkflowDefinitionDetails["definition"] | undefined;
  definitionId: string;
  onMetaChange?(patch: { name?: string; description?: string }): void;
}) {
  const editable = !!onMetaChange;
  const [name, setName] = useState(definition?.name ?? "");
  const [description, setDescription] = useState(definition?.description ?? "");
  useEffect(() => { setName(definition?.name ?? ""); }, [definition?.name]);
  useEffect(() => { setDescription(definition?.description ?? ""); }, [definition?.description]);

  const commitName = () => {
    const trimmed = name.trim();
    if (trimmed && trimmed !== (definition?.name ?? "")) onMetaChange?.({ name: trimmed });
    else if (!trimmed) setName(definition?.name ?? "");
  };
  const commitDescription = () => {
    if (description !== (definition?.description ?? "")) onMetaChange?.({ description });
  };

  return (
    <section className="wf-properties-section">
      <h3>Information</h3>
      <dl className="wf-properties-info">
        <dt><label htmlFor="wf-def-name">Name</label></dt>
        <dd>
          {editable ? (
            <input
              id="wf-def-name"
              type="text"
              aria-label="Workflow name"
              value={name}
              onChange={event => setName(event.target.value)}
              onBlur={commitName}
            />
          ) : (definition?.name ?? "—")}
        </dd>
        <dt><label htmlFor="wf-def-description">Description</label></dt>
        <dd>
          {editable ? (
            <textarea
              id="wf-def-description"
              aria-label="Workflow description"
              rows={2}
              value={description}
              placeholder="No description"
              onChange={event => setDescription(event.target.value)}
              onBlur={commitDescription}
            />
          ) : (definition?.description?.trim() ? definition.description : <span className="wf-muted">No description</span>)}
        </dd>
        <dt>Definition ID</dt>
        <dd><code>{definitionId}</code></dd>
      </dl>
    </section>
  );
}

export function WorkflowPropertiesView({ details, draft, context, onStateChange, onDefinitionMetaChange }: {
  details: WorkflowDefinitionDetails | null;
  draft: WorkflowDraft;
  context: StudioEndpointContext;
  onStateChange(producer: StateProducer): void;
  onDefinitionMetaChange?(patch: { name?: string; description?: string }): void;
}) {
  return (
    <div className="wf-properties-view">
      <InformationSection
        definition={details?.definition}
        definitionId={draft.definitionId}
        onMetaChange={onDefinitionMetaChange}
      />

      <IncidentStrategySection
        strategyOptions={draft.state.strategyOptions}
        context={context}
        onChange={incidentStrategy => onStateChange(state => ({
          ...state,
          strategyOptions: updateIncidentStrategyReference(state.strategyOptions, incidentStrategy)
        }))}
      />

      <PersistenceModeSection
        context={context}
        strategyOptions={draft.state.strategyOptions}
        onChange={persistenceMode => onStateChange(state => ({
          ...state,
          strategyOptions: updatePersistenceMode(state.strategyOptions, persistenceMode)
        }))}
      />

    </div>
  );
}

export type WorkflowDataSection = "variables" | "inputs" | "outputs";

/** Workflow declarations share the scoped editors and the draft's existing save and undo path. */
export function WorkflowDataView({ section, draft, context, onStateChange }: {
  section: WorkflowDataSection;
  draft: WorkflowDraft;
  context: StudioEndpointContext;
  onStateChange(producer: StateProducer): void;
}) {
  const { typeOptions, storageOptions, storageOptionsStatus, editorForAlias } = useDescriptorOptions(context);
  return (
    <div className="wf-properties-view">
      {section === "variables" ? <VariablesEditor
        items={toRecords(draft.state.variables)}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        storageOptionsStatus={storageOptionsStatus}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, variables: next as VariableDefinition[] }))}
      /> : section === "inputs" ? <InputsEditor
        items={toRecords(draft.state.inputs)}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        storageOptionsStatus={storageOptionsStatus}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, inputs: next }))}
      /> : <OutputsEditor
        items={toRecords(draft.state.outputs)}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, outputs: next }))}
      />}
    </div>
  );
}
