import { useId, useState } from "react";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "./workflowTypes";
import {
  defaultExpressionDescriptors,
  getLiteralEditorValue,
  readWrappedInput,
  withLiteralValue,
  withSyntax,
  writeInputValue
} from "./activityProperties";

export interface ActivityPropertiesPanelProps {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor | null;
  editors: StudioActivityPropertyEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  descriptorStatus: "loading" | "ready" | "failed";
  onChange(activity: ActivityNode): void;
}

export function ActivityPropertiesPanel({
  activity,
  descriptor,
  editors,
  expressionDescriptors,
  descriptorStatus,
  onChange
}: ActivityPropertiesPanelProps) {
  if (descriptorStatus === "loading") {
    return <p className="wf-muted">Loading activity properties...</p>;
  }

  if (!descriptor) {
    return <p className="wf-muted">No activity descriptor is available for this activity.</p>;
  }

  const inputs = descriptor.inputs
    .filter(input => input.isBrowsable !== false)
    .sort((left, right) => (left.order ?? 0) - (right.order ?? 0) || left.name.localeCompare(right.name));

  if (inputs.length === 0) {
    return <p className="wf-muted">This activity does not expose editable properties.</p>;
  }

  const groups = groupInputs(inputs);
  const syntaxDescriptors = expressionDescriptors.length > 0 ? expressionDescriptors : defaultExpressionDescriptors;

  return (
    <div className="wf-properties">
      <span className="wf-section-label">Properties</span>
      {groups.map(group => (
        <section key={group.category} className="wf-property-group">
          {groups.length > 1 ? <h4>{group.category}</h4> : null}
          {group.inputs.map(input => (
            <PropertyRow
              key={input.name}
              activity={activity}
              input={input}
              editors={editors}
              expressionDescriptors={syntaxDescriptors}
              onChange={onChange}
            />
          ))}
        </section>
      ))}
    </div>
  );
}

function PropertyRow({
  activity,
  input,
  editors,
  expressionDescriptors,
  onChange
}: {
  activity: ActivityNode;
  input: StudioActivityInputDescriptor;
  editors: StudioActivityPropertyEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  onChange(activity: ActivityNode): void;
}) {
  const readOnly = input.isReadOnly === true;
  const context: StudioActivityPropertyEditorContext = { activity, expressionDescriptors, readOnly };
  const editor = resolveEditor(editors, input, context);
  const EditorComponent = editor?.component;
  const wrapped = input.isWrapped !== false ? readWrappedInput(activity, input) : null;
  const syntax = wrapped?.expression.type ?? "Literal";
  const value = getLiteralEditorValue(activity, input);

  const setRaw = (nextValue: unknown) => {
    const next = wrapped ? withLiteralValue(wrapped, nextValue) : nextValue;
    onChange(writeInputValue(activity, input, next));
  };

  const setSyntax = (nextSyntax: string) => {
    if (!wrapped) return;
    onChange(writeInputValue(activity, input, withSyntax(wrapped, nextSyntax)));
  };

  return (
    <div className="wf-property-row">
      <div className="wf-property-row-header">
        <label>{input.displayName || input.name}</label>
        <span>{formatTypeName(input.typeName)}</span>
      </div>
      {input.description ? <p>{input.description}</p> : null}
      {wrapped ? (
        <SyntaxPicker
          label={`${input.displayName || input.name} expression syntax`}
          value={syntax}
          descriptors={expressionDescriptors}
          disabled={readOnly}
          onChange={setSyntax}
        />
      ) : null}
      {EditorComponent ? (
        <EditorComponent
          descriptor={input}
          value={value}
          disabled={readOnly}
          context={context}
          onChange={setRaw}
        />
      ) : (
        <input type="text" value={value == null ? "" : String(value)} disabled={readOnly} onChange={event => setRaw(event.target.value)} />
      )}
    </div>
  );
}

function SyntaxPicker({
  label,
  value,
  descriptors,
  disabled,
  onChange
}: {
  label: string;
  value: string;
  descriptors: StudioExpressionDescriptor[];
  disabled: boolean;
  onChange(value: string): void;
}) {
  const [open, setOpen] = useState(false);
  const listboxId = useId();
  const selected = descriptors.find(descriptor => descriptor.type === value);

  return (
    <div className="wf-syntax-picker" onBlur={event => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
    }}>
      <button
        type="button"
        className={open ? "wf-syntax-picker-trigger open" : "wf-syntax-picker-trigger"}
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        disabled={disabled}
        onClick={() => setOpen(current => !current)}
      >
        <span>{selected?.displayName || selected?.type || value}</span>
      </button>
      {open ? (
        <div id={listboxId} role="listbox" className="wf-syntax-picker-menu" aria-label={label}>
          {descriptors.map(descriptor => {
            const optionLabel = descriptor.displayName || descriptor.type;
            const selectedOption = descriptor.type === value;
            return (
              <button
                type="button"
                role="option"
                aria-selected={selectedOption}
                key={descriptor.type}
                className={selectedOption ? "selected" : ""}
                onClick={() => {
                  onChange(descriptor.type);
                  setOpen(false);
                }}
              >
                {optionLabel}
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}

function resolveEditor(
  editors: StudioActivityPropertyEditorContribution[],
  input: StudioActivityInputDescriptor,
  context: StudioActivityPropertyEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => editor.supports(input, context));
}

function groupInputs(inputs: StudioActivityInputDescriptor[]) {
  const groups = new Map<string, StudioActivityInputDescriptor[]>();
  for (const input of inputs) {
    const category = input.category?.trim() || "General";
    groups.set(category, [...(groups.get(category) ?? []), input]);
  }

  return [...groups.entries()].map(([category, groupedInputs]) => ({ category, inputs: groupedInputs }));
}

function formatTypeName(typeName: string) {
  return typeName.split(".").filter(Boolean).at(-1) ?? typeName;
}
