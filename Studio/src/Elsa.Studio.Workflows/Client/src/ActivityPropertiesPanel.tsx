import { type KeyboardEvent as ReactKeyboardEvent, useEffect, useId, useRef, useState } from "react";
import { Maximize2, X } from "lucide-react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";
import type {
  StudioActivityDescriptor,
  StudioActivityCustomProperties,
  StudioActivityInputDescriptor,
  StudioActivityPropertyGroupDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext,
  StudioExpressionEditorContribution,
  StudioExpressionEditorContext,
  StudioExpressionEditorDiagnostic,
  StudioExpressionEditorProps,
  StudioExpressionDescriptor,
  StudioExpressionEditingMode
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode, VisibleVariableView, WorkflowDefinitionState } from "./workflowTypes";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import {
  describeCollectionType,
  describeDictionaryType,
  formatTypeName,
  getLiteralEditorValue,
  getLiteralDefaultValue,
  isRepeaterOptOut,
  planExpressionModeTransition,
  readWrappedInput,
  withLiteralValue,
  withExpression,
  writeInputValue
} from "./activityProperties";
import { readOptionsProvider, useActivityInputOptions } from "./activityInputOptions";
import {
  readWorkflowInputs,
  WorkflowReferenceAuthoringProvider
} from "./workflowReferenceAuthoring";
import { CollectionValueEditor } from "./CollectionValueEditor";
import { DictionaryValueEditor } from "./DictionaryValueEditor";
import { clearDictionaryEditorSessionScope } from "./dictionaryEditorSession";

const inlineSyntaxEditorIds = new Set([
  "studio.property.singleline",
  "studio.property.text-fallback",
  "studio.property.checkbox"
]);
export interface ActivityPropertiesPanelProps {
  context?: StudioEndpointContext;
  workflowState?: WorkflowDefinitionState;
  activity: ActivityNode;
  descriptor: StudioActivityDescriptor | null;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  expressionDescriptorStatus: "loading" | "ready" | "failed";
  onRetryDescriptors?: () => void;
  descriptorStatus: "loading" | "ready" | "failed";
  // Variables visible from this activity's scope (nearest-scope first) for the Variable picker, plus
  // the analysis status so the picker can explain an absent backend endpoint instead of showing empty.
  visibleVariables: VisibleVariableView[];
  scopeStatus: ScopedVariableAnalysisStatus;
  scopeRetry?: () => void;
  dictionarySessionScope?: string;
  onChange(activity: ActivityNode): void;
}

export function ActivityPropertiesPanel({
  context = unavailableEndpointContext,
  workflowState = {},
  activity,
  descriptor,
  editors,
  expressionEditors,
  expressionDescriptors,
  expressionDescriptorStatus,
  onRetryDescriptors,
  descriptorStatus,
  visibleVariables,
  scopeStatus,
  scopeRetry,
  dictionarySessionScope,
  onChange
}: ActivityPropertiesPanelProps) {
  const generatedDictionarySessionScope = useId();
  const effectiveDictionarySessionScope = dictionarySessionScope ?? generatedDictionarySessionScope;

  useEffect(() => () => clearDictionaryEditorSessionScope(effectiveDictionarySessionScope), [effectiveDictionarySessionScope]);

  if (descriptorStatus === "loading") {
    return <p className="wf-muted">Loading activity properties...</p>;
  }

  if (!descriptor) {
    return <p className="wf-muted">No activity descriptor is available for this activity.</p>;
  }

  const inputs = descriptor.inputs.filter(input => input.isBrowsable !== false);

  if (inputs.length === 0) {
    return <p className="wf-muted">This activity does not expose editable properties.</p>;
  }

  const groups = groupInputs(inputs, readPropertyGroupMetadata(descriptor.customProperties));
  return (
    <WorkflowReferenceAuthoringProvider
      workflowState={workflowState}
      workflowInputs={readWorkflowInputs(workflowState.inputs)}
      visibleVariables={visibleVariables}
      status={scopeStatus}
      retry={scopeRetry}
    >
    <div className="wf-properties">
      <span className="wf-section-label">Properties</span>
      <ExpressionDescriptorStatus
        status={expressionDescriptorStatus}
        hasSnapshot={expressionDescriptors.length > 0}
        onRetry={onRetryDescriptors}
      />
      {groups.map(group => (
        <section key={group.category} className="wf-property-group">
          {groups.length > 1 || group.configured || group.category !== "General" ? <h4>{group.label}</h4> : null}
          {group.inputs.map(input => (
            <PropertyRow
              key={input.name}
              activity={activity}
              activityDescriptor={descriptor}
              endpointContext={context}
              workflowState={workflowState}
              dictionarySessionScope={effectiveDictionarySessionScope}
              input={input}
              editors={editors}
              expressionEditors={expressionEditors}
              expressionDescriptors={expressionDescriptors}
              onChange={onChange}
            />
          ))}
        </section>
      ))}
    </div>
    </WorkflowReferenceAuthoringProvider>
  );
}

