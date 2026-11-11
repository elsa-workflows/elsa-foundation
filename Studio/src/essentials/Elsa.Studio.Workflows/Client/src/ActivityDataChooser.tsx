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
import { AlertTriangle, Database, LocateFixed } from "lucide-react";
import { AnchoredPopover, SkeletonRows } from "@elsa-workflows/studio-ui";
import { authSessionEndedEvent, type StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import type { ActivityDataBinding, ActivityDataContextResult, ActivityDataTarget } from "./api/activityDataSelection";
import {
  activityResultSyntax,
  describeOutput,
  describeReferenceFinding,
  groupActivityOutputSources,
  readActivityResultReference,
  type ActivityDataProducerGroup,
  type ActivityDataSelectionHost,
  type ActivityResultReference
} from "./activityDataSelection";
import { getInputPropertyName } from "./activityProperties";
import { provideActivityResultReferenceEditor } from "./activityResultReferenceContribution";
import "./activityDataChooser.css";

/**
 * Choose data for whole activity outputs (spec 1215 T020): one source browser, used by the property row's Choose data
 * action and by a stored reference's Replace, and the readable reference itself. The browser holds only transient
 * request and focus state. Choosing commits through the caller's own edit path, once.
 */

const ActivityDataSelectionContext = createContext<ActivityDataSelectionHost | null>(null);

export function ActivityDataSelectionProvider({ host, children }: { host: ActivityDataSelectionHost | null; children: ReactNode }) {
  return <ActivityDataSelectionContext.Provider value={host}>{children}</ActivityDataSelectionContext.Provider>;
}

export function useActivityDataSelectionHost() {
  return useContext(ActivityDataSelectionContext);
}

type SourcesView =
  | { status: "loading" }
  | { status: "failed" }
  | { status: "answered"; result: ActivityDataContextResult; requestedBase: string };

/**
 * Loads the sources for one input while the browser is open. A request answers one document revision and target: an
 * edit, a target switch or closing aborts it, and a late answer is dropped. A stale answer caused by a save the editor
 * has since acknowledged is asked again; nothing here saves.
 */
export function useActivityDataSources(host: ActivityDataSelectionHost | null, target: ActivityDataTarget, open: boolean) {
  const [view, setView] = useState<SourcesView>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const hostRef = useRef(host);
  hostRef.current = host;
  const documentRevision = host?.documentRevision;
  const requestSources = host?.requestSources;
  const baseRevision = host?.baseRevision ?? "";

  useEffect(() => {
    if (!open || !requestSources) return;
    const controller = new AbortController();
    const requestedBase = hostRef.current?.baseRevision ?? "";
    setView({ status: "loading" });
    requestSources({ nodeId: target.nodeId, inputKey: target.inputKey }, controller.signal).then(
      result => { if (!controller.signal.aborted) setView({ status: "answered", result, requestedBase }); },
      () => { if (!controller.signal.aborted) setView({ status: "failed" }); }
    );
    return () => controller.abort();
  }, [attempt, documentRevision, open, requestSources, target.inputKey, target.nodeId]);

  const retry = useCallback(() => setAttempt(current => current + 1), []);
  const staleForOlderBase = view.status === "answered" && view.result.state === "stale" && view.requestedBase !== baseRevision;
  useEffect(() => {
    if (open && staleForOlderBase) retry();
  }, [open, retry, staleForOlderBase]);

  return { view, retry };
}

interface ActivityDataBrowserProps {
  host: ActivityDataSelectionHost;
  target: ActivityDataTarget;
  inputLabel: string;
  anchorRef: RefObject<HTMLElement | null>;
  open: boolean;
  current: ActivityResultReference | null;
  onSelect(binding: ActivityDataBinding): void;
  onClose(restoreFocus: boolean): void;
}

/** The shared source browser: a non-modal popover anchored to the control that opened it. */
export function ActivityDataBrowser({ host, target, inputLabel, anchorRef, open, current, onSelect, onClose }: ActivityDataBrowserProps) {
  const { view, retry } = useActivityDataSources(host, target, open);
  const panelRef = useRef<HTMLDivElement>(null);
  const headingId = useId();
  const groups = view.status === "answered" && (view.result.state === "supported" || view.result.state === "empty")
    ? groupActivityOutputSources(view.result.sources, nodeId => host.describeActivity(nodeId))
    : [];

  useEffect(() => {
    if (!open) return;
    // Revoked sessions take their metadata with them.
    const close = () => onClose(false);
    window.addEventListener(authSessionEndedEvent, close);
    return () => window.removeEventListener(authSessionEndedEvent, close);
  }, [onClose, open]);

  // Move focus into the browser once it is placed, and again when its content settles, unless the author already
  // moved focus to another of its controls.
  const contentKey = view.status === "answered" ? `${view.result.state}:${groups.length}` : view.status;
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
    if (event.key === "Escape" || event.key === "Tab") {
      // Tab leaves the popover by returning to its trigger, so focus never lands at the end of the document.
      event.preventDefault();
      event.stopPropagation();
      onClose(true);
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
      onDismiss={reason => onClose(reason !== "anchor-hidden")}
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
          <small>Outputs of activities that run before this one.</small>
        </div>
        <ActivityDataBrowserContent
          view={view}
          groups={groups}
          current={current}
          inputLabel={inputLabel}
          onRetry={retry}
          onSelect={binding => {
            onSelect(binding);
            onClose(true);
          }}
        />
      </div>
    </AnchoredPopover>
  );
}

function ActivityDataBrowserContent({ view, groups, current, inputLabel, onRetry, onSelect }: {
  view: SourcesView;
  groups: ActivityDataProducerGroup[];
  current: ActivityResultReference | null;
  inputLabel: string;
  onRetry(): void;
  onSelect(binding: ActivityDataBinding): void;
}) {
  if (view.status === "loading") return <SkeletonRows rows={3} label="Loading data sources" />;
  if (view.status === "failed") {
    return <ChooserNotice tone="danger" message="Data sources couldn't be loaded." onRetry={onRetry} />;
  }
  const { result } = view;
  if (result.state !== "supported" && result.state !== "empty") {
    const refusal = describeRefusal(result);
    return <ChooserNotice tone={refusal.retry ? "danger" : "info"} message={refusal.message} onRetry={refusal.retry ? onRetry : undefined} />;
  }
  const constraint = result.target?.constraint;
  if (constraint && !constraint.allowedExpressionTypes.some(type => type.toLowerCase() === activityResultSyntax.toLowerCase())) {
    return <ChooserNotice tone="info" message={constraint.message || `${inputLabel} accepts only its own kind of reference.`} />;
  }
  if (groups.length === 0) {
    return <ChooserNotice tone="info" message="No earlier activity outputs reach this input. Add an activity before this one, or use an expression." />;
  }
  return (
    <>
      <div className="wf-data-chooser-list" role="listbox" aria-label="Activity outputs">
        {groups.map(group => <ProducerGroup key={group.producerNodeId} group={group} current={current} onSelect={onSelect} />)}
      </div>
      {result.hasMoreSources ? <p className="wf-data-chooser-note">Showing the first 100 sources.</p> : null}
    </>
  );
}

function ProducerGroup({ group, current, onSelect }: {
  group: ActivityDataProducerGroup;
  current: ActivityResultReference | null;
  onSelect(binding: ActivityDataBinding): void;
}) {
  const labelId = useId();
  return (
    <div role="group" aria-labelledby={labelId} className="wf-data-chooser-group">
      <div id={labelId} className="wf-data-chooser-group-label">{group.label}</div>
      {group.options.map(option => {
        const selected = current?.producerNodeId === option.producerNodeId && current.projectionKey === option.projectionKey;
        const binding = option.source.binding;
        const reasonId = `${labelId}-${option.source.id}`;
        return (
          <button
            key={option.source.id}
            type="button"
            role="option"
            tabIndex={-1}
            aria-selected={selected}
            aria-disabled={binding ? undefined : true}
            aria-describedby={option.unavailableReason ? reasonId : undefined}
            className={["wf-data-chooser-option", selected ? "selected" : ""].filter(Boolean).join(" ")}
            onClick={() => { if (binding) onSelect(binding); }}
            onKeyDown={event => {
              if (event.key !== "Enter" && event.key !== " ") return;
              event.preventDefault();
              if (binding) onSelect(binding);
            }}
          >
            <span className="wf-data-chooser-option-label">{option.label}</span>
            {option.typeLabel ? <span className="wf-data-chooser-option-type">{option.typeLabel}</span> : null}
            {option.unavailableReason ? <small id={reasonId}>{option.unavailableReason}</small> : null}
          </button>
        );
      })}
    </div>
  );
}

function ChooserNotice({ tone, message, onRetry }: { tone: "info" | "danger"; message: string; onRetry?(): void }) {
  return (
    <div className="wf-data-chooser-notice" data-tone={tone} role={tone === "danger" ? "alert" : "status"}>
      <span>{message}</span>
      {onRetry ? <button type="button" onClick={onRetry}>Retry</button> : null}
    </div>
  );
}

/** Refusals keep the safe code's meaning without echoing it; only failures a retry can fix offer Retry. */
function describeRefusal(result: ActivityDataContextResult): { message: string; retry: boolean } {
  switch (result.state) {
    case "denied":
      return result.code === "activity-data.read-only"
        ? { message: "This draft is read-only, so its data can't be browsed.", retry: false }
        : { message: "You don't have access to browse this workflow's data.", retry: false };
    case "stale":
      return result.code === "activity-data.base-changed" || result.code === "activity-data.base-stale"
        ? { message: "This draft was saved somewhere else. Reload it to choose data.", retry: true }
        : { message: "The data sources changed while loading.", retry: true };
    case "unavailable":
      return result.code === "activity-data.metadata-policy-unavailable"
        ? { message: "This server restricts data browsing, so Choose data isn't available. Use an expression instead.", retry: false }
        : { message: "Data sources aren't available right now.", retry: true };
    case "incompatible":
      return { message: "This Studio can't read the data sources this server reported.", retry: false };
    default:
      return { message: "This input can't take data from other activities.", retry: false };
  }
}

/** The property row's Choose data action. Renders nothing where the document owner offers no source analysis. */
export function ChooseDataButton({ target, inputLabel, disabled, onSelect }: {
  target: ActivityDataTarget;
  inputLabel: string;
  disabled?: boolean;
  onSelect(binding: ActivityDataBinding): void;
}) {
  const host = useActivityDataSelectionHost();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const close = useCallback((restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  }, []);
  if (!host) return null;
  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={open ? "wf-choose-data-trigger open" : "wf-choose-data-trigger"}
        aria-label={`Choose data for ${inputLabel}`}
        title="Choose data"
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={disabled}
        onClick={() => (open ? close(false) : setOpen(true))}
      >
        <Database size={13} aria-hidden="true" />
      </button>
      {open ? (
        <ActivityDataBrowser
          host={host}
          target={target}
          inputLabel={inputLabel}
          anchorRef={triggerRef}
          open={open}
          current={null}
          onSelect={onSelect}
          onClose={close}
        />
      ) : null}
    </>
  );
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
  const [browsing, setBrowsing] = useState(false);
  const inputLabel = descriptor.displayName || descriptor.name;
  const nodeId = readNodeId(context.activity);
  const target = { nodeId, inputKey: getInputPropertyName(descriptor) };
  const producer = reference && host ? host.describeActivity(reference.producerNodeId) : null;
  const missing = Boolean(reference && host && !producer);
  const problem = !reference
    ? "No activity output is chosen."
    : missing
      ? "The source activity is no longer in this workflow. Choose another output or clear it."
      : host?.findingsFor(target).map(describeReferenceFinding).find((text): text is string => text != null) ?? null;
  const producerLabel = reference ? producer?.label ?? reference.producerNodeId : "";
  const outputLabel = reference ? describeOutput(reference.projectionKey, producer) : "";
  const canBrowse = Boolean(host && nodeId && !disabled);

  useEffect(() => {
    if (initialFocus) replaceRef.current?.focus();
  }, [initialFocus]);

  const closeBrowser = useCallback((restoreFocus: boolean) => {
    setBrowsing(false);
    if (restoreFocus) replaceRef.current?.focus();
  }, []);

  return (
    <div className="wf-data-reference" data-problem={problem ? "true" : undefined}>
      {reference ? (
        <p className="wf-data-reference-source">
          <Database size={13} aria-hidden="true" />
          <span>
            <span className="wf-visually-hidden">Data from </span>
            <strong>{producerLabel}</strong>
            <span aria-hidden="true"> › </span>
            <span className="wf-visually-hidden">, output </span>
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
            onClick={() => (browsing ? closeBrowser(false) : setBrowsing(true))}
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
