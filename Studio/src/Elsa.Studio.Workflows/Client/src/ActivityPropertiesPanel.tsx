import { type KeyboardEvent as ReactKeyboardEvent, memo, useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Maximize2, SlidersHorizontal, X } from "lucide-react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";
import {
  authSessionEndedEvent,
  authSessionStartedEvent,
  expressionEditorSessionEndedEvent,
  expressionToolingAuthorizationRestoredEvent
} from "@elsa-workflows/studio-sdk";
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
  StudioExpressionAuthoringContext,
  StudioExpressionDocument,
  StudioExpressionToolingClient,
  StudioExpressionToolingResult,
  StudioExpressionValidationResult,
  StudioExpressionDescriptor,
  StudioExpressionEditingMode
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode, VisibleVariableView, WorkflowDefinitionState } from "./workflowTypes";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ScopedVariableAnalysisStatus } from "./api/workflowDesign";
import {
  formatTypeName,
  getInputPropertyName,
  getLiteralEditorValue,
  getLiteralDefaultValue,
  isRepeaterOptOut,
  planExpressionModeTransition,
  readWrappedInput,
  withConversion,
  withLiteralValue,
  withExpression,
  writeInputValue,
  type WrappedActivityInputValue
} from "./activityProperties";
import {
  builtInConversionProfiles,
  conversionModeDescriptors,
  describeInferredSource,
  isDefaultConversion,
  readConversionMode,
  readConversionProfile,
  withConversionMode,
  withConversionProfile,
  type ConversionMode,
  type ConversionProfileReference
} from "./conversionSettings";
import { listConversionProfiles } from "./api/expressions";
import { describeCollectionForInput, describeDictionaryForInput } from "./collectionInputDescriptor";
import { readOptionsProvider, useActivityInputOptions } from "./activityInputOptions";
import {
  readWorkflowInputs,
  WorkflowReferenceAuthoringProvider
} from "./workflowReferenceAuthoring";
import { CollectionValueEditor } from "./CollectionValueEditor";
import { DictionaryValueEditor } from "./DictionaryValueEditor";
import { clearDictionaryEditorSessionScope } from "./dictionaryEditorSession";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import { createActivityExpressionDocument } from "./activityExpressionDocument";

const inlineSyntaxEditorIds = new Set([
  "studio.property.singleline",
  "studio.property.text-fallback",
  "studio.property.checkbox"
]);
const inlineTextTypeNames = new Set(["string", "system.string", "text", "uri", "system.uri"]);
export interface ActivityPropertiesPanelProps {
  context?: StudioEndpointContext;
  draftId?: string;
  expressionTooling?: StudioExpressionToolingClient;
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
  expressionEditorSessionScope?: string;
  /** Lets an enclosing Inspector tab provide the section context without a duplicate heading. */
  showHeading?: boolean;
  /** Overrides the standalone empty-state copy when the panel is presented as activity inputs. */
  emptyLabel?: string;
  onChange(activity: ActivityNode): void;
}

