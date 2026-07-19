import { useEffect, useMemo, useRef, useState } from "react";
import {
  StudioHttpError,
  type StudioActivityDefinitionImplementationEditorContribution,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionForkPreview,
  ActivityDefinitionForkReceipt,
  ActivityDefinitionManagementView
} from "./activityDefinitionTypes";
import {
  applyActivityDefinitionFork,
  getActivityDefinitionForkStatus,
  previewActivityDefinitionFork,
  useActivityAuthoringCapabilities
} from "./api/activityDesign";
import {
  choiceValue,
  contractSummary,
  migrationProviderChoices
} from "./activityDefinitionDraftManagementShared";
import { ActivityDefinitionManagementDialogActions, ActivityDefinitionManagementDialogShell } from "./ActivityDefinitionManagementDialogShell";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";

export function ActivityDefinitionForkDialog({
  context,
  definition,
  activityEditors,
  onClose,
  onApplied
}: {
  context: StudioEndpointContext;
  definition: ActivityDefinitionManagementView;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onClose(): void;
  onApplied(receipt: ActivityDefinitionForkReceipt): void;
}) {
  const recommendation = definition.lifecycle.recommendation!;
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const choices = useMemo(
    () => migrationProviderChoices(
      capabilitiesQuery.data,
      activityEditors,
      recommendation.providerSchemaVersion
    ),
    [activityEditors, capabilitiesQuery.data, recommendation.providerSchemaVersion]
  );
  const [category, setCategory] = useState(definition.definition.category);
  const [displayName, setDisplayName] = useState(definition.definition.displayName);
  const [description, setDescription] = useState(definition.definition.description ?? "");
  const [target, setTarget] = useState("");
  const [preview, setPreview] = useState<ActivityDefinitionForkPreview | null>(null);
  const [applyIdempotencyKey, setApplyIdempotencyKey] = useState("");
  const [previewing, setPreviewing] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const previewAttempt = useRef<{ material: string; idempotencyKey: string } | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const submitting = previewing || applying;
  useDialogFocus(dialogRef, submitting ? null : onClose);

  useEffect(() => {
    if (!choices.some(choice => choiceValue(choice) === target)) {
      const preferred = choices.find(choice =>
        choice.providerKey === recommendation.providerKey &&
        choice.schemaVersion === recommendation.providerSchemaVersion
      ) ?? choices[0];
      setTarget(preferred ? choiceValue(preferred) : "");
    }
  }, [choices, recommendation.providerKey, recommendation.providerSchemaVersion, target]);

  const choice = choices.find(item => choiceValue(item) === target);
  const canPreview = Boolean(
    !submitting &&
    !preview &&
    choice?.contribution &&
    category.trim() &&
    displayName.trim()
  );

  const requestPreview = async () => {
    if (!canPreview || !choice) return;
    setPreviewing(true);
    setError(null);
    const request = {
      sourceVersionId: recommendation.versionId,
      category: category.trim(),
      displayName: displayName.trim(),
      description: description.trim() || null,
      targetProviderKey: choice.providerKey,
      targetProviderSchemaVersion: choice.schemaVersion
    };
    const material = JSON.stringify(request);
    if (previewAttempt.current?.material !== material) {
      previewAttempt.current = {
        material,
        idempotencyKey: operationIdentity("activity-fork-preview")
      };
    }
    try {
      const reviewed = await previewActivityDefinitionFork(
        context,
        definition.definition.definitionId,
        {
          ...request,
          idempotencyKey: previewAttempt.current.idempotencyKey
        }
      );
      ensurePreviewMatchesRequest(
        reviewed,
        definition.definition.definitionId,
        recommendation.versionId,
        choice.providerKey,
        choice.schemaVersion
      );
      setPreview(reviewed);
      setApplyIdempotencyKey(operationIdentity("activity-fork-apply"));
    } catch (cause) {
      setError(forkError(cause, "The fork preview could not be established. The source remains unchanged."));
    } finally {
      setPreviewing(false);
    }
  };

  const apply = async () => {
    if (!preview || !applyIdempotencyKey || applying) return;
    setApplying(true);
    setError(null);
    try {
      const receipt = await applyActivityDefinitionFork(
        context,
        preview.candidateId,
        preview.requestFingerprint,
        applyIdempotencyKey
      );
      ensureReceiptMatchesPreview(receipt, preview, applyIdempotencyKey);
      onApplied(receipt);
    } catch (cause) {
      const reconciled = await reconcileFork(context, applyIdempotencyKey, preview, cause);
      if (reconciled) onApplied(reconciled);
      else setError(forkError(cause, "The fork outcome could not be confirmed. Retry Apply with the same reviewed operation."));
    } finally {
      setApplying(false);
    }
  };

  const editRequest = () => {
    setPreview(null);
    setApplyIdempotencyKey("");
    setError(null);
  };
  const canApply = Boolean(
    preview?.contractComparison.isCompatible &&
    !preview.providerMigration.diagnostics.some(diagnostic => diagnostic.severity === "Error")
  );

  return <ActivityDefinitionManagementDialogShell
    dialogRef={dialogRef}
    title={preview ? "Review activity fork" : "Fork recommended activity version"}
    kicker="Source-owned to Design-owned"
    submitting={submitting}
    onClose={onClose}
  >
    {!preview ? <>
      <dl className="ad-review-facts">
        <div><dt>Source definition</dt><dd><code>{definition.definition.definitionId}</code></dd></div>
        <div><dt>Recommended version</dt><dd>{recommendation.version} · {recommendation.lifecycle}<br /><code>{recommendation.versionId}</code></dd></div>
        <div><dt>Source provider</dt><dd><code>{recommendation.providerKey}</code> · schema {recommendation.providerSchemaVersion}</dd></div>
      </dl>
      <p className="ad-dialog-note">Forking always starts from the one recommended active version. Studio does not offer an explicit version override.</p>
      <label className="ad-dialog-field"><span>Category</span><input value={category} maxLength={200} onChange={event => setCategory(event.target.value)} /></label>
      <label className="ad-dialog-field"><span>Display name</span><input value={displayName} maxLength={200} onChange={event => setDisplayName(event.target.value)} /></label>
      <label className="ad-dialog-field"><span>Description <small>Optional</small></span><textarea value={description} maxLength={2000} onChange={event => setDescription(event.target.value)} /></label>
      <label className="ad-dialog-field"><span>Design-owned provider</span><select value={target} onChange={event => setTarget(event.target.value)}><option value="" disabled>Select a supported provider</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.capability.displayName} · schema {item.schemaVersion}</option>)}</select></label>
      {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading supported provider conversions…</div> : null}
      {capabilitiesQuery.isError || choices.length === 0 ? <div className="ad-inline-error" role="alert">No authorable provider conversion is advertised for the recommended source schema. Nothing can be forked.</div> : null}
      {choice && !choice.contribution ? <div className="ad-inline-error" role="alert">The exact Studio editor for this target provider schema is unavailable. Forking is disabled.</div> : null}
    </> : <ForkReview preview={preview} />}
    {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
    {preview ? <footer><button type="button" onClick={editRequest} disabled={submitting}>Edit request</button><button type="button" onClick={onClose} disabled={submitting}>Cancel</button><button type="button" className="ad-primary-action" onClick={() => void apply()} disabled={submitting || !canApply}>{applying ? "Applying atomically…" : "Apply reviewed fork"}</button></footer>
      : <ActivityDefinitionManagementDialogActions submitting={submitting} submitLabel="Create fork preview" canSubmit={canPreview} onClose={onClose} onSubmit={requestPreview} />}
  </ActivityDefinitionManagementDialogShell>;
}

