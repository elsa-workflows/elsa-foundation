import { useCallback, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft, WorkflowExecutionInputs, WorkflowTestRunStart, WorkflowTestRunView } from "../workflowTypes";
import {
  getWorkflowDefinitionVersion,
  getWorkflowPromotionVersionCapabilities,
  preflightDraftPromotion,
  promoteDraft
} from "../api/workflowDesign";
import {
  getPublicationPolicy,
  isPublishingActivation,
  listPublicationSlots,
  preflightPublicationSnapshot,
  publishVersion,
  startWorkflowDraftTestRun,
  type PublicationIntent
} from "../api/publishing";
import { getDraftValidations } from "../api/draftValidations";
import { decorateConversionDiagnostic } from "../conversionSettings";
import { buildExportPayload, downloadWorkflowJson } from "../workflowSerialization";
import {
  createDraftSnapshotId,
  describeWorkflowError,
  extractExpressionValidationDiagnosticMessages,
  getDraftSignature,
  isRejectedTestRun,
  readWorkflowErrorPayload
} from "./editorHelpers";
import type { WorkflowEditorOperation, WorkflowErrorInput, WorkflowTestRunState } from "./editorTypes";
import {
  anonymousArtifactFileName,
  describeExecutableArtifactExportFailure,
  downloadExecutableArtifact,
  publishFirstMessage,
  type PublishedExecutableTarget
} from "./useExecutableArtifactExport";
import {
  createPublicationReview,
  publicationBlockedMessage,
  publicationIntentFor,
  publicationPreflightMatchesIntent,
  splitPublicationValidations,
  withPublicationWarnings,
  type PublicationReviewState,
  type PublicationVersionSelection
} from "./publicationReview";

interface WorkflowOperationsParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  details: WorkflowDefinitionDetails | null;
  catalog: ActivityCatalogItem[];
  busy: boolean;
  /**
   * The published version whose compiled artifact can be exported (foundation #1304), or null when the
   * workflow has no published version or the server does not advertise the export relation.
   */
  publishedExecutable?: PublishedExecutableTarget | null;
  saveDraft(draft: WorkflowDraft, savedStatus: string): Promise<WorkflowDraft>;
  flushPendingSave(): Promise<void>;
  reload(options?: { shouldLoadDraft(): boolean }): Promise<void>;
  startTestRun(testRun: WorkflowTestRunState): void;
  setPublishedArtifact(id: string | null): void;
  setOperation(operation: WorkflowEditorOperation): void;
  setStatus(value: string): void;
  setError(value: WorkflowErrorInput): void;
  setActiveRightPanelId(id: string): void;
  setInspectorCollapsed(collapsed: boolean): void;
  setAutosavePaused(paused: boolean): void;
}

