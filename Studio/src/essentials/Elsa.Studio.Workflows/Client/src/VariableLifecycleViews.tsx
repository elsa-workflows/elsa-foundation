import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  compareInstances,
  getVariableWritePayload,
  listInstanceJournal,
  listVariableWrites,
  type InstanceComparison,
  type JournalCommit,
  type VariableValueStateView,
  type VariableWriteEntry,
  type VariableWriteFilter,
  type VariableWriterView
} from "./api/variableLifecycle";
import { RuntimeCapturedValue } from "./workflow-editor/RuntimeValueEvidence";
import { formatDate } from "./workflowFormatting";
import "./VariableLifecycle.css";

// Spec 1229 FR-017: the write history, the commit-ordered journal and the comparison of two instances. Values appear
// only as value states; a captured value is read through the permission-checked payload relation on request.

const outcomeLabels: Record<string, string> = {
  applied: "Applied",
  lostUpdateWarning: "Lost update",
  lostUpdateFault: "Refused (Fault)",
  acceptedOverride: "Accepted override"
};

const sourceLabels: Record<string, string> = {
  capture: "Capture",
  intrinsic: "Intrinsic",
  sessionChange: "Session change",
  alteration: "Operator change",
  backgroundResult: "Background result",
  acceptedCompletion: "Accepted completion"
};

const valueStateLabels: Record<string, string> = {
  captured: "Captured",
  omitted: "Not captured",
  redacted: "Redacted",
  expired: "Expired"
};

/** A value state, never shown as null; a captured value is read on request. */
export function VariableValueStateLabel({ value, label, onShowValue }: {
  value: VariableValueStateView;
  label?: string;
  onShowValue?(value: VariableValueStateView): void;
}) {

  const text = valueStateLabels[value.state] ?? value.state;
  return (
    <span className="wf-variable-value-state" data-state={value.state} data-operational={value.isOperational || undefined}>
      {label ? <span className="wf-variable-value-label">{label}</span> : null}
      <span className="wf-variable-value-chip">{text}</span>
      {value.state === "captured" && value.payloadRef && onShowValue
        ? <button type="button" className="wf-link-button" onClick={() => onShowValue(value)}>Show value</button>
        : null}
      {value.digest ? <code className="wf-variable-digest" title={value.digest}>{shortDigest(value.digest)}</code> : null}
    </span>
  );
}

export function describeWriter(writer: VariableWriterView) {
  if (writer.alterationId) return `Operator change (${writer.alterationId})`;
  const node = writer.executableNodeId ?? "unknown node";
  const path = writer.branchId ? ` · branch ${writer.branchId}` : writer.executionPathId ? ` · path ${writer.executionPathId}` : "";
  return `${node}${path}`;
}