function ForkReview({ preview }: { preview: ActivityDefinitionForkPreview }) {
  return <>
    <dl className="ad-review-facts">
      <div><dt>Exact source</dt><dd>{preview.source.version} · {preview.source.lifecycle}<br /><code>{preview.source.versionId}</code></dd></div>
      <div><dt>New Activity Type Key</dt><dd><code>{preview.target.activityTypeKey}</code></dd></div>
      <div><dt>New definition</dt><dd><code>{preview.target.definitionId}</code></dd></div>
      <div><dt>Initial draft</dt><dd><code>{preview.target.draftId}</code></dd></div>
      <div><dt>Provider conversion</dt><dd><code>{preview.providerMigration.sourceProviderKey}</code> schema {preview.providerMigration.sourceProviderSchemaVersion} → <code>{preview.providerMigration.targetProviderKey}</code> schema {preview.providerMigration.targetProviderSchemaVersion}</dd></div>
      <div><dt>Public contract</dt><dd>{contractSummary(preview.target.contract)} · {preview.contractComparison.isCompatible ? "compatible" : "incompatible"}</dd></div>
      <div><dt>Reservation expires</dt><dd>{formatTimestamp(preview.expiresAt)}</dd></div>
    </dl>
    <div className="ad-atomic-review"><strong>Atomic result</strong><span>Apply creates exactly these reserved Design-owned identities and opens the new draft. The source definition and version remain unchanged.</span></div>
    {preview.contractComparison.changes.length ? <ul className="ad-review-change-list">{preview.contractComparison.changes.map((change, index) => <li key={`${change.kind}:${change.referenceKey ?? index}`}><strong>{change.kind}</strong><span>{change.detail}</span></li>)}</ul> : <div className="ad-inline-status" role="status">The public contract is copied without changes.</div>}
    {preview.providerMigration.diagnostics.length ? <ul className="ad-review-change-list">{preview.providerMigration.diagnostics.map((diagnostic, index) => <li key={`${diagnostic.code ?? "migration"}:${index}`}><strong>{diagnostic.severity ?? "info"}</strong><span>{diagnostic.message}</span></li>)}</ul> : null}
  </>;
}