// The editor's async command handlers: export JSON, save, promote+publish, and dispatch a transient test
// run. Each guards on `busy`, drives the shared `operation` flag, and reports progress via status/error.
export function useWorkflowOperations({
  context,
  draft,
  details,
  catalog,
  busy,
  publishedExecutable = null,
  saveDraft,
  flushPendingSave,
  reload,
  startTestRun,
  setPublishedArtifact,
  setOperation,
  setStatus,
  setError,
  setActiveRightPanelId,
  setInspectorCollapsed,
  setAutosavePaused
}: WorkflowOperationsParams) {
  const [publicationReview, setPublicationReview] = useState<PublicationReviewState | null>(null);
  const latestDraftRef = useRef(draft);
  latestDraftRef.current = draft;
  const publicationReviewRequest = useRef(0);
  const publicationMutationInFlight = useRef(false);
  const publicationPreparationInFlight = useRef(false);
  const [quickPublishing, setQuickPublishing] = useState(false);
  const [expressionValidationWarning, setExpressionValidationWarning] = useState<{
    draft: WorkflowDraft;
    inputs: WorkflowExecutionInputs;
    start: WorkflowTestRunStart;
    startLabel: string;
    workflowInputNames: string[];
    onStarted?: (view: WorkflowTestRunView) => Promise<void>;
  } | null>(null);
  const exportJson = useCallback(() => {
    if (!draft) return;
    const name = details?.definition.name;
    downloadWorkflowJson(buildExportPayload(draft, name), name);
    setStatus("Exported workflow as JSON.");
  }, [draft, details, setStatus]);

  // Distinct from `exportJson` above: that serializes the design draft, this downloads the *compiled*
  // artifact closure the server produced for the published version (foundation #1304). The payload is
  // saved exactly as it arrived — Studio never assembles or reshapes a closure (FR-C-004).
  const exportExecutableArtifact = useCallback(async () => {
    if (busy) return;
    if (!publishedExecutable) {
      setError({ message: publishFirstMessage });
      return;
    }
    setOperation("exportingArtifact");
    setStatus("Exporting executable artifact...");
    setError("");
    try {
      const fileName = await downloadExecutableArtifact(context, publishedExecutable);
      // Both name segments come from the root artifact's identity; the double fallback means it carried
      // none, which is worth saying rather than shipping an anonymous file as if it were normal.
      setStatus(fileName === anonymousArtifactFileName
        ? `Exported ${fileName} — the artifact carried no definition id or version.`
        : `Exported executable artifact as ${fileName}.`);
    } catch (e) {
      setStatus("");
      setError(describeExecutableArtifactExportFailure(e));
    } finally {
      setOperation("idle");
    }
  }, [busy, context, publishedExecutable, setError, setOperation, setStatus]);

  const save = useCallback(async () => {
    if (!draft || busy) return;
    setOperation("saving");
    setStatus("Saving...");
    try {
      await saveDraft(draft, "Saved");
    } catch {
      // saveDraft surfaces the error in the editor alert.
    } finally {
      setOperation("idle");
    }
  }, [draft, busy, saveDraft, setOperation, setStatus]);

  const reviewPublication = useCallback(async (
    review: PublicationReviewState,
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection
  ) => {
    const request = ++publicationReviewRequest.current;
    setPublicationReview(current => current ? {
      ...current,
      phase: "review",
      intent,
      versionSelection,
      reviewPending: true,
      reviewFailed: false,
      preflight: undefined,
      versionPreflight: undefined,
      failureMessage: undefined
    } : current);
    setError("");
    try {
      const requestedVersion = versionSelection.mode === "exact"
        ? versionSelection.requestedVersion.trim()
        : undefined;
      const [preflight, versionPreflight] = await Promise.all([
        preflightSnapshot(context, review, intent),
        review.versionPreflightSupported
          ? preflightDraftPromotion(context, review.draftSnapshot.id, requestedVersion)
          : Promise.resolve(undefined)
      ]);
      if (request !== publicationReviewRequest.current) return;
      setPublicationReview(current => current ? {
        ...current,
        phase: "review",
        intent,
        versionSelection,
        reviewPending: false,
        reviewFailed: false,
        preflight,
        versionPreflight,
        proposedVersion: versionPreflight?.resolvedVersion ?? current.proposedVersion,
        failureMessage: undefined
      } : current);
    } catch (error) {
      if (request !== publicationReviewRequest.current) return;
      const reason = decorateConversionDiagnostic(error instanceof Error ? error.message : String(error));
      setPublicationReview(current => current ? {
        ...current,
        phase: "review",
        intent,
        versionSelection,
        reviewPending: false,
        reviewFailed: true,
        preflight: undefined,
        versionPreflight: undefined,
        failureMessage: `Authoritative review failed. No changes were saved, promoted, or published. ${reason}`
      } : current);
      setError(`Could not prepare an authoritative publication review. No changes were saved, promoted, or published. ${reason}`);
    }
  }, [context, setError]);

  const executePublication = useCallback(async (
    publicationReview: PublicationReviewState,
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection,
    direct = false
  ) => {
    if (!publicationReview || publicationReview.phase === "success") return;
    if (intent.action === "sideBySide" && !intent.slotName?.trim()) {
      setError("Enter a meaningful slot name for side-by-side publication.");
      return;
    }
    if (intent.action === "sideBySide" && intent.slotName?.trim().toLowerCase() === "default") {
      setError("The default slot is reserved for replacement publication. Choose another named slot.");
      return;
    }
    if (publicationReview.validationErrors.length) {
      setPublicationReview(current => current ? { ...current, phase: "validationBlocked", intent } : current);
      setError("Resolve validation errors before publishing. No changes were saved or promoted.");
      return;
    }
    const reviewedPreflight = publicationReview.preflight;
    const versionMatches = publicationReview.versionSelection.mode === versionSelection.mode
      && (versionSelection.mode === "automatic"
        || publicationReview.versionSelection.mode === "exact"
        && publicationReview.versionSelection.requestedVersion.trim() === versionSelection.requestedVersion.trim());
    if (publicationReview.reviewPending
      || !publicationPreflightMatchesIntent(reviewedPreflight, intent)
      || !versionMatches) {
      await reviewPublication(publicationReview, intent, versionSelection);
      return;
    }
    if (!reviewedPreflight.canActivate) {
      setError(publicationBlockedMessage(reviewedPreflight));
      return;
    }
    if (publicationReview.versionPreflight && !publicationReview.versionPreflight.isReady) {
      setError("The selected version is not ready for promotion. Resolve the version issue before publishing.");
      return;
    }
    if (publicationMutationInFlight.current) return;
    publicationMutationInFlight.current = true;
    setOperation("publishing");
    setError("");
    let promotedVersionId = publicationReview.promotedVersionId;
    let savedDraft = publicationReview.savedDraft;
    try {
      if (!promotedVersionId) {
        if (!savedDraft) {
          setPublicationReview(current => current ? { ...current, phase: "publishing", progressStep: "saving", intent } : current);
          setStatus("Saving publication snapshot...");
          const justSaved = await saveDraft(publicationReview.draftSnapshot, "Publication snapshot saved");
          savedDraft = justSaved;
          setPublicationReview(current => current ? { ...current, savedDraft: justSaved, draftSnapshot: justSaved, intent } : current);
        }
        const persistedDraft = savedDraft;
        // Only errors block; a warning on the saved draft is listed in the review and publication goes on (spec 261).
        const validationErrors = splitPublicationValidations(persistedDraft.validationErrors).errors;
        if (!persistedDraft.state.rootActivity) validationErrors.unshift("Workflow has no root activity.");
        if (validationErrors.length) {
          setPublicationReview(current => current ? {
            ...current,
            phase: "savedFailure",
            progressStep: undefined,
            validationErrors,
            executableStatus: "blocked",
            savedDraft: persistedDraft,
            draftSnapshot: persistedDraft,
            failureMessage: "The reviewed draft was saved, but validation blocked promotion. No version or publication was created. Close the review, correct the validation errors, and try again.",
            intent
          } : current);
          setStatus("");
          setError("The reviewed draft was saved, but it failed validation and was not promoted or published.");
          return;
        }

        setPublicationReview(current => current ? { ...current, phase: "publishing", progressStep: "promoting", savedDraft: persistedDraft, draftSnapshot: persistedDraft, intent } : current);
        setStatus("Promoting publication version...");
        const promoted = await promoteDraft(
          context,
          persistedDraft.id,
          versionSelection.mode === "exact" ? versionSelection.requestedVersion.trim() : undefined);
        promotedVersionId = promoted.id;
        setPublicationReview(current => current ? { ...current, promotedVersionId, proposedVersion: promoted.version } : current);
      }

      setPublicationReview(current => current ? { ...current, progressStep: "publishing", preflight: reviewedPreflight, intent } : current);
      setStatus("Publishing executable reference...");
      const published = await publishVersion(context, promotedVersionId, {
        ...intent,
        action: reviewedPreflight.resolvedAction,
        slotName: reviewedPreflight.slotName,
        preflightToken: reviewedPreflight.preflightToken
      });
      setPublishedArtifact(published.artifactId);
      setPublicationReview(current => direct ? null : current ? {
        ...current,
        phase: "success",
        progressStep: undefined,
        preflight: reviewedPreflight,
        intent,
        promotedVersionId,
        published
      } : current);
      setStatus(`Published to ${published.slotName}`);
      try {
        // Direct publication leaves the editor usable. Refresh only if the author has not made
        // newer edits since the published snapshot; autosave will persist those edits after resuming.
        const shouldLoadDraft = () => {
          const currentDraft = latestDraftRef.current;
          const currentSignature = currentDraft && getDraftSignature(currentDraft);
          return currentDraft?.id === publicationReview.draftSnapshot.id
            && (currentSignature === getDraftSignature(publicationReview.draftSnapshot)
              || Boolean(savedDraft && currentSignature === getDraftSignature(savedDraft)));
        };
        await reload({ shouldLoadDraft });
        if (!shouldLoadDraft()) {
          setStatus(`Published to ${published.slotName}; newer edits remain in your draft.`);
        }
      } catch (reloadError) {
        setError(`Publication succeeded, but the editor could not refresh: ${reloadError instanceof Error ? reloadError.message : String(reloadError)}`);
      }
      setAutosavePaused(false);
    } catch (e) {
      setStatus("");
      const failureMessage = decorateConversionDiagnostic(e instanceof Error ? e.message : String(e));
      const publicationValidationErrors = extractExpressionValidationDiagnosticMessages(e);
      if (promotedVersionId && isStalePreflightError(e)) {
        setPublicationReview(current => current ? {
          ...current,
          phase: "partialFailure",
          progressStep: undefined,
          promotedVersionId,
          preflight: undefined,
          intent,
          failureMessage: "The reviewed publication target became stale before activation. The promoted version was retained; review the target again before publishing."
        } : current);
      } else if (promotedVersionId) {
        setPublicationReview(current => current ? {
          ...current,
          phase: "partialFailure",
          progressStep: undefined,
          promotedVersionId,
          intent,
          ...(publicationValidationErrors.length
            ? { validationErrors: publicationValidationErrors, executableStatus: "blocked" as const }
            : {}),
          failureMessage: publicationValidationErrors.length
            ? `Publication was blocked by ${publicationValidationErrors.length} expression validation error${publicationValidationErrors.length === 1 ? "" : "s"} (listed above). The promoted version was retained; correct the expressions, then review and publish again.`
            : `Publication failed after promotion: ${failureMessage}. The promoted version was retained; retry Publish or close this review and publish it later.`
        } : current);
      } else if (savedDraft) {
        const failedSavedDraft = savedDraft;
        // A validation-only 409 from promotion carries the enriched error list (foundation #937); surface
        // it so the dialog shows what to fix instead of only a count. Falls back to the retry copy otherwise.
        const promotionValidationErrors = parsePromotionValidationErrors(e);
        setPublicationReview(current => current ? {
          ...current,
          phase: "savedFailure",
          progressStep: undefined,
          savedDraft: failedSavedDraft,
          draftSnapshot: failedSavedDraft,
          ...(promotionValidationErrors.length
            ? { validationErrors: promotionValidationErrors, executableStatus: "blocked" as const }
            : {}),
          intent,
          failureMessage: promotionValidationErrors.length
            ? `The reviewed draft was saved, but promotion was refused by ${promotionValidationErrors.length} validation error${promotionValidationErrors.length === 1 ? "" : "s"} (listed above). Correct them, then review and publish again. No version or publication was created.`
            : `The reviewed draft was saved, but promotion failed: ${failureMessage}. No version or publication was created. Retry Publish to promote the already-saved snapshot.`
        } : current);
      } else {
        setPublicationReview(current => current ? { ...current, phase: "review", progressStep: undefined, intent } : current);
      }
      setError(failureMessage);
    } finally {
      publicationMutationInFlight.current = false;
      setOperation("idle");
    }
  }, [context, reload, reviewPublication, saveDraft, setAutosavePaused, setError, setOperation, setPublishedArtifact, setStatus]);

  const confirmPublication = useCallback(async (
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection = publicationReview?.versionSelection ?? { mode: "automatic" }
  ) => {
    if (publicationReview) await executePublication(publicationReview, intent, versionSelection);
  }, [executePublication, publicationReview]);

  const preparePublication = useCallback(async (mode: "publish" | "review" = "review") => {
    if (!draft || busy || publicationPreparationInFlight.current || publicationMutationInFlight.current) return;
    publicationPreparationInFlight.current = true;
    setAutosavePaused(true);
    setOperation("publicationPreflight");
    setError("");
    // Settle any in-flight or pending autosave before capturing the review snapshot. Previously a save
    // racing the review left "Review & publish" appearing to do nothing; now it awaits (with feedback).
    setStatus(mode === "publish" ? "Saving before publication..." : "Saving before review...");
    try {
      await flushPendingSave();
      const draftSnapshot = structuredClone(draft);
      setStatus(mode === "publish" ? "Preparing publication..." : "Preparing publication review...");
      // Policy and preflight reads never promote a version. Both actions use the same snapshot
      // and authoritative evidence; only direct Publish proceeds without an author review dialog.
      const [policy, slotsView, versionCapabilities] = await Promise.all([
        getPublicationPolicy(context, draftSnapshot.definitionId),
        listPublicationSlots(context, draftSnapshot.definitionId),
        getWorkflowPromotionVersionCapabilities(context)
      ]);
      const slots = slotsView.available ? slotsView.slots : [];
      // Only a publishing-sourced activation has a design version. A slot occupied by another activation
      // source stays in the review as occupied, without a version, rather than failing the review.
      const publishedSlots = slots.filter(isPublishingActivation);
      const incompleteSlot = publishedSlots.find(slot => !slot.publication?.versionId);
      if (incompleteSlot) throw new Error(`Publication slot '${incompleteSlot.slotName}' did not include its active version.`);
      const versionsById = new Map<string, Awaited<ReturnType<typeof getWorkflowDefinitionVersion>>>();
      await Promise.all(publishedSlots.map(async slot => {
        const versionId = slot.publication!.versionId;
        if (!versionsById.has(versionId)) versionsById.set(versionId, await getWorkflowDefinitionVersion(context, versionId));
      }));
      const slotVersions = Object.fromEntries(publishedSlots.map(slot => [slot.slotName, versionsById.get(slot.publication!.versionId)!]));
      let review = createPublicationReview({
        draft: draftSnapshot,
        details,
        policy,
        slots,
        slotsUnavailableReason: slotsView.available ? undefined : slotsView.reason,
        slotVersions,
        catalog
      });
      review.versionPreflightSupported = versionCapabilities.preflight;
      review.exactVersionSupported = versionCapabilities.exact;
      // Spec 261 FR-515: list the server's validation warnings (for example a replay-safe compensation step) in the
      // review. They never block; a host without the validations relation, or a failed read, shows none.
      review = withPublicationWarnings(review, await getDraftValidations(context, draftSnapshot.id).catch(() => null));
      if (!review.validationErrors.length && !(review.intent.action === "sideBySide" && !review.intent.slotName)) {
        const intent = publicationIntentFor(review, review.intent.action ?? "replace", review.intent.slotName ?? review.policy.defaultSlotName);
        const [preflight, versionPreflight] = await Promise.all([
          preflightSnapshot(context, review, intent),
          review.versionPreflightSupported
            ? preflightDraftPromotion(context, review.draftSnapshot.id)
            : Promise.resolve(undefined)
        ]);
        review = {
          ...review,
          intent,
          preflight,
          versionPreflight,
          proposedVersion: versionPreflight?.resolvedVersion ?? review.proposedVersion
        };
      }
      const canPublishDirectly = mode === "publish"
        && !review.validationErrors.length
        && !review.slotsUnavailableReason
        && publicationPreflightMatchesIntent(review.preflight, review.intent)
        && review.preflight.canActivate
        && (!review.versionPreflight || review.versionPreflight.isReady);
      if (canPublishDirectly) setQuickPublishing(true);
      setPublicationReview(review);
      if (canPublishDirectly) {
        await executePublication(review, review.intent, review.versionSelection, true);
      } else {
        setStatus(mode === "publish" ? "Publication needs review." : "");
      }
    } catch (error) {
      setPublicationReview(null);
      setAutosavePaused(false);
      setStatus("");
      setError(`Could not prepare a trustworthy publication review. No version was promoted or published. ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      publicationPreparationInFlight.current = false;
      setQuickPublishing(false);
      setOperation("idle");
    }
  }, [draft, details, catalog, busy, context, executePublication, flushPendingSave, setAutosavePaused, setError, setOperation, setStatus]);

  const cancelPublication = useCallback(() => {
    publicationReviewRequest.current += 1;
    setPublicationReview(null);
    setAutosavePaused(false);
    setStatus(publicationReview?.promotedVersionId
      ? "Publication review closed; the promoted version remains available."
      : publicationReview?.savedDraft
        ? "Publication review closed; the draft was saved, but no version was promoted or published."
        : "Publication cancelled; no changes were saved, promoted, or published.");
  }, [publicationReview, setAutosavePaused, setStatus]);

  const openPublishedExecutable = useCallback(() => {
    publicationReviewRequest.current += 1;
    setPublicationReview(null);
    setAutosavePaused(false);
    setActiveRightPanelId("artifacts");
    setInspectorCollapsed(false);
  }, [setActiveRightPanelId, setAutosavePaused, setInspectorCollapsed]);

  const dispatchTestRun = useCallback(async (
    draftSnapshot: WorkflowDraft,
    inputs: WorkflowExecutionInputs,
    start: WorkflowTestRunStart = { kind: "direct" },
    startLabel = "Direct start",
    workflowInputNames: string[] = [],
    onStarted?: (view: WorkflowTestRunView) => Promise<void>,
    acknowledgeUnavailableExpressionValidation = false
  ): Promise<WorkflowTestRunView | null> => {
    const draftSignature = getDraftSignature(draftSnapshot);
    setStatus("Preparing test run...");
    try {
      setOperation("testRunPreparing");
      setStatus("Preparing test run...");
      const snapshotId = createDraftSnapshotId(draftSnapshot);

      setOperation("testRunStarting");
      setStatus("Starting test run...");
      const nextTestRun = await startWorkflowDraftTestRun(context, {
        definitionId: draftSnapshot.definitionId,
        snapshotId,
        state: draftSnapshot.state,
        activityPresentation: draftSnapshot.activityPresentation,
        inputs,
        start,
        acknowledgeUnavailableExpressionValidation
      });
      if (isExpressionValidationUnavailable(nextTestRun) && !acknowledgeUnavailableExpressionValidation) {
        setExpressionValidationWarning({ draft: draftSnapshot, inputs, start, startLabel, workflowInputNames, onStarted });
        setStatus("Expression validation is unavailable. Confirmation is required before this Test Run can proceed.");
        return null;
      }
      startTestRun({ draftSignature, view: nextTestRun, startLabel, workflowInputNames });
      setActiveRightPanelId("runtime");
      setInspectorCollapsed(false);
      setStatus(isRejectedTestRun(nextTestRun) ? "Test run rejected" : "Test run dispatched");
      if (onStarted && !isRejectedTestRun(nextTestRun)) await onStarted(nextTestRun);
      return nextTestRun;
    } catch (e) {
      setStatus("");
      setError(describeWorkflowError(e, "The test run could not be dispatched."));
      return null;
    } finally {
      setOperation("idle");
    }
  }, [context, startTestRun, setActiveRightPanelId, setInspectorCollapsed, setOperation, setStatus, setError]);

  const run = useCallback(() => {
    if (!draft?.state.rootActivity || busy) return;
    setActiveRightPanelId("test-drive");
    setInspectorCollapsed(false);
  }, [draft?.state.rootActivity, busy, setActiveRightPanelId, setInspectorCollapsed]);

  const runTestDrive = useCallback(async (
    draftSnapshot: WorkflowDraft,
    inputs: WorkflowExecutionInputs,
    start: WorkflowTestRunStart,
    startLabel: string,
    workflowInputNames: string[],
    onStarted?: (view: WorkflowTestRunView) => Promise<void>
  ) => dispatchTestRun(draftSnapshot, inputs, start, startLabel, workflowInputNames, onStarted), [dispatchTestRun]);

  const updateTestDriveResult = useCallback((draftSignature: string, view: WorkflowTestRunView, startLabel: string, workflowInputNames: string[], driverActionResult?: WorkflowTestRunState["driverActionResult"], driverActionError?: string) => {
    startTestRun({
      draftSignature,
      view,
      startLabel,
      workflowInputNames,
      ...(driverActionResult ? { driverActionResult } : {}),
      ...(driverActionError ? { driverActionError } : {})
    });
  }, [startTestRun]);

  const confirmUnavailableExpressionValidation = useCallback(async () => {
    const pending = expressionValidationWarning;
    if (!pending) return;
    setExpressionValidationWarning(null);
    await dispatchTestRun(pending.draft, pending.inputs, pending.start, pending.startLabel, pending.workflowInputNames, pending.onStarted, true);
  }, [dispatchTestRun, expressionValidationWarning]);
  const cancelUnavailableExpressionValidation = useCallback(() => {
    setExpressionValidationWarning(null);
    setStatus("Test Run canceled because expression validation was unavailable.");
  }, [setStatus]);

  return {
    exportJson,
    exportExecutableArtifact,
    save,
    preparePublication,
    publicationReview: quickPublishing ? null : publicationReview,
    reviewPublication,
    confirmPublication,
    cancelPublication,
    openPublishedExecutable,
    expressionValidationWarning,
    confirmUnavailableExpressionValidation,
    cancelUnavailableExpressionValidation,
    run,
    runTestDrive,
    updateTestDriveResult
  };
}

function isExpressionValidationUnavailable(testRun: { status: string; reason?: string | null }) {
  return testRun.status.toLowerCase() === "rejected" &&
    /expression validation is unavailable/i.test(testRun.reason ?? "");
}

async function preflightSnapshot(
  context: StudioEndpointContext,
  review: PublicationReviewState,
  intent: PublicationIntent
) {
  return preflightPublicationSnapshot(context, {
    definitionId: review.draftSnapshot.definitionId,
    state: review.draftSnapshot.state,
    layout: review.draftSnapshot.layout,
    ...intent
  });
}

function isStalePreflightError(error: unknown) {
  if (!error || typeof error !== "object") return false;
  const candidate = error as { status?: unknown; statusCode?: unknown; response?: { status?: unknown } };
  return candidate.status === 409 || candidate.statusCode === 409 || candidate.response?.status === 409;
}

/**
 * Extracts the per-error validation messages from a promotion 409 ProblemDetails (foundation #937).
 * Tolerates both FastEndpoints shapes: `errors` as an array of `{ reason | message }` and as a
 * dictionary of `path -> string[]`. Returns an empty list when the payload carries no structured errors.
 */
export function parsePromotionValidationErrors(error: unknown): string[] {
  const payload = readWorkflowErrorPayload(error);
  if (!payload || typeof payload !== "object") return [];
  const errors = (payload as { errors?: unknown }).errors;
  const messages: string[] = [];

  if (Array.isArray(errors)) {
    for (const entry of errors) {
      if (typeof entry === "string") { messages.push(entry); continue; }
      if (entry && typeof entry === "object") {
        const record = entry as Record<string, unknown>;
        const message = record.reason ?? record.message ?? record.detail ?? record.errorMessage;
        if (typeof message === "string" && message.trim()) messages.push(message.trim());
      }
    }
  } else if (errors && typeof errors === "object") {
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (typeof value === "string" && value.trim()) messages.push(value.trim());
      else if (Array.isArray(value)) {
        for (const item of value) if (typeof item === "string" && item.trim()) messages.push(item.trim());
      }
    }
  }

  // Drop the summary "N validation error(s) present." line the backend keeps for backward compatibility
  // when we have the itemized entries; keep it only if it's the only thing available.
  const itemized = messages.filter(message => !/^Cannot promote draft .* validation error\(s\) present\.?$/i.test(message));
  return itemized.length ? Array.from(new Set(itemized)) : Array.from(new Set(messages));
}
