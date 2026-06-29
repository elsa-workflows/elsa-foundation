import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Plus, Trash2 } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listStorageDriverDescriptors, listVariableTypeDescriptors } from "./api/workflows";
import { formatDate } from "./workflowFormatting";
import {
  argumentIsArrayKeys,
  argumentNameKeys,
  argumentTypeKeys,
  createInput,
  createOutput,
  createVariable,
  friendlyDriverLabel,
  friendlyTypeLabel,
  generateUniqueName,
  inputDefaultValueKeys,
  inputStorageKeys,
  isPlainRecord,
  readBooleanField,
  readStringField,
  updateInput,
  updateOutput,
  updateVariable,
  variableNameKeys,
  variableStorageKeys,
  variableTypeKeys,
  variableValueKeys
} from "./workflowProperties";
import type {
  StorageDriverDescriptor,
  VariableTypeDescriptor,
  WorkflowDefinitionDetails,
  WorkflowDefinitionState,
  WorkflowDraft,
  WorkflowInput,
  WorkflowOutput,
  WorkflowVariable
} from "./workflowTypes";

type StateProducer = (state: WorkflowDefinitionState) => WorkflowDefinitionState;

interface PickerOption {
  value: string;
  label: string;
  group?: string;
}

// Loads the Type/Storage picker descriptors once per editor mount. A failed fetch resolves to
// an empty list, which makes the pickers fall back to free-text inputs (no invented endpoint).
function useDescriptorOptions(context: StudioEndpointContext) {
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
      ? variableTypes.map(descriptor => ({
          value: descriptor.typeName,
          label: friendlyTypeLabel(descriptor.displayName, descriptor.typeName),
          group: descriptor.category?.trim() || "Other"
        }))
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

  return { typeOptions, storageOptions };
}

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
      {!hasValue ? <option value={value}>{value}</option> : null}
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

function VariablesEditor({ items, typeOptions, storageOptions, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: PickerOption[] | null;
  onChange(next: unknown[]): void;
}) {
  const { add, update, remove } = buildCollectionOps<WorkflowVariable>(items, onChange, {
    namePrefix: "Variable",
    nameKeys: variableNameKeys,
    create: name => createVariable({ name, typeName: pickDefaultType(typeOptions) }),
    patch: updateVariable
  });

  return (
    <EditorSection
      title="Variables"
      addLabel="Add variable"
      emptyLabel="No variables defined."
      headers={["Name", "Type", "Default", "Storage"]}
      isEmpty={items.length === 0}
      onAdd={add}
    >
      {items.map((item, index) => {
        const name = readStringField(item, variableNameKeys);
        return (
          <tr key={index}>
            <td>
              <input type="text" aria-label="Variable name" value={name} onChange={event => update(index, { name: event.target.value })} />
            </td>
            <td>
              <PickerCell
                ariaLabel="Variable type"
                value={readStringField(item, variableTypeKeys)}
                options={typeOptions}
                placeholder="Type"
                onChange={value => update(index, { typeName: value })}
              />
            </td>
            <td>
              <input
                type="text"
                aria-label="Variable default value"
                value={readStringField(item, variableValueKeys)}
                placeholder="(empty)"
                onChange={event => update(index, { value: event.target.value === "" ? null : event.target.value })}
              />
            </td>
            <td>
              <PickerCell
                ariaLabel="Variable storage driver"
                value={readStringField(item, variableStorageKeys)}
                options={storageOptions}
                placeholder="—"
                allowEmpty
                onChange={value => update(index, { storageDriverTypeName: value || null })}
              />
            </td>
            <RemoveCell label={`Remove variable ${name || index + 1}`} onRemove={() => remove(index)} />
          </tr>
        );
      })}
    </EditorSection>
  );
}

