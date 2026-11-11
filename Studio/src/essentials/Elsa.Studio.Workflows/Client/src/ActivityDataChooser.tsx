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
import type { ActivityDataBinding, ActivityDataContextResult, ActivityDataSourceFacts, ActivityDataTarget } from "./api/activityDataSelection";
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
import { ActivityOutputIcon } from "./activityOutputIcon";
import { getInputPropertyName } from "./activityProperties";
import { provideActivityResultReferenceEditor } from "./activityResultReferenceContribution";
import "./activityDataChooser.css";

/**
 * Choose data for whole activity outputs (spec 1215 T020): one source browser, used by the property row's Choose data
 * action, its syntax-picker entry and a stored reference's Replace, and the readable reference itself. The browser
 * holds only transient request and focus state. Choosing commits through the caller's own edit path, once.
 */

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
  const hostRef = useRef(host);
  hostRef.current = host;
  const documentRevision = host?.documentRevision;
  const requestSources = host?.requestSources;
  const baseRevision = host?.baseRevision ?? "";
  const saving = host?.saving === true;

  useEffect(() => {
    if (!open || !requestSources) return;
    const controller = new AbortController();
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
  const staleForOlderBase = stale && view.requestedBase !== baseRevision;
  useEffect(() => {
    if (open && staleForOlderBase && !saving) retry();
  }, [open, retry, saving, staleForOlderBase]);

  return { view, retry, awaitingSave: stale && (saving || staleForOlderBase) };
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
  const { view, retry, awaitingSave } = useActivityDataSources(host, target, open);
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
  const choose = (source: ActivityDataSourceFacts, label: string) => {
    setLostChoice(null);
    if (view.status === "answered" && view.refreshing) setWaitingFor({ id: source.id, label });
    else if (source.binding) commit(source.binding);
  };
  useEffect(() => {
    if (!waitingFor || view.status === "loading" || view.status === "answered" && view.refreshing) return;
    setWaitingFor(null);
    const fresh = view.status === "answered" && listsSources(view.result)
      ? view.result.sources.find(source => source.id === waitingFor.id)
      : undefined;
    if (fresh?.binding) commit(fresh.binding);
    else setLostChoice(`${waitingFor.label} can no longer be used for this input after your latest changes. Choose another output.`);
  }, [commit, view, waitingFor]);

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
        ?? panel.querySelector<HTMLElement>("button")
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
    if (event.key === "Tab") {
      // As in the syntax picker, Tab closes the popover and moves on; it continues from the opener in the row's own
      // order, because the popover itself lives at the end of the document.
      event.preventDefault();
      event.stopPropagation();
      onClose(event.shiftKey ? "previous" : "next");
      return;
    }
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
          <small>Outputs of activities that run before {consumerLabel}.</small>
        </div>
        <ActivityDataBrowserContent
          view={view}
          awaitingSave={awaitingSave}
          groups={groups}
          current={current}
          inputLabel={inputLabel}
          consumerLabel={consumerLabel}
          waitingFor={waitingFor?.id ?? null}
          onRetry={retry}
          onChoose={choose}
        />
        {lostChoice ? <p className="wf-data-chooser-note" role="status">{lostChoice}</p> : null}
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
        : refusal.action === "reload" ? { label: "Reload workflow", run: () => window.location.reload() } : undefined} />;
  }
  const constraint = result.target?.constraint;
  if (constraint && !constraint.allowedExpressionTypes.some(type => type.toLowerCase() === activityResultSyntax.toLowerCase())) {
    return <ChooserNotice tone="info" message={constraint.message || `${inputLabel} doesn't accept activity outputs.`} />;
  }
  const bounded = result.hasMoreSources;
  const sourceBound = (maximumSourcePages * sourcePageSize).toLocaleString("en-US");
  if (groups.length === 0) {
    return <ChooserNotice tone="info" message={bounded
      ? `Studio looked at the first ${sourceBound} data sources and found no activity outputs among them. Use an expression for this input.`
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
      {bounded ? <p className="wf-data-chooser-note">Showing outputs from the first {sourceBound} data sources. Use an expression for an activity not listed.</p> : null}
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
            onClick={() => { if (binding) onChoose(option.source, option.label); }}
            onKeyDown={event => {
              if (event.key !== "Enter" && event.key !== " ") return;
              event.preventDefault();
              if (binding) onChoose(option.source, option.label);
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
        ? { message: "This draft is read-only, so its outputs can't be browsed." }
        : { message: "You don't have permission to browse this workflow's outputs." };
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
export function ActivityResultReferenceEditor({ descriptor, value, disabled, initialFocus, context, onChange }: StudioExpressionEditorProps) {
  const host = useActivityDataSelectionHost();
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
            aria-label={`Go to ${producerLabel}`}
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
      {browsing && host ? (
        <ActivityDataBrowser
          key={`${target.nodeId}\u001f${target.inputKey}`}
          host={host}
          target={target}
          inputLabel={inputLabel}
          anchorRef={replaceRef}
          open={browsing}
          current={reference}
          onSelect={binding => {
            // Only the role this editor owns is stored here; the server returns no other kind for an activity output.
            if (binding.expressionType === activityResultSyntax) onChange(binding.value);
          }}
          onClose={closeBrowser}
        />
      ) : null}
    </div>
  );
}

function readNodeId(activity: unknown) {
  const nodeId = activity && typeof activity === "object" ? (activity as Record<string, unknown>).nodeId : null;
  return typeof nodeId === "string" ? nodeId : "";
}

provideActivityResultReferenceEditor(ActivityResultReferenceEditor);
