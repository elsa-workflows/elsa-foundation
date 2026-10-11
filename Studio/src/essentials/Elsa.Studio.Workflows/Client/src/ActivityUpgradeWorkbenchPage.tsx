import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ArrowLeft, CheckCircle2, Plus, RefreshCw, Trash2 } from "lucide-react";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityUpgradePlanReview } from "./ActivityUpgradePlanReview";
import {
  activityUpgradeOutcomeUnknown,
  addExactUpgradeRoot,
  buildActivityUpgradePlanRequest,
  createActivityUpgradeDraft,
  createActivityUpgradeReceiptId,
  normalizePlanStatus,
  receiptIsTerminal,
  removeExactUpgradeRoot
} from "./activityUpgradeModel";
import type {
  ActivityUpgradeApplyReceipt,
  ActivityUpgradeApplyResult,
  ActivityUpgradePlan,
  ActivityUpgradePublicationHandoff,
  ActivityUpgradeRootKind,
  ActivityUpgradeStage
} from "./activityUpgradeTypes";
import {
  applyActivityUpgradeStage,
  createActivityUpgradePlan,
  getActivityUpgradePlan,
  getActivityUpgradeReceipt,
  refreshActivityUpgradePlan
} from "./api/activityUpgrades";
import { ApiCapabilityUnavailableError } from "./api/capabilities";
import { observeActivityDefinitions, type ActivityDefinitionsObservation } from "./activityDefinitionObservability";
import "./activityDefinitions.css";
import "./activityUpgradeWorkbench.css";

type BusyOperation = "review" | "load" | "apply" | "receipt" | "refresh" | null;
type OutcomeState = {
  receiptId: string;
  idempotencyKey: string;
  state: "checking" | "preparing" | "not-found" | "rejected";
};

interface PersistedUpgradeSession {
  planId: string;
  receipts: Array<{ planId: string; receiptId: string }>;
  idempotencyByStage: Record<string, string>;
}

const storageKey = "elsa.activity-upgrade-workbench.v1";
const rootKindOptions: Array<{ value: ActivityUpgradeRootKind; label: string }> = [
  { value: "ActivityDraft", label: "Activity Definition draft" },
  { value: "ActivityVersion", label: "Activity Definition Version" },
  { value: "WorkflowDraft", label: "Workflow draft" },
  { value: "WorkflowVersion", label: "Workflow Version" }
];

