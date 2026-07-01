import { type DragEvent, useEffect, useId, useState } from "react";
import { ChevronDown, ChevronUp, GripVertical, Maximize2, Plus, Trash2, X } from "lucide-react";
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
import type { ActivityNode, VariableReference, VisibleVariableView } from "./workflowTypes";
import type { ScopedVariableAnalysisStatus } from "./api/workflows";
import { makeVariableReference, readVariableReference, WORKFLOW_SCOPE_ID } from "./scopedVariables";
import {
  defaultCollectionItem,
  defaultExpressionDescriptors,
  describeCollectionType,
  formatTypeName,
  getLiteralEditorValue,
  isRepeaterOptOut,
  makeCollectionElementDescriptor,
  moveCollectionItem,
  readWrappedInput,
  toLiteralCollection,
  withLiteralValue,
  withSyntax,
  writeInputValue
} from "./activityProperties";

const inlineSyntaxEditorIds = new Set(["studio.property.singleline", "studio.property.text-fallback"]);
const variableSyntax = "Variable";

export interface ActivityPropertiesPanelProps {
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor | null;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  descriptorStatus: "loading" | "ready" | "failed";
  // Variables visible from this activity's scope (nearest-scope first) for the Variable picker, plus
  // the analysis status so the picker can explain an absent backend endpoint instead of showing empty.
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  onChange(activity: ActivityNode): void;
}

