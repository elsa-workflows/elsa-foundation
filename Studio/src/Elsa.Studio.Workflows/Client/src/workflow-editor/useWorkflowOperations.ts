import { useCallback, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, WorkflowDefinitionDetails, WorkflowDraft, WorkflowExecutionInputs, WorkflowInput } from "../workflowTypes";
import {
  getWorkflowDefinitionVersion,
  getWorkflowPromotionVersionCapabilities,
  preflightDraftPromotion,
  promoteDraft
} from "../api/workflowDesign";
import {
  getPublicationPolicy,
  listPublicationSlots,
  preflightPublicationSnapshot,
  publishVersion,
  startWorkflowDraftTestRun,
  type PublicationIntent
} from "../api/publishing";
import { decorateConversionDiagnostic } from "../conversionSettings";
import { buildExportPayload, downloadWorkflowJson } from "../workflowSerialization";
import { readWorkflowInputs } from "../workflowReferenceAuthoring";
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
  createPublicationReview,
  publicationIntentFor,
  publicationPreflightMatchesIntent,
  type PublicationReviewState,
  type PublicationVersionSelection
} from "./publicationReview";

interface WorkflowOperationsParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  details: WorkflowDefinitionDetails | null;
  catalog: ActivityCatalogItem[];
  busy: boolean;
  saveDraft(draft: WorkflowDraft, savedStatus: string): Promise<WorkflowDraft>;
  flushPendingSave(): Promise<void>;
  reload(): Promise<void>;
  startTestRun(testRun: WorkflowTestRunState): void;
  clearTestRun(): void;
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
  saveDraft,
  flushPendingSave,
  reload,
  startTestRun,
  clearTestRun,
  setPublishedArtifact,
  setOperation,
  setStatus,
  setError,
  setActiveRightPanelId,
  setInspectorCollapsed,
  setAutosavePaused
}: WorkflowOperationsParams) {
  const [publicationReview, setPublicationReview] = useState<PublicationReviewState | null>(null);
  const publicationReviewRequest = useRef(0);
  const publicationMutationInFlight = useRef(false);
  const [runInputPrompt, setRunInputPrompt] = useState<{
    draft: WorkflowDraft;
    inputs: WorkflowInput[];
  } | null>(null);
  const [expressionValidationWarning, setExpressionValidationWarning] = useState<{
    draft: WorkflowDraft;
    inputs: WorkflowExecutionInputs;
  } | null>(null);
  const exportJson = useCallback(() => {
    if (!draft) return;
    const name = details?.definition.name;
    downloadWorkflowJson(buildExportPayload(draft, name), name);
    setStatus("Exported workflow as JSON.");
  }, [draft, details, setStatus]);

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

  const preparePublication = useCallback(async () => {
    if (!draft || busy) return;
    setAutosavePaused(true);
    setOperation("publicationPreflight");
    setError("");
    // Settle any in-flight or pending autosave before capturing the review snapshot. Previously a save
    // racing the review left "Review & publish" appearing to do nothing; now it awaits (with feedback).
    setStatus("Saving before review...");
    await flushPendingSave();
    const draftSnapshot = structuredClone(draft);
    setStatus("Preparing publication review...");
    try {
      // Review preparation is deliberately read-only. The captured draft is not saved or promoted
      // until the author explicitly presses Publish.
      const [policy, slots, versionCapabilities] = await Promise.all([
        getPublicationPolicy(context, draftSnapshot.definitionId),
        listPublicationSlots(context, draftSnapshot.definitionId),
        getWorkflowPromotionVersionCapabilities(context)
      ]);
      const occupiedSlots = slots.filter(slot => slot.activePublicationId || slot.publication);
      const incompleteSlot = occupiedSlots.find(slot => !slot.publication?.versionId);
      if (incompleteSlot) throw new Error(`Publication slot '${incompleteSlot.slotName}' did not include its active version.`);
      const versionsById = new Map<string, Awaited<ReturnType<typeof getWorkflowDefinitionVersion>>>();
      await Promise.all(occupiedSlots.map(async slot => {
        const versionId = slot.publication!.versionId;
        if (!versionsById.has(versionId)) versionsById.set(versionId, await getWorkflowDefinitionVersion(context, versionId));
      }));
      const slotVersions = Object.fromEntries(occupiedSlots.map(slot => [slot.slotName, versionsById.get(slot.publication!.versionId)!]));
      const review = createPublicationReview({
        draft: draftSnapshot,
        details,
        policy,
        slots,
        slotVersions,
        catalog
      });
      review.versionPreflightSupported = versionCapabilities.preflight;
      review.exactVersionSupported = versionCapabilities.exact;
      if (review.validationErrors.length || review.intent.action === "sideBySide" && !review.intent.slotName) {
        setPublicationReview(review);
      } else {
        const intent = publicationIntentFor(review, review.intent.action ?? "replace", review.intent.slotName ?? review.policy.defaultSlotName);
        const [preflight, versionPreflight] = await Promise.all([
          preflightSnapshot(context, review, intent),
          review.versionPreflightSupported
            ? preflightDraftPromotion(context, review.draftSnapshot.id)
            : Promise.resolve(undefined)
        ]);
        setPublicationReview({
          ...review,
          intent,
          preflight,
          versionPreflight,
          proposedVersion: versionPreflight?.resolvedVersion ?? review.proposedVersion
        });
      }
      setStatus("");
    } catch (error) {
      setPublicationReview(null);
      setAutosavePaused(false);
      setStatus("");
      setError(`Could not prepare a trustworthy publication review. No changes were saved, promoted, or published. ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      setOperation("idle");
    }
  }, [draft, details, catalog, busy, context, flushPendingSave, setAutosavePaused, setError, setOperation, setStatus]);

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

  const confirmPublication = useCallback(async (
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection = publicationReview?.versionSelection ?? { mode: "automatic" }
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
      setError("Server preflight blocks this target. Resolve the listed conflicts or review another target.");
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
        const validationErrors = persistedDraft.validationErrors
          .map(error => error.message?.trim())
          .filter((message): message is string => Boolean(message));
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
      setPublicationReview(current => current ? {
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
        await reload();
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
  }, [context, publicationReview, reload, reviewPublication, saveDraft, setAutosavePaused, setError, setOperation, setPublishedArtifact, setStatus]);

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
    acknowledgeUnavailableExpressionValidation = false
  ) => {
    const draftSignature = getDraftSignature(draftSnapshot);
    clearTestRun();
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
        acknowledgeUnavailableExpressionValidation
      });
      if (isExpressionValidationUnavailable(nextTestRun) && !acknowledgeUnavailableExpressionValidation) {
        setExpressionValidationWarning({ draft: draftSnapshot, inputs });
        setStatus("Expression validation is unavailable. Confirmation is required before this Test Run can proceed.");
        return;
      }
      startTestRun({ draftSignature, view: nextTestRun });
      setActiveRightPanelId("runtime");
      setInspectorCollapsed(false);
      setStatus(isRejectedTestRun(nextTestRun) ? "Test run rejected" : "Test run dispatched");
    } catch (e) {
      setStatus("");
      setError(describeWorkflowError(e, "The test run could not be dispatched."));
    } finally {
      setOperation("idle");
    }
  }, [context, clearTestRun, startTestRun, setActiveRightPanelId, setInspectorCollapsed, setOperation, setStatus, setError]);

  const run = useCallback(async () => {
    if (!draft?.state.rootActivity || busy) return;
    const inputs = readWorkflowInputs(draft.state.inputs);
    if (inputs.length > 0) {
      setRunInputPrompt({ draft, inputs });
      return;
    }
    await dispatchTestRun(draft, {});
  }, [draft, busy, dispatchTestRun]);

  const confirmRunInputs = useCallback(async (inputs: WorkflowExecutionInputs) => {
    const pending = runInputPrompt;
    if (!pending) return;
    setRunInputPrompt(null);
    await dispatchTestRun(pending.draft, inputs);
  }, [dispatchTestRun, runInputPrompt]);

  const cancelRunInputs = useCallback(() => setRunInputPrompt(null), []);
  const confirmUnavailableExpressionValidation = useCallback(async () => {
    const pending = expressionValidationWarning;
    if (!pending) return;
    setExpressionValidationWarning(null);
    await dispatchTestRun(pending.draft, pending.inputs, true);
  }, [dispatchTestRun, expressionValidationWarning]);
  const cancelUnavailableExpressionValidation = useCallback(() => {
    setExpressionValidationWarning(null);
    setStatus("Test Run canceled because expression validation was unavailable.");
  }, [setStatus]);

  return {
    exportJson,
    save,
    preparePublication,
    publicationReview,
    reviewPublication,
    confirmPublication,
    cancelPublication,
    openPublishedExecutable,
    runInputPrompt,
    confirmRunInputs,
    cancelRunInputs,
    expressionValidationWarning,
    confirmUnavailableExpressionValidation,
    cancelUnavailableExpressionValidation,
    run
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