export function ActivityUpgradeWorkbenchPage({ context }: { context: StudioEndpointContext }) {
  const locationSeed = useMemo(readLocationSeed, []);
  const persisted = useMemo(readSession, []);
  const [draft, setDraft] = useState(() => createActivityUpgradeDraft(locationSeed));
  const [rootKind, setRootKind] = useState<ActivityUpgradeRootKind>("ActivityDraft");
  const [rootId, setRootId] = useState("");
  const [plan, setPlan] = useState<ActivityUpgradePlan | null>(null);
  const [busy, setBusy] = useState<BusyOperation>(null);
  const [error, setError] = useState<string | null>(null);
  const [reviewedEvidenceStale, setReviewedEvidenceStale] = useState(false);
  const [results, setResults] = useState<ActivityUpgradeApplyResult[]>([]);
  const [publicationVersions, setPublicationVersions] = useState<Record<string, string>>({});
  const [outcome, setOutcome] = useState<OutcomeState | null>(null);
  const [successorFrom, setSuccessorFrom] = useState<string | null>(null);
  const idempotencyByStage = useRef<Record<string, string>>(persisted?.idempotencyByStage ?? {});
  const receipts = useRef<Array<{ planId: string; receiptId: string }>>(persisted?.receipts ?? []);
  const hasNewSelectionSeed = Boolean(locationSeed.rootId || locationSeed.fromVersionId || locationSeed.toVersionId);
  const requestedPlanId = locationSeed.planId ?? (hasNewSelectionSeed ? null : persisted?.planId) ?? null;
  const activePlanId = plan?.planId ?? null;

  useEffect(() => {
    if (!requestedPlanId) return;
    const controller = new AbortController();
    setBusy("load");
    setError(null);
    void getActivityUpgradePlan(context, requestedPlanId, controller.signal).then(
      loaded => {
        if (controller.signal.aborted) return;
        setPlan(loaded);
        setReviewedEvidenceStale(false);
        setBusy(null);
        persistSession(loaded.planId, receipts.current, idempotencyByStage.current);
        writePlanLocation(loaded.planId, "replace");
      },
      cause => {
        if (controller.signal.aborted) return;
        setBusy(null);
        setError(privateSafeReadError(cause));
      }
    );
    return () => controller.abort();
  }, [context, requestedPlanId]);

  useEffect(() => {
    if (!activePlanId || receipts.current.length === 0) return;
    const controller = new AbortController();
    void Promise.all(receipts.current.map(reference =>
      getActivityUpgradeReceipt(context, reference.planId, reference.receiptId, controller.signal)
        .catch(() => null)
    )).then(receipts => {
      if (controller.signal.aborted) return;
      const restored = receipts
        .filter((receipt): receipt is ActivityUpgradeApplyReceipt => Boolean(receipt?.result))
        .map(receipt => receipt.result!);
      if (restored.length) {
        setResults(current => restored.reduce((history, result) => replaceResult(history, result), current));
      }
    });
    return () => controller.abort();
  }, [activePlanId, context]);

  const review = async () => {
    let request;
    try {
      request = buildActivityUpgradePlanRequest(draft);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "The exact upgrade selection is incomplete.");
      return;
    }
    const controller = new AbortController();
    setBusy("review");
    setError(null);
    setPlan(null);
    setResults([]);
    setOutcome(null);
    observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: "pending" });
    try {
      const reviewed = await createActivityUpgradePlan(context, request, controller.signal);
      setPlan(reviewed);
      setReviewedEvidenceStale(false);
      receipts.current = [];
      idempotencyByStage.current = {};
      persistSession(reviewed.planId, [], {});
      writePlanLocation(reviewed.planId, "push");
      observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: "ready" });
    } catch (cause) {
      setError(privateSafePlanError(cause));
      observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: upgradeFailureOutcome(cause) });
    } finally {
      setBusy(null);
    }
  };

  const applyStage = async (stage: ActivityUpgradeStage) => {
    if (!plan) return;
    const operationKey = stageOperationKey(plan.planId, stage.stageId);
    const idempotencyKey = idempotencyByStage.current[operationKey] ?? operationId();
    idempotencyByStage.current[operationKey] = idempotencyKey;
    persistSession(plan.planId, receipts.current, idempotencyByStage.current);
    const expectedReceiptId = await createActivityUpgradeReceiptId(plan.planId, idempotencyKey);
    setBusy("apply");
    setError(null);
    setOutcome(null);
    observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "pending" });
    try {
      const result = await applyActivityUpgradeStage(context, plan.planId, stage.stageId, idempotencyKey);
      await acceptApplyResult(result);
    } catch (cause) {
      const unknown = activityUpgradeOutcomeUnknown(cause);
      if (unknown || mayHaveAmbiguousOutcome(cause)) {
        observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "reconciling" });
        await reconcileOutcome(plan.planId, unknown?.receiptId ?? expectedReceiptId, idempotencyKey);
      } else {
        setError(privateSafeMutationError(cause));
        if (isStalePlan(cause)) setReviewedEvidenceStale(true);
        observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: upgradeFailureOutcome(cause) });
      }
    } finally {
      setBusy(null);
    }
  };

  const acceptApplyResult = async (result: ActivityUpgradeApplyResult) => {
    rememberReceipt(result.planId, result.receiptId);
    setResults(current => replaceResult(current, result));
    setPublicationVersions({});
    setOutcome(null);
    observeActivityDefinitions({
      event: "upgrade-apply",
      surface: "upgrade-workbench",
      outcome: result.awaitingPublications.length ? "waiting" : "completed"
    });
    if (!plan) return;
    try {
      const confirmed = await getActivityUpgradePlan(context, plan.planId);
      setPlan(confirmed);
      setReviewedEvidenceStale(false);
    } catch {
      setReviewedEvidenceStale(true);
      setError("The stage returned a durable receipt, but the updated authoritative plan could not be confirmed. Apply remains paused.");
    }
  };

  const reconcileOutcome = async (planId: string, receiptId: string, idempotencyKey: string) => {
    setBusy("receipt");
    setOutcome({ receiptId, idempotencyKey, state: "checking" });
    try {
      const receipt = await getActivityUpgradeReceipt(context, planId, receiptId);
      rememberReceipt(planId, receipt.receiptId);
      if (!receiptIsTerminal(receipt)) {
        setOutcome({ receiptId, idempotencyKey, state: "preparing" });
        observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "waiting" });
        return;
      }
      if (receipt.result) {
        await acceptApplyResult(receipt.result);
        return;
      }
      setOutcome({ receiptId, idempotencyKey, state: "rejected" });
      setError(`The durable receipt rejected this stage${receipt.rejectionCode ? ` (${receipt.rejectionCode})` : ""}. The reviewed plan remains visible.`);
      observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "failed" });
    } catch (cause) {
      if (cause instanceof StudioHttpError && cause.status === 404) {
        setOutcome({ receiptId, idempotencyKey, state: "not-found" });
        observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "unavailable" });
        return;
      }
      setOutcome({ receiptId, idempotencyKey, state: "preparing" });
      setError("Outcome unknown. Durable receipt status could not yet be confirmed; the mutation will not be retried.");
      observeActivityDefinitions({ event: "upgrade-apply", surface: "upgrade-workbench", outcome: "failed" });
    }
  };

  const refreshSuccessor = async (result: ActivityUpgradeApplyResult) => {
    if (!plan || !result.receiptId || result.awaitingPublications.length === 0) return;
    const publishedDrafts = result.awaitingPublications.map(handoff => ({
      draftId: handoff.draftId,
      publishedVersionId: publicationVersions[handoff.draftId]?.trim() ?? ""
    }));
    if (publishedDrafts.some(selection => !selection.publishedVersionId)) {
      setError("Enter the actual exact published version for every handoff draft.");
      return;
    }
    const predecessorId = plan.planId;
    setBusy("refresh");
    setError(null);
    observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: "pending" });
    try {
      const successor = await refreshActivityUpgradePlan(context, predecessorId, [{
        receiptId: result.receiptId,
        publishedDrafts
      }]);
      setPlan(successor);
      setReviewedEvidenceStale(false);
      setSuccessorFrom(predecessorId);
      setPublicationVersions({});
      setOutcome(null);
      persistSession(successor.planId, receipts.current, idempotencyByStage.current);
      writePlanLocation(successor.planId, "push");
      observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: "ready" });
    } catch (cause) {
      setError(privateSafeMutationError(cause));
      observeActivityDefinitions({ event: "upgrade-plan", surface: "upgrade-workbench", outcome: upgradeFailureOutcome(cause) });
    } finally {
      setBusy(null);
    }
  };

  const rememberReceipt = (ownerPlanId: string, receiptId: string | null | undefined) => {
    if (!receiptId || receipts.current.some(reference => reference.planId === ownerPlanId && reference.receiptId === receiptId)) return;
    receipts.current = [...receipts.current, { planId: ownerPlanId, receiptId }];
    if (plan) persistSession(plan.planId, receipts.current, idempotencyByStage.current);
  };

  const currentPlanResult = [...results].reverse().find(result => result.planId === plan?.planId) ?? null;
  const awaitingResult = [...results].reverse().find(result =>
    result.planId === plan?.planId && result.awaitingPublications.length > 0
  ) ?? null;
  const complete = Boolean(plan && normalizePlanStatus(plan.status) === "Applied" && currentPlanResult?.receiptId);

  return <main className="ad-page au-page" aria-labelledby="activity-upgrade-title">
    <a className="ad-back" href="/workflows/activity-definitions"><ArrowLeft size={16} aria-hidden /> Activity Definitions</a>
    <header className="ad-page-header">
      <div>
        <span className="ad-kicker">Activity Definition maintenance</span>
        <h1 id="activity-upgrade-title">Staged dependency upgrades</h1>
        <p>Choose explicit exact roots, review the backend's authoritative dependency closure, and apply one bottom-up atomic stage at a time.</p>
      </div>
      {plan ? <button type="button" onClick={() => void reloadPlan()} disabled={Boolean(busy)}><RefreshCw size={16} aria-hidden /> Confirm plan</button> : null}
    </header>

    <div className="au-scope-note" role="note">
      <strong>Broad planning is deliberate</strong>
      <span>A single placed occurrence stays in the Inspector quick flow. This workbench never selects unseen pages or publishes created drafts automatically.</span>
    </div>

    {!plan && busy === "load" ? <div className="ad-skeleton" role="status" aria-busy="true"><span>Loading the persistent authoritative plan…</span><div /><div /><div /></div> : null}
    {!plan && busy !== "load" ? <UpgradeSelection
      draft={draft}
      rootKind={rootKind}
      rootId={rootId}
      reviewing={busy === "review"}
      onDraftChange={setDraft}
      onRootKindChange={setRootKind}
      onRootIdChange={setRootId}
      onAddRoot={() => {
        setDraft(current => addExactUpgradeRoot(current, { kind: rootKind, id: rootId }));
        setRootId("");
      }}
      onRemoveRoot={(kind, id) => setDraft(current => removeExactUpgradeRoot(current, { kind, id }))}
      onReview={() => void review()}
    /> : null}

    {error ? <div className="ad-inline-error au-error" role="alert"><AlertTriangle size={18} aria-hidden /><span>{error}</span></div> : null}
    {(successorFrom ?? plan?.predecessorPlanId) && plan ? <div className="au-successor" role="status"><CheckCircle2 size={18} aria-hidden /><div><strong>Successor plan</strong><span><code>{plan.planId}</code> now follows <code>{successorFrom ?? plan.predecessorPlanId}</code> using actual published versions.</span></div></div> : null}
    {plan ? <ActivityUpgradePlanReview plan={plan} stale={reviewedEvidenceStale} applying={busy === "apply" || busy === "receipt"} onApply={stage => void applyStage(stage)} /> : null}

    {outcome ? <OutcomeUnknown
      outcome={outcome}
      busy={busy === "receipt"}
      onCheck={() => plan && void reconcileOutcome(plan.planId, outcome.receiptId, outcome.idempotencyKey)}
      onRetry={() => {
        const stage = plan?.stages.find(item =>
          idempotencyByStage.current[stageOperationKey(plan.planId, item.stageId)] === outcome.idempotencyKey
        );
        if (stage) void applyStage(stage);
      }}
    /> : null}

    {results.length ? <ReceiptHistory results={results} planId={plan?.planId ?? ""} /> : null}
    {awaitingResult ? <PublicationHandoff
      result={awaitingResult}
      planId={plan?.planId ?? awaitingResult.planId}
      values={publicationVersions}
      refreshing={busy === "refresh"}
      onChange={(draftId, value) => setPublicationVersions(current => ({ ...current, [draftId]: value }))}
      onRefresh={() => void refreshSuccessor(awaitingResult)}
    /> : null}
    {complete ? <div className="au-complete" role="status"><CheckCircle2 size={22} aria-hidden /><div><strong>Upgrade complete</strong><span>Every authoritative stage is applied. Child publication remained separately reviewed.</span></div></div> : null}
  </main>;

  async function reloadPlan() {
    if (!plan) return;
    setBusy("load");
    setError(null);
    try {
      const confirmed = await getActivityUpgradePlan(context, plan.planId);
      setPlan(confirmed);
      setReviewedEvidenceStale(false);
    } catch (cause) {
      setReviewedEvidenceStale(true);
      setError(privateSafeReadError(cause));
    } finally {
      setBusy(null);
    }
  }
}