async function reconcileFork(
  context: StudioEndpointContext,
  idempotencyKey: string,
  preview: ActivityDefinitionForkPreview,
  cause: unknown
) {
  if (cause instanceof StudioHttpError) {
    const payload = cause.payload as { errorCode?: string } | null;
    if (payload?.errorCode !== "activity.fork.outcome-unknown" && cause.status < 500) return null;
  }
  try {
    const receipt = await getActivityDefinitionForkStatus(context, idempotencyKey);
    ensureReceiptMatchesPreview(receipt, preview, idempotencyKey);
    return receipt;
  } catch {
    return null;
  }
}

function ensurePreviewMatchesRequest(
  preview: ActivityDefinitionForkPreview,
  definitionId: string,
  sourceVersionId: string,
  targetProviderKey: string,
  targetProviderSchemaVersion: string
) {
  if (
    preview.source.definitionId !== definitionId ||
    preview.source.versionId !== sourceVersionId ||
    preview.source.lifecycle !== "Active" ||
    preview.providerMigration.targetProviderKey !== targetProviderKey ||
    preview.providerMigration.targetProviderSchemaVersion !== targetProviderSchemaVersion ||
    preview.target.providerKey !== targetProviderKey ||
    preview.target.providerSchemaVersion !== targetProviderSchemaVersion
  ) {
    throw new Error("The fork preview did not match the requested source and target binding.");
  }
}

function ensureReceiptMatchesPreview(
  receipt: ActivityDefinitionForkReceipt,
  preview: ActivityDefinitionForkPreview,
  idempotencyKey: string
) {
  if (
    receipt.idempotencyKey !== idempotencyKey ||
    receipt.candidateId !== preview.candidateId ||
    receipt.requestFingerprint !== preview.requestFingerprint ||
    receipt.definition.definitionId !== preview.target.definitionId ||
    receipt.definition.activityTypeKey !== preview.target.activityTypeKey ||
    receipt.draft.definitionId !== preview.target.definitionId ||
    receipt.draft.draftId !== preview.target.draftId
  ) {
    throw new Error("The fork receipt did not match the reviewed reservation.");
  }
}

function forkError(error: unknown, fallback: string) {
  if (error instanceof StudioHttpError) {
    const payload = error.payload as { errorCode?: string } | null;
    switch (payload?.errorCode) {
      case "activity.fork.preview-idempotency-conflict":
        return "The preview inputs changed after this operation identity was reserved. Review the current request again.";
      case "activity.fork.preview-expired":
      case "activity.fork.candidate-expired":
        return "The reviewed fork reservation expired. Create a new preview before applying.";
      case "activity.fork.candidate-stale":
        return "The source, provider conversion, or reviewed candidate changed. Create and review a new preview; nothing was created.";
      case "activity.fork.collision":
        return "A reserved target identity is no longer available. Nothing was created; create a new preview.";
      case "activity.provider.migration-unsupported":
        return "The exact provider conversion is unsupported. No definition or draft was created.";
      case "activity.contract.capability-rejected":
        return "The historical contract is outside the activated authoring capability catalog. Nothing was created.";
      case "activity.fork.idempotency-conflict":
        return "This Apply operation identity is already bound to different reviewed fork material.";
    }
    if (error.status === 403) return "This account is not authorized to fork the recommended Activity Definition version.";
  }
  return fallback;
}

function operationIdentity(prefix: string) {
  const suffix = globalThis.crypto?.randomUUID?.()
    ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
  return `${prefix}-${suffix}`;
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
