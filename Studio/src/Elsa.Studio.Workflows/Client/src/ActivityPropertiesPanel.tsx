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
        <select
          className="wf-property-syntax"
          value={syntax}
          disabled={readOnly}
          aria-label={`${input.displayName || input.name} expression syntax`}
          onChange={event => setSyntax(event.target.value)}
        >
          {expressionDescriptors.map(descriptor => (
            <option key={descriptor.type} value={descriptor.type}>
              {descriptor.displayName || descriptor.type}
            </option>
          ))}
        </select>
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