function ExpressionDescriptorStatus({
  status,
  hasSnapshot,
  onRetry
}: {
  status: "loading" | "ready" | "failed";
  hasSnapshot: boolean;
  onRetry?: () => void;
}) {
  if (status === "loading") {
    return <p className="wf-muted" role="status">{hasSnapshot
      ? "Refreshing expression types... Using the last loaded metadata."
      : "Loading expression types..."}</p>;
  }
  if (status === "ready" && !hasSnapshot) {
    return <p className="wf-muted" role="status">No expression types are available.</p>;
  }
  if (status !== "failed") return null;
  return (
    <div className="wf-property-options-error" role="alert">
      <span>{hasSnapshot
        ? "Expression types could not be refreshed. Using the last loaded metadata."
        : "Expression types could not be loaded."}</span>
      {onRetry ? <button type="button" className="wf-expression-descriptors-retry" onClick={onRetry}>Retry</button> : null}
    </div>
  );
}

const unavailableEndpointContext = {} as StudioEndpointContext;

function PropertyRow({
  activity,
  activityDescriptor,
  endpointContext,
  workflowState,
  dictionarySessionScope,
  input,
  editors,
  expressionEditors,
  expressionDescriptors,
  onChange
}: {
  activity: ActivityNode;
  activityDescriptor: StudioActivityDescriptor;
  endpointContext: StudioEndpointContext;
  workflowState: WorkflowDefinitionState;
  dictionarySessionScope: string;
  input: StudioActivityInputDescriptor;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  onChange(activity: ActivityNode): void;
}) {
  const readOnly = input.isReadOnly === true;
  const dynamicOptions = useActivityInputOptions(endpointContext, workflowState, activity, activityDescriptor, input);
  const provider = readOptionsProvider(input);
  const effectiveInput = provider ? {
    ...input,
    uiSpecifications: { ...input.uiSpecifications, options: dynamicOptions.options }
  } : input;
  const editorDisabled = readOnly || (!!provider && dynamicOptions.status !== "ready");
  const context: StudioActivityPropertyEditorContext = { activity, expressionDescriptors, readOnly: editorDisabled };
  const editor = resolveEditor(editors, effectiveInput, context);
  const EditorComponent = editor?.component;
  const wrapped = input.isWrapped !== false ? readWrappedInput(activity, input) : null;
  const syntax = wrapped?.expression.type ?? "Literal";
  const expressionDescriptor = expressionDescriptors.find(descriptor => descriptor.type === syntax);
  const editingMode = expressionDescriptor?.editingMode;
  const value = getLiteralEditorValue(activity, input);
  const dictionaryType = wrapped && !isRepeaterOptOut(effectiveInput) && (editingMode === "literal" || syntax === "Object")
    ? describeDictionaryType(effectiveInput.typeName)
    : null;
  const collectionType = wrapped && editingMode === "literal" && !isRepeaterOptOut(effectiveInput)
    ? describeCollectionType(effectiveInput.typeName)
    : null;
  const makeExpressionContext = (targetSyntax: string): StudioExpressionEditorContext => ({
    activity,
    descriptor: effectiveInput,
    expressionDescriptors,
    readOnly,
    surface: "inline",
    syntax: targetSyntax
  });
  const inlineExpressionContext: StudioExpressionEditorContext | null = wrapped ? makeExpressionContext(syntax) : null;
  const currentRequiresAdmission = editingMode === "structured" || editingMode === "reference";
  const admittedExpressionEditor = inlineExpressionContext && currentRequiresAdmission
    ? resolveAdmittedExpressionEditor(expressionEditors, inlineExpressionContext)
    : null;
  const inlineExpressionEditor = inlineExpressionContext
    ? currentRequiresAdmission ? admittedExpressionEditor : resolveExpressionEditor(expressionEditors, inlineExpressionContext)
    : null;
  const InlineExpressionEditorComponent = inlineExpressionEditor?.surfaces.inline;
  const inlineDiagnosticProvider = inlineExpressionContext
    ? resolveExpressionDiagnosticProvider(expressionEditors, inlineExpressionContext)
    : null;
  const inlineDiagnostics = inlineDiagnosticProvider && inlineExpressionContext
    ? getExpressionEditorDiagnostics(inlineDiagnosticProvider, inlineExpressionContext, value)
    : [];
  // A collection renders a multi-row repeater, not a single-line field, so the inline text chrome (the
  // overlaid syntax picker + expand button, positioned top:4/bottom:4 of the field) must not wrap it —
  // it would stretch down the whole list and cover the per-row reorder controls. Such inputs still get a
  // syntax picker, but the block one above the list. `uiHint: "singleline"` is common on list inputs, so
  // gating on the collection itself (not the hint) is what keeps the two features from colliding.
  const structuredCollectionType = editingMode === "structured" && admittedExpressionEditor && !isRepeaterOptOut(effectiveInput)
    ? describeCollectionType(effectiveInput.typeName)
    : null;
  const isCollectionEditor = dictionaryType != null || collectionType != null || structuredCollectionType != null;
  const useInlineSyntaxPicker = Boolean(wrapped && !isCollectionEditor && (
    editingMode === "text" || editingMode === "structured" || isSingleLineTextInput(input, editor?.id)
  ));
  const useToggleLayout = editor?.id === "studio.property.checkbox" && editingMode === "literal";
  const canExpandEditor = Boolean(wrapped && (
    dictionaryType != null ||
    editingMode === "text" ||
    (!isCollectionEditor && editingMode === "structured" && !!inlineExpressionEditor?.surfaces.expanded) ||
    (!isCollectionEditor && editingMode === "literal" && isExpandableTextInput(input, editor?.id))
  ));
  const [expanded, setExpanded] = useState(false);
  const [focusRequested, setFocusRequested] = useState(false);
  const [pendingTransition, setPendingTransition] = useState<{
    descriptor: StudioExpressionDescriptor;
    nextValue: unknown;
  } | null>(null);
  const rowRef = useRef<HTMLDivElement>(null);
  const cancelTransitionRef = useRef<HTMLButtonElement>(null);
  const transitionDescriptionId = useId();

  useEffect(() => {
    if (!focusRequested) return;
    const frame = requestAnimationFrame(() => {
      const editor = rowRef.current?.querySelector<HTMLElement>(
        "input:not(:disabled), textarea:not(:disabled), select:not(:disabled), [contenteditable='true']"
      );
      editor?.focus();
      setFocusRequested(false);
    });
    return () => cancelAnimationFrame(frame);
  }, [focusRequested]);

  useEffect(() => {
    if (!pendingTransition) return;
    const frame = requestAnimationFrame(() => cancelTransitionRef.current?.focus());
    return () => cancelAnimationFrame(frame);
  }, [pendingTransition]);

  const setRaw = (nextValue: unknown) => {
    const next = wrapped ? withLiteralValue(wrapped, nextValue) : nextValue;
    onChange(writeInputValue(activity, input, next));
  };

  const getUnavailableReason = (descriptor: StudioExpressionDescriptor): string | null => {
    if (descriptor.editingMode === "literal" || descriptor.editingMode === "text") return null;
    const targetContext = makeExpressionContext(descriptor.type);
    const candidates = expressionEditors.filter(editor => editor.supports(targetContext));
    if (!candidates.some(editor => !!editor.surfaces.inline)) {
      return `${descriptor.displayName || descriptor.type} requires an inline editor Contribution.`;
    }
    if (!candidates.some(editor => !!editor.surfaces.inline && !!editor.createDefaultValue)) {
      return `${descriptor.displayName || descriptor.type} requires a default value factory.`;
    }
    return null;
  };

  const getTargetDefaultValue = (descriptor: StudioExpressionDescriptor): unknown => {
    if (descriptor.editingMode === "literal") return getLiteralDefaultValue(input);
    const targetContext = makeExpressionContext(descriptor.type);
    const provider = descriptor.editingMode === "structured" || descriptor.editingMode === "reference"
      ? resolveAdmittedExpressionEditor(expressionEditors, targetContext)
      : resolveExpressionDefaultProvider(expressionEditors, targetContext);
    if (provider?.createDefaultValue) return provider.createDefaultValue(targetContext);
    if (descriptor.editingMode === "text") return "";
    return null;
  };

  const applyTransition = (descriptor: StudioExpressionDescriptor, nextValue: unknown) => {
    if (!wrapped) return;
    setPendingTransition(null);
    setFocusRequested(true);
    onChange(writeInputValue(activity, input, withExpression(wrapped, descriptor.type, nextValue)));
  };

  const cancelTransition = () => {
    setPendingTransition(null);
    requestAnimationFrame(() => rowRef.current?.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.focus());
  };

  const closeExpanded = () => {
    setExpanded(false);
    requestAnimationFrame(() => rowRef.current?.querySelector<HTMLButtonElement>(
      ".wf-dictionary-open-expanded, .wf-property-expand-row, .wf-expression-expand-button"
    )?.focus());
  };

  const setSyntax = (nextSyntax: string) => {
    if (!wrapped || nextSyntax === syntax) return;
    const nextDescriptor = expressionDescriptors.find(descriptor => descriptor.type === nextSyntax);
    if (!nextDescriptor || getUnavailableReason(nextDescriptor)) return;
    const transition = planExpressionModeTransition(
      editingMode ?? "structured",
      nextDescriptor.editingMode,
      input.typeName,
      value,
      getTargetDefaultValue(nextDescriptor)
    );
    if (transition.requiresConfirmation) {
      setExpanded(false);
      setPendingTransition({ descriptor: nextDescriptor, nextValue: transition.nextValue });
      return;
    }
    applyTransition(nextDescriptor, transition.nextValue);
  };
  // The whole collection concept resolves to a single node: a collection-scoped editor when one claims
  // the value (e.g. a multi-select for option sets), otherwise a repeater of per-element editors.
  const collectionEditor = collectionType
    ? (
      <CollectionValueEditor
        input={effectiveInput}
        elementTypeName={collectionType.elementTypeName}
        value={value}
        editors={editors}
        context={context}
        disabled={editorDisabled}
        onChange={setRaw}
      />
    )
    : null;
  const dictionaryEditor = dictionaryType ? (
    <DictionaryValueEditor
      key={expanded ? "dictionary-inline-expanded" : "dictionary-inline-collapsed"}
      input={effectiveInput}
      sessionScopeKey={dictionarySessionScope}
      valueTypeName={dictionaryType.valueTypeName}
      value={value}
      editors={editors}
      context={context}
      disabled={editorDisabled}
      onOpenExpanded={() => setExpanded(true)}
      onChange={setRaw}
    />
  ) : null;
  const contributedExpressionEditor = InlineExpressionEditorComponent && inlineExpressionContext ? (
    <InlineExpressionEditorComponent
      descriptor={effectiveInput}
      syntax={syntax}
      value={value}
      disabled={editingMode === "structured" ? editorDisabled : readOnly}
      initialFocus={focusRequested && !expanded}
      context={inlineExpressionContext}
      onChange={setRaw}
    />
  ) : null;
  const valueEditor = editingMode === "text" && inlineExpressionContext ? (
    contributedExpressionEditor ?? (
      <GenericTextExpressionEditor
        descriptor={effectiveInput}
        syntax={syntax}
        value={value}
        disabled={readOnly}
        initialFocus={focusRequested && !expanded}
        context={inlineExpressionContext}
        onChange={setRaw}
      />
    )
  ) : dictionaryEditor ?? collectionEditor ?? contributedExpressionEditor ?? (editingMode === "literal"
    ? renderEditor(EditorComponent, effectiveInput, value, editorDisabled, context, setRaw)
    : <UnavailableExpressionEditor syntax={syntax} />);

  return (
    <div ref={rowRef} className="wf-property-row">
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
          getUnavailableReason={getUnavailableReason}
          disabled={readOnly}
          onChange={setSyntax}
        />
      ) : null}
      {useInlineSyntaxPicker ? (
        <div className={useToggleLayout ? "wf-expression-field wf-expression-field--toggle" : "wf-expression-field"}>
          <div className="wf-expression-editor">
            {valueEditor}
            {renderExpressionDiagnostics(inlineDiagnostics)}
          </div>
          <SyntaxPicker
            label={`${input.displayName || input.name} expression syntax`}
            value={syntax}
            descriptors={expressionDescriptors}
            getUnavailableReason={getUnavailableReason}
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
      {pendingTransition ? (
        <div
          className="wf-expression-transition-confirmation"
          role="alertdialog"
          aria-label="Confirm expression type change"
          aria-describedby={transitionDescriptionId}
          onKeyDown={event => {
            if (event.key !== "Escape") return;
            event.preventDefault();
            cancelTransition();
          }}
        >
          <p id={transitionDescriptionId}>
            Switching to {pendingTransition.descriptor.displayName || pendingTransition.descriptor.type} will replace the current value.
          </p>
          <div>
            <button
              type="button"
              onClick={() => applyTransition(pendingTransition.descriptor, pendingTransition.nextValue)}
            >
              Replace value
            </button>
            <button
              ref={cancelTransitionRef}
              type="button"
              onClick={cancelTransition}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}
      {canExpandEditor && !useInlineSyntaxPicker && !dictionaryType ? (
        <button
          type="button"
          className="wf-property-expand-row"
          aria-label={`Open expanded ${input.displayName || input.name} editor`}
          onClick={() => setExpanded(true)}
        >
          <Maximize2 size={13} /> Open expanded editor
        </button>
      ) : null}
      {provider && dynamicOptions.status === "loading" ? (
        <p className="wf-property-options-status" role="status">Loading options...</p>
      ) : null}
      {provider && dynamicOptions.status === "error" ? (
        <div className="wf-property-options-error" role="alert">
          <span>Options are unavailable.</span>
          <button type="button" onClick={dynamicOptions.retry}>Retry</button>
        </div>
      ) : null}
      {provider && dynamicOptions.status === "ready" && dynamicOptions.options.length === 0 ? (
        <p className="wf-property-options-status" role="status">No options available.</p>
      ) : null}
      {expanded ? (
        <ExpandedPropertyEditor
          input={input}
          dictionarySessionScope={dictionarySessionScope}
          value={value}
          syntax={syntax}
          editingMode={editingMode}
          descriptors={expressionDescriptors}
          getUnavailableReason={getUnavailableReason}
          activity={activity}
          propertyEditors={editors}
          expressionEditors={expressionEditors}
          disabled={readOnly}
          onChange={setRaw}
          onSyntaxChange={setSyntax}
          onClose={closeExpanded}
        />
      ) : null}
    </div>
  );
}

function ExpandedPropertyEditor({
  input,
  dictionarySessionScope,
  value,
  syntax,
  editingMode,
  descriptors,
  getUnavailableReason,
  activity,
  propertyEditors,
  expressionEditors,
  disabled,
  onChange,
  onSyntaxChange,
  onClose
}: {
  input: StudioActivityInputDescriptor;
  dictionarySessionScope: string;
  value: unknown;
  syntax: string;
  editingMode: StudioExpressionEditingMode | undefined;
  descriptors: StudioExpressionDescriptor[];
  getUnavailableReason(descriptor: StudioExpressionDescriptor): string | null;
  activity: ActivityNode;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  disabled: boolean;
  onChange(value: unknown): void;
  onSyntaxChange(value: string): void;
  onClose(): void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const fallbackEditorRef = useRef<HTMLTextAreaElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;
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
  const diagnosticProvider = resolveExpressionDiagnosticProvider(expressionEditors, expressionContext);
  const diagnostics = diagnosticProvider ? getExpressionEditorDiagnostics(diagnosticProvider, expressionContext, value) : [];
  const useTextFallback = editingMode === "text";
  const dictionaryType = (editingMode === "literal" || syntax === "Object") && !isRepeaterOptOut(input)
    ? describeDictionaryType(input.typeName)
    : null;
  const fallbackHint = useTextFallback && !ExpressionEditorComponent
    ? getExpressionEditorFallbackHint(expressionEditors, expressionContext)
    : null;

  useEffect(() => {
    if (!ExpressionEditorComponent) fallbackEditorRef.current?.focus();
  }, [ExpressionEditorComponent, syntax]);

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const focusableSelector = [
      "button:not(:disabled)",
      "input:not(:disabled)",
      "textarea:not(:disabled)",
      "select:not(:disabled)",
      "[href]",
      "[tabindex]:not([tabindex='-1'])"
    ].join(",");
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== "Tab") return;
      const focusable = [...(dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? [])]
        .filter(element => element.offsetParent !== null || element === document.activeElement);
      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current?.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = previousOverflow;
      previouslyFocused?.focus();
    };
  }, []);

  return (
    <div className="wf-property-editor-backdrop">
      <section
        ref={dialogRef}
        className="wf-property-editor-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
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
              getUnavailableReason={getUnavailableReason}
              disabled={disabled}
              onChange={onSyntaxChange}
            />
            <span>{formatTypeName(input.typeName)}</span>
          </div>
          {input.description ? <p>{input.description}</p> : null}
          {dictionaryType ? (
            <DictionaryValueEditor
              input={input}
              sessionScopeKey={dictionarySessionScope}
              valueTypeName={dictionaryType.valueTypeName}
              value={value}
              editors={propertyEditors}
              context={{ activity, expressionDescriptors: descriptors, readOnly: disabled }}
              disabled={disabled}
              variant="expanded"
              onChange={onChange}
            />
          ) : ExpressionEditorComponent ? (
            <ExpressionEditorComponent
              descriptor={input}
              syntax={syntax}
              value={value}
              disabled={disabled}
              initialFocus
              context={expressionContext}
              onChange={onChange}
            />
          ) : useTextFallback || editingMode === "literal" ? (
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
                ref={fallbackEditorRef}
                onChange={event => onChange(event.target.value)}
              />
            </>
          ) : (
            <UnavailableExpressionEditor syntax={syntax} />
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

function GenericTextExpressionEditor({ descriptor, syntax, value, disabled, initialFocus, onChange }: StudioExpressionEditorProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (initialFocus) inputRef.current?.focus();
  }, [initialFocus, syntax]);

  return (
    <input
      type="text"
      aria-label={`${descriptor.displayName || descriptor.name} expression`}
      value={value == null ? "" : String(value)}
      disabled={disabled}
      ref={inputRef}
      spellCheck={false}
      autoCapitalize="off"
      autoCorrect="off"
      onChange={event => onChange(event.target.value)}
    />
  );
}

function UnavailableExpressionEditor({ syntax }: { syntax: string }) {
  return (
    <p className="wf-expression-editor-hint" role="status">
      No editor is available for {syntax}. The current value is preserved and read-only.
    </p>
  );
}

export function SyntaxPicker({
  label,
  value,
  descriptors,
  getUnavailableReason = () => null,
  disabled,
  variant = "block",
  onChange
}: {
  label: string;
  value: string;
  descriptors: StudioExpressionDescriptor[];
  getUnavailableReason?(descriptor: StudioExpressionDescriptor): string | null;
  disabled: boolean;
  variant?: "block" | "inline";
  onChange(value: string): void;
}) {
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const listboxId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const selected = descriptors.find(descriptor => descriptor.type === value);
  const selectedIndex = Math.max(0, descriptors.findIndex(descriptor => descriptor.type === value));
  const unavailableReasons = descriptors.map(getUnavailableReason);
  const availableIndices = descriptors.flatMap((_, index) => unavailableReasons[index] ? [] : [index]);
  const className = [
    "wf-syntax-picker-trigger",
    variant === "inline" ? "inline" : "",
    open ? "open" : ""
  ].filter(Boolean).join(" ");

  useEffect(() => {
    if (!open) return;
    const frame = requestAnimationFrame(() => {
      if (activeIndex >= 0) optionRefs.current[activeIndex]?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [activeIndex, open]);

  const openPicker = (index = selectedIndex) => {
    setActiveIndex(unavailableReasons[index] ? availableIndices[0] ?? -1 : index);
    setOpen(true);
  };

  const closePicker = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  };

  const focusOption = (index: number, direction: 1 | -1) => {
    if (availableIndices.length === 0) return;
    let nextIndex = (index + descriptors.length) % descriptors.length;
    while (unavailableReasons[nextIndex]) {
      nextIndex = (nextIndex + direction + descriptors.length) % descriptors.length;
    }
    setActiveIndex(nextIndex);
    optionRefs.current[nextIndex]?.focus();
  };

  const selectOption = (index: number) => {
    const descriptor = descriptors[index];
    if (!descriptor || unavailableReasons[index]) return;
    onChange(descriptor.type);
    closePicker(true);
  };

  const handleTriggerKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (open && event.key === "Escape") {
      event.preventDefault();
      closePicker(true);
      return;
    }
    if (open && event.key === "Tab") {
      setOpen(false);
      return;
    }
    if (event.key === "ArrowDown" || event.key === "ArrowUp" || event.key === "Home" || event.key === "End") {
      event.preventDefault();
      const index = event.key === "Home"
        ? availableIndices[0] ?? -1
        : event.key === "End"
          ? availableIndices.at(-1) ?? -1
          : selectedIndex;
      openPicker(index);
    }
  };

  const handleListboxKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const focusedIndex = Math.max(0, optionRefs.current.findIndex(option => option === event.target));
    if (event.key === "ArrowDown") {
      event.preventDefault();
      focusOption(focusedIndex + 1, 1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      focusOption(focusedIndex - 1, -1);
    } else if (event.key === "Home") {
      event.preventDefault();
      if (availableIndices[0] != null) focusOption(availableIndices[0], 1);
    } else if (event.key === "End") {
      event.preventDefault();
      if (availableIndices.at(-1) != null) focusOption(availableIndices.at(-1)!, -1);
    } else if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      selectOption(focusedIndex);
    } else if (event.key === "Escape") {
      event.preventDefault();
      closePicker(true);
    } else if (event.key === "Tab") {
      setOpen(false);
    }
  };

  return (
    <div className={variant === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker"}>
      <button
        ref={triggerRef}
        type="button"
        className={className}
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        disabled={disabled}
        onClick={() => open ? closePicker(false) : openPicker()}
        onKeyDown={handleTriggerKeyDown}
      >
        <span>{selected?.displayName || selected?.type || value}</span>
      </button>
      <AnchoredPopover
        anchorRef={triggerRef}
        open={open}
        className="wf-syntax-picker-menu"
        minWidth={variant === "inline" ? 176 : 0}
        maxHeight={210}
        onDismiss={reason => closePicker(reason !== "anchor-hidden")}
      >
        <div id={listboxId} role="listbox" aria-label={label} onKeyDown={handleListboxKeyDown}>
          {descriptors.map((descriptor, index) => {
            const optionLabel = descriptor.displayName || descriptor.type;
            const selectedOption = descriptor.type === value;
            const unavailableReason = unavailableReasons[index];
            return (
              <button
                ref={element => { optionRefs.current[index] = element; }}
                type="button"
                role="option"
                aria-selected={selectedOption}
                aria-disabled={unavailableReason ? true : undefined}
                disabled={!!unavailableReason}
                title={unavailableReason ?? undefined}
                tabIndex={-1}
                key={descriptor.type}
                className={selectedOption ? "selected" : ""}
                onFocus={() => setActiveIndex(index)}
                onClick={() => selectOption(index)}
              >
                <span>{optionLabel}</span>
                {unavailableReason ? <small>{unavailableReason}</small> : null}
              </button>
            );
          })}
        </div>
      </AnchoredPopover>
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

function resolveAdmittedExpressionEditor(
  editors: StudioExpressionEditorContribution[],
  context: StudioExpressionEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => !!editor.surfaces.inline && !!editor.createDefaultValue && editor.supports(context));
}

function resolveExpressionDefaultProvider(
  editors: StudioExpressionEditorContribution[],
  context: StudioExpressionEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => !!editor.createDefaultValue && editor.supports(context));
}

function resolveExpressionDiagnosticProvider(
  editors: StudioExpressionEditorContribution[],
  context: StudioExpressionEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => !!editor.diagnostics && editor.supports(context));
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

type ResolvedPropertyGroupMetadata = Required<StudioActivityPropertyGroupDescriptor>;

function readPropertyGroupMetadata(customProperties: StudioActivityCustomProperties | undefined): ResolvedPropertyGroupMetadata[] {
  if (!customProperties) return [];

  const candidates = customProperties.propertyGroups;
  if (!Array.isArray(candidates)) return [];

  const groups: ResolvedPropertyGroupMetadata[] = [];
  const seen = new Set<string>();
  candidates.forEach((candidate, index) => {
    const record = asRecord(candidate);
    const category = readFirstNonEmptyString(record?.category, record?.name, record?.id);
    if (!category || seen.has(category)) return;

    const label = readFirstNonEmptyString(record?.label, record?.displayName) ?? category;
    const configuredOrder = typeof record?.order === "number" && Number.isFinite(record.order) ? record.order : index;
    groups.push({ category, label, order: configuredOrder });
    seen.add(category);
  });
  return groups;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function readNonEmptyString(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function readFirstNonEmptyString(...values: unknown[]): string | null {
  for (const value of values) {
    const text = readNonEmptyString(value);
    if (text) return text;
  }

  return null;
}

function groupInputs(inputs: StudioActivityInputDescriptor[], metadata: ResolvedPropertyGroupMetadata[]) {
  const metadataByCategory = new Map(metadata.map(group => [group.category, group]));
  const groups = new Map<string, StudioActivityInputDescriptor[]>();
  const orderedInputs = [...inputs]
    .sort((left, right) => (left.order ?? 0) - (right.order ?? 0) || left.name.localeCompare(right.name));

  for (const input of orderedInputs) {
    const category = input.category?.trim() || "General";
    const group = groups.get(category);
    if (group) group.push(input);
    else groups.set(category, [input]);
  }

  return [...groups.entries()]
    .map(([category, groupedInputs]) => {
      const configured = metadataByCategory.get(category);
      return {
        category,
        label: configured?.label ?? category,
        order: configured?.order ?? Math.min(...groupedInputs.map(input => input.order ?? 0)),
        configured: configured != null,
        inputs: groupedInputs
      };
    })
    .sort((left, right) =>
      left.order - right.order ||
      left.category.localeCompare(right.category)
    );
}

function isSingleLineTextInput(input: StudioActivityInputDescriptor, editorId: string | undefined) {
  if (input.uiHint?.toLowerCase() === "multiline") return false;
  if (editorId && !inlineSyntaxEditorIds.has(editorId)) return false;
  if (editorId === "studio.property.checkbox") return true;

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
