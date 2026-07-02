import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Plus, Trash2 } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listStorageDriverDescriptors, listVariableTypeDescriptors } from "./api/workflows";
import { formatDate } from "./workflowFormatting";
import {
  argumentNameKeys,
  createInput,
  createOutput,
  createVariable,
  descriptorAlias,
  friendlyDriverLabel,
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
  updateOutput,
  updateVariable,
  variableNameKeys
} from "./workflowProperties";
import { collectionKinds } from "./workflowTypes";
import type {
  CollectionKind,
  StorageDriverDescriptor,
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

// Loads the Type/Storage picker descriptors once per editor mount. A failed fetch resolves to
// an empty list, which makes the pickers fall back to free-text inputs (no invented endpoint).
export function useDescriptorOptions(context: StudioEndpointContext) {
  const [variableTypes, setVariableTypes] = useState<VariableTypeDescriptor[] | null>(null);
  const [storageDrivers, setStorageDrivers] = useState<StorageDriverDescriptor[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    listVariableTypeDescriptors(context).then(
      descriptors => { if (!cancelled) setVariableTypes(descriptors); },
      () => { if (!cancelled) setVariableTypes([]); }
    );
    listStorageDriverDescriptors(context).then(
      descriptors => { if (!cancelled) setStorageDrivers(descriptors); },
      () => { if (!cancelled) setStorageDrivers([]); }
    );
    return () => { cancelled = true; };
  }, [context]);

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
  const storageOptions = useMemo<PickerOption[] | null>(
    () => storageDrivers && storageDrivers.length > 0
      ? storageDrivers
          .filter(descriptor => !descriptor.deprecated)
          .map(descriptor => ({
            value: descriptor.typeName,
            label: friendlyDriverLabel(descriptor.displayName, descriptor.typeName)
          }))
      : null,
    [storageDrivers]
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

  return { typeOptions, storageOptions, editorForAlias };
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

function PickerCell({ value, options, placeholder, allowEmpty, ariaLabel, onChange }: {
  value: string;
  options: PickerOption[] | null;
  placeholder?: string;
  allowEmpty?: boolean;
  ariaLabel: string;
  onChange(value: string): void;
}) {
  // No descriptors available → free-text fallback so the field stays editable.
  if (!options) {
    return (
      <input
        type="text"
        aria-label={ariaLabel}
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
    <select aria-label={ariaLabel} value={value} onChange={event => onChange(event.target.value)}>
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
  if (editor === "date") return /^\d{4}-\d{2}-\d{2}$/.test(value);
  return true;
}

// Type-aware default-value editor. The `editor` hint comes from the selected type's descriptor
// (checkbox for bool, number, date, else text). Unknown hints fall back to text; `none` renders a
// disabled field so a non-editable type's stored default is preserved but not edited. A stored value
// that doesn't fit the specialized control degrades to text so it stays visible and isn't clobbered.
function DefaultValueCell({ value, editor, ariaLabel, onChange }: {
  value: string;
  editor: string;
  ariaLabel: string;
  onChange(value: string): void;
}) {
  const specialized = fitsEditor(editor, value);
  if (specialized && editor === "checkbox") {
    return (
      <input
        type="checkbox"
        aria-label={ariaLabel}
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
      value={value}
      placeholder="(empty)"
      disabled={editor === "none"}
      onChange={event => onChange(event.target.value)}
    />
  );
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
        <button type="button" className="wf-properties-add" onClick={onAdd}><Plus size={14} /> {addLabel}</button>
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
  items, typeOptions, storageOptions, editorForAlias,
  namePrefix, nameKeys, title, addLabel, emptyLabel,
  create, patch, columns, warnings, onChange
}: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: PickerOption[] | null;
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
        const referenceKey = readStringField(item, referenceKeyKeys);
        const warning = referenceKey ? warnings?.get(referenceKey) : undefined;
        // A collection's default is a single scalar editor only for a `Single` type; collection defaults
        // fall back to free text (editing a list/array default inline is out of scope).
        const editor = argType.collectionKind === "Single" ? editorForAlias(argType.alias) : "text";
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
              <td>
                {/* Only Variables render this column; the value is their ArgumentValue default. */}
                <DefaultValueCell
                  ariaLabel={`${namePrefix} default value`}
                  value={readVariableDefault((item as VariableDefinition).default)}
                  editor={editor}
                  onChange={value => update(index, { default: literalDefault(value) })}
                />
              </td>
            ) : null}
            {columns.storage ? (
              <td>
                <PickerCell
                  ariaLabel={`${namePrefix} storage driver`}
                  value={readStringField(item, storageDriverKeys)}
                  options={storageOptions}
                  placeholder="—"
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

export function VariablesEditor({ items, typeOptions, storageOptions, editorForAlias, title = "Variables", addLabel = "Add variable", emptyLabel = "No variables defined.", warnings, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: PickerOption[] | null;
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
      editorForAlias={editorForAlias}
      namePrefix="Variable"
      nameKeys={variableNameKeys}
      title={title}
      addLabel={addLabel}
      emptyLabel={emptyLabel}
      create={(name, alias) => createVariable({ name, alias })}
      patch={(existing, next) => updateVariable(existing as VariableDefinition, next)}
      columns={{ default: true, storage: true }}
      warnings={warnings}
      onChange={onChange}
    />
  );
}

function InputsEditor({ items, typeOptions, storageOptions, editorForAlias, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: PickerOption[] | null;
  editorForAlias: EditorForAlias;
  onChange(next: unknown[]): void;
}) {
  return (
    <ArgumentsEditor
      items={items}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
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
  storageOptions: PickerOption[] | null;
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
// Properties tab — e.g. the container-scoped variables section in the activity inspector. Workflow-
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
  const { typeOptions, storageOptions, editorForAlias } = useDescriptorOptions(context);
  return (
    <VariablesEditor
      items={toRecords(variables)}
      typeOptions={typeOptions}
      storageOptions={storageOptions}
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
  const { typeOptions, storageOptions, editorForAlias } = useDescriptorOptions(context);
  const variables = toRecords(draft.state.variables);
  const inputs = toRecords(draft.state.inputs);
  const outputs = toRecords(draft.state.outputs);
  const versions = details?.versions ?? [];

  return (
    <div className="wf-properties-view">
      <InformationSection
        definition={details?.definition}
        definitionId={draft.definitionId}
        onMetaChange={onDefinitionMetaChange}
      />

      <VariablesEditor
        items={variables}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, variables: next as VariableDefinition[] }))}
      />
      <InputsEditor
        items={inputs}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, inputs: next }))}
      />
      <OutputsEditor
        items={outputs}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        editorForAlias={editorForAlias}
        onChange={next => onStateChange(state => ({ ...state, outputs: next }))}
      />

      <section className="wf-properties-section">
        <h3>Versions</h3>
        {versions.length === 0 ? (
          <p className="wf-muted">No published versions yet.</p>
        ) : (
          <ul className="wf-properties-versions">
            {versions.map(version => (
              <li key={version.id}>
                <span className="wf-properties-version-tag">v{version.version}</span>
                <time>{formatDate(version.createdAt)}</time>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