function UpgradeSelection({
  draft,
  rootKind,
  rootId,
  reviewing,
  onDraftChange,
  onRootKindChange,
  onRootIdChange,
  onAddRoot,
  onRemoveRoot,
  onReview
}: {
  draft: ReturnType<typeof createActivityUpgradeDraft>;
  rootKind: ActivityUpgradeRootKind;
  rootId: string;
  reviewing: boolean;
  onDraftChange(value: ReturnType<typeof createActivityUpgradeDraft>): void;
  onRootKindChange(value: ActivityUpgradeRootKind): void;
  onRootIdChange(value: string): void;
  onAddRoot(): void;
  onRemoveRoot(kind: ActivityUpgradeRootKind, id: string): void;
  onReview(): void;
}) {
  return <section className="au-selection" aria-labelledby="activity-upgrade-selection">
    <header><span>1</span><div><h2 id="activity-upgrade-selection">Exact replacement and roots</h2><p>Each root is added individually. An immutable root asks the backend to plan a new clone; it is never rewritten.</p></div></header>
    <div className="au-replacement-grid">
      <label><span>Replacement source version</span><input aria-label="Replacement source version" value={draft.fromVersionId} onChange={event => onDraftChange({ ...draft, fromVersionId: event.target.value })} placeholder="Exact current Activity Definition Version ID" /></label>
      <span aria-hidden>→</span>
      <label><span>Replacement target version</span><input aria-label="Replacement target version" value={draft.toVersionId} onChange={event => onDraftChange({ ...draft, toVersionId: event.target.value })} placeholder="Exact target Activity Definition Version ID" /></label>
    </div>
    <div className="au-root-add">
      <label><span>Root kind</span><select value={rootKind} onChange={event => onRootKindChange(event.target.value as ActivityUpgradeRootKind)}>{rootKindOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>
      <label><span>Exact root identity</span><input value={rootId} onChange={event => onRootIdChange(event.target.value)} placeholder="Exact draft or immutable version ID" /></label>
      <button type="button" onClick={onAddRoot} disabled={!rootId.trim()}><Plus size={16} aria-hidden /> Add exact root</button>
    </div>
    {draft.roots.length ? <ul className="au-root-list">
      {draft.roots.map(root => <li key={`${root.kind}:${root.id}`}>
        <div><strong>{root.kind}</strong><code>{root.id}</code><span>{root.kind.endsWith("Version") ? "Immutable root · the plan must clone a draft" : "Mutable exact draft root"}</span></div>
        <button type="button" aria-label={`Remove ${root.kind} ${root.id}`} onClick={() => onRemoveRoot(root.kind, root.id)}><Trash2 size={16} aria-hidden /></button>
      </li>)}
    </ul> : <div className="ad-inline-status" role="status">No exact root selected. Nothing outside this explicit list can enter the plan.</div>}
    <fieldset className="au-options">
      <legend>Authoritative discovery scope</legend>
      <label><input type="checkbox" checked={draft.includeTransitiveDependents} onChange={event => onDraftChange({ ...draft, includeTransitiveDependents: event.target.checked })} /><span><strong>Include transitive dependents</strong><small>The backend still returns the exact closure and order.</small></span></label>
      <label><input type="checkbox" checked={draft.createDraftsForPublishedDependents} onChange={event => onDraftChange({ ...draft, createDraftsForPublishedDependents: event.target.checked })} /><span><strong>Plan clones for immutable dependents</strong><small>Created drafts remain unpublished until separately reviewed.</small></span></label>
    </fieldset>
    <footer><span>Planning is side-effect free. Apply is unavailable until this exact selection receives a backend Plan ID.</span><button type="button" className="ad-primary-action" onClick={onReview} disabled={reviewing}>{reviewing ? "Reviewing authoritative state…" : "Review authoritative plan"}</button></footer>
  </section>;
}

function OutcomeUnknown({
  outcome,
  busy,
  onCheck,
  onRetry
}: {
  outcome: OutcomeState;
  busy: boolean;
  onCheck(): void;
  onRetry(): void;
}) {
  return <section className="au-outcome" role="alert" aria-labelledby="activity-upgrade-outcome">
    <AlertTriangle size={22} aria-hidden />
    <div><h2 id="activity-upgrade-outcome">Outcome unknown</h2><p>{outcome.state === "not-found" ? "Durable status was queried and no receipt is visible. Retrying with the same idempotency key cannot duplicate a completed mutation." : outcome.state === "rejected" ? "The durable receipt rejected the operation. The reviewed evidence remains visible." : "Studio will query the durable receipt before any retry. No second mutation request is sent while status is Preparing or unavailable."}</p><code>{outcome.receiptId}</code></div>
    <div>{outcome.state === "not-found" ? <button type="button" onClick={onRetry}>Retry same idempotent stage</button> : <button type="button" onClick={onCheck} disabled={busy || outcome.state === "rejected"}>{busy ? "Checking durable status…" : "Check durable status"}</button>}</div>
  </section>;
}

function ReceiptHistory({ results, planId }: { results: ActivityUpgradeApplyResult[]; planId: string }) {
  return <section className="au-receipts" aria-labelledby="activity-upgrade-receipts" role="status" aria-live="polite">
    <header><CheckCircle2 size={18} aria-hidden /><div><h2 id="activity-upgrade-receipts">Durable apply receipt{results.length === 1 ? "" : "s"}</h2><p>Success stays in this workbench. Each receipt belongs to one exact atomic stage.</p></div></header>
    <ol>{results.map(result => <li key={result.receiptId ?? `${result.planId}:${result.stageId}`}>
      <div><strong>{String(result.status)}</strong><code>{result.receiptId ?? "Receipt unavailable"}</code></div>
      <span>Plan <code>{result.planId || planId}</code> · stage <code>{result.stageId ?? "Unknown"}</code> · {formatDateTime(result.appliedAt)}</span>
      <span>{result.drafts.length} draft{result.drafts.length === 1 ? "" : "s"} created or updated · no automatic publication</span>
    </li>)}</ol>
  </section>;
}

function PublicationHandoff({
  result,
  planId,
  values,
  refreshing,
  onChange,
  onRefresh
}: {
  result: ActivityUpgradeApplyResult;
  planId: string;
  values: Record<string, string>;
  refreshing: boolean;
  onChange(draftId: string, value: string): void;
  onRefresh(): void;
}) {
  return <section className="au-handoff" aria-labelledby="activity-upgrade-handoff">
    <header><span>Publication handoff</span><h2 id="activity-upgrade-handoff">Awaiting separately reviewed publication</h2><p>Publish each exact child draft in its normal editor. Then enter the actual immutable version returned by publication to create a successor plan.</p></header>
    <div className="ad-inline-status" role="status" aria-live="polite">Publication handoff ready for {result.awaitingPublications.length} exact child draft{result.awaitingPublications.length === 1 ? "" : "s"}. No draft was published automatically.</div>
    <div className="au-handoff-list">{result.awaitingPublications.map(handoff => <HandoffRow key={handoff.draftId} handoff={handoff} planId={planId} value={values[handoff.draftId] ?? ""} onChange={value => onChange(handoff.draftId, value)} />)}</div>
    <footer><span>Refresh never guesses a version or rewrites this predecessor plan.</span><button type="button" className="ad-primary-action" onClick={onRefresh} disabled={refreshing}>{refreshing ? "Creating successor…" : "Refresh successor plan"}</button></footer>
  </section>;
}

function HandoffRow({ handoff, planId, value, onChange }: { handoff: ActivityUpgradePublicationHandoff; planId: string; value: string; onChange(value: string): void }) {
  const href = handoff.draftKind === "ActivityDraft"
    ? `/workflows/activity-definitions?definition=${encodeURIComponent(handoff.definitionId)}&section=editor&draft=${encodeURIComponent(handoff.draftId)}&returnPlan=${encodeURIComponent(planId)}`
    : `/workflows/definitions?definition=${encodeURIComponent(handoff.definitionId)}&draft=${encodeURIComponent(handoff.draftId)}&returnPlan=${encodeURIComponent(planId)}`;
  return <article>
    <div><strong>{handoff.draftKind}</strong><code>{handoff.draftId}</code><span>Revision {handoff.revision} · required by {handoff.requiredByStageIds.join(", ")}</span></div>
    <a href={href}>Open next required draft</a>
    <label><span>Actual published version</span><input aria-label={`Published version for ${handoff.draftId}`} value={value} onChange={event => onChange(event.target.value)} placeholder="Exact immutable version ID" /></label>
  </article>;
}

function readLocationSeed() {
  const parameters = new URLSearchParams(window.location.search);
  return {
    planId: parameters.get("plan"),
    rootKind: parameters.get("rootKind"),
    rootId: parameters.get("rootId"),
    fromVersionId: parameters.get("fromVersion"),
    toVersionId: parameters.get("toVersion")
  };
}

function writePlanLocation(planId: string, mode: "push" | "replace") {
  const parameters = new URLSearchParams(window.location.search);
  parameters.set("plan", planId);
  parameters.delete("rootKind");
  parameters.delete("rootId");
  parameters.delete("fromVersion");
  parameters.delete("toVersion");
  window.history[mode === "push" ? "pushState" : "replaceState"]({}, "", `/workflows/activity-definitions/upgrades?${parameters}`);
}

function readSession(): PersistedUpgradeSession | null {
  try {
    const raw = window.sessionStorage?.getItem(storageKey);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<PersistedUpgradeSession>;
    if (typeof value.planId !== "string" || !Array.isArray(value.receipts) || !value.idempotencyByStage || typeof value.idempotencyByStage !== "object") return null;
    return {
      planId: value.planId,
      receipts: value.receipts.filter((item): item is { planId: string; receiptId: string } =>
        Boolean(item) &&
        typeof item === "object" &&
        typeof (item as { planId?: unknown }).planId === "string" &&
        typeof (item as { receiptId?: unknown }).receiptId === "string"
      ),
      idempotencyByStage: Object.fromEntries(Object.entries(value.idempotencyByStage).filter((entry): entry is [string, string] => typeof entry[1] === "string"))
    };
  } catch {
    return null;
  }
}

function persistSession(planId: string, receipts: Array<{ planId: string; receiptId: string }>, idempotencyByStage: Record<string, string>) {
  try {
    window.sessionStorage?.setItem(storageKey, JSON.stringify({ planId, receipts, idempotencyByStage }));
  } catch {
    // The plan remains recoverable from its URL even when session storage is unavailable.
  }
}

function operationId() {
  return globalThis.crypto.randomUUID?.() ?? `activity-upgrade-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function stageOperationKey(planId: string, stageId: string) {
  return `${planId}\u001f${stageId}`;
}

function replaceResult(current: ActivityUpgradeApplyResult[], result: ActivityUpgradeApplyResult) {
  const identity = result.receiptId ?? `${result.planId}:${result.stageId}`;
  return [...current.filter(item => (item.receiptId ?? `${item.planId}:${item.stageId}`) !== identity), result];
}

function privateSafeReadError(error: unknown) {
  if (error instanceof ApiCapabilityUnavailableError) return capabilityUnavailableMessage;
  if (error instanceof StudioHttpError && [401, 403, 404].includes(error.status)) return "The authoritative plan is unavailable for this account or address. No hidden identity or count is shown.";
  return "The authoritative plan could not be confirmed. No unconfirmed plan is shown.";
}

function privateSafePlanError(error: unknown) {
  if (error instanceof ApiCapabilityUnavailableError) return capabilityUnavailableMessage;
  if (error instanceof StudioHttpError && [401, 403, 404].includes(error.status)) return "Planning is unavailable for this account or exact selection. No hidden identity or count is shown.";
  return privateSafeMutationError(error);
}

function privateSafeMutationError(error: unknown) {
  if (error instanceof ApiCapabilityUnavailableError) return capabilityUnavailableMessage;
  if (error instanceof StudioHttpError) {
    const payload = error.payload as { errorCode?: string } | null;
    if ([401, 403, 404].includes(error.status)) return "The operation is unavailable for this account or exact plan. No hidden identity is shown.";
    if (payload?.errorCode === "activity.upgrade.stale-plan") return "The plan is stale. No partial stage mutation was committed; create or confirm a fresh authoritative plan.";
    if (payload?.errorCode === "activity.upgrade.plan-expired") return "The plan expired before mutation. Review a fresh authoritative plan.";
    if (payload?.errorCode) return `The backend rejected the stage (${payload.errorCode}). The reviewed evidence remains visible.`;
  }
  return "The operation failed before Studio could confirm an authoritative result.";
}

const capabilityUnavailableMessage = "This backend does not advertise staged Activity Definition upgrades. No fallback or unconfirmed plan is used.";

function mayHaveAmbiguousOutcome(error: unknown) {
  return !(error instanceof StudioHttpError) || error.status >= 500;
}

function isStalePlan(error: unknown) {
  if (!(error instanceof StudioHttpError)) return false;
  const payload = error.payload as { errorCode?: string } | null;
  return payload?.errorCode === "activity.upgrade.stale-plan";
}

function upgradeFailureOutcome(error: unknown): NonNullable<ActivityDefinitionsObservation["outcome"]> {
  if (error instanceof ApiCapabilityUnavailableError) return "unavailable";
  if (!(error instanceof StudioHttpError)) return "failed";
  if (error.status === 401 || error.status === 403) return "forbidden";
  if (error.status === 404) return "not-found";
  const payload = error.payload as { errorCode?: string } | null;
  if (payload?.errorCode === "activity.upgrade.stale-plan") return "stale";
  if (payload?.errorCode === "activity.upgrade.plan-expired") return "expired";
  return "failed";
}

function formatDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
