import { graphPublicationValidationFailure } from "./activityPublicationValidationFailure";
import { AdvancedSection, TechnicalDetails, StudioFactList, StudioButton, StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { useEffect, useId, useMemo, useRef, useState, type RefObject } from "react";
import { AlertTriangle, CheckCircle2, ExternalLink, GitCompareArrows, LocateFixed, RefreshCw, ShieldCheck } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { StudioHttpError, type StudioActivityDiagnostic, type StudioActivityDiagnosticFocusResult, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { activityDesignKeys, getActivityDefinition } from "./api/activityDesign";
import { compareSemVer, parseSemVer } from "./semver";
import { PublishActionButton, type WorkflowPublishMode } from "./workflow-editor/PublishActionButton";
import {
  getActivityPublicationReceipt,
  preflightActivityDraftPublication,
  publishActivityDraft,
  type ActivityPublicationChange,
  type ActivityPublicationOutcome,
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
  openOnMount = false,
  mode = "full",
  titleLevel = "h2"
}: {
  context: StudioEndpointContext;
  definitionId: string;
  draftId: string;
  currentRevision: number;
  currentSignature: string;
  disabled: boolean;
  flushExactSavedRevision(): Promise<ExactDraftBinding | null>;
  onPublicationPending(pending: boolean): void;
  /** Receives the exact applied outcome; existing callers may ignore it. */
  onPublished(outcome: ActivityPublicationOutcome): void;
  onFocusDiagnostic(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  /** Omit to hide the link, for example inside a flow that must not leave the current editor. */
  onOpenVersion?(versionId: string): void;
  onOpenWorkflows?: () => void;
  triggerRef?: RefObject<HTMLButtonElement | null>;
  openOnMount?: boolean;
  mode?: "full" | "toolbar";
  /** The review title's heading level where the review is embedded under another heading. */
  titleLevel?: "h2" | "h3";
}) {
  const queryClient = useQueryClient();
  const blockedReasonId = useId();
  const [phase, setPhase] = useState<ReviewPhase>("idle");
  const [rejectedDiagnostics, setRejectedDiagnostics] = useState<StudioActivityDiagnostic[]>([]);
  const [preflight, setPreflight] = useState<ActivityPublicationPreflight | null>(null);
  const [binding, setBinding] = useState<ExactDraftBinding | null>(null);
  const [version, setVersion] = useState("");
  const [receipt, setReceipt] = useState<ActivityPublicationReceipt | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [recommendationEvidence, setRecommendationEvidence] = useState<"first" | "unchanged" | "unconfirmed" | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const localTriggerRef = useRef<HTMLButtonElement | null>(null);
  const reviewTriggerRef = triggerRef ?? localTriggerRef;
  const directPublishRequestedRef = useRef(false);
  const operationKeyRef = useRef<string | null>(null);
  const reviewedRecommendationRef = useRef<string | null>(null);
  const recommendationVerificationRef = useRef(0);
  const openOnMountHandled = useRef(false);
  const prepareRef = useRef<() => Promise<void>>(async () => {});

  useEffect(() => {
    if ((!preflight && !rejectedDiagnostics.length) || !binding || phase === "publishing" || phase === "success") return;
    if (binding.revision === currentRevision && binding.signature === currentSignature) return;
    setPreflight(null);
    setRejectedDiagnostics([]);
    setBinding(null);
    setPhase("stale");
    setFailure("The draft changed after review. Studio discarded the review without publishing.");
    operationKeyRef.current = null;
  }, [binding, currentRevision, currentSignature, phase, preflight, rejectedDiagnostics]);

  const prepare = async () => {
    if (disabled || phase === "preparing" || phase === "publishing" || phase === "unknown" || mode === "toolbar" && phase === "success") return;
    setPhase("preparing");
    setRejectedDiagnostics([]);
    setFailure(null);
    setReceipt(null);
    setRecommendationEvidence(null);
    recommendationVerificationRef.current += 1;
    setPreflight(null);
    operationKeyRef.current = null;
    let reviewed: ExactDraftBinding | null = null;
    let preflightRequested = false;
    try {
      const exact = await flushExactSavedRevision();
      reviewed = exact;
      if (!exact) {
        setPhase("failed");
        setFailure("Studio could not confirm an exact saved draft revision. Publication review was not opened.");
        return;
      }
      setBinding(exact);
      const definition = await getActivityDefinition(context, definitionId);
      const expectedHead = definition.definition.headVersionId ?? definition.lifecycle.head?.versionId ?? null;
      reviewedRecommendationRef.current = definition.definition.recommendedVersionId ?? definition.lifecycle.recommendation?.versionId ?? null;
      preflightRequested = true;
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
      const diagnostics = reviewed && preflightRequested ? graphPublicationValidationFailure(error, draftId, reviewed.revision) : null;
      setRejectedDiagnostics(diagnostics ?? []);
      setFailure(diagnostics ? null : publicationFailureMessage(error, "Publication preflight could not be confirmed. Nothing was published."));
    }
  };
  prepareRef.current = prepare;

  useEffect(() => {
    if (!openOnMount) return;
    setDialogOpen(true);
    if (disabled || openOnMountHandled.current || phase !== "idle") return;
    openOnMountHandled.current = true;
    void prepareRef.current();
  }, [disabled, openOnMount, phase]);

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
      onPublished(next.outcome);
      if (mode === "toolbar") setDialogOpen(false);
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
      setFailure("The reviewed revision or latest published version is stale. Studio reopened preflight without publishing.");
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
        setFailure("The reviewed revision or latest published version is stale. Studio reopened preflight without publishing.");
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
  const diagnosticSource = uniquePublicationDiagnostics(receipt?.diagnostics?.length ? receipt.diagnostics : preflight?.diagnostics ?? rejectedDiagnostics);
  useEffect(() => {
    if (!directPublishRequestedRef.current || phase === "preparing") return;
    directPublishRequestedRef.current = false;
    const exactBindingIsCurrent = binding?.revision === currentRevision && binding.signature === currentSignature;
    if (phase === "review" && preflight?.isPublishable && exactBindingIsCurrent && diagnosticSource.length === 0 && !versionError) {
      void publish();
      return;
    }
    setDialogOpen(true);
  }, [binding, currentRevision, currentSignature, diagnosticSource.length, phase, preflight, versionError]);
  const hasBlockingDiagnostics = diagnosticSource.some(item => diagnosticSeverity(item.severity) === "Error");
  const diagnosticCounts = ["Error", "Warning", "Info"].flatMap(severity => {
    const count = diagnosticSource.filter(item => diagnosticSeverity(item.severity) === severity).length;
    return count ? [`${count} ${severity.toLowerCase()}${count === 1 || severity === "Info" ? "" : "s"}`] : [];
  }).join(" · ");
  const publicationBlocked = Boolean(rejectedDiagnostics.length || preflight && phase !== "success" && (!preflight.isPublishable || phase === "failed" && (receipt?.status === "Rejected" || receipt?.status === "Failed")));
  const needsAttention = phase === "failed" || phase === "stale" || phase === "unknown" || publicationBlocked || hasBlockingDiagnostics;
  const actionableStatus = needsAttention || phase === "review" && diagnosticSource.length > 0;
  const attentionTitle = publicationBlocked && hasBlockingDiagnostics ? "Publication blocked" : phase === "failed" ? preflight ? "Publication did not complete" : "Review could not be prepared"
    : publicationBlocked ? "Publication blocked" : publicationStatusLabel(phase, preflight?.isPublishable);
  const closeAllowed = phase !== "publishing" && phase !== "unknown";
  const reviewPanelRef = useRef<HTMLElement>(null);
  useEffect(() => {
    if (mode === "toolbar" && dialogOpen && !closeAllowed) reviewPanelRef.current?.focus();
  }, [closeAllowed, dialogOpen, mode]);
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

  const publishFromToolbar = (selectedMode: WorkflowPublishMode) => {
    if (selectedMode === "review") {
      setDialogOpen(true);
      if (phase === "idle") void prepare();
      return;
    }

    const exactBindingIsCurrent = binding?.revision === currentRevision && binding.signature === currentSignature;
    if (phase === "review" && preflight?.isPublishable && exactBindingIsCurrent && diagnosticSource.length === 0 && !versionError) {
      void publish();
      return;
    }
    if (phase === "idle") {
      directPublishRequestedRef.current = true;
      void prepare();
      return;
    }
    setDialogOpen(true);
  };

  const blockerPanel = publicationBlocked ? <div className="ad-publication-blockers" id={blockedReasonId} role="alert">
        <h3>Resolve these issues before publishing</h3>
        <p>{hasBlockingDiagnostics
          ? "Publication is blocked by the issues below. Correct the draft, then check again."
          : phase === "failed"
          ? "Publication did not complete. Review the notes below, correct the draft or resolve the service problem, then check again."
          : "The activity is not ready to publish. Review the readiness checks below or ask an administrator to resolve unavailable services, then check again."}</p>
        {diagnosticSource.length ? <PublicationDiagnostics diagnostics={diagnosticSource} onFocus={focusDiagnostic} embedded disabled={phase === "publishing" || phase === "unknown"} /> : null}
        <div className="ad-publication-blocker-actions">
          {mode === "toolbar" ? <StudioButton onClick={() => setDialogOpen(false)}>Return to editor</StudioButton> : null}
          <StudioButton onClick={() => void prepare()} disabled={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}><RefreshCw size={15} aria-hidden /> Check again</StudioButton>
        </div>
      </div> : null;

  const selectedVersion = phase === "success" ? receipt?.outcome?.version ?? version : version;
  const reviewDetails = preflight ? <>
      {blockerPanel}
      <PublicationSummary preflight={preflight} blocked={publicationBlocked} publishedOutcome={phase === "success" ? receipt?.outcome ?? null : null} version={selectedVersion} compact={mode === "toolbar"} />
      {mode === "toolbar" ? <>
        {!preflight.hasBaseline ? <>
          <p>{phase === "success" ? "First version published. Existing workflows are unaffected." : "Publish the first version for use in workflows."}</p>
          <AdvancedSection label="Review initial activity contents">
            <ImpactReview changes={preflight.impactFirstChanges} compact />
          </AdvancedSection>
        </> : <AdvancedSection label="Changes">
          <ImpactReview changes={preflight.impactFirstChanges} compact />
        </AdvancedSection>}
        {(preflight.isPublishable || hasBlockingDiagnostics) ? <TechnicalDetails label="Technical readiness checks">
          <ReadinessReview preflight={preflight} blocked={publicationBlocked} previous={phase === "failed" && Boolean(receipt)} compact />
        </TechnicalDetails> : <ReadinessReview preflight={preflight} blocked={publicationBlocked} previous={phase === "failed" && Boolean(receipt)} compact />}
        {!publicationBlocked && diagnosticSource.length ? <PublicationDiagnostics diagnostics={diagnosticSource} onFocus={focusDiagnostic} disabled={phase === "publishing" || phase === "unknown"} /> : null}
        {!publicationBlocked ? <AdvancedSection label="Version">
          <div className="ad-publication-version">
            <StudioField label={<><span>Publication version</span></>}><input value={version} onChange={event => setVersion(event.target.value.trim())} aria-invalid={Boolean(versionError)} disabled={phase === "publishing" || phase === "unknown" || phase === "success"} /></StudioField>
            <div><strong>Minimum valid version: {preflight.minimumVersion}</strong><span>Suggested version based on these changes. You can choose a higher version.</span>{preflight.validVersions.length > 1 ? <span>Server suggestions: {preflight.validVersions.join(", ")}</span> : null}</div>
            {versionError ? <p role="alert">{versionError}</p> : null}
          </div>
        </AdvancedSection> : null}
      </> : <>
        <ImpactReview changes={preflight.impactFirstChanges} compact={false} />
        <ReadinessReview preflight={preflight} blocked={publicationBlocked} previous={phase === "failed" && Boolean(receipt)} compact={false} />
        {!publicationBlocked && diagnosticSource.length ? <PublicationDiagnostics diagnostics={diagnosticSource} onFocus={focusDiagnostic} disabled={phase === "publishing" || phase === "unknown"} /> : null}
        {!publicationBlocked ? <div className="ad-publication-version">
          <StudioField label={<><span>Publication version</span></>}><input value={version} onChange={event => setVersion(event.target.value.trim())} aria-invalid={Boolean(versionError)} disabled={phase === "publishing" || phase === "unknown" || phase === "success"} /></StudioField>
          <div><strong>Minimum valid version: {preflight.minimumVersion}</strong><span>The minimum is selected by default. Advanced authors may enter any higher unique exact semantic version.</span>{preflight.validVersions.length > 1 ? <span>Server suggestions: {preflight.validVersions.join(", ")}</span> : null}</div>
          {versionError ? <p role="alert">{versionError}</p> : null}
        </div> : null}
      </>}
      {mode === "full" ? <footer className="ad-publication-actions">
        <span><ShieldCheck size={16} aria-hidden /> Publication is atomic and idempotent for this reviewed revision and head.</span>
        <StudioButton variant="primary" aria-describedby={publicationBlocked ? blockedReasonId : undefined} onClick={() => void publish()} disabled={disabled || phase !== "review" || !preflight.isPublishable || Boolean(versionError)}>
          {phase === "publishing" ? "Publishing atomically…" : phase === "success" ? `Published ${selectedVersion}` : `Publish ${selectedVersion || "exact version"}`}
        </StudioButton>
      </footer> : null}
    </> : null;

  const reviewPanel = <section ref={reviewPanelRef} tabIndex={mode === "toolbar" ? -1 : undefined} className="ad-publication-review" aria-labelledby={mode === "full" ? "activity-publication-title" : undefined}>
    {mode === "full" ? <header className="ad-publication-header">
      <div>{titleLevel === "h3" ? <h3 id="activity-publication-title">Review &amp; publish</h3> : <h2 id="activity-publication-title">Review &amp; publish</h2>}<p>Studio first saves the exact draft, then asks the backend for one authoritative impact and readiness projection.</p></div>
      <StudioButton variant="primary" onClick={() => void prepare()} disabled={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}>
        <GitCompareArrows size={16} aria-hidden /> {phase === "preparing" ? "Saving & reviewing…" : preflight ? "Reopen preflight" : "Prepare publication"}
      </StudioButton>
    </header> : null}
    {mode === "toolbar" && phase !== "success" && !publicationBlocked ? <div className="ad-publication-prepare-row"><StudioButton onClick={() => void prepare()} disabled={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}><GitCompareArrows size={16} aria-hidden /> {phase === "preparing" ? "Saving & reviewing…" : preflight ? "Refresh review" : "Prepare publication"}</StudioButton></div> : null}
    {phase === "idle" ? <p className="ad-publication-empty">No publication review is active. Preparing a review never writes an immutable version.</p> : null}
    {!preflight ? blockerPanel : null}
    {failure && (phase === "stale" || phase === "failed" || phase === "unknown") ? <div className="ad-publication-status is-warning" role="alert"><AlertTriangle size={18} aria-hidden /><span>{failure}</span>{phase === "unknown" ? <StudioButton onClick={() => void reconcile()}><RefreshCw size={15} aria-hidden /> Reconcile authoritative status</StudioButton> : null}</div> : null}
    {phase === "success" && receipt?.outcome ? <div className="ad-publication-success" role="status">
      <CheckCircle2 size={20} aria-hidden />
      <div><strong>Published immutable version {receipt.outcome.version}</strong><span>{recommendationMessage(recommendationEvidence, mode === "toolbar")}</span></div>
      {onOpenWorkflows ? <StudioButton onClick={onOpenWorkflows}>Open workflows <ExternalLink size={15} aria-hidden /></StudioButton> : null}
      {onOpenVersion ? <StudioButton onClick={() => onOpenVersion(receipt.outcome!.definitionVersionId)}>Open immutable version <ExternalLink size={15} aria-hidden /></StudioButton> : null}
    </div> : null}
    {phase === "success" && mode === "toolbar" ? <TechnicalDetails label="Publication review details">{reviewDetails}</TechnicalDetails> : reviewDetails}
  </section>;

  if (mode === "full") return reviewPanel;
  if (phase === "success") return null;
  return <div className="ad-publication-toolbar">
    <PublishActionButton
      ariaLabel="Activity Definition publication actions"
      preferenceKey="elsa-studio-activity-definition-publish-mode"
      primaryButtonRef={reviewTriggerRef}
      busy={disabled || phase === "preparing" || phase === "publishing" || phase === "unknown"}
      saving={phase === "preparing"}
      unavailableReason={disabled ? "This Activity Definition cannot be published in its current state." : undefined}
      onPublish={publishFromToolbar}
    />
    {phase !== "idle" && !dialogOpen ? actionableStatus ? <div className={`ad-publication-toolbar-status${needsAttention ? " is-attention" : ""}`} role={needsAttention ? "alert" : "status"}>
      <AlertTriangle size={14} aria-hidden />
      <div><strong>{attentionTitle}{diagnosticCounts ? ` · ${diagnosticCounts}` : ""}</strong>{failure ? <p>{failure}</p> : null}</div>
      <StudioButton variant="ghost" size="sm" className="ad-draft-diagnostics-link" onClick={() => setDialogOpen(true)} aria-haspopup="dialog">{diagnosticSource.length ? "View issues" : "View details"}</StudioButton>
    </div> : <span className="ad-publication-toolbar-status" role="status">{publicationStatusLabel(phase, preflight?.isPublishable)}</span> : null}
    {dialogOpen ? <StudioDialog open title="Review & publish" className="ad-publication-dialog"
      description="Studio saves the current draft, then checks its impact and readiness before publication."
      closeLabel="Close publication review" dismissDisabled={!closeAllowed} onClose={() => setDialogOpen(false)}
      onCloseAutoFocus={event => { event.preventDefault(); reviewTriggerRef.current?.focus(); }}
      footer={<StudioDialogFooter confirmType="button" onCancel={() => setDialogOpen(false)} cancelDisabled={!closeAllowed}
        cancelLabel="Cancel"
        confirmLabel={preflight ? phase === "publishing" ? "Publishing atomically…" : `Publish ${selectedVersion || "exact version"}` : undefined}
        confirmDisabled={disabled || phase !== "review" || !preflight?.isPublishable || Boolean(versionError)}
        confirmProps={{ "aria-describedby": publicationBlocked ? blockedReasonId : undefined }} onConfirm={() => void publish()} />}
    >{reviewPanel}{preflight ? <p>Existing workflows keep the activity version they already use.</p> : null}</StudioDialog> : null}
  </div>;
}

function PublicationSummary({ preflight, publishedOutcome, version, compact = false, blocked = false }: { preflight: ActivityPublicationPreflight; blocked?: boolean; publishedOutcome: ActivityPublicationReceipt["outcome"]; version: string; compact?: boolean }) {
  if (!compact) return <StudioFactList>
    <div><dt>Exact draft revision</dt><dd>{preflight.draftRevision}</dd></div>
    <div><dt>Latest published version</dt><dd>{publishedOutcome ? `${publishedOutcome.version} · ${publishedOutcome.definitionVersionId}` : preflight.definitionHeadVersionId ?? "No latest published version"}</dd></div>
    <div><dt>Baseline</dt><dd>{publishedOutcome ? "This draft is now published" : preflight.hasBaseline ? "Compared with latest published version" : "First publication · no published baseline exists"}</dd></div>
    <div><dt>Required bump</dt><dd>{preflight.diff?.requiredBump ?? "None"}</dd></div>
  </StudioFactList>;
  return <>
    <StudioFactList>
      <div><dt>Version</dt><dd>{version || "Not selected"}</dd></div>
      <div><dt>Readiness</dt><dd>{preflight.isPublishable && !blocked ? "Ready to publish" : "Blocked"}</dd></div>
      <div><dt>Change</dt><dd>{!preflight.hasBaseline ? "First publication" : preflight.diff?.requiredBump ? `${preflight.diff.requiredBump} version change` : "No version change required"}</dd></div>
      <div><dt>Baseline</dt><dd>{publishedOutcome ? "This draft is now published" : preflight.hasBaseline ? "Compared with latest published version" : "No published baseline exists"}</dd></div>
    </StudioFactList>
    <TechnicalDetails label="Exact publication identities">
      <StudioFactList>
        <div><dt>Exact draft revision</dt><dd>{preflight.draftRevision}</dd></div>
        <div><dt>Latest published version</dt><dd>{publishedOutcome ? `${publishedOutcome.version} · ${publishedOutcome.definitionVersionId}` : preflight.definitionHeadVersionId ?? "No latest published version"}</dd></div>
        <div><dt>Required bump</dt><dd>{preflight.diff?.requiredBump ?? "None"}</dd></div>
      </StudioFactList>
    </TechnicalDetails>
  </>;
}

function ImpactReview({ changes, compact = false }: { changes: ActivityPublicationChange[]; compact?: boolean }) {
  const groups = useMemo(() => groupChanges(changes), [changes]);
  return <section className="ad-publication-impact" aria-labelledby="activity-impact-title"><header><h3 id="activity-impact-title">{compact ? "Changes" : "Impact-first comparison"}</h3><p>{compact ? "Review what will change before publishing." : "Impact is shown before contract, provider, dependency, and presentation grouping."}</p></header>
    {!changes.length ? <p className="ad-publication-empty">No behavioral or presentation changes were reported.</p> : null}
    {groups.map(group => <section key={group.impact} className={`ad-impact-group ${impactClass(group.impact)}`}><h4>{compact && group.impact === "NonBehavioral" ? "Non-behavioral changes" : group.label}</h4>{group.areas.map(area => <div key={area.area}><h5>{area.area}</h5><ul>{area.changes.map(change => <li key={change.changeId}><strong>{safeChangeKind(change.kind)}</strong><span>{change.message}</span>{compact ? <TechnicalDetails label="Show exact values"><small>{changeValueSummary(change)}</small></TechnicalDetails> : <small>{changeValueSummary(change)}</small>}</li>)}</ul></div>)}</section>)}

  </section>;
}

function ReadinessReview({ preflight, compact = false, blocked = false, previous = false }: { preflight: ActivityPublicationPreflight; compact?: boolean; blocked?: boolean; previous?: boolean }) {
  const readiness = [preflight.provider, ...preflight.storage, ...preflight.runtime];
  return <section className="ad-publication-readiness" aria-labelledby="activity-readiness-title"><header><h3 id="activity-readiness-title">{previous ? "Checks from the earlier review" : compact ? "Readiness" : "Dependencies &amp; Runtime readiness"}</h3><span className={preflight.isPublishable && !blocked ? "is-ready" : "is-blocked"}>{previous ? "Recheck after correcting the issues above" : preflight.isPublishable && !blocked ? "Ready to publish" : "Publication blocked"}</span></header>
    <div className="ad-readiness-grid">{readiness.map((item, index) => <article key={`${item.kind}:${item.key}:${index}`}><strong>{item.kind}</strong>{compact ? <TechnicalDetails><code>{item.key}{item.schemaVersion ? ` · ${item.schemaVersion}` : ""}</code></TechnicalDetails> : <code>{item.key}{item.schemaVersion ? ` · ${item.schemaVersion}` : ""}</code>}<span>{item.status}</span></article>)}</div>
    {preflight.dependencies.length ? <TechnicalDetails label={<>{preflight.dependencies.length} exact direct {preflight.dependencies.length === 1 ? "dependency" : "dependencies"}</>}><ul>{preflight.dependencies.map(item => <li key={item.occurrenceId}><strong>{item.version}</strong><code>{item.definitionId} · {item.versionId}</code></li>)}</ul></TechnicalDetails> : <p>No direct reusable-activity dependencies were reported.</p>}
  </section>;
}

function PublicationDiagnostics({ diagnostics, onFocus, disabled = false, embedded = false }: { diagnostics: StudioActivityDiagnostic[]; embedded?: boolean; onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>; disabled?: boolean }) {
  return <section className="ad-publication-diagnostics" aria-label={embedded ? "Publication issues" : undefined} aria-labelledby={embedded ? undefined : "publication-diagnostics-title"}>{!embedded ? <h3 id="publication-diagnostics-title">{diagnostics.some(item => diagnosticSeverity(item.severity) === "Error") ? "What needs to change" : "Publication notes"}</h3> : null}<ol>{diagnostics.map((item, index) => {
    const focusable = Boolean(item.location?.jsonPointer || item.location?.referenceKey || item.location?.nodeOrigin?.length);
    const action = item.metadata?.actionLabel?.trim();
    return <li key={`${item.code}:${index}`}>
      <strong>{item.message}</strong>
      {item.remediation ? <p>{item.remediation}</p> : null}
      {focusable ? <StudioButton onClick={event => void onFocus(item, event.currentTarget)} disabled={disabled}><LocateFixed size={15} aria-hidden /> {action && action.length <= 80 ? action : "Go to setting"}</StudioButton> : null}
      <TechnicalDetails label="Diagnostic details"><span>{diagnosticSeverity(item.severity)}</span><code>{safeDiagnosticCode(item.code)}</code></TechnicalDetails>
    </li>;
  })}</ol></section>;
}

function diagnosticSeverity(severity: string) {
  const normalized = severity.toLowerCase();
  return normalized === "warning" ? "Warning" : normalized === "info" ? "Info" : "Error";
}

function uniquePublicationDiagnostics(diagnostics: StudioActivityDiagnostic[]) {
  const seen = new Set<string>();
  return diagnostics.filter(item => {
    const key = JSON.stringify([item.code, diagnosticSeverity(item.severity), item.message, item.remediation ?? null, item.subject, item.location ?? null, item.metadata?.actionLabel ?? null]);
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  }).sort((left, right) => ["Error", "Warning", "Info"].indexOf(diagnosticSeverity(left.severity)) - ["Error", "Warning", "Info"].indexOf(diagnosticSeverity(right.severity)));
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
  return /^[a-z0-9][a-z0-9._:-]{0,127}$/.test(code) ? code : "activity.publication.issue";
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