export function ActivityPropertiesPanel({
  activity,
  descriptor,
  editors,
  expressionEditors,
  expressionDescriptors,
  descriptorStatus,
  visibleVariables,
  scopeStatus,
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
              visibleVariables={visibleVariables}
              scopeStatus={scopeStatus}
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
  visibleVariables,
  scopeStatus,
  onChange
}: {
  activity: ActivityNode;
  input: StudioActivityInputDescriptor;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  onChange(activity: ActivityNode): void;
}) {
  const readOnly = input.isReadOnly === true;
  const context: StudioActivityPropertyEditorContext = { activity, expressionDescriptors, readOnly };
  const editor = resolveEditor(editors, input, context);
  const EditorComponent = editor?.component;
  const wrapped = input.isWrapped !== false ? readWrappedInput(activity, input) : null;
  const syntax = wrapped?.expression.type ?? "Literal";
  const value = getLiteralEditorValue(activity, input);
  // Collection-typed literals get structured authoring: a collection-scoped editor (e.g. a multi-select
  // for option sets) owns the whole value when one claims it; otherwise a repeater of per-element editors.
  // The repeater authors collection literals under both the "Literal" syntax (freshly added inputs) and
  // the "Object" syntax — how they come back from the backend, which needs Object to JSON-deserialize the
  // list into ICollection<T> (see toWireArgument in activityInputWire).
  const lowerSyntax = syntax.toLowerCase();
  const isStructuredLiteralSyntax = lowerSyntax === "literal" || lowerSyntax === "object";
  const collectionType = wrapped && isStructuredLiteralSyntax && !isRepeaterOptOut(input)
    ? describeCollectionType(input.typeName)
    : null;
  const collectionScopedEditor = collectionType
    ? resolveEditor(editors, input, { ...context, scope: "collection" })
    : undefined;
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
  // A collection renders a multi-row repeater, not a single-line field, so the inline text chrome (the
  // overlaid syntax picker + expand button, positioned top:4/bottom:4 of the field) must not wrap it —
  // it would stretch down the whole list and cover the per-row reorder controls. Such inputs still get a
  // syntax picker, but the block one above the list. `uiHint: "singleline"` is common on list inputs, so
  // gating on the collection itself (not the hint) is what keeps the two features from colliding.
  const isCollectionEditor = collectionType != null;
  const useInlineSyntaxPicker = Boolean(wrapped && !isCollectionEditor && isSingleLineTextInput(input, editor?.id));
  const canExpandEditor = Boolean(wrapped && !isCollectionEditor && isExpandableTextInput(input, editor?.id));
  const [expanded, setExpanded] = useState(false);

  const setRaw = (nextValue: unknown) => {
    const next = wrapped ? withLiteralValue(wrapped, nextValue) : nextValue;
    onChange(writeInputValue(activity, input, next));
  };

  const setSyntax = (nextSyntax: string) => {
    if (!wrapped) return;
    onChange(writeInputValue(activity, input, withSyntax(wrapped, nextSyntax)));
  };
  // The whole collection concept resolves to a single node: a collection-scoped editor when one claims
  // the value (e.g. a multi-select for option sets), otherwise a repeater of per-element editors.
  const collectionEditor = collectionType
    ? collectionScopedEditor
      ? renderEditor(collectionScopedEditor.component, input, value, readOnly, { ...context, scope: "collection" }, setRaw)
      : (
        <CollectionLiteralEditor
          input={input}
          elementTypeName={collectionType.elementTypeName}
          value={value}
          editors={editors}
          context={context}
          disabled={readOnly}
          onChange={setRaw}
        />
      )
    : null;
  const valueEditor = syntax === variableSyntax && wrapped ? (
    <VariablePicker
      value={value}
      visibleVariables={visibleVariables}
      scopeStatus={scopeStatus}
      disabled={readOnly}
      onChange={setRaw}
    />
  ) : collectionEditor ?? (InlineExpressionEditorComponent && inlineExpressionContext ? (
    <InlineExpressionEditorComponent
      descriptor={input}
      syntax={syntax}
      value={value}
      disabled={readOnly}
      context={inlineExpressionContext}
      onChange={setRaw}
    />
  ) : renderEditor(EditorComponent, input, value, readOnly, context, setRaw));

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

// Repeater for collection-typed literals: each row reuses the registered element editor (text, checkbox,
// dropdown, …) resolved in "element" scope, with add / reorder / remove controls around it.
function collectionItemClassName(index: number, dragIndex: number | null, overIndex: number | null) {
  return [
    "wf-collection-item",
    dragIndex === index ? "dragging" : "",
    dragIndex !== null && dragIndex !== index && overIndex === index ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}

function CollectionLiteralEditor({
  input,
  elementTypeName,
  value,
  editors,
  context,
  disabled,
  onChange
}: {
  input: StudioActivityInputDescriptor;
  elementTypeName: string | null;
  value: unknown;
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  onChange(value: unknown): void;
}) {
  const items = toLiteralCollection(value);
  const elementDescriptor = makeCollectionElementDescriptor(input, elementTypeName);
  const elementContext: StudioActivityPropertyEditorContext = { ...context, scope: "element" };
  const ElementComponent = resolveEditor(editors, elementDescriptor, elementContext)?.component;
  const label = input.displayName || input.name;
  const replaceAt = (index: number, next: unknown) =>
    onChange(items.map((current, position) => (position === index ? next : current)));

  // Drag-to-reorder. Only the grip handle is draggable (so it never competes with text selection in the
  // row editors); the rows are drop zones. The list reorders atomically on drop via the shared
  // moveCollectionItem helper — index keys stay valid because items don't shift mid-gesture.
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const resetDrag = () => {
    setDragIndex(null);
    setOverIndex(null);
  };
  const handleDragStart = (index: number) => (event: DragEvent) => {
    setDragIndex(index);
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", String(index));
  };
  const handleDragOver = (index: number) => (event: DragEvent) => {
    if (dragIndex === null) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    if (overIndex !== index) setOverIndex(index);
  };
  const handleDrop = (index: number) => (event: DragEvent) => {
    event.preventDefault();
    if (dragIndex !== null && dragIndex !== index) onChange(moveCollectionItem(items, dragIndex, index));
    resetDrag();
  };

  return (
    <div className="wf-collection-editor">
      {items.length === 0 ? (
        <p className="wf-collection-empty">No items yet.</p>
      ) : (
        <ul className="wf-collection-items">
          {items.map((item, index) => (
            <li
              key={index}
              className={collectionItemClassName(index, dragIndex, overIndex)}
              onDragOver={handleDragOver(index)}
              onDrop={handleDrop(index)}
            >
              <span
                className="wf-collection-item-handle"
                draggable={!disabled}
                aria-label={`Drag ${label} item ${index + 1} to reorder`}
                title="Drag to reorder"
                onDragStart={handleDragStart(index)}
                onDragEnd={resetDrag}
              >
                <GripVertical size={13} aria-hidden="true" />
              </span>
              <div className="wf-collection-item-editor">
                {renderEditor(ElementComponent, elementDescriptor, item, disabled, elementContext, next => replaceAt(index, next))}
              </div>
              <div className="wf-collection-item-actions">
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} up`}
                  disabled={disabled || index === 0}
                  onClick={() => onChange(moveCollectionItem(items, index, index - 1))}
                >
                  <ChevronUp size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} down`}
                  disabled={disabled || index === items.length - 1}
                  onClick={() => onChange(moveCollectionItem(items, index, index + 1))}
                >
                  <ChevronDown size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button danger"
                  aria-label={`Remove ${label} item ${index + 1}`}
                  disabled={disabled}
                  onClick={() => onChange(items.filter((_, position) => position !== index))}
                >
                  <Trash2 size={13} />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <button
        type="button"
        className="wf-collection-add"
        disabled={disabled}
        onClick={() => onChange([...items, defaultCollectionItem(elementTypeName)])}
      >
        <Plus size={13} /> Add item
      </button>
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

// Encodes/decodes a picker option as "scopeId::referenceKey" so the same reference key in different
// declaring scopes stays distinct.
const variableOptionSeparator = "::";

function normalizeScopeId(scopeId: string | null | undefined): string {
  return !scopeId || scopeId === WORKFLOW_SCOPE_ID ? WORKFLOW_SCOPE_ID : scopeId;
}

function variableOptionKey(referenceKey: string, scopeId: string | null | undefined): string {
  return `${normalizeScopeId(scopeId)}${variableOptionSeparator}${referenceKey}`;
}

function parseVariableOptionKey(key: string): { scopeId: string; referenceKey: string } | null {
  const index = key.indexOf(variableOptionSeparator);
  if (index < 0) return null;
  const referenceKey = key.slice(index + variableOptionSeparator.length);
  return referenceKey ? { scopeId: key.slice(0, index), referenceKey } : null;
}

// Scope-aware variable picker for inputs whose expression syntax is "Variable". Lists only the
// variables visible from the selected activity (nearest-scope first, supplied by the backend design
// analysis) and writes a structured VariableReference. An out-of-scope existing reference is shown as
// a distinct, repairable option rather than silently dropped (ADR-0027 repair surface).
function VariablePicker({ value, visibleVariables, scopeStatus, disabled, onChange }: {
  value: unknown;
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  disabled?: boolean;
  onChange(value: VariableReference): void;
}) {
  const parsed = readVariableReference(value);
  // After switching syntax to "Variable" the previous literal value is preserved (a bare string).
  // Only treat the value as an existing reference when it is structured or matches a visible variable,
  // so arbitrary leftover literal text is not surfaced as a phantom "(not visible)" option.
  const valueIsStructured = (!!value && typeof value === "object") || (typeof value === "string" && value.trim().startsWith("{"));
  const current = parsed && (valueIsStructured || visibleVariables.some(variable => variable.referenceKey === parsed.referenceKey))
    ? parsed
    : null;
  const currentKey = current ? variableOptionKey(current.referenceKey, current.declaringScopeId) : "";
  const currentIsVisible = !!current && visibleVariables.some(
    variable => variable.referenceKey === current.referenceKey && variable.scopeId === normalizeScopeId(current.declaringScopeId)
  );

  return (
    <div className="wf-variable-picker">
      <select
        aria-label="Variable reference"
        value={currentKey}
        disabled={disabled}
        onChange={event => {
          const parsed = parseVariableOptionKey(event.target.value);
          if (parsed) onChange(makeVariableReference(parsed.referenceKey, parsed.scopeId));
        }}
      >
        <option value="">Select a variable…</option>
        {current && !currentIsVisible ? (
          <option value={currentKey}>{current.referenceKey} (not visible from this scope)</option>
        ) : null}
        {visibleVariables.map(variable => {
          const optionKey = variableOptionKey(variable.referenceKey, variable.scopeId);
          return (
            <option key={optionKey} value={optionKey}>
              {variable.name}{variable.isWorkflowScope ? " · workflow" : " · container"}
            </option>
          );
        })}
      </select>
      {scopeStatus === "unavailable" ? (
        <p className="wf-variable-picker-note">Variable scope information is unavailable (pending backend support). Existing references are preserved.</p>
      ) : scopeStatus === "ready" && visibleVariables.length === 0 ? (
        <p className="wf-variable-picker-note">No variables are visible here. Declare one on the workflow or a container scope.</p>
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