function InputsEditor({ items, typeOptions, storageOptions, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  storageOptions: PickerOption[] | null;
  onChange(next: unknown[]): void;
}) {
  const { add, update, remove } = buildCollectionOps<WorkflowInput>(items, onChange, {
    namePrefix: "Input",
    nameKeys: argumentNameKeys,
    create: name => createInput({ name, type: pickDefaultType(typeOptions) }),
    patch: updateInput
  });

  return (
    <EditorSection
      title="Inputs"
      addLabel="Add input"
      emptyLabel="No inputs defined."
      headers={["Name", "Type", "Array", "Default", "Storage"]}
      isEmpty={items.length === 0}
      onAdd={add}
    >
      {items.map((item, index) => {
        const name = readStringField(item, argumentNameKeys);
        return (
          <tr key={index}>
            <td>
              <input type="text" aria-label="Input name" value={name} onChange={event => update(index, { name: event.target.value })} />
            </td>
            <td>
              <PickerCell
                ariaLabel="Input type"
                value={readStringField(item, argumentTypeKeys)}
                options={typeOptions}
                placeholder="Type"
                onChange={value => update(index, { type: value })}
              />
            </td>
            <td className="wf-properties-cell-center">
              <input
                type="checkbox"
                aria-label="Input is array"
                checked={readBooleanField(item, argumentIsArrayKeys)}
                onChange={event => update(index, { isArray: event.target.checked })}
              />
            </td>
            <td>
              <input
                type="text"
                aria-label="Input default value"
                value={readStringField(item, inputDefaultValueKeys)}
                placeholder="(empty)"
                onChange={event => update(index, { defaultValue: event.target.value === "" ? null : event.target.value })}
              />
            </td>
            <td>
              <PickerCell
                ariaLabel="Input storage driver"
                value={readStringField(item, inputStorageKeys)}
                options={storageOptions}
                placeholder="—"
                allowEmpty
                onChange={value => update(index, { storageDriverType: value || null })}
              />
            </td>
            <RemoveCell label={`Remove input ${name || index + 1}`} onRemove={() => remove(index)} />
          </tr>
        );
      })}
    </EditorSection>
  );
}

function OutputsEditor({ items, typeOptions, onChange }: {
  items: Record<string, unknown>[];
  typeOptions: PickerOption[] | null;
  onChange(next: unknown[]): void;
}) {
  const { add, update, remove } = buildCollectionOps<WorkflowOutput>(items, onChange, {
    namePrefix: "Output",
    nameKeys: argumentNameKeys,
    create: name => createOutput({ name, type: pickDefaultType(typeOptions) }),
    patch: updateOutput
  });

  return (
    <EditorSection
      title="Outputs"
      addLabel="Add output"
      emptyLabel="No outputs defined."
      headers={["Name", "Type", "Array"]}
      isEmpty={items.length === 0}
      onAdd={add}
    >
      {items.map((item, index) => {
        const name = readStringField(item, argumentNameKeys);
        return (
          <tr key={index}>
            <td>
              <input type="text" aria-label="Output name" value={name} onChange={event => update(index, { name: event.target.value })} />
            </td>
            <td>
              <PickerCell
                ariaLabel="Output type"
                value={readStringField(item, argumentTypeKeys)}
                options={typeOptions}
                placeholder="Type"
                onChange={value => update(index, { type: value })}
              />
            </td>
            <td className="wf-properties-cell-center">
              <input
                type="checkbox"
                aria-label="Output is array"
                checked={readBooleanField(item, argumentIsArrayKeys)}
                onChange={event => update(index, { isArray: event.target.checked })}
              />
            </td>
            <RemoveCell label={`Remove output ${name || index + 1}`} onRemove={() => remove(index)} />
          </tr>
        );
      })}
    </EditorSection>
  );
}

function toRecords(value: unknown[] | undefined): Record<string, unknown>[] {
  return (value ?? []).filter(isPlainRecord);
}

export function WorkflowPropertiesView({ details, draft, context, onStateChange }: {
  details: WorkflowDefinitionDetails | null;
  draft: WorkflowDraft;
  context: StudioEndpointContext;
  onStateChange(producer: StateProducer): void;
}) {
  const { typeOptions, storageOptions } = useDescriptorOptions(context);
  const variables = toRecords(draft.state.variables);
  const inputs = toRecords(draft.state.inputs);
  const outputs = toRecords(draft.state.outputs);
  const versions = details?.versions ?? [];
  const description = details?.definition.description?.trim();

  return (
    <div className="wf-properties-view">
      <section className="wf-properties-section">
        <h3>Information</h3>
        <dl className="wf-properties-info">
          <dt>Name</dt>
          <dd>{details?.definition.name ?? "—"}</dd>
          <dt>Description</dt>
          <dd>{description ? description : <span className="wf-muted">No description</span>}</dd>
          <dt>Definition ID</dt>
          <dd><code>{draft.definitionId}</code></dd>
        </dl>
      </section>

      <VariablesEditor
        items={variables}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        onChange={next => onStateChange(state => ({ ...state, variables: next }))}
      />
      <InputsEditor
        items={inputs}
        typeOptions={typeOptions}
        storageOptions={storageOptions}
        onChange={next => onStateChange(state => ({ ...state, inputs: next }))}
      />
      <OutputsEditor
        items={outputs}
        typeOptions={typeOptions}
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
