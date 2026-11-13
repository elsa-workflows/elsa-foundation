import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type ReactNode,
  type RefObject
} from "react";
import { AlertTriangle, LocateFixed } from "lucide-react";
import { AnchoredPopover, SkeletonRows } from "@elsa-workflows/studio-ui";
import { authSessionEndedEvent, type StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { matchesMemberOffer, type ActivityDataBinding, ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataTarget } from "./api/activityDataSelection";
import {
  activityResultSyntax,
  collectSourcePages,
  describeOutput,
  describeReferenceFinding,
  groupActivityOutputSources,
  maximumSourcePages,
  readActivityResultReference,
  sourcePageSize,
  type ActivityDataProducerGroup,
  type ActivityDataSelectionHost,
  type ActivityResultReference
} from "./activityDataSelection";
import { activityDataBreadcrumbs, describeActivityDataMember, readActivityDataMemberReference } from "./activityDataPickerModel";
import { useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";
import { ActivityDataMemberBrowser } from "./ActivityDataMemberBrowser";
import { ActivityOutputIcon } from "./activityOutputIcon";
import { getInputPropertyName } from "./activityProperties";
import { provideActivityResultReferenceEditor } from "./activityResultReferenceContribution";
import "./activityDataChooser.css";

/**
 * Choose data for whole activity outputs (spec 1215 T020): one source browser, used by the property row's Choose data
 * action, its syntax-picker entry and a stored reference's Replace, and the readable reference itself. The browser
 * holds only transient request and focus state. Choosing commits through the caller's own edit path, once.
 */

const ActivityDataBindingChangeContext = createContext<((binding: ActivityDataBinding) => void) | null>(null);
export function ActivityDataBindingChangeProvider({ onSelect, children }: { onSelect(binding: ActivityDataBinding): void; children: ReactNode }) {
  return <ActivityDataBindingChangeContext.Provider value={onSelect}>{children}</ActivityDataBindingChangeContext.Provider>;
}

const ActivityDataSelectionContext = createContext<ActivityDataSelectionHost | null>(null);
// Rows only need to know whether Choose data exists; a boolean keeps them from re-rendering on every document edit.
const ActivityDataSelectionAvailableContext = createContext(false);

export function ActivityDataSelectionProvider({ host, children }: { host: ActivityDataSelectionHost | null; children: ReactNode }) {
  return (
    <ActivityDataSelectionAvailableContext.Provider value={host != null}>
      <ActivityDataSelectionContext.Provider value={host}>{children}</ActivityDataSelectionContext.Provider>
    </ActivityDataSelectionAvailableContext.Provider>
  );
}

export function useActivityDataSelectionHost() {
  return useContext(ActivityDataSelectionContext);
}

export function useActivityDataSelectionAvailable() {
  return useContext(ActivityDataSelectionAvailableContext);
}

type SourcesView =
  | { status: "loading" }
  | { status: "failed" }
  | { status: "answered"; result: ActivityDataContextResult; requestedBase: string; refreshing?: boolean };

/**
 * Loads the sources for one input while the browser is open, page by page. A request answers one document revision
 * and target: an edit, a target switch or closing aborts it, and a late answer is dropped. While a newer revision is
 * asked about, the previous list stays in place, marked as updating. A stale answer while the editor's own save is in
 * flight, or caused by a save it has since acknowledged, is waited out and asked again; nothing here saves.
 */
export function useActivityDataSources(host: ActivityDataSelectionHost | null, target: ActivityDataTarget, open: boolean) {
  const [view, setView] = useState<SourcesView>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const activeRequest = useRef<AbortController | null>(null);
  const hostRef = useRef(host);
  hostRef.current = host;
  const documentRevision = host?.documentRevision;
  const requestSources = host?.requestSources;
  const baseRevision = host?.baseRevision ?? "";
  const saving = host?.saving === true;

  useEffect(() => {
    if (!open || !requestSources) return;
    const controller = new AbortController();
    activeRequest.current = controller;
    const requestedBase = hostRef.current?.baseRevision ?? "";
    const request = { nodeId: target.nodeId, inputKey: target.inputKey };
    setView(previous => previous.status === "answered" && listsSources(previous.result)
      ? { ...previous, refreshing: true }
      : { status: "loading" });
    collectSourcePages(skip => requestSources(request, controller.signal, skip), controller.signal).then(
      result => { if (!controller.signal.aborted) setView({ status: "answered", result, requestedBase }); },
      () => { if (!controller.signal.aborted) setView({ status: "failed" }); }
    );
    return () => controller.abort();
  }, [attempt, documentRevision, open, requestSources, target.inputKey, target.nodeId]);

  const retry = useCallback(() => setAttempt(current => current + 1), []);
  const stale = view.status === "answered" && view.result.state === "stale";
  const olderBase = view.status === "answered" && view.requestedBase !== baseRevision;
  const staleForOlderBase = stale && olderBase;
  useEffect(() => {
    if (open && olderBase && !saving) retry();
  }, [open, retry, saving, olderBase]);

  const refuse = useCallback((result: ActivityDataContextResult) => {
    activeRequest.current?.abort();
    setView({ status: "answered", result: { ...result, sources: [], target: null, memberReadsEnabled: false }, requestedBase: hostRef.current?.baseRevision ?? "" });
  }, []);
  return { view, retry, refuse, awaitingSave: stale && (saving || staleForOlderBase) };
}

interface ActivityDataBrowserProps {
  host: ActivityDataSelectionHost;
  target: ActivityDataTarget;
  inputLabel: string;
  anchorRef: RefObject<HTMLElement | null>;
  open: boolean;
  current: ActivityResultReference | null;
  onSelect(binding: ActivityDataBinding): void;
  /** `focus` is where focus should go: back to the opener, onwards in tab order, or nowhere (pointer dismissal). */
  onClose(focus: "opener" | "next" | "previous" | "none"): void;
}

/**
 * The shared source browser: a non-modal popover anchored to the control that opened it. Callers key it by target, so
 * a pending choice can never land in another activity's input.
 */
export function ActivityDataBrowser({ host, target, inputLabel, anchorRef, open, current, onSelect, onClose }: ActivityDataBrowserProps) {
  const { view, retry, refuse, awaitingSave } = useActivityDataSources(host, target, open);
  const memberEnabled = view.status === "answered" && listsSources(view.result) && view.result.memberReadsEnabled === true;
  const panelRef = useRef<HTMLDivElement>(null);
  const headingId = useId();
  const consumerLabel = host.describeActivity(target.nodeId)?.label ?? "this activity";
  const groups = view.status === "answered" && listsSources(view.result)
    ? groupActivityOutputSources(view.result.sources, nodeId => host.describeActivity(nodeId))
    : [];
  // Facts for an older document are never stored. A choice made while the current document is being asked about
  // waits for that answer and then stores the source's current binding, or says why it can't.
  const [waitingFor, setWaitingFor] = useState<{ id: string; label: string } | null>(null);
  const [lostChoice, setLostChoice] = useState<string | null>(null);
  const commit = useCallback((binding: ActivityDataBinding) => {
    onSelect(binding);
    onClose("opener");
  }, [onClose, onSelect]);
  // A listing is current only once it answers the document on screen: an edit committed but not yet asked about
  // (the refresh effect has not run) makes the shown list an older document's, like an explicit refresh does.
  const currentListing = view.status === "answered" && listsSources(view.result) && !view.refreshing &&
    view.result.documentRevision === host.documentRevision ? view.result : null;
  const choose = (source: ActivityDataSourceFacts, label: string) => {
    setLostChoice(null);
    if (!currentListing) setWaitingFor({ id: source.id, label });
    else if (source.binding) commit(source.binding);
  };
  const seenListing = useRef<ActivityDataContextResult | null>(null);
  useEffect(() => {
    if (currentListing) {
      const isNew = currentListing !== seenListing.current;
      seenListing.current = currentListing;
      if (waitingFor) {
        setWaitingFor(null);
        const fresh = currentListing.sources.find(source => source.id === waitingFor.id);
        if (fresh?.binding) commit(fresh.binding);
        else setLostChoice(`${waitingFor.label} can no longer be used for this input after your latest changes. Choose another output.`);
      } else if (isNew) {
        setLostChoice(null);
      }
      return;
    }
    // A refresh that ends without a listing (failed, refused, or waiting out a save) drops the pending choice
    // silently: there is no newer list to say anything about.
    if (waitingFor && (view.status === "failed" || view.status === "answered" && !listsSources(view.result))) setWaitingFor(null);
  }, [commit, currentListing, view, waitingFor]);

  useEffect(() => {
    if (!open) return;
    // Revoked sessions take their metadata with them.
    const close = () => onClose("none");
    window.addEventListener(authSessionEndedEvent, close);
    return () => window.removeEventListener(authSessionEndedEvent, close);
  }, [onClose, open]);

  // Move focus into the browser once it is placed, and again when its content settles, unless the author already
  // moved focus to another of its controls.
  const contentKey = view.status === "answered" ? `${view.result.state}:${groups.length}:${awaitingSave}` : view.status;
  useLayoutEffect(() => {
    if (!open) return;
    const frame = requestAnimationFrame(() => {
      const panel = panelRef.current;
      if (!panel) return;
      const focused = panel.ownerDocument.activeElement;
      if (focused && focused !== panel && panel.contains(focused)) return;
      const options = [...panel.querySelectorAll<HTMLElement>("[role='option']")];
      const preferred = options.find(option => option.getAttribute("aria-selected") === "true")
        ?? options.find(option => option.getAttribute("aria-disabled") !== "true")
        ?? options[0]
        ?? panel.querySelector<HTMLElement>("input:not(:disabled),button:not(:disabled)")
        ?? panel;
      preferred.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [contentKey, open]);

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose("opener");
      return;
    }
    if (event.key === "Tab" && memberEnabled) {
      const controls = [...(panelRef.current?.querySelectorAll<HTMLElement>("button:not(:disabled),input:not(:disabled)") ?? [])].filter(control => control.tabIndex >= 0);
      const target = event.target as HTMLElement;
      const next = event.shiftKey ? controls.filter(control => control.compareDocumentPosition(target) & Node.DOCUMENT_POSITION_FOLLOWING).at(-1)
        : controls.find(control => control.compareDocumentPosition(target) & Node.DOCUMENT_POSITION_PRECEDING);
      event.preventDefault(); event.stopPropagation();
      if (next) next.focus(); else onClose(event.shiftKey ? "previous" : "next");
      return;
    }
    if (event.key === "Tab") {
      // As in the syntax picker, Tab closes the popover and moves on; it continues from the opener in the row's own
      // order, because the popover itself lives at the end of the document.
      event.preventDefault();
      event.stopPropagation();
      onClose(event.shiftKey ? "previous" : "next");
      return;
    }
    if (memberEnabled && event.target instanceof HTMLInputElement) return;
    const options = [...(panelRef.current?.querySelectorAll<HTMLElement>("[role='option']") ?? [])];
    if (options.length === 0) return;
    const index = options.findIndex(option => option === event.target);
    const move = (next: number) => {
      event.preventDefault();
      options[(next + options.length) % options.length]?.focus();
    };
    if (event.key === "ArrowDown") move(index + 1);
    else if (event.key === "ArrowUp") move(index < 0 ? options.length - 1 : index - 1);
    else if (event.key === "Home") move(0);
    else if (event.key === "End") move(options.length - 1);
  };

  return (
    <AnchoredPopover
      anchorRef={anchorRef}
      portalContainer={anchorRef.current?.closest(".wf-property-editor-dialog") ?? undefined}
      open={open}
      className="wf-data-chooser"
      minWidth={288}
      maxHeight={360}
      onDismiss={reason => onClose(reason === "anchor-hidden" ? "none" : "opener")}
    >
      <div
        ref={panelRef}
        className="wf-data-chooser-panel"
        role="dialog"
        aria-labelledby={headingId}
        tabIndex={-1}
        onKeyDown={handleKeyDown}
      >
        <div className="wf-data-chooser-heading">
          <span id={headingId}>Choose data for {inputLabel}</span>
          <small>{memberEnabled ? "Earlier outputs, workflow inputs and variables." : <>Outputs of activities that run before {consumerLabel}.</>}</small>
        </div>
        {memberEnabled && view.status === "answered" ? <ActivityDataMemberBrowser
          host={host} target={target} result={view.result} current={!!currentListing && view.result.baseRevision === host.baseRevision}
          refreshing={view.refreshing === true} onSelect={commit} onRefusal={refuse} onRefresh={retry} /> : <ActivityDataBrowserContent
          view={view}
          awaitingSave={awaitingSave}
          groups={groups}
          current={current}
          inputLabel={inputLabel}
          consumerLabel={consumerLabel}
          waitingFor={waitingFor?.id ?? null}
          onRetry={retry}
          onChoose={choose}
        />}
        {lostChoice ? <p className="wf-data-chooser-note wf-data-chooser-lost" role="status">{lostChoice}</p> : null}
      </div>
    </AnchoredPopover>
  );
}

function ActivityDataBrowserContent({ view, awaitingSave, groups, current, inputLabel, consumerLabel, waitingFor, onRetry, onChoose }: {
  view: SourcesView;
  awaitingSave: boolean;
  groups: ActivityDataProducerGroup[];
  current: ActivityResultReference | null;
  inputLabel: string;
  consumerLabel: string;
  waitingFor: string | null;
  onRetry(): void;
  onChoose(source: ActivityDataSourceFacts, label: string): void;
}) {
  if (view.status === "loading") return <SkeletonRows rows={3} label="Loading outputs" />;
  if (view.status === "failed") {
    return <ChooserNotice tone="danger" message="Studio couldn't load the outputs. Try again." action={{ label: "Retry", run: onRetry }} />;
  }
  if (awaitingSave) return <ChooserNotice tone="info" message="Updating for your latest changes…" />;
  const { result } = view;
  if (!listsSources(result)) {
    const refusal = describeRefusal(result);
    return <ChooserNotice tone={refusal.action ? "danger" : "info"} message={refusal.message}
      action={refusal.action === "retry" ? { label: "Retry", run: onRetry }
        : refusal.action === "reload" ? { label: "Reload this workflow", run: () => window.location.reload() } : undefined} />;
  }
  const constraint = result.target?.constraint;
  if (constraint && !constraint.allowedExpressionTypes.some(type => type.toLowerCase() === activityResultSyntax.toLowerCase())) {
    return <ChooserNotice tone="info" message={constraint.message || `${inputLabel} doesn't accept activity outputs.`} />;
  }
  const bounded = result.hasMoreSources;
  const sourceBound = (maximumSourcePages * sourcePageSize).toLocaleString("en-US");
  if (groups.length === 0) {
    return <ChooserNotice tone="info" message={bounded
      ? `Studio checked the first ${sourceBound} inputs, variables and activity outputs available here and found no activity outputs among them. Use an expression for this input.`
      : `No earlier activity has an output for this input. Add one before ${consumerLabel}, or use an expression.`} />;
  }
  const refreshing = view.refreshing === true;
  return (
    <>
      <div className="wf-data-chooser-list" role="listbox" aria-label="Activity outputs" aria-busy={refreshing || undefined}>
        {groups.map(group => (
          <ProducerGroup key={group.producerNodeId} group={group} current={current} waitingFor={waitingFor} onChoose={onChoose} />
        ))}
      </div>
      {refreshing ? <p className="wf-data-chooser-note" role="status">Updating for your latest changes…</p> : null}
      {bounded ? <p className="wf-data-chooser-note">Showing the activity outputs among the first {sourceBound} inputs, variables and activity outputs available here. Use an expression for an activity not listed.</p> : null}
    </>
  );
}

function listsSources(result: ActivityDataContextResult) {
  return result.state === "supported" || result.state === "empty";
}

function ProducerGroup({ group, current, waitingFor, onChoose }: {
  group: ActivityDataProducerGroup;
  current: ActivityResultReference | null;
  waitingFor: string | null;
  onChoose(source: ActivityDataSourceFacts, label: string): void;
}) {
  const labelId = useId();
  const producerName = group.disambiguation ? `${group.label} · ${group.disambiguation}` : group.label;
  // When every output is unavailable for one reason, say it once for the producer; each option points at it. The
  // heading and reason are references for the group's name and descriptions, not listbox content of their own.
  const sharedReasonId = `${labelId}-reason`;
  const shared = group.sharedUnavailableReason;
  return (
    <div role="group" aria-labelledby={labelId} className="wf-data-chooser-group">
      <div id={labelId} className="wf-data-chooser-group-label" aria-hidden="true">
        {group.label}
        {group.disambiguation ? <span className="wf-data-chooser-group-id"> · {group.disambiguation}</span> : null}
      </div>
      {shared ? <div id={sharedReasonId} className="wf-data-chooser-group-reason" aria-hidden="true">{shared}</div> : null}
      {group.options.map(option => {
        const selected = current?.producerNodeId === option.producerNodeId && current.projectionKey === option.projectionKey;
        const binding = option.source.binding;
        const ownReason = shared ? null : option.unavailableReason;
        const reasonId = shared ? sharedReasonId : `${labelId}-${option.source.id}`;
        return (
          <button
            key={option.source.id}
            type="button"
            role="option"
            tabIndex={-1}
            aria-selected={selected}
            aria-disabled={binding ? undefined : true}
            aria-busy={waitingFor === option.source.id || undefined}
            aria-describedby={option.unavailableReason ? reasonId : undefined}
            className={["wf-data-chooser-option", selected ? "selected" : ""].filter(Boolean).join(" ")}
            onClick={() => { if (binding) onChoose(option.source, `${producerName} › ${option.label}`); }}
            onKeyDown={event => {
              if (event.key !== "Enter" && event.key !== " ") return;
              event.preventDefault();
              if (binding) onChoose(option.source, `${producerName} › ${option.label}`);
            }}
          >
            <span className="wf-data-chooser-option-label">{option.label}</span>
            {option.typeLabel ? <><span className="wf-visually-hidden">, </span><span className="wf-data-chooser-option-type">{option.typeLabel}</span></> : null}
            {binding ? null : <><span className="wf-visually-hidden">, </span><span className="wf-data-chooser-option-tag">Not available</span></>}
            {ownReason ? <small id={reasonId} aria-hidden="true">{ownReason}</small> : null}
          </button>
        );
      })}
    </div>
  );
}

function ChooserNotice({ tone, message, action }: { tone: "info" | "danger"; message: string; action?: { label: string; run(): void } }) {
  return (
    <div className="wf-data-chooser-notice" data-tone={tone} role={tone === "danger" ? "alert" : "status"}>
      <span>{message}</span>
      {action ? <button type="button" onClick={action.run}>{action.label}</button> : null}
    </div>
  );
}

/** Refusals keep the safe code's meaning without echoing it, and each offers the action its message names. */
function describeRefusal(result: ActivityDataContextResult): { message: string; action?: "retry" | "reload" } {
  switch (result.state) {
    case "denied":
      return result.code === "activity-data.read-only"
        ? { message: "This draft is read-only, so the activity outputs in this workflow can't be browsed." }
        : { message: "You don't have permission to browse activity outputs in this workflow." };
    case "stale":
      return result.code === "activity-data.base-changed" || result.code === "activity-data.base-stale"
        ? { message: "This workflow was changed somewhere else. Reload it to choose an output.", action: "reload" }
        : { message: "The outputs changed while Studio was loading them. Try again.", action: "retry" };
    case "unavailable":
      return result.code === "activity-data.metadata-policy-unavailable"
        ? { message: "This server restricts browsing outputs, so Choose data isn't available. Use an expression instead." }
        : { message: "Outputs aren't available right now. Try again in a moment.", action: "retry" };
    case "incompatible":
      return { message: "This version of Studio can't read the outputs this server reported. Use an expression instead, or update Studio." };
    default:
      // The server reports problems anywhere in the document, and an input it cannot resolve, in the same way.
      return { message: "Studio can't analyze this workflow until its problems are fixed. Fix the issues in the validation list, then try again.", action: "retry" };
  }
}

/**
 * The property row's Choose data action. Renders nothing where the document owner offers no source analysis. The row
 * owns whether it is open, because the syntax picker's "Activity output…" entry opens the same browser.
 */
export function ChooseDataButton({ target, inputLabel, open, disabled, onToggle, onClose, onSelect }: {
  target: ActivityDataTarget;
  inputLabel: string;
  open: boolean;
  disabled?: boolean;
  onToggle(): void;
  onClose(focus: "opener" | "next" | "previous" | "none"): void;
  onSelect(binding: ActivityDataBinding): void;
}) {
  const host = useActivityDataSelectionHost();
  const triggerRef = useRef<HTMLButtonElement>(null);
  if (!host) return null;
  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={open ? "wf-choose-data-trigger open" : "wf-choose-data-trigger"}
        aria-label={`Choose data for ${inputLabel}`}
        title="Choose data from an earlier activity"
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={disabled}
        onClick={onToggle}
      >
        <ActivityOutputIcon size={14} aria-hidden="true" />
      </button>
      {open ? (
        <ActivityDataBrowser
          key={`${target.nodeId}\u001f${target.inputKey}`}
          host={host}
          target={target}
          inputLabel={inputLabel}
          anchorRef={triggerRef}
          open={open}
          current={null}
          onSelect={onSelect}
          onClose={onClose}
        />
      ) : null}
    </>
  );
}

/** Moves focus from an element to the next or previous one in tab order. */
export function focusBeside(element: HTMLElement | null, direction: "next" | "previous") {
  if (!element) return;
  const tabbable = [...element.ownerDocument.querySelectorAll<HTMLElement>(
    "a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex='-1'])"
  )].filter(candidate => candidate.tabIndex >= 0 && candidate.getClientRects().length > 0 && !candidate.closest("[inert]"));
  const index = tabbable.indexOf(element);
  const next = index < 0 ? null : tabbable[direction === "next" ? index + 1 : index - 1];
  (next ?? element).focus();
}

/**
 * The readable reference for a stored `ActivityResult` input: which activity and which output, with go to, replace
 * and clear. The stored value is shown, never rewritten: replacing stores the server's new binding, clearing asks the
 * row to unbind the input (a null value), and every other field of the stored binding, such as a legacy producer scope,
 * stays as authored until then.
 */
export function ActivityResultReferenceEditor(props: StudioExpressionEditorProps) {
  return props.syntax === activityResultSyntax ? <WholeActivityResultReferenceEditor {...props} /> : <ActivityDataMemberReferenceEditor {...props} />;
}

function WholeActivityResultReferenceEditor({ descriptor, value, disabled, initialFocus, context, onChange }: StudioExpressionEditorProps) {
  const host = useActivityDataSelectionHost();
  const changeBinding = useContext(ActivityDataBindingChangeContext);
  const reference = readActivityResultReference(value);
  const replaceRef = useRef<HTMLButtonElement>(null);
  // Open for one target only: when the row shows another activity, an open browser and any pending choice go away.
  const [browsingFor, setBrowsingFor] = useState<string | null>(null);
  const inputLabel = descriptor.displayName || descriptor.name;
  const nodeId = readNodeId(context.activity);
  const target = { nodeId, inputKey: getInputPropertyName(descriptor) };
  const targetKey = `${target.nodeId}\u001f${target.inputKey}`;
  const browsing = browsingFor === targetKey;
  const producer = reference && host ? host.describeActivity(reference.producerNodeId) : null;
  const missing = Boolean(reference && host && !producer);
  const problem = !reference
    ? "No output is chosen yet. Use Choose data to pick one."
    : missing
      ? "The source activity is no longer in this workflow. Replace it or clear it."
      : host?.findingsFor(target).map(describeReferenceFinding).find((text): text is string => text != null) ?? null;
  const producerLabel = reference ? producer?.label ?? reference.producerNodeId : "";
  const disambiguation = reference && producer?.sharesLabel ? reference.producerNodeId : null;
  const outputLabel = reference ? describeOutput(reference.projectionKey, producer) : "";
  const canBrowse = Boolean(host && nodeId && !disabled);

  useEffect(() => {
    if (initialFocus) replaceRef.current?.focus();
  }, [initialFocus]);

  const closeBrowser = useCallback((focus: "opener" | "next" | "previous" | "none") => {
    setBrowsingFor(null);
    if (focus === "opener") replaceRef.current?.focus();
    else if (focus !== "none") focusBeside(replaceRef.current, focus);
  }, []);

  return (
    <div className="wf-data-reference" data-problem={problem ? "true" : undefined}>
      {reference ? (
        <p className="wf-data-reference-source">
          <ActivityOutputIcon size={14} aria-hidden="true" />
          <span>
            <span className="wf-visually-hidden">Source: </span>
            <strong>{producerLabel}</strong>
            {disambiguation ? <span className="wf-data-reference-id"> · {disambiguation}</span> : null}
            <span aria-hidden="true"> › </span>
            <span className="wf-visually-hidden">, </span>
            {outputLabel}
          </span>
        </p>
      ) : null}
      {problem ? (
        <p className="wf-data-reference-problem">
          <AlertTriangle size={13} aria-hidden="true" />
          <span>{problem}</span>
        </p>
      ) : null}
      <div className="wf-data-reference-actions">
        {reference && producer && host?.revealActivity ? (
          <button
            type="button"
            aria-label={`Go to ${producerLabel}${disambiguation ? ` · ${disambiguation}` : ""}`}
            onClick={() => host.revealActivity?.(reference.producerNodeId)}
          >
            <LocateFixed size={13} aria-hidden="true" /> Go to
          </button>
        ) : null}
        {canBrowse ? (
          <button
            ref={replaceRef}
            type="button"
            className="wf-data-reference-replace"
            aria-label={reference ? `Replace data for ${inputLabel}` : `Choose data for ${inputLabel}`}
            aria-haspopup="dialog"
            aria-expanded={browsing}
            onClick={() => (browsing ? closeBrowser("none") : setBrowsingFor(targetKey))}
          >
            {reference ? "Replace" : "Choose data"}
          </button>
        ) : null}
        {!disabled ? (
          <button type="button" aria-label={`Clear data for ${inputLabel}`} onClick={() => onChange(null)}>Clear</button>
        ) : null}
      </div>
      {browsing && host && canBrowse ? (
        <ActivityDataBrowser
          key={`${target.nodeId}\u001f${target.inputKey}`}
          host={host}
          target={target}
          inputLabel={inputLabel}
          anchorRef={replaceRef}
          open={browsing}
          current={reference}
          onSelect={binding => {
            // The ordinary row owns cross-role edits; other surfaces keep this editor's whole-output contract.
            if (changeBinding) changeBinding(binding);
            else if (binding.expressionType === activityResultSyntax) onChange(binding.value);
          }}
          onClose={closeBrowser}
        />
      ) : null}
    </div>
  );
}

function ActivityDataMemberReferenceEditor({ descriptor, syntax, value, disabled, initialFocus, context, onChange }: StudioExpressionEditorProps) {
  const host = useActivityDataSelectionHost();
  const changeBinding = useContext(ActivityDataBindingChangeContext);
  const authoring = useWorkflowReferenceAuthoring();
  const binding = { expressionType: syntax, value };
  const member = readActivityDataMemberReference(binding);
  const raw = value && typeof value === "object" ? value as Record<string, unknown> : null;
  const requestKey = syntax === "WorkflowRequest" && typeof raw?.memberKey === "string" ? raw.memberKey : null;
  const replaceRef = useRef<HTMLButtonElement>(null);
  const target = { nodeId: readNodeId(context.activity), inputKey: getInputPropertyName(descriptor) };
  const targetKey = `${target.nodeId}\u001f${target.inputKey}`;
  const [browsingFor, setBrowsingFor] = useState<string | null>(null);
  const [details, setDetails] = useState<ActivityDataContextResult | null>(null);
  const queryKey = JSON.stringify(member ?? (requestKey ? { memberKey: requestKey, path: raw?.path ?? null } : null));
  useEffect(() => {
    setDetails(null);
    if (!host || disabled || (!member && !requestKey) || member?.kind === "InputMember") return;
    const controller = new AbortController();
    const source = member ? memberRootProposal(member) : { expressionType: "WorkflowRequest", value };
    host.requestSources(target, controller.signal, 0, { source, memberPath: member?.memberPath ?? [], includeMemberShapes: true }).then(result => {
      if (!controller.signal.aborted && result.documentRevision === host.documentRevision && (result.state === "supported" || result.state === "empty")) setDetails(result);
    }, () => undefined);
    const revoke = () => { controller.abort(); setDetails(null); };
    window.addEventListener(authSessionEndedEvent, revoke);
    return () => { controller.abort(); window.removeEventListener(authSessionEndedEvent, revoke); };
    // The bounded query identity, not the decoded object instance, owns this request.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host?.documentRevision, host?.baseRevision, host?.requestSources, queryKey, target.nodeId, target.inputKey, disabled]);
  const labels = { describeActivity: (nodeId: string) => host?.describeActivity(nodeId) ?? null,
    describeScope: (scope: string) => host?.describeActivity(scope)?.label ?? null };
  const source = details?.sources.find(source => {
    if (!source.binding) return false;
    if (member) return matchesMemberOffer(memberRootProposal(member), member.memberPath, source.binding);
    const returned = source.binding.value && typeof source.binding.value === "object" ? source.binding.value as Record<string, unknown> : null;
    return source.binding.expressionType === "WorkflowRequest" && returned?.memberKey === requestKey && (returned.path ?? null) === (raw?.path ?? null);
  });
  const description = member ? describeActivityDataMember(binding, details?.sources ?? [], labels, source?.shape) : null;
  const variable = member?.kind === "VariableMember" ? authoring?.visibleVariables.find(variable => variable.referenceKey === member.referenceKey && variable.scopeId === member.declaringScopeId) : null;
  const input = member?.kind === "InputMember" ? authoring?.workflowInputs.find(input => input.referenceKey === member.referenceKey) : null;
  const fallbackLabel = variable ? `${variable.name} (${labels.describeScope(variable.scopeId) ?? (variable.isWorkflowScope ? "Workflow" : variable.scopeId)})`
    : input ? input.displayName || input.name : null;
  const text = fallbackLabel && member ? [fallbackLabel, ...activityDataBreadcrumbs(member.memberPath).slice(1).map(crumb => crumb.label)].join(" › ")
    : description?.text ?? (requestKey ? source?.displayName ?? requestKey : null);
  const canBrowse = !!host && !!changeBinding && !!target.nodeId && !disabled;
  const close = useCallback((focus: "opener" | "next" | "previous" | "none") => {
    setBrowsingFor(null);
    if (focus === "opener") replaceRef.current?.focus(); else if (focus !== "none") focusBeside(replaceRef.current, focus);
  }, []);
  useEffect(() => { if (initialFocus) replaceRef.current?.focus(); }, [initialFocus]);
  const problem = host?.findingsFor(target).map(describeReferenceFinding).find(text => text != null);
  return <div className="wf-data-reference" data-problem={problem ? "true" : undefined}>
    {text ? <p className="wf-data-reference-source"><ActivityOutputIcon size={14} aria-hidden="true" /><span><span className="wf-visually-hidden">Source: </span>{text}</span></p>
      : <p className="wf-data-reference-problem">This reference is not supported here. Its value is preserved.</p>}
    {description?.typeLabel ? <p className="wf-data-chooser-note">{description.typeLabel}</p> : member && member.kind !== "ActivityResultMember" ? <p className="wf-data-chooser-note">The member type is checked when the workflow runs.</p> : null}
    {problem ? <p className="wf-data-reference-problem">{problem}</p> : null}
    {!host || !changeBinding ? <p className="wf-data-chooser-note">Source selection is unavailable in this editor. The reference is preserved.</p> : null}
    {canBrowse ? <div className="wf-data-reference-actions">
      {member?.producerNodeId && host.describeActivity(member.producerNodeId) && host.revealActivity ? <button type="button" onClick={() => host.revealActivity?.(member.producerNodeId!)}>Go to</button> : null}
      <button ref={replaceRef} type="button" aria-label={`Replace data for ${descriptor.displayName || descriptor.name}`} aria-haspopup="dialog"
        aria-expanded={browsingFor === targetKey} onClick={() => setBrowsingFor(targetKey)}>Replace</button>
      <button type="button" aria-label={`Clear data for ${descriptor.displayName || descriptor.name}`} onClick={() => onChange(null)}>Clear</button>
    </div> : null}
    {canBrowse && browsingFor === targetKey ? <ActivityDataBrowser key={targetKey} host={host} target={target}
      inputLabel={descriptor.displayName || descriptor.name} anchorRef={replaceRef} open current={null} onSelect={changeBinding} onClose={close} /> : null}
  </div>;
}

function memberRootProposal(member: NonNullable<ReturnType<typeof readActivityDataMemberReference>>): ActivityDataBinding {
  switch (member.kind) {
    case "ActivityResultMember": return { expressionType: "ActivityResult", value: { producerNodeId: member.producerNodeId, projectionKey: member.projectionKey, isOptional: member.isOptional === true } };
    case "VariableMember": return { expressionType: "Variable", value: { referenceKey: member.referenceKey, declaringScopeId: member.declaringScopeId } };
    case "InputMember": return { expressionType: "Input", value: { referenceKey: member.referenceKey } };
    case "WorkflowRequestMember": return { expressionType: "WorkflowRequest", value: { memberKey: member.memberKey, path: null } };
  }
}

function readNodeId(activity: unknown) {
  const nodeId = activity && typeof activity === "object" ? (activity as Record<string, unknown>).nodeId : null;
  return typeof nodeId === "string" ? nodeId : "";
}

provideActivityResultReferenceEditor(ActivityResultReferenceEditor);