/** The write history of an instance, in commit order, with lost updates and concurrent overwrites marked. */
export function VariableWriteHistoryTable({ entries, onShowValue }: {
  entries: VariableWriteEntry[];
  onShowValue?(entry: VariableWriteEntry, value: VariableValueStateView): void;
}) {

  if (entries.length === 0) return <div className="wf-empty">No variable writes recorded.</div>;
  return (
    <table className="wf-variable-history" aria-label="Variable write history">
      <thead>
        <tr>
          <th scope="col">Commit</th>
          <th scope="col">Variable</th>
          <th scope="col">Writer</th>
          <th scope="col">Outcome</th>
          <th scope="col">Versions</th>
          <th scope="col">Value</th>
        </tr>
      </thead>
      <tbody>
        {entries.map(entry => (
          <tr key={entry.entryId} data-outcome={entry.outcome} data-current={entry.isCurrentValue || undefined} data-overwrite={entry.concurrentOverwrite ? true : undefined}>
            <td>
              {entry.commitSequence == null ? <span title="Recorded before commit order existed">≈</span> : formatCommit(entry.commitSequence)}
              {entry.orderApproximate ? <small className="wf-variable-note"> approximate</small> : null}
            </td>
            <td><code>{entry.variableKey}</code>{entry.frameId ? <small className="wf-variable-note"> {entry.frameId}</small> : null}</td>
            <td>
              <span>{describeWriter(entry.writer)}</span>
              <small className="wf-variable-note"> {sourceLabels[entry.source] ?? entry.source}</small>
            </td>
            <td>
              <span className="wf-variable-outcome" data-outcome={entry.outcome}>{outcomeLabels[entry.outcome] ?? entry.outcome}</span>
              {entry.isCurrentValue ? <span className="wf-variable-current">Current value</span> : null}
              {entry.concurrentOverwrite ? (
                <span className="wf-variable-overwrite" role="note">
                  {entry.concurrentOverwrite.isLostUpdate ? "Lost update over " : "Concurrent overwrite of "}
                  {describeWriter(entry.concurrentOverwrite.overwrittenWriter)}
                </span>
              ) : null}
              {!entry.concurrentOverwrite && entry.otherWriter
                ? <span className="wf-variable-overwrite" role="note">Met a version of {describeWriter(entry.otherWriter)}</span>
                : null}
              {entry.resolvesEntryId ? <span className="wf-variable-note">Resolves a refused write</span> : null}
            </td>
            <td>
              {entry.baseVersion == null ? "blind" : `base v${entry.baseVersion}`} · v{entry.currentVersion} → v{entry.newVersion}
              {!entry.isApplied ? <small className="wf-variable-note"> not applied</small> : null}
            </td>
            <td>
              <div className="wf-variable-values">
                <VariableValueStateLabel value={entry.value} label={entry.refusedValue ? "Refused" : "Written"} onShowValue={value => onShowValue?.(entry, value)} />
                {entry.overwrittenValue
                  ? <VariableValueStateLabel value={entry.overwrittenValue} label="Overwritten" onShowValue={value => onShowValue?.(entry, value)} />
                  : null}
              </div>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The instance journal: one block per commit, with its transitions, input refreshes and writes. */
export function InstanceJournalList({ commits }: { commits: JournalCommit[] }) {
  useStudioTimeZone();
  if (commits.length === 0) return <div className="wf-empty">No commits recorded.</div>;
  return (
    <ol className="wf-variable-journal" aria-label="Commit journal">
      {commits.map((commit, index) => (
        <li key={commit.commitSequence ?? `approximate-${index}`} data-approximate={commit.orderApproximate || undefined}>
          <header>
            <strong>{commit.commitSequence == null ? "Before commit order was recorded" : `Commit ${formatCommit(commit.commitSequence)}`}</strong>
            {commit.occurredAt ? <time dateTime={commit.occurredAt}>{formatDate(commit.occurredAt)}</time> : null}
          </header>
          <ul>
            {commit.transitions.map(transition => (
              <li key={`${transition.activityExecutionId}-${transition.kind}`}>
                <span className="wf-variable-transition" data-kind={transition.kind}>{transition.kind}</span>{" "}
                <code title={transition.runTimePath}>{transition.executableNodeId}</code>
                {transition.branchId ? <small className="wf-variable-note"> branch {transition.branchId}</small> : null}
                {transition.evaluation ? <small className="wf-variable-note"> evaluation {transition.evaluation}</small> : null}
              </li>
            ))}
            {commit.inputRefreshes.map(refresh => (
              <li key={`${refresh.activityExecutionId}-refresh-${refresh.replacedByEvaluation}`}>
                <span className="wf-variable-transition" data-kind="inputRefresh">input refresh</span>{" "}
                evaluation {refresh.supersededEvaluation} → {refresh.replacedByEvaluation}: {refresh.reason}
              </li>
            ))}
            {commit.writes.map(write => (
              <li key={write.entryId}>
                <span className="wf-variable-transition" data-kind="write">write</span>{" "}
                <code>{write.variableKey}</code> v{write.currentVersion} → v{write.newVersion} by {describeWriter(write.writer)}
                {write.outcome !== "applied" ? <span className="wf-variable-outcome" data-outcome={write.outcome}> {outcomeLabels[write.outcome] ?? write.outcome}</span> : null}
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ol>
  );
}

/** Where two instances of one version first differ and which variables ended differently, by digest only. */
export function InstanceComparisonSummary({ comparison }: { comparison: InstanceComparison }) {

  if (!comparison.comparable) {
    return (
      <div className="wf-variable-comparison" role="status">
        These instances cannot be compared{comparison.refusalReason === "differentExecutable" ? ": they do not run the same workflow version." : "."}
      </div>
    );
  }
  const differing = comparison.variables.filter(variable => variable.finalDigestDiffers || variable.writtenDigestsDiffer || variable.digestUnknown);
  return (
    <div className="wf-variable-comparison">
      {comparison.partial ? <p className="wf-variable-note">One instance is still running; only what it committed is compared.</p> : null}
      {comparison.firstDivergence ? (
        <section aria-label="First divergence">
          <h4>First difference at commit #{comparison.firstDivergence.commitIndex + 1}</h4>
          <div className="wf-variable-divergence">
            <div><h5>This instance</h5>{comparison.firstDivergence.left ? <InstanceJournalList commits={[comparison.firstDivergence.left]} /> : <p>No further commits.</p>}</div>
            <div><h5>Other instance</h5>{comparison.firstDivergence.right ? <InstanceJournalList commits={[comparison.firstDivergence.right]} /> : <p>No further commits.</p>}</div>
          </div>
        </section>
      ) : <p>Both instances committed the same steps in the same order.</p>}
      {differing.length === 0 ? <p>All variables ended with the same values.</p> : (
        <table className="wf-variable-history" aria-label="Variables that differ">
          <thead><tr><th scope="col">Variable</th><th scope="col">Final value</th><th scope="col">Written values</th></tr></thead>
          <tbody>
            {differing.map(variable => (
              <tr key={`${variable.framePath}-${variable.variableKey}`}>
                <td><code>{variable.variableKey}</code> <small className="wf-variable-note">{variable.framePath}</small></td>
                <td>{variable.digestUnknown ? "unknown" : variable.finalDigestDiffers ? "differs" : "same"}</td>
                <td>{variable.writtenDigestsDiffer ? `differ (${variable.leftWriteCount} vs ${variable.rightWriteCount} writes)` : variable.digestUnknown ? "unknown" : "same"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

type LifecycleTab = "history" | "journal" | "compare";
const lifecycleTabs: readonly LifecycleTab[] = ["history", "journal", "compare"];
const lifecycleTabTitles: Record<LifecycleTab, string> = { history: "Write history", journal: "Journal", compare: "Compare" };
type Loaded<T> = { status: "idle" | "loading" } | { status: "ready"; items: T[]; next: string | null; total?: number | null } | { status: "failed"; error: string };

/**
 * The variable lifecycle panel of an instance: the write history (optionally filtered, for example to lost updates),
 * the journal, and a comparison with another instance. Pages continue from the cursor the Runtime returns.
 */
export function VariableLifecyclePanel({ context, workflowExecutionId, initialFilter = {}, initialTab = "history" }: {
  context: StudioEndpointContext;
  workflowExecutionId: string;
  initialFilter?: VariableWriteFilter;
  initialTab?: LifecycleTab;
}) {

  const [tab, setTab] = useState<LifecycleTab>(initialTab);
  const [filter, setFilter] = useState<VariableWriteFilter>(initialFilter);
  const [history, setHistory] = useState<Loaded<VariableWriteEntry>>({ status: "idle" });
  const [journal, setJournal] = useState<Loaded<JournalCommit>>({ status: "idle" });
  const [otherId, setOtherId] = useState("");
  const [comparison, setComparison] = useState<{ status: "idle" | "loading" } | { status: "ready"; value: InstanceComparison } | { status: "failed"; error: string }>({ status: "idle" });
  const [shownValues, setShownValues] = useState<Record<string, ShownValue>>({});

  const idBase = useId();
  const tabButtons = useRef<Partial<Record<LifecycleTab, HTMLButtonElement | null>>>({});
  // Arrow keys, Home and End move between the views, as in the run details tablist.
  const moveTab = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = lifecycleTabs.indexOf(tab);
    const next = event.key === "ArrowRight" ? lifecycleTabs[(index + 1) % lifecycleTabs.length]
      : event.key === "ArrowLeft" ? lifecycleTabs[(index + lifecycleTabs.length - 1) % lifecycleTabs.length]
      : event.key === "Home" ? lifecycleTabs[0]
      : event.key === "End" ? lifecycleTabs[lifecycleTabs.length - 1]
      : null;
    if (!next) return;
    event.preventDefault();
    setTab(next);
    tabButtons.current[next]?.focus();
  };
  const historyRequest = useRef(0);
  const journalRequest = useRef(0);
  const compareRequest = useRef(0);
  const instance = useRef(workflowExecutionId);
  instance.current = workflowExecutionId;

  // Another instance starts from nothing: results of the previous one are dropped, and its pending answers ignored.
  useEffect(() => {
    setComparison({ status: "idle" });
    setShownValues({});
    setOtherId("");
    compareRequest.current++;
  }, [workflowExecutionId]);

  const loadHistory = useCallback(async (cursor: string | null) => {
    const request = ++historyRequest.current;
    setHistory(current => cursor && current.status === "ready" ? current : { status: "loading" });
    try {
      const page = await listVariableWrites(context, workflowExecutionId, filter, { cursor, take: 100 });
      if (request !== historyRequest.current) return;
      setHistory(current => ({
        status: "ready",
        items: cursor && current.status === "ready" ? [...current.items, ...page.items] : page.items,
        next: page.next ?? null,
        total: page.concurrentOverwriteTotal
      }));
    } catch (error) {
      if (request === historyRequest.current) setHistory({ status: "failed", error: errorMessage(error) });
    }
  }, [context, workflowExecutionId, filter]);

  const loadJournal = useCallback(async (cursor: string | null) => {
    const request = ++journalRequest.current;
    setJournal(current => cursor && current.status === "ready" ? current : { status: "loading" });
    try {
      const page = await listInstanceJournal(context, workflowExecutionId, { cursor, take: 50 });
      if (request !== journalRequest.current) return;
      setJournal(current => ({
        status: "ready",
        items: cursor && current.status === "ready" ? [...current.items, ...page.items] : page.items,
        next: page.next ?? null
      }));
    } catch (error) {
      if (request === journalRequest.current) setJournal({ status: "failed", error: errorMessage(error) });
    }
  }, [context, workflowExecutionId]);

  useEffect(() => { if (tab === "history") void loadHistory(null); }, [tab, loadHistory]);
  useEffect(() => { if (tab === "journal") void loadJournal(null); }, [tab, loadJournal]);

  const showValue = useCallback(async (entry: VariableWriteEntry, value: VariableValueStateView) => {
    if (!value.payloadRef) return;
    const key = `${entry.entryId}:${value.payloadRef.value}`;
    // Names the variable, which value and the commit, so several shown values stay apart.
    const label = `${entry.variableKey}, ${value.payloadRef.value}${entry.commitSequence == null ? "" : ` at commit ${formatCommit(entry.commitSequence)}`}`;
    setShownValues(current => ({ ...current, [key]: { status: "loading", label } }));
    try {
      const read = await getVariableWritePayload(context, workflowExecutionId, value.payloadRef);
      if (instance.current !== workflowExecutionId) return;
      setShownValues(current => ({ ...current, [key]: { status: "ready", payload: read.payload, captureMode: read.captureMode, label } }));
    } catch (error) {
      if (instance.current !== workflowExecutionId) return;
      setShownValues(current => ({ ...current, [key]: { status: "failed", error: errorMessage(error), label } }));
    }
  }, [context, workflowExecutionId]);

  const compare = useCallback(async () => {
    if (!otherId.trim()) return;
    const request = ++compareRequest.current;
    setComparison({ status: "loading" });
    try {
      const value = await compareInstances(context, workflowExecutionId, otherId.trim());
      if (request === compareRequest.current) setComparison({ status: "ready", value });
    } catch (error) {
      if (request === compareRequest.current) setComparison({ status: "failed", error: errorMessage(error) });
    }
  }, [context, workflowExecutionId, otherId]);

  return (
    <section className="wf-variable-lifecycle" aria-label="Variable lifecycle">
      <div className="wf-variable-tabs" role="tablist" aria-label="Variable lifecycle views" onKeyDown={moveTab}>
        {lifecycleTabs.map(name => (
          <button
            key={name}
            ref={element => { tabButtons.current[name] = element; }}
            id={`${idBase}-${name}-tab`}
            type="button"
            role="tab"
            aria-selected={tab === name}
            aria-controls={`${idBase}-${name}-panel`}
            tabIndex={tab === name ? 0 : -1}
            onClick={() => setTab(name)}>
            {lifecycleTabTitles[name]}
          </button>
        ))}
      </div>
      {tab === "history" ? (
        <div role="tabpanel" id={`${idBase}-history-panel`} aria-labelledby={`${idBase}-history-tab`}>
          <div className="wf-variable-filters">
            <label>
              Variable <input value={filter.variableKey ?? ""} onChange={event => setFilter(current => ({ ...current, variableKey: event.target.value || undefined }))} />
            </label>
            <label>
              Outcome{" "}
              <select value={filter.outcome ?? ""} onChange={event => setFilter(current => ({ ...current, outcome: (event.target.value || undefined) as VariableWriteFilter["outcome"] }))}>
                <option value="">Any</option>
                {Object.entries(outcomeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            </label>
            <label>
              Source{" "}
              <select value={filter.source ?? ""} onChange={event => setFilter(current => ({ ...current, source: event.target.value || undefined }))}>
                <option value="">Any</option>
                {Object.entries(sourceLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            </label>
            <label>
              Frame <input value={filter.frameId ?? ""} onChange={event => setFilter(current => ({ ...current, frameId: event.target.value || undefined }))} />
            </label>
            <label>
              <input type="checkbox" checked={filter.overwrites ?? false} onChange={event => setFilter(current => ({ ...current, overwrites: event.target.checked || undefined }))} /> Concurrent overwrites only
            </label>
          </div>
          <PagedContent loaded={history} onMore={next => void loadHistory(next)}>
            {items => (
              <>
                {history.status === "ready" && history.total != null ? <p className="wf-variable-note">{history.total} concurrent overwrite(s)</p> : null}
                <VariableWriteHistoryTable entries={items} onShowValue={(entry, value) => void showValue(entry, value)} />
                <ShownValues values={shownValues} />
              </>
            )}
          </PagedContent>
        </div>
      ) : null}
      {tab === "journal" ? (
        <div role="tabpanel" id={`${idBase}-journal-panel`} aria-labelledby={`${idBase}-journal-tab`}>
          <PagedContent loaded={journal} onMore={next => void loadJournal(next)}>{items => <InstanceJournalList commits={items} />}</PagedContent>
        </div>
      ) : null}
      {tab === "compare" ? (
        <div role="tabpanel" id={`${idBase}-compare-panel`} aria-labelledby={`${idBase}-compare-tab`}>
          <form className="wf-variable-filters" onSubmit={event => { event.preventDefault(); void compare(); }}>
            <label>Other instance <input value={otherId} onChange={event => setOtherId(event.target.value)} placeholder="Workflow instance ID" /></label>
            <button type="submit" className="wf-button" disabled={!otherId.trim() || comparison.status === "loading"}>Compare</button>
          </form>
          {comparison.status === "loading" ? <div className="wf-empty">Comparing…</div> : null}
          {comparison.status === "failed" ? <div className="wf-error" role="alert">{comparison.error}</div> : null}
          {comparison.status === "ready" ? <InstanceComparisonSummary comparison={comparison.value} /> : null}
        </div>
      ) : null}
    </section>
  );
}

function PagedContent<T>({ loaded, onMore, children }: {
  loaded: Loaded<T>;
  onMore(next: string): void;
  children(items: T[]): ReactNode;
}) {

  if (loaded.status === "failed") return <div className="wf-error" role="alert">{loaded.error}</div>;
  if (loaded.status !== "ready") return <div className="wf-empty">Loading…</div>;
  const next = loaded.next;
  return (
    <>
      {children(loaded.items)}
      {next ? <button type="button" className="wf-button" onClick={() => onMore(next)}>Load more</button> : null}
    </>
  );
}

type ShownValue = ({ status: "loading" } | { status: "ready"; payload: unknown; captureMode: string } | { status: "failed"; error: string }) & { label: string };

function ShownValues({ values }: { values: Record<string, ShownValue> }) {

  const entries = Object.entries(values);
  if (entries.length === 0) return null;
  return (
    <dl className="wf-variable-shown-values">
      {entries.map(([key, value]) => (
        <div key={key}>
          <dt>{value.label}</dt>
          <dd>
            {value.status === "loading" ? "Resolving…" : value.status === "failed" ? <span role="alert">{value.error}</span> : <RuntimeCapturedValue value={value.payload} captureMode={value.captureMode} />}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function formatCommit(sequence: number) {
  // The commit sequence is a lease token in the high 32 bits and an ordinal below it; show it as token.ordinal.
  const token = Math.floor(sequence / 2 ** 32);
  const ordinal = sequence % 2 ** 32;
  return token > 0 ? `${token}.${ordinal}` : `${ordinal}`;
}

function shortDigest(digest: string) {
  const value = digest.includes(":") ? digest.slice(digest.indexOf(":") + 1) : digest;
  return value.slice(0, 8);
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
