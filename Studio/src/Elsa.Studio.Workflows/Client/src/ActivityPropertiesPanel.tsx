import { useEffect, useId, useState } from "react";
import { Maximize2, X } from "lucide-react";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext,
  StudioExpressionEditorContribution,
  StudioExpressionEditorContext,
  StudioExpressionEditorDiagnostic,
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

const inlineSyntaxEditorIds = new Set(["studio.property.singleline", "studio.property.text-fallback"]);

export interface ActivityPropertiesPanelProps {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor | null;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  descriptorStatus: "loading" | "ready" | "failed";
  onChange(activity: ActivityNode): void;
}

export function ActivityPropertiesPanel({
  activity,
  descriptor,
  editors,
  expressionEditors,
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
              expressionEditors={expressionEditors}
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
  expressionEditors,
  expressionDescriptors,
  onChange
}: {
  activity: ActivityNode;
  input: StudioActivityInputDescriptor;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
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
  const inlineExpressionContext: StudioExpressionEditorContext | null = wrapped ? {
    activity,
    descriptor: input,
    expressionDescriptors,
    readOnly,
    surface: "inline",
    syntax
  } : null;
  const inlineExpressionEditor = inlineExpressionContext ? resolveExpressionEditor(expressionEditors, inlineExpressionContext) : null;
  const InlineExpressionEditorComponent = inlineExpressionEditor?.surfaces.inline;
  const inlineDiagnostics = inlineExpressionEditor && inlineExpressionContext
    ? getExpressionEditorDiagnostics(inlineExpressionEditor, inlineExpressionContext, value)
    : [];
  const useInlineSyntaxPicker = Boolean(wrapped && isSingleLineTextInput(input, editor?.id));
  const canExpandEditor = Boolean(wrapped && isExpandableTextInput(input, editor?.id));
  const [expanded, setExpanded] = useState(false);

  const setRaw = (nextValue: unknown) => {
    const next = wrapped ? withLiteralValue(wrapped, nextValue) : nextValue;
    onChange(writeInputValue(activity, input, next));
  };

  const setSyntax = (nextSyntax: string) => {
    if (!wrapped) return;
    onChange(writeInputValue(activity, input, withSyntax(wrapped, nextSyntax)));
  };
  const valueEditor = InlineExpressionEditorComponent && inlineExpressionContext ? (
    <InlineExpressionEditorComponent
      descriptor={input}
      syntax={syntax}
      value={value}
      disabled={readOnly}
      context={inlineExpressionContext}
      onChange={setRaw}
    />
  ) : renderEditor(EditorComponent, input, value, readOnly, context, setRaw);

  return (
    <div className="wf-property-row">
      <div className="wf-property-row-header">
        <label>{input.displayName || input.name}</label>
        <span>{formatTypeName(input.typeName)}</span>
      </div>
      {input.description ? <p>{input.description}</p> : null}
      {wrapped && !useInlineSyntaxPicker ? (
        <SyntaxPicker
          label={`${input.displayName || input.name} expression syntax`}
          value={syntax}
          descriptors={expressionDescriptors}
          disabled={readOnly}
          onChange={setSyntax}
        />
      ) : null}
      {useInlineSyntaxPicker ? (
        <div className="wf-expression-field">
          <div className="wf-expression-editor">
            {valueEditor}
            {renderExpressionDiagnostics(inlineDiagnostics)}
          </div>
          <SyntaxPicker
            label={`${input.displayName || input.name} expression syntax`}
            value={syntax}
            descriptors={expressionDescriptors}
            disabled={readOnly}
            variant="inline"
            onChange={setSyntax}
          />
          {canExpandEditor ? (
            <button
              type="button"
              className="wf-expression-expand-button"
              aria-label={`Open expanded ${input.displayName || input.name} editor`}
              title="Open expanded editor"
              onClick={() => setExpanded(true)}
            >
              <Maximize2 size={13} />
            </button>
          ) : null}
        </div>
      ) : (
        <>
          {valueEditor}
          {renderExpressionDiagnostics(inlineDiagnostics)}
        </>
      )}
      {canExpandEditor && !useInlineSyntaxPicker ? (
        <button
          type="button"
          className="wf-property-expand-row"
          aria-label={`Open expanded ${input.displayName || input.name} editor`}
          onClick={() => setExpanded(true)}
        >
          <Maximize2 size={13} /> Open expanded editor
        </button>
      ) : null}
      {expanded ? (
        <ExpandedPropertyEditor
          input={input}
          value={value}
          syntax={syntax}
          descriptors={expressionDescriptors}
          activity={activity}
          expressionEditors={expressionEditors}
          disabled={readOnly}
          onChange={setRaw}
          onSyntaxChange={setSyntax}
          onClose={() => setExpanded(false)}
        />
      ) : null}
    </div>
  );
}

function ExpandedPropertyEditor({
  input,
  value,
  syntax,
  descriptors,
  activity,
  expressionEditors,
  disabled,
  onChange,
  onSyntaxChange,
  onClose
}: {
  input: StudioActivityInputDescriptor;
  value: unknown;
  syntax: string;
  descriptors: StudioExpressionDescriptor[];
  activity: ActivityNode;
  expressionEditors: StudioExpressionEditorContribution[];
  disabled: boolean;
  onChange(value: unknown): void;
  onSyntaxChange(value: string): void;
  onClose(): void;
}) {
  const titleId = useId();
  const displayName = input.displayName || input.name;
  const expressionContext: StudioExpressionEditorContext = {
    activity,
    descriptor: input,
    expressionDescriptors: descriptors,
    readOnly: disabled,
    surface: "expanded",
    syntax
  };
  const expressionEditor = resolveExpressionEditor(expressionEditors, expressionContext);
  const ExpressionEditorComponent = expressionEditor?.surfaces.expanded;
  const diagnostics = expressionEditor ? getExpressionEditorDiagnostics(expressionEditor, expressionContext, value) : [];
  const fallbackHint = !ExpressionEditorComponent ? getExpressionEditorFallbackHint(expressionEditors, expressionContext) : null;

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  return (
    <div className="wf-property-editor-backdrop">
      <section className="wf-property-editor-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header>
          <div>
            <span>Property editor</span>
            <h3 id={titleId}>{displayName}</h3>
          </div>
          <button type="button" aria-label={`Close ${displayName} editor`} onClick={onClose}>
            <X size={16} />
          </button>
        </header>
        <div className="wf-property-editor-body">
          <div className="wf-property-editor-toolbar">
            <SyntaxPicker
              label={`${displayName} expression syntax`}
              value={syntax}
              descriptors={descriptors}
              disabled={disabled}
              onChange={onSyntaxChange}
            />
            <span>{formatTypeName(input.typeName)}</span>
          </div>
          {input.description ? <p>{input.description}</p> : null}
          {ExpressionEditorComponent ? (
            <ExpressionEditorComponent
              descriptor={input}
              syntax={syntax}
              value={value}
              disabled={disabled}
              context={expressionContext}
              onChange={onChange}
            />
          ) : (
            <>
              {fallbackHint ? (
                <p className="wf-expression-editor-hint">
                  {fallbackHint}
                </p>
              ) : null}
              <textarea
                aria-label={`${displayName} expanded value`}
                value={value == null ? "" : String(value)}
                disabled={disabled}
                spellCheck={false}
                onChange={event => onChange(event.target.value)}
              />
            </>
          )}
          {renderExpressionDiagnostics(diagnostics)}
        </div>
        <footer>
          <span>Changes update the draft immediately.</span>
          <button type="button" onClick={onClose}>Close</button>
        </footer>
      </section>
    </div>
  );
}

function renderEditor(
  EditorComponent: StudioActivityPropertyEditorContribution["component"] | undefined,
  input: StudioActivityInputDescriptor,
  value: unknown,
  disabled: boolean,
  context: StudioActivityPropertyEditorContext,
  onChange: (value: unknown) => void
) {
  return EditorComponent ? (
    <EditorComponent
      descriptor={input}
      value={value}
      disabled={disabled}
      context={context}
      onChange={onChange}
    />
  ) : (
    <input type="text" value={value == null ? "" : String(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />
  );
}

function SyntaxPicker({
  label,
  value,
  descriptors,
  disabled,
  variant = "block",
  onChange
}: {
  label: string;
  value: string;
  descriptors: StudioExpressionDescriptor[];
  disabled: boolean;
  variant?: "block" | "inline";
  onChange(value: string): void;
}) {
  const [open, setOpen] = useState(false);
  const listboxId = useId();
  const selected = descriptors.find(descriptor => descriptor.type === value);
  const className = [
    "wf-syntax-picker-trigger",
    variant === "inline" ? "inline" : "",
    open ? "open" : ""
  ].filter(Boolean).join(" ");

  return (
    <div className={variant === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker"} onBlur={event => {
      if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
    }}>
      <button
        type="button"
        className={className}
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

function resolveExpressionEditor(
  editors: StudioExpressionEditorContribution[],
  context: StudioExpressionEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => !!editor.surfaces[context.surface] && editor.supports(context));
}

function getExpressionEditorDiagnostics(
  editor: StudioExpressionEditorContribution,
  context: StudioExpressionEditorContext,
  value: unknown
) {
  return editor.diagnostics?.(context, value) ?? [];
}

function getExpressionEditorFallbackHint(
  editors: StudioExpressionEditorContribution[],
  context: StudioExpressionEditorContext
) {
  if (context.syntax.toLowerCase() === "literal") return null;

  const metadata = [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => editor.supports(context) && editor.metadata)?.metadata;

  if (!metadata) return `No enhanced editor is registered for ${context.syntax}. Using the generic text editor.`;

  const displayName = metadata.displayName?.trim() || "enhanced editor";
  const installHint = metadata.installHint?.trim();
  const baseHint = `No ${displayName} is registered for ${context.syntax}. Using the generic text editor.`;
  return installHint ? `${baseHint} ${installHint}` : baseHint;
}

function renderExpressionDiagnostics(diagnostics: StudioExpressionEditorDiagnostic[]) {
  if (diagnostics.length === 0) return null;

  return (
    <div className="wf-expression-editor-diagnostics" role="status">
      {diagnostics.map((diagnostic, index) => {
        const severity = diagnostic.severity ?? "info";
        return (
          <p key={`${diagnostic.code ?? "diagnostic"}-${index}`} className={`wf-expression-editor-diagnostic ${severity}`}>
            {diagnostic.code ? <span>{diagnostic.code}</span> : null}
            {diagnostic.message}
          </p>
        );
      })}
    </div>
  );
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

function isSingleLineTextInput(input: StudioActivityInputDescriptor, editorId: string | undefined) {
  if (input.uiHint?.toLowerCase() === "multiline") return false;
  if (editorId && !inlineSyntaxEditorIds.has(editorId)) return false;

  const normalizedType = input.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(normalizedType) || input.uiHint?.toLowerCase() === "singleline";
}

function isExpandableTextInput(input: StudioActivityInputDescriptor, editorId: string | undefined) {
  const uiHint = input.uiHint?.toLowerCase();
  if (uiHint === "checkbox" || uiHint === "dropdown") return false;
  if (editorId && !inlineSyntaxEditorIds.has(editorId) && uiHint !== "multiline") return false;

  const normalizedType = input.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(normalizedType) ||
    uiHint === "singleline" ||
    uiHint === "multiline";
}