export function ActivityPropertiesPanel({
  context = unavailableEndpointContext,
  draftId = "transient",
  expressionTooling,
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
  expressionEditorSessionScope,
  showHeading = true,
  emptyLabel = "This activity does not expose editable properties.",
  onChange
}: ActivityPropertiesPanelProps) {
  const generatedDictionarySessionScope = useId();
  const effectiveDictionarySessionScope = dictionarySessionScope ?? generatedDictionarySessionScope;
  const effectiveExpressionEditorSessionScope = expressionEditorSessionScope ?? effectiveDictionarySessionScope;
  const conversionProfiles = useConversionProfiles(context);
  const [activeToolingProperty, setActiveToolingProperty] = useState<string>();
  const toolingAuthorization = useExpressionToolingAuthorization(effectiveExpressionEditorSessionScope);
  const activateToolingProperty = useCallback((property: string) => setActiveToolingProperty(property), []);

  useEffect(() => () => clearDictionaryEditorSessionScope(effectiveDictionarySessionScope), [effectiveDictionarySessionScope]);
  useEffect(() => {
    if (expressionEditorSessionScope) return;
    return () => {
      window.dispatchEvent(new CustomEvent(expressionEditorSessionEndedEvent, {
        detail: { scope: effectiveExpressionEditorSessionScope }
      }));
    };
  }, [effectiveExpressionEditorSessionScope, expressionEditorSessionScope]);

  if (descriptorStatus === "loading") {
    return <p className="wf-muted">Loading activity properties...</p>;
  }

  if (!descriptor) {
    return <p className="wf-muted">No activity descriptor is available for this activity.</p>;
  }

  const inputs = descriptor.inputs.filter(input => input.isBrowsable !== false);

  if (inputs.length === 0) {
    return <p className="wf-muted">{emptyLabel}</p>;
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
      {showHeading ? <span className="wf-section-label">Properties</span> : null}
      <ExpressionDescriptorStatus
        status={expressionDescriptorStatus}
        hasSnapshot={expressionDescriptors.length > 0}
        onRetry={onRetryDescriptors}
      />
      {groups.map(group => (
        <section key={group.category} className="wf-property-group">
          {groups.length > 1 || group.configured || group.category !== "General" ? <h4>{group.label}</h4> : null}
          {group.inputs.map(input => (
            <MemoizedPropertyRow
              key={input.name}
              activity={activity}
              activityDescriptor={descriptor}
              endpointContext={context}
              draftId={draftId}
              expressionTooling={expressionTooling}
              workflowState={workflowState}
              dictionarySessionScope={effectiveDictionarySessionScope}
              expressionEditorSessionScope={effectiveExpressionEditorSessionScope}
              toolingAuthorizationAvailable={toolingAuthorization.available}
              toolingAuthorizationEpoch={toolingAuthorization.epoch}
              toolingAuthorizationConfirmationRequired={toolingAuthorization.confirmationRequired}
              onToolingAuthorizationConfirmed={toolingAuthorization.confirm}
              input={input}
              editors={editors}
              expressionEditors={expressionEditors}
              expressionDescriptors={expressionDescriptors}
              conversionProfiles={conversionProfiles}
              toolingActive={activeToolingProperty === `${activity.nodeId}\u001f${input.referenceKey?.trim() || input.name}`}
              onToolingFocus={activateToolingProperty}
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

function useExpressionToolingSnapshot(
  tooling: StudioExpressionToolingClient | undefined,
  document: StudioExpressionDocument,
  workflowState: WorkflowDefinitionState,
  enabled: boolean,
  authorizationEpoch: number,
  authorizationConfirmationRequired: boolean,
  onAuthorizationConfirmed: () => void
) {
  const [authoringContext, setAuthoringContext] =
    useState<StudioExpressionToolingResult<StudioExpressionAuthoringContext>>();
  const [validation, setValidation] =
    useState<StudioExpressionToolingResult<StudioExpressionValidationResult>>();
  const validateNowRef = useRef<() => void>(() => {});
  const validateNow = useCallback(() => validateNowRef.current(), []);

  useEffect(() => {
    if (!tooling || !enabled) {
      validateNowRef.current = () => {};
      setAuthoringContext(undefined);
      setValidation(undefined);
      return;
    }

    // A snapshot is revision-bound. Suppress the previous revision immediately while the
    // debounced context and validation requests for the new source are in flight.
    setAuthoringContext(undefined);
    setValidation(undefined);
    const controller = new AbortController();
    let started = false;
    const run = () => {
      if (started) return;
      started = true;
      if (timer !== undefined) window.clearTimeout(timer);
      void (async () => {
      try {
        const nextContext = await tooling.getAuthoringContext(document, workflowState, controller.signal);
        if (controller.signal.aborted) return;
        setAuthoringContext(nextContext);
        if ((nextContext.state !== "ready" && nextContext.state !== "supported-empty") || !nextContext.data) {
          setValidation(undefined);
          return;
        }
        if (authorizationConfirmationRequired) onAuthorizationConfirmed();
        if (nextContext.data.capabilities?.semanticValidation === false) {
          setValidation(undefined);
          return;
        }
        const nextValidation = await tooling.validate(document, nextContext.data, controller.signal);
        if (!controller.signal.aborted) setValidation(nextValidation);
      } catch {
        if (controller.signal.aborted) return;
        const unavailable: StudioExpressionToolingResult<never> = {
          state: "unavailable",
          contractVersion: 1,
          expressionType: document.expressionType
        };
        setAuthoringContext(unavailable);
        setValidation(undefined);
      }
      })();
    };
    const timer = window.setTimeout(run, 180);
    validateNowRef.current = run;

    return () => {
      if (validateNowRef.current === run) validateNowRef.current = () => {};
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [
    authorizationConfirmationRequired,
    authorizationEpoch,
    document,
    enabled,
    onAuthorizationConfirmed,
    tooling,
    workflowState
  ]);

  return { authoringContext, validation, validateNow };
}

function useExpressionToolingAuthorization(scope: string) {
  const [state, setState] = useState({ available: true, confirmationRequired: false, epoch: 0 });
  useEffect(() => {
    const revoke = () => setState(current => ({
      available: false,
      confirmationRequired: true,
      epoch: current.epoch + 1
    }));
    const restore = () => setState(current => ({
      available: true,
      confirmationRequired: true,
      epoch: current.epoch + 1
    }));
    window.addEventListener(authSessionEndedEvent, revoke);
    window.addEventListener(authSessionStartedEvent, restore);
    return () => {
      window.removeEventListener(authSessionEndedEvent, revoke);
      window.removeEventListener(authSessionStartedEvent, restore);
    };
  }, []);
  const confirm = useCallback(() => {
    setState(current => ({ ...current, confirmationRequired: false }));
    window.dispatchEvent(new CustomEvent(expressionToolingAuthorizationRestoredEvent, {
      detail: { scope }
    }));
  }, [scope]);
  return { ...state, confirm };
}

type PropertyRowProps = {
  activity: ActivityNode;
  activityDescriptor: StudioActivityDescriptor;
  endpointContext: StudioEndpointContext;
  draftId: string;
  expressionTooling?: StudioExpressionToolingClient;
  workflowState: WorkflowDefinitionState;
  dictionarySessionScope: string;
  expressionEditorSessionScope: string;
  toolingAuthorizationAvailable: boolean;
  toolingAuthorizationEpoch: number;
  toolingAuthorizationConfirmationRequired: boolean;
  onToolingAuthorizationConfirmed(): void;
  input: StudioActivityInputDescriptor;
  editors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  conversionProfiles: ConversionProfileReference[];
  toolingActive: boolean;
  onToolingFocus(property: string): void;
  onChange(activity: ActivityNode): void;
};

function PropertyRow({
  activity,
  activityDescriptor,
  endpointContext,
  draftId,
  expressionTooling,
  workflowState,
  dictionarySessionScope,
  expressionEditorSessionScope,
  toolingAuthorizationAvailable,
  toolingAuthorizationEpoch,
  toolingAuthorizationConfirmationRequired,
  onToolingAuthorizationConfirmed,
  input,
  editors,
  expressionEditors,
  expressionDescriptors,
  conversionProfiles,
  toolingActive,
  onToolingFocus,
  onChange
}: PropertyRowProps) {
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
  const expressionSource = value == null ? "" : String(value);
  const documentVersions = useRef(new Map<string, { source: string; version: number }>());
  const propertyKey = input.referenceKey?.trim() || input.name;
  const toolingPropertyKey = `${activity.nodeId}\u001f${propertyKey}`;
  const activateTooling = useCallback(() => onToolingFocus(toolingPropertyKey), [onToolingFocus, toolingPropertyKey]);
  const documentKey = `${draftId}\u001f${activity.nodeId}\u001f${propertyKey}\u001f${syntax}`;
  const previousDocumentVersion = documentVersions.current.get(documentKey);
  const documentVersion = previousDocumentVersion
    ? previousDocumentVersion.source === expressionSource
      ? previousDocumentVersion
      : { source: expressionSource, version: previousDocumentVersion.version + 1 }
    : { source: expressionSource, version: 0 };
  documentVersions.current.set(documentKey, documentVersion);
  const expressionDocument = useMemo(() => createActivityExpressionDocument({
    draftId,
    activityId: activity.nodeId,
    propertyKey,
    expressionType: syntax,
    source: expressionSource,
    sourceVersion: documentVersion.version
  }), [activity.nodeId, documentVersion.version, draftId, expressionSource, propertyKey, syntax]);
  const toolingSnapshot = useExpressionToolingSnapshot(
    expressionTooling,
    expressionDocument,
    workflowState,
    toolingAuthorizationAvailable && toolingActive && wrapped != null && editingMode === "text",
    toolingAuthorizationEpoch,
    toolingAuthorizationConfirmationRequired,
    onToolingAuthorizationConfirmed
  );
  const dictionaryType = wrapped && !isRepeaterOptOut(effectiveInput) && (editingMode === "literal" || syntax === "Object")
    ? describeDictionaryForInput(effectiveInput)
    : null;
  const collectionType = wrapped && editingMode === "literal" && !isRepeaterOptOut(effectiveInput)
    ? describeCollectionForInput(effectiveInput)
    : null;
  const makeExpressionContext = (
    targetSyntax: string,
    surface: StudioExpressionEditorContext["surface"] = "inline"
  ): StudioExpressionEditorContext => ({
    activity,
    descriptor: effectiveInput,
    expressionDescriptors,
    readOnly,
    surface,
    syntax: targetSyntax,
    document: targetSyntax === syntax ? expressionDocument : createActivityExpressionDocument({
      draftId,
      activityId: activity.nodeId,
      propertyKey,
      expressionType: targetSyntax,
      source: expressionSource,
      sourceVersion: 0
    }),
    tooling: expressionTooling,
    editorSessionScope: expressionEditorSessionScope,
    onFocus: activateTooling,
    onBlur: toolingSnapshot.validateNow,
    authoringContext: targetSyntax === syntax ? toolingSnapshot.authoringContext : undefined,
    validation: targetSyntax === syntax ? toolingSnapshot.validation : undefined
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
  // A collection repeater renders multiple rows, not a single-line field, so the inline text chrome (the
  // overlaid syntax picker + expand button, positioned top:4/bottom:4 of the field) must not wrap it —
  // it would stretch down the whole list and cover the per-row reorder controls. Repeaters still get a
  // block picker above the list, while dictionaries use a separate toolbar that keeps the picker clear
  // of their table. `uiHint: "singleline"` is common on list inputs, so gating on the collection itself
  // (not the hint) is what keeps the two features from colliding.
  const structuredCollectionType = editingMode === "structured" && admittedExpressionEditor && !isRepeaterOptOut(effectiveInput)
    ? describeCollectionForInput(effectiveInput)
    : null;
  const isCollectionEditor = dictionaryType != null || collectionType != null || structuredCollectionType != null;
  const useInlineSyntaxPicker = Boolean(wrapped && !isCollectionEditor && (
    editingMode === "text" || editingMode === "structured" || isSingleLineTextInput(input, editor?.id)
  ));
  const useDictionarySyntaxPicker = Boolean(wrapped && dictionaryType != null);
  const useToggleLayout = editor?.id === "studio.property.checkbox" && editingMode === "literal";
  const canExpandEditor = Boolean(wrapped && (
    dictionaryType != null ||
    editingMode === "text" ||
    (!isCollectionEditor && editingMode === "structured" && !!inlineExpressionEditor?.surfaces.expanded) ||
    (!isCollectionEditor && editingMode === "literal" && isExpandableTextInput(input, editor?.id))
  ));
  const [expanded, setExpanded] = useState(false);
  const [focusRequested, setFocusRequested] = useState(false);
  const [conversionOpen, setConversionOpen] = useState(false);
  const [pendingTransition, setPendingTransition] = useState<{
    descriptor: StudioExpressionDescriptor;
    nextValue: unknown;
  } | null>(null);
  const rowRef = useRef<HTMLDivElement>(null);
  const cancelTransitionRef = useRef<HTMLButtonElement>(null);
  const conversionToggleRef = useRef<HTMLButtonElement>(null);
  const transitionDescriptionId = useId();
  const conversionRegionId = useId();
  const conversionMode = wrapped ? readConversionMode(wrapped.conversion) : "auto";
  const conversionAuthored = wrapped ? !isDefaultConversion(wrapped.conversion) : false;
  const conversionModeDisplayName =
    conversionModeDescriptors.find(descriptor => descriptor.mode === conversionMode)?.displayName ?? "Auto";
  const conversionCaption = wrapped
    ? `${describeInferredSource(wrapped.expression.type, wrapped.expression.value, conversionMode)} → ${formatTypeName(input.typeName)}`
    : "";
  const latestProperty = useRef({ activity, input, onChange });
  latestProperty.current = { activity, input, onChange };

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

  useEffect(() => {
    if (!conversionOpen) return;
    // Scoped to the conversion control — a bare trigger query would hit the row's own syntax picker.
    const frame = requestAnimationFrame(() =>
      rowRef.current?.querySelector<HTMLButtonElement>(".wf-conversion-control .wf-syntax-picker-trigger")?.focus());
    return () => cancelAnimationFrame(frame);
  }, [conversionOpen]);

  const closeConversion = () => {
    setConversionOpen(false);
    requestAnimationFrame(() => conversionToggleRef.current?.focus());
  };

  const setRaw = useCallback((nextValue: unknown) => {
    const current = latestProperty.current;
    const currentWrapped = current.input.isWrapped !== false
      ? readWrappedInput(current.activity, current.input)
      : null;
    const next = currentWrapped ? withLiteralValue(currentWrapped, nextValue) : nextValue;
    current.onChange(writeInputValue(current.activity, current.input, next));
  }, []);

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
      onExpand={() => {
        activateTooling();
        setExpanded(true);
      }}
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
  // A single session must never be mounted into two editor engines at once. While the dialog owns the
  // text document, unmount the compact surface; closing remounts it from the same URI-scoped session.
  const renderedValueEditor = expanded && editingMode === "text" ? null : valueEditor;

  return (
    <div ref={rowRef} className="wf-property-row">
      <div className="wf-property-row-header">
        <label>{input.displayName || input.name}</label>
        <div className="wf-property-row-header-meta">
          <span>{formatTypeName(input.typeName)}</span>
          {wrapped ? (
            <button
              ref={conversionToggleRef}
              type="button"
              className={conversionAuthored ? "wf-conversion-toggle authored" : "wf-conversion-toggle"}
              aria-label={`${input.displayName || input.name} conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              title={`Conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              aria-expanded={conversionOpen}
              aria-controls={conversionRegionId}
              onClick={() => (conversionOpen ? closeConversion() : setConversionOpen(true))}
            >
              <SlidersHorizontal size={13} />
            </button>
          ) : null}
        </div>
      </div>
      {input.description ? <p>{input.description}</p> : null}
      {wrapped && !useInlineSyntaxPicker && !useDictionarySyntaxPicker ? (
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
            {renderedValueEditor}
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
      ) : useDictionarySyntaxPicker ? (
        <div className="wf-dictionary-expression">
          <div className="wf-dictionary-expression-toolbar">
            <SyntaxPicker
              label={`${input.displayName || input.name} expression syntax`}
              value={syntax}
              descriptors={expressionDescriptors}
              getUnavailableReason={getUnavailableReason}
              disabled={readOnly}
              variant="inline"
              onChange={setSyntax}
            />
          </div>
          {renderedValueEditor}
          {renderExpressionDiagnostics(inlineDiagnostics)}
        </div>
      ) : (
        <>
          {renderedValueEditor}
          {renderExpressionDiagnostics(inlineDiagnostics)}
        </>
      )}
      {wrapped && (conversionOpen || conversionAuthored) ? (
        <div id={conversionRegionId} className="wf-conversion-region">
          {conversionOpen ? (
            <div
              onKeyDown={event => {
                // The nested mode picker preventDefaults its own Escape (closing its listbox) — only
                // an unhandled Escape collapses the reveal.
                if (event.key !== "Escape" || event.defaultPrevented) return;
                event.preventDefault();
                event.stopPropagation();
                closeConversion();
              }}
            >
              <ConversionControl
                inputLabel={input.displayName || input.name}
                targetTypeName={input.typeName}
                wrapped={wrapped}
                profiles={conversionProfiles}
                disabled={readOnly}
                onChange={next => onChange(writeInputValue(activity, input, next))}
              />
            </div>
          ) : (
            <button
              type="button"
              className="wf-conversion-chip"
              aria-label={`Edit ${input.displayName || input.name} conversion: ${conversionModeDisplayName} (${conversionCaption})`}
              onClick={() => setConversionOpen(true)}
            >
              {conversionModeDisplayName} · {conversionCaption}
            </button>
          )}
        </div>
      ) : null}
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
          expressionContext={makeExpressionContext(syntax, "expanded")}
          disabled={readOnly}
          wrapped={wrapped}
          conversionProfiles={conversionProfiles}
          onChange={setRaw}
          onConversionChange={next => onChange(writeInputValue(activity, input, next))}
          onSyntaxChange={setSyntax}
          onClose={closeExpanded}
        />
      ) : null}
    </div>
  );
}

const MemoizedPropertyRow = memo(PropertyRow, arePropertyRowPropsEqual);

/**
 * Input values are immutable bindings on the activity. Editing one must not make every unrelated
 * property editor rebuild (and, for code editors, reconfigure its language surface). Activity
 * metadata still participates in the comparison so a real activity-level change reaches every row.
 */
function arePropertyRowPropsEqual(previous: PropertyRowProps, next: PropertyRowProps) {
  if (
    previous.activityDescriptor !== next.activityDescriptor ||
    previous.endpointContext !== next.endpointContext ||
    previous.draftId !== next.draftId ||
    previous.expressionTooling !== next.expressionTooling ||
    previous.dictionarySessionScope !== next.dictionarySessionScope ||
    previous.expressionEditorSessionScope !== next.expressionEditorSessionScope ||
    previous.toolingAuthorizationAvailable !== next.toolingAuthorizationAvailable ||
    previous.toolingAuthorizationEpoch !== next.toolingAuthorizationEpoch ||
    previous.toolingAuthorizationConfirmationRequired !== next.toolingAuthorizationConfirmationRequired ||
    previous.onToolingAuthorizationConfirmed !== next.onToolingAuthorizationConfirmed ||
    previous.input !== next.input ||
    previous.editors !== next.editors ||
    previous.expressionEditors !== next.expressionEditors ||
    previous.expressionDescriptors !== next.expressionDescriptors ||
    previous.conversionProfiles !== next.conversionProfiles ||
    previous.toolingActive !== next.toolingActive ||
    previous.onToolingFocus !== next.onToolingFocus ||
    previous.onChange !== next.onChange
  ) return false;

  // Dynamic option providers and the active code editor intentionally observe workflow state. Inactive
  // expression previews do not: their source and document identity are their only changing inputs.
  if ((previous.toolingActive || readOptionsProvider(previous.input)) && previous.workflowState !== next.workflowState) {
    return false;
  }

  if (previous.activity === next.activity) return true;
  const propertyName = getInputPropertyName(previous.input);
  return Object.is(previous.activity[propertyName], next.activity[propertyName]) &&
    hasSameActivityMetadata(previous.activity, next.activity, previous.activityDescriptor.inputs);
}

function hasSameActivityMetadata(
  previous: ActivityNode,
  next: ActivityNode,
  inputs: StudioActivityInputDescriptor[]
) {
  const inputPropertyNames = new Set(inputs.map(getInputPropertyName));
  const propertyNames = new Set([...Object.keys(previous), ...Object.keys(next)]);
  for (const propertyName of propertyNames) {
    if (inputPropertyNames.has(propertyName)) continue;
    if (!Object.is(previous[propertyName], next[propertyName])) return false;
  }
  return true;
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
  expressionContext,
  disabled,
  wrapped,
  conversionProfiles,
  onChange,
  onConversionChange,
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
  expressionContext: StudioExpressionEditorContext;
  disabled: boolean;
  wrapped: WrappedActivityInputValue | null;
  conversionProfiles: ConversionProfileReference[];
  onChange(value: unknown): void;
  onConversionChange(next: WrappedActivityInputValue): void;
  onSyntaxChange(value: string): void;
  onClose(): void;
}) {
  const titleId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const fallbackEditorRef = useRef<HTMLTextAreaElement>(null);
  const displayName = input.displayName || input.name;
  const expressionEditor = resolveExpressionEditor(expressionEditors, expressionContext);
  const ExpressionEditorComponent = expressionEditor?.surfaces.expanded;
  const diagnosticProvider = resolveExpressionDiagnosticProvider(expressionEditors, expressionContext);
  const diagnostics = diagnosticProvider ? getExpressionEditorDiagnostics(diagnosticProvider, expressionContext, value) : [];
  const useTextFallback = editingMode === "text";
  const dictionaryType = (editingMode === "literal" || syntax === "Object") && !isRepeaterOptOut(input)
    ? describeDictionaryForInput(input)
    : null;
  const fallbackHint = useTextFallback && !ExpressionEditorComponent
    ? getExpressionEditorFallbackHint(expressionEditors, expressionContext)
    : null;

  useEffect(() => {
    if (!ExpressionEditorComponent) fallbackEditorRef.current?.focus();
  }, [ExpressionEditorComponent, syntax]);

  useDialogFocus(dialogRef, onClose, false);

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
          {wrapped ? (
            <ConversionControl
              inputLabel={displayName}
              targetTypeName={input.typeName}
              wrapped={wrapped}
              profiles={conversionProfiles}
              disabled={disabled}
              onChange={onConversionChange}
            />
          ) : null}
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

// Conversion modes reuse the SyntaxPicker listbox; only `type`/`displayName` are read by the picker.
const conversionModePickerDescriptors: StudioExpressionDescriptor[] = conversionModeDescriptors.map(descriptor => ({
  type: descriptor.mode,
  displayName: descriptor.displayName,
  description: descriptor.description,
  editingMode: "literal"
}));

// One request per shell; failures fall back to the built-in profiles (suggestions only — the Profile
// mode keeps free-form id/version entry, so an unreachable listing never blocks authoring).
const conversionProfilesCache = new Map<string, Promise<ConversionProfileReference[]>>();

export function useConversionProfiles(context: StudioEndpointContext): ConversionProfileReference[] {
  const [profiles, setProfiles] = useState<ConversionProfileReference[]>(builtInConversionProfiles);

  useEffect(() => {
    let cancelled = false;
    const key = context.baseUrl ?? "";
    let request = conversionProfilesCache.get(key);
    if (!request) {
      request = listConversionProfiles(context).catch(() => {
        conversionProfilesCache.delete(key);
        return builtInConversionProfiles;
      });
      conversionProfilesCache.set(key, request);
    }
    request.then(result => {
      if (!cancelled) setProfiles(result);
    });
    return () => {
      cancelled = true;
    };
  }, [context]);

  return profiles;
}

/**
 * The binding-edge conversion controls (#449, foundation #782): a mode picker defaulting to Auto, a
 * profile id/version pair for the Profile mode, and the inferred source-representation → destination
 * caption. The authored request lives on the wrapped input value and rides the wire as
 * `ArgumentState.conversion`; the published pinned plan is inspected in the Executable Inspector.
 */
export function ConversionControl({
  inputLabel,
  targetTypeName,
  wrapped,
  profiles,
  disabled,
  onChange
}: {
  inputLabel: string;
  targetTypeName: string;
  wrapped: WrappedActivityInputValue;
  profiles: ConversionProfileReference[];
  disabled: boolean;
  onChange(next: WrappedActivityInputValue): void;
}) {
  const datalistId = useId();
  const mode = readConversionMode(wrapped.conversion);
  const profile = readConversionProfile(wrapped.conversion);

  const setMode = (nextMode: string) => {
    onChange(withConversion(wrapped, withConversionMode(wrapped.conversion, nextMode as ConversionMode)));
  };

  const setProfile = (next: ConversionProfileReference) => {
    onChange(withConversion(wrapped, withConversionProfile(wrapped.conversion, next)));
  };

  const setProfileId = (id: string) => {
    // Selecting a known profile id fills the version when the author hasn't typed one yet.
    const known = profiles.find(candidate => candidate.id === id);
    setProfile({ id, version: profile?.version || (known?.version ?? "") });
  };

  return (
    <div className="wf-conversion-control">
      <div className="wf-conversion-row">
        <span className="wf-conversion-label">Conversion</span>
        <SyntaxPicker
          label={`${inputLabel} conversion mode`}
          value={mode}
          descriptors={conversionModePickerDescriptors}
          disabled={disabled}
          onChange={setMode}
        />
        <span
          className="wf-conversion-caption"
          title="Inferred while authoring; the published executable's pinned plan is authoritative."
        >
          {describeInferredSource(wrapped.expression.type, wrapped.expression.value, mode)} → {formatTypeName(targetTypeName)}
        </span>
      </div>
      {mode === "profile" ? (
        <div className="wf-conversion-profile">
          <input
            type="text"
            aria-label={`${inputLabel} conversion profile id`}
            placeholder="Profile id"
            list={datalistId}
            value={profile?.id ?? ""}
            disabled={disabled}
            spellCheck={false}
            autoCapitalize="off"
            autoCorrect="off"
            onChange={event => setProfileId(event.target.value)}
          />
          <input
            type="text"
            aria-label={`${inputLabel} conversion profile version`}
            placeholder="Version"
            value={profile?.version ?? ""}
            disabled={disabled}
            spellCheck={false}
            onChange={event => setProfile({ id: profile?.id ?? "", version: event.target.value })}
          />
          <datalist id={datalistId}>
            {profiles.map(candidate => (
              <option key={`${candidate.id}@${candidate.version}`} value={candidate.id}>
                {`${candidate.id}@${candidate.version}`}
              </option>
            ))}
          </datalist>
        </div>
      ) : null}
    </div>
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
                aria-label={unavailableReason ? `${optionLabel} — ${unavailableReason}` : optionLabel}
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
  // Preserve the catalog delivery order (author-intended: the primary field stays first) and only let an
  // explicit `order` override it. Ties keep the original index, never alphabetical — sorting by name is
  // what pushed the primary input (e.g. Url) to the bottom.
  const orderedInputs = inputs
    .map((input, index) => ({ input, index }))
    .sort((left, right) =>
      (left.input.order ?? left.index) - (right.input.order ?? right.index) ||
      left.index - right.index)
    .map(entry => entry.input);

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

  const normalizedType = input.typeName.split(",", 1)[0]?.trim().toLowerCase();
  return inlineTextTypeNames.has(normalizedType) || input.uiHint?.toLowerCase() === "singleline";
}

function isExpandableTextInput(input: StudioActivityInputDescriptor, editorId: string | undefined) {
  const uiHint = input.uiHint?.toLowerCase();
  if (uiHint === "checkbox" || uiHint === "dropdown") return false;
  if (editorId && !inlineSyntaxEditorIds.has(editorId) && uiHint !== "multiline") return false;

  const normalizedType = input.typeName.split(",", 1)[0]?.trim().toLowerCase();
  return inlineTextTypeNames.has(normalizedType) ||
    uiHint === "singleline" ||
    uiHint === "multiline";
}
