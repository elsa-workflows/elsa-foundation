import { useEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import { AlertTriangle, CheckCircle2, ExternalLink, GitCompareArrows, LocateFixed, RefreshCw, ShieldCheck, X } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { StudioHttpError, type StudioActivityDiagnostic, type StudioActivityDiagnosticFocusResult, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { activityDesignKeys, getActivityDefinition } from "./api/activityDesign";
import { compareSemVer, parseSemVer } from "./semver";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import {
  getActivityPublicationReceipt,
  preflightActivityDraftPublication,
  publishActivityDraft,
  type ActivityPublicationChange,
  type ActivityPublicationPreflight,
  type ActivityPublicationReceipt
} from "./api/publishing";

type ReviewPhase = "idle" | "preparing" | "review" | "publishing" | "success" | "stale" | "unknown" | "failed";

interface ExactDraftBinding {
  revision: number;
  signature: string;
}

export function ActivityDefinitionPublicationReview({
  context,
  definitionId,
  draftId,
  currentRevision,
  currentSignature,
  disabled,
  flushExactSavedRevision,
  onPublicationPending,
  onPublished,
  onFocusDiagnostic,
  onOpenVersion,
  onOpenWorkflows,
  triggerRef,
  mode = "full"
}: {
  context: StudioEndpointContext;
  definitionId: string;
  draftId: string;
  currentRevision: number;
  currentSignature: string;
  disabled: boolean;
  flushExactSavedRevision(): Promise<ExactDraftBinding | null>;
  onPublicationPending(pending: boolean): void;
  onPublished(): void;
  onFocusDiagnostic(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  onOpenVersion(versionId: string): void;
  onOpenWorkflows?: () => void;
  triggerRef?: RefObject<HTMLButtonElement | null>;
  mode?: "full" | "toolbar";
}) {
  const queryClient = useQueryClient();
  const [phase, setPhase] = useState<ReviewPhase>("idle");
  const [preflight, setPreflight] = useState<ActivityPublicationPreflight | null>(null);
  const [binding, setBinding] = useState<ExactDraftBinding | null>(null);
  const [version, setVersion] = useState("");
  const [receipt, setReceipt] = useState<ActivityPublicationReceipt | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [recommendationEvidence, setRecommendationEvidence] = useState<"first" | "unchanged" | "unconfirmed" | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const operationKeyRef = useRef<string | null>(null);
  const reviewedRecommendationRef = useRef<string | null>(null);
  const recommendationVerificationRef = useRef(0);

  useEffect(() => {
    if (!preflight || !binding || phase === "publishing" || phase === "success") return;
    if (binding.revision === currentRevision && binding.signature === currentSignature) return;
    setPreflight(null);
    setBinding(null);
    setPhase("stale");
    setFailure("The draft changed after review. Studio discarded the review without publishing.");
    operationKeyRef.current = null;
  }, [binding, currentRevision, currentSignature, phase, preflight]);

  const prepare = async () => {
    if (disabled || phase === "preparing" || phase === "publishing" || phase === "unknown" || mode === "toolbar" && phase === "success") return;
    setPhase("preparing");
    setFailure(null);
    setReceipt(null);
    setRecommendationEvidence(null);
    recommendationVerificationRef.current += 1;
    setPreflight(null);
    operationKeyRef.current = null;
    try {
      const exact = await flushExactSavedRevision();
      if (!exact) {
        setPhase("failed");
        setFailure("Studio could not confirm an exact saved draft revision. Publication review was not opened.");
        return;
      }
      const definition = await getActivityDefinition(context, definitionId);
      const expectedHead = definition.definition.headVersionId ?? definition.lifecycle.head?.versionId ?? null;
      reviewedRecommendationRef.current = definition.definition.recommendedVersionId ?? definition.lifecycle.recommendation?.versionId ?? null;
      const review = await preflightActivityDraftPublication(context, draftId, exact.revision, expectedHead);
      if (review.draftId !== draftId || review.definitionId !== definitionId || review.draftRevision !== exact.revision) {
        setPhase("failed");
        setFailure("The server returned a publication review for different material. Nothing was published.");
        return;
      }
      setBinding(exact);
      setPreflight(review);
      setVersion(review.minimumVersion);
      setPhase("review");
    } catch (error) {
      setPhase("failed");
      setFailure(publicationFailureMessage(error, "Publication preflight could not be confirmed. Nothing was published."));
    }
  };

  const applyReceipt = (next: ActivityPublicationReceipt) => {
    if (!receiptMatchesReview(next, operationKeyRef.current, preflight, version)) {
      setPhase("unknown");
      setFailure("The authoritative receipt did not match this reviewed operation. Reconcile the existing operation before starting another publication.");
      return;
    }
    setReceipt(next);
    if (next.status === "Applied") {
      if (!outcomeMatchesReview(next.outcome, preflight, version)) {
        setPhase("unknown");
        setFailure("The publication receipt did not contain an exact matching outcome. Reconcile the existing operation before starting another publication.");
        return;
      }
      setPhase("success");
      setFailure(null);
      onPublished();
      onPublicationPending(false);
      // Refresh the definition, drafts, and versions caches so the workbench's published-head and
      // recommendation panels reflect the new immutable version without a manual page reload.
      void queryClient.invalidateQueries({ queryKey: activityDesignKeys.definitionResources });
      void verifyRecommendation(next.outcome.definitionVersionId);
      return;
    }
    if (next.status === "Stale") {
      setPreflight(null);
      setBinding(null);
      setPhase("stale");
      setFailure("The reviewed revision or published head is stale. Studio reopened preflight without publishing.");
      operationKeyRef.current = null;
      onPublicationPending(false);
      return;
    }
    if (next.status === "OutcomeUnknown") {
      setPhase("unknown");
      setFailure("The publication outcome is not yet authoritative. Reconcile status before trying another operation.");
      return;
    }
    if (next.status === "Rejected" || next.status === "Failed") {
      setPhase("failed");
      setFailure(next.status === "Rejected"
        ? "The authoritative publication request was rejected. Review the returned diagnostics before reopening preflight."
        : "The authoritative publication operation failed. No success is assumed.");
      onPublicationPending(false);
      return;
    }
    setPhase("unknown");
    setFailure("The publication receipt is not in a recognized terminal state. Reconcile the existing operation before starting another publication.");
  };

  const verifyRecommendation = async (publishedVersionId: string) => {
    const verification = ++recommendationVerificationRef.current;
    try {
      const definition = await getActivityDefinition(context, definitionId);
      if (verification !== recommendationVerificationRef.current) return;
      const recommendation = definition.definition.recommendedVersionId ?? definition.lifecycle.recommendation?.versionId ?? null;
      setRecommendationEvidence(preflight?.hasBaseline
        ? recommendation === reviewedRecommendationRef.current ? "unchanged" : "unconfirmed"
        : recommendation === publishedVersionId ? "first" : "unconfirmed");
    } catch {
      if (verification !== recommendationVerificationRef.current) return;
      setRecommendationEvidence("unconfirmed");
    }
  };

  const reconcile = async (key = operationKeyRef.current) => {
    if (!key) return;
    try {
      applyReceipt(await getActivityPublicationReceipt(context, key));
    } catch (error) {
      setPhase("unknown");
      setFailure(publicationFailureMessage(error, "The authoritative publication outcome could not be reconciled yet."));
    }
  };

  const publish = async () => {
    if (!preflight || !binding || phase !== "review" || !preflight.isPublishable || !isExactVersionAtLeast(version, preflight.minimumVersion)) return;
    if (binding.revision !== currentRevision || binding.signature !== currentSignature) {
      setPreflight(null);
      setBinding(null);
      setPhase("stale");
      setFailure("The draft changed after review. Studio discarded the review without publishing.");
      operationKeyRef.current = null;
      return;
    }
    const key = operationKeyRef.current ?? createOperationKey();
    operationKeyRef.current = key;
    onPublicationPending(true);
    setPhase("publishing");
    setFailure(null);
    try {
      applyReceipt(await publishActivityDraft(context, draftId, {
        expectedDraftRevision: preflight.draftRevision,
        expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId ?? null,
        version,
        reviewToken: preflight.reviewToken,
        idempotencyKey: key
      }));
    } catch (error) {
      if (isStalePublication(error)) {
        setPreflight(null);
        setBinding(null);
        setPhase("stale");
        setFailure("The reviewed revision or published head is stale. Studio reopened preflight without publishing.");
        operationKeyRef.current = null;
        onPublicationPending(false);
        return;
      }
      await reconcile(key);
    }
  };

  const versionError = preflight && !isExactVersionAtLeast(version, preflight.minimumVersion)
    ? `Enter an exact semantic version at or above ${preflight.minimumVersion}.`
    : null;
  const diagnosticSource = receipt?.diagnostics?.length ? receipt.diagnostics : preflight?.diagnostics ?? [];
  const closeAllowed = phase !== "publishing" && phase !== "unknown";
  const focusDiagnostic = async (diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement) => {
    if (mode === "toolbar") {
      if (closeAllowed) setDialogOpen(false);
      // Let the dialog unmount and restore focus before focusing the editor location.
      await new Promise<void>(resolve => {
        if (typeof window.requestAnimationFrame === "function") window.requestAnimationFrame(() => resolve());
        else window.setTimeout(resolve, 0);
      });
    }
    return onFocusDiagnostic(diagnostic, trigger);
  };

  const reviewDetails = preflight ? <>
      <PublicationSummary preflight={preflight} publishedOutcome={phase === "success" ? receipt?.outcome ?? null : null} compact={mode === "toolbar"} />
      <ImpactReview changes={preflight.impactFirstChanges} diagnostics={preflight.diagnostics} compact={mode === "toolbar"} />
      <ReadinessReview preflight={preflight} compact={mode === "toolbar"} />
      {diagnosticSource.length ? <PublicationDiagnostics diagnostics={diagnosticSource} onFocus={focusDiagnostic} disabled={phase === "publishing" || phase === "unknown"} /> : null}
      <div className="ad-publication-version">
        <label><span>Publication version</span><input value={version} onChange={event => setVersion(event.target.value.trim())} aria-invalid={Boolean(versionError)} disabled={phase === "publishing" || phase === "unknown" || phase === "success"} /></label>
        <div><strong>Minimum valid version: {preflight.minimumVersion}</strong><span>{mode === "toolbar" ? "Suggested version based on these changes. You can choose a higher version." : "The minimum is selected by default. Advanced authors may enter any higher unique exact semantic version."}</span>{preflight.validVersions.length > 1 ? <span>Server suggestions: {preflight.validVersions.join(", ")}</span> : null}</div>
        {versionError ? <p role="alert">{versionError}</p> : null}
      </div>
      <footer className="ad-publication-actions">
        <span><ShieldCheck size={16} aria-hidden /> {mode === "toolbar" ? "Existing workflows keep the activity version they already use." : "Publication is atomic and idempotent for this reviewed revision and head."}</span>
        <button type="button" className="ad-primary-action" onClick={() => void publish()} disabled={disabled || phase !== "review" || !preflight.isPublishable || Boolean(versionError)}>
          {phase === "publishing" ? "Publishing atomically…" : phase === "success" ? `Published ${receipt?.outcome?.version ?? version}` : `Publish ${version || "exact version"}`}
        </button>
      </footer>
    </> : null;

  const reviewPanel = <section className="ad-publication-review" aria-labelledby="activity-publication-title">
    <header className="ad-publication-header">
      <div><span className="ad-kicker">{mode === "toolbar" ? "Publish activity" : "Immutable publication"}</span><h2 id="activity-publication-title">Review &amp; publish</h2><p>{mode === "full" ? "Studio first saves the exact draft, then asks the backend for one authoritative impact and readiness projection." : "Studio saves the current draft, then checks its impact and readiness before publication."}</p></div>
      {mode === "full" ? <button type="button" className="ad-primary-action" onClick={() => void prepare()} disabled={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}>
        <GitCompareArrows size={16} aria-hidden /> {phase === "preparing" ? "Saving & reviewing…" : preflight ? "Reopen preflight" : "Prepare publication"}
      </button> : null}
      {mode === "toolbar" ? <button type="button" className="ad-publication-dialog-close" aria-label="Close publication review" onClick={() => closeAllowed && setDialogOpen(false)} disabled={!closeAllowed}><X size={18} aria-hidden /></button> : null}
    </header>
    {mode === "toolbar" && phase !== "success" ? <div className="ad-publication-prepare-row"><button type="button" className="ad-primary-action" onClick={() => void prepare()} disabled={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}><GitCompareArrows size={16} aria-hidden /> {phase === "preparing" ? "Saving & reviewing…" : preflight ? "Reopen preflight" : "Prepare publication"}</button></div> : null}
    {phase === "idle" ? <p className="ad-publication-empty">No publication review is active. Preparing a review never writes an immutable version.</p> : null}
    {phase === "stale" || phase === "failed" || phase === "unknown" ? <div className="ad-publication-status is-warning" role="alert"><AlertTriangle size={18} aria-hidden /><span>{failure}</span>{phase === "unknown" ? <button type="button" onClick={() => void reconcile()}><RefreshCw size={15} aria-hidden /> Reconcile authoritative status</button> : null}</div> : null}
    {phase === "success" && receipt?.outcome ? <div className="ad-publication-success" role="status">
      <CheckCircle2 size={20} aria-hidden />
      <div><strong>Published immutable version {receipt.outcome.version}</strong><span>{recommendationMessage(recommendationEvidence, mode === "toolbar")}</span></div>
      {onOpenWorkflows ? <button type="button" onClick={onOpenWorkflows}>Open workflows <ExternalLink size={15} aria-hidden /></button> : null}
      <button type="button" onClick={() => onOpenVersion(receipt.outcome!.definitionVersionId)}>Open immutable version <ExternalLink size={15} aria-hidden /></button>
    </div> : null}
    {phase === "success" && mode === "toolbar" ? <details className="ad-publication-technical-details"><summary>Publication review details</summary>{reviewDetails}</details> : reviewDetails}
  </section>;

  if (mode === "full") return reviewPanel;
  return <div className="ad-publication-toolbar">
    <button ref={triggerRef} type="button" className="ad-publication-toolbar-trigger" onClick={() => {
      setDialogOpen(true);
      if (phase === "idle" || phase === "stale" || phase === "failed") void prepare();
    }} disabled={disabled && phase === "idle"} aria-haspopup="dialog" aria-expanded={dialogOpen}>
      <GitCompareArrows size={16} aria-hidden /> {publicationTriggerLabel(phase, Boolean(preflight))}
    </button>
    {phase !== "idle" && !dialogOpen ? <span className="ad-publication-toolbar-status" role="status">{publicationStatusLabel(phase, preflight?.isPublishable)}</span> : null}
    {dialogOpen ? <PublicationDialog canClose={closeAllowed} onClose={() => closeAllowed && setDialogOpen(false)}>{reviewPanel}</PublicationDialog> : null}
  </div>;
}

function PublicationSummary({ preflight, publishedOutcome, compact = false }: { preflight: ActivityPublicationPreflight; publishedOutcome: ActivityPublicationReceipt["outcome"]; compact?: boolean }) {
  if (!compact) return <dl className="ad-publication-summary">
    <div><dt>Exact draft revision</dt><dd>{preflight.draftRevision}</dd></div>
    <div><dt>Published head</dt><dd>{publishedOutcome ? `${publishedOutcome.version} · ${publishedOutcome.definitionVersionId}` : preflight.definitionHeadVersionId ?? "No published head"}</dd></div>
    <div><dt>Baseline</dt><dd>{publishedOutcome ? "This draft is now published" : preflight.hasBaseline ? "Compared with published head" : "First publication · no published baseline exists"}</dd></div>
    <div><dt>Required bump</dt><dd>{preflight.diff?.requiredBump ?? "None"}</dd></div>
  </dl>;
  return <>
    <dl className="ad-publication-summary">
      <div><dt>Version</dt><dd>{publishedOutcome?.version ?? preflight.minimumVersion}</dd></div>
      <div><dt>Readiness</dt><dd>{preflight.isPublishable ? "Ready to publish" : "Blocked"}</dd></div>
      <div><dt>Change</dt><dd>{!preflight.hasBaseline ? "First publication" : preflight.diff?.requiredBump ? `${preflight.diff.requiredBump} version change` : "No version change required"}</dd></div>
      <div><dt>Baseline</dt><dd>{publishedOutcome ? "This draft is now published" : preflight.hasBaseline ? "Compared with published head" : "No published baseline exists"}</dd></div>
    </dl>
    <details className="ad-publication-technical-details">
      <summary>Exact publication identities</summary>
      <dl className="ad-publication-summary">
        <div><dt>Exact draft revision</dt><dd>{preflight.draftRevision}</dd></div>
        <div><dt>Published head</dt><dd>{publishedOutcome ? `${publishedOutcome.version} · ${publishedOutcome.definitionVersionId}` : preflight.definitionHeadVersionId ?? "No published head"}</dd></div>
        <div><dt>Required bump</dt><dd>{preflight.diff?.requiredBump ?? "None"}</dd></div>
      </dl>
    </details>
  </>;
}

function ImpactReview({ changes, diagnostics, compact = false }: { changes: ActivityPublicationChange[]; diagnostics: StudioActivityDiagnostic[]; compact?: boolean }) {
  const groups = useMemo(() => groupChanges(changes), [changes]);
  const warnings = diagnostics.filter(item => item.severity === "Warning");
  return <section className="ad-publication-impact" aria-labelledby="activity-impact-title"><header><h3 id="activity-impact-title">{compact ? "Changes" : "Impact-first comparison"}</h3><p>{compact ? "Review what will change before publishing." : "Impact is shown before contract, provider, dependency, and presentation grouping."}</p></header>
    {!changes.length ? <p className="ad-publication-empty">No behavioral or presentation changes were reported.</p> : null}
    {groups.map(group => <section key={group.impact} className={`ad-impact-group ${impactClass(group.impact)}`}><h4>{compact && group.impact === "NonBehavioral" ? "Non-behavioral changes" : group.label}</h4>{group.areas.map(area => <div key={area.area}><h5>{area.area}</h5><ul>{area.changes.map(change => <li key={change.changeId}><strong>{safeChangeKind(change.kind)}</strong><span>{change.message}</span>{compact ? <details><summary>Show exact values</summary><small>{changeValueSummary(change)}</small></details> : <small>{changeValueSummary(change)}</small>}</li>)}</ul></div>)}</section>)}
    {warnings.length ? <section className="ad-impact-group is-warning"><h4>Warnings</h4><ul>{warnings.map((warning, index) => <li key={`${warning.code}:${index}`}><strong>{warning.code}</strong><span>{warning.message}</span></li>)}</ul></section> : null}
  </section>;
}

function ReadinessReview({ preflight, compact = false }: { preflight: ActivityPublicationPreflight; compact?: boolean }) {
  const readiness = [preflight.provider, ...preflight.storage, ...preflight.runtime];
  return <section className="ad-publication-readiness" aria-labelledby="activity-readiness-title"><header><h3 id="activity-readiness-title">{compact ? "Readiness" : "Dependencies &amp; Runtime readiness"}</h3><span className={preflight.isPublishable ? "is-ready" : "is-blocked"}>{preflight.isPublishable ? "Ready to publish" : "Publication blocked"}</span></header>
    <div className="ad-readiness-grid">{readiness.map((item, index) => <article key={`${item.kind}:${item.key}:${index}`}><strong>{item.kind}</strong>{compact ? <details><summary>Technical details</summary><code>{item.key}{item.schemaVersion ? ` · ${item.schemaVersion}` : ""}</code></details> : <code>{item.key}{item.schemaVersion ? ` · ${item.schemaVersion}` : ""}</code>}<span>{item.status}</span></article>)}</div>
    {preflight.dependencies.length ? <details><summary>{preflight.dependencies.length} exact direct {preflight.dependencies.length === 1 ? "dependency" : "dependencies"}</summary><ul>{preflight.dependencies.map(item => <li key={item.occurrenceId}><strong>{item.version}</strong><code>{item.definitionId} · {item.versionId}</code></li>)}</ul></details> : <p>No direct reusable-activity dependencies were reported.</p>}
  </section>;
}

function PublicationDiagnostics({ diagnostics, onFocus, disabled = false }: { diagnostics: StudioActivityDiagnostic[]; onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>; disabled?: boolean }) {
  return <section className="ad-publication-diagnostics" aria-labelledby="publication-diagnostics-title"><h3 id="publication-diagnostics-title">Publication diagnostics</h3><ol>{diagnostics.map((item, index) => <li key={`${item.code}:${index}`}><div><strong>{item.severity}</strong><code>{safeDiagnosticCode(item.code)}</code></div><p>{item.message}</p>{item.remediation ? <span>{item.remediation}</span> : null}<button type="button" onClick={event => void onFocus(item, event.currentTarget)} disabled={disabled}><LocateFixed size={15} aria-hidden /> Focus location</button></li>)}</ol></section>;
}

function PublicationDialog({ canClose, onClose, children }: { canClose: boolean; onClose(): void; children: ReactNode }) {
  const dialogRef = useRef<HTMLElement | null>(null);
  useDialogFocus(dialogRef, canClose ? onClose : null);
  return <div className="ad-dialog-backdrop ad-publication-dialog-backdrop" role="presentation" onMouseDown={event => {
    if (canClose && event.target === event.currentTarget) onClose();
  }}>
    <section ref={dialogRef} className="ad-dialog ad-publication-dialog" role="dialog" aria-modal="true" aria-labelledby="activity-publication-title" tabIndex={-1}>
      {children}
    </section>
  </div>;
}

function publicationTriggerLabel(phase: ReviewPhase, hasPreflight: boolean) {
  if (phase === "success") return "View publication";
  if (phase === "preparing") return "Review in progress";
  return hasPreflight ? "Continue publication review" : "Review & publish";
}

function publicationStatusLabel(phase: ReviewPhase, publishable?: boolean) {
  if (phase === "success") return "Published";
  if (phase === "review") return publishable ? "Ready to publish" : "Review required";
  if (phase === "preparing") return "Preparing review";
  if (phase === "unknown") return "Outcome needs reconciliation";
  if (phase === "stale") return "Review is out of date";
  if (phase === "failed") return "Review needs attention";
  return "Review required";
}

function groupChanges(changes: ActivityPublicationChange[]) {
  const knownImpacts = ["Breaking", "Additive", "NonBehavioral"];
  const normalized = changes.map(change => ({ ...change, impact: knownImpacts.find(impact => impact.toLowerCase() === change.impact.toLowerCase()) ?? change.impact }));
  const futureImpacts = [...new Set(normalized.map(change => change.impact))]
    .filter(impact => !knownImpacts.includes(impact))
    .sort((left, right) => left.localeCompare(right));
  const impactOrder = [...knownImpacts, ...futureImpacts];
  return impactOrder.flatMap(impact => {
    const matching = normalized.filter(change => change.impact === impact);
    if (!matching.length) return [];
    const areaOrder = ["Contract", "Provider", "Dependency", "Presentation", "Other"];
    const areas = areaOrder.flatMap(area => {
      const areaChanges = matching.filter(change => publicationArea(change.area) === area);
      return areaChanges.length ? [{ area, changes: areaChanges }] : [];
    });
    return [{ impact, label: knownImpacts.includes(impact) ? impact : `Other impact · ${safeImpact(impact)}`, areas }];
  });
}

function publicationArea(area: string) {
  if (["contract", "default", "outcome", "durability"].includes(area.toLowerCase())) return "Contract";
  if (["provider", "implementation"].includes(area.toLowerCase())) return "Provider";
  if (area.toLowerCase() === "dependency") return "Dependency";
  if (area.toLowerCase() === "presentation") return "Presentation";
  return "Other";
}

function safeChangeKind(kind: string) {
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/.test(kind)) return "Provider-specific change";
  return kind.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replaceAll(/[._-]+/g, " ");
}

function changeValueSummary(change: ActivityPublicationChange) {
  const before = safeChangeValue(change, change.before);
  const after = safeChangeValue(change, change.after);
  if (before === null && after === null) return `Required bump: ${change.requiredBump}`;
  return `${before ?? "Not present"} → ${after ?? "Not present"} · required bump ${change.requiredBump}`;
}

export function safeChangeValue(change: ActivityPublicationChange, value: unknown): string | null {
  if (value === undefined || value === null) return null;
  const protectedChange = /secret|protected|credential|password|token/i.test(`${change.kind} ${change.area}`);
  if (isExplicitlyRedacted(value)) return "Protected value redacted";
  if (protectedChange) return "Protected value withheld";
  if (typeof value === "string") {
    return value.length > 80 ? `${value.slice(0, 77)}…` : value;
  }
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return "Structured value changed";
}

function isExplicitlyRedacted(value: unknown) {
  if (typeof value === "string") return /^\[?redacted\]?$/i.test(value.trim());
  if (!value || typeof value !== "object") return false;
  const marker = value as { redacted?: unknown; isRedacted?: unknown; kind?: unknown };
  return marker.redacted === true || marker.isRedacted === true || marker.kind === "Redacted";
}

function safeImpact(impact: string) {
  return /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(impact) ? impact : "provider-specific";
}

function impactClass(impact: string) {
  if (impact === "Breaking") return "is-breaking";
  if (impact === "Additive") return "is-additive";
  if (impact === "NonBehavioral") return "is-nonbehavioral";
  return "is-future";
}

export function isExactVersionAtLeast(value: string, minimum: string) {
  const candidate = parseSemVer(value);
  const floor = parseSemVer(minimum);
  if (!candidate || !floor) return false;
  return compareSemVer(candidate, floor) >= 0;
}

function recommendationMessage(evidence: "first" | "unchanged" | "unconfirmed" | null, plain = false) {
  if (plain) {
    if (evidence === "first") return "The server now recommends this first published version.";
    if (evidence === "unchanged") return "The recommendation stayed unchanged. Publishing did not move it.";
    if (evidence === "unconfirmed") return "The version was published, but Studio could not confirm the current recommendation.";
    return "Checking which version is recommended…";
  }
  if (evidence === "first") return "The first published version became recommended automatically.";
  if (evidence === "unchanged") return "The existing recommended version was not moved.";
  if (evidence === "unconfirmed") return "The immutable version was published, but recommendation state could not be confirmed.";
  return "Confirming the definition's recommendation state…";
}

function isStalePublication(error: unknown) {
  if (!(error instanceof StudioHttpError)) return false;
  const code = (error.payload as { errorCode?: unknown } | null)?.errorCode;
  return code === "activity.publication.review-stale" ||
    code === "activity.draft.stale-revision" ||
    code === "activity.definition.head-stale";
}

function publicationFailureMessage(error: unknown, fallback: string) {
  if (error instanceof StudioHttpError && (error.status === 401 || error.status === 403)) return "Publication is not authorized in this context. No protected publication details were disclosed.";
  if (error instanceof StudioHttpError && error.status === 404) return "The exact authorized publication resource could not be confirmed. No hidden identity was disclosed.";
  return fallback;
}

function safeDiagnosticCode(code: string) {
  return /^[a-z0-9][a-z0-9._-]{0,127}$/.test(code) ? code : "activity.publication.issue";
}

function createOperationKey() {
  const cryptoApi = typeof globalThis.crypto === "undefined"
    ? null
    : globalThis.crypto as Crypto & { randomUUID?: () => string };
  if (cryptoApi?.randomUUID) return cryptoApi.randomUUID();
  if (cryptoApi) {
    const bytes = cryptoApi.getRandomValues(new Uint8Array(16));
    return `activity-publish-${[...bytes].map(value => value.toString(16).padStart(2, "0")).join("")}`;
  }
  throw new Error("Secure operation identity generation is unavailable.");
}

function receiptMatchesReview(
  receipt: ActivityPublicationReceipt,
  idempotencyKey: string | null,
  preflight: ActivityPublicationPreflight | null,
  version: string
) {
  return Boolean(idempotencyKey && preflight &&
    receipt.idempotencyKey === idempotencyKey &&
    receipt.draftId === preflight.draftId &&
    receipt.expectedDraftRevision === preflight.draftRevision &&
    (receipt.expectedDefinitionHeadVersionId ?? null) === (preflight.definitionHeadVersionId ?? null) &&
    receipt.reviewToken === preflight.reviewToken &&
    receipt.requestedVersion === version);
}

function outcomeMatchesReview(
  outcome: ActivityPublicationReceipt["outcome"],
  preflight: ActivityPublicationPreflight | null,
  version: string
): outcome is NonNullable<ActivityPublicationReceipt["outcome"]> {
  return Boolean(outcome && preflight &&
    outcome.definitionId === preflight.definitionId &&
    outcome.draftId === preflight.draftId &&
    outcome.version === version &&
    outcome.definitionVersionId.trim().length > 0);
}
