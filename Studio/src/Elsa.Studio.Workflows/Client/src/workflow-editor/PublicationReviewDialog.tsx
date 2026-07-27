import React, { useEffect, useMemo, useRef, useState } from "react";
import type { PublicationIntent } from "../api/publishing";
import {
  publicationBaselineFor,
  publicationChangesFor,
  publicationIntentForChannel,
  publicationPreflightMatchesIntent,
  type PublicationChangeCount,
  type PublicationReviewState,
  type PublicationVersionSelection
} from "./publicationReview";
import { useDialogFocus } from "./useDialogFocus";
import "./publicationReview.css";

const createChannelValue = "__create-publication-channel__";

export function PublicationReviewDialog({
  review,
  busy,
  onReview,
  onPublish,
  onCancel,
  onOpenPublishedExecutable = () => undefined
}: {
  review: PublicationReviewState;
  busy: boolean;
  onReview?(
    review: PublicationReviewState,
    intent: PublicationIntent,
    versionSelection: PublicationVersionSelection
  ): Promise<void>;
  onPublish(intent: PublicationIntent, versionSelection?: PublicationVersionSelection): Promise<void>;
  onCancel(): void;
  onOpenPublishedExecutable?(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const channelNames = useMemo(
    () => [...new Set([review.policy.defaultSlotName, ...review.slots.map(slot => slot.slotName)])],
    [review.policy.defaultSlotName, review.slots]);
  const initialChannel = review.intent.slotName || review.policy.defaultSlotName;
  const [channelMode, setChannelMode] = useState<"existing" | "create">(
    channelNames.includes(initialChannel) ? "existing" : "create");
  const [existingChannel, setExistingChannel] = useState(
    channelNames.includes(initialChannel) ? initialChannel : review.policy.defaultSlotName);
  const [newChannel, setNewChannel] = useState(
    channelNames.includes(initialChannel) ? "" : initialChannel);
  const [versionSelection, setVersionSelection] = useState<PublicationVersionSelection>(review.versionSelection);
  const editableReview = review.phase === "review" || review.phase === "validationBlocked";
  const selectedChannel = channelMode === "existing" ? existingChannel : newChannel.trim();
  const channelIsValid = Boolean(selectedChannel)
    && !(channelMode === "create" && selectedChannel.toLowerCase() === review.policy.defaultSlotName.toLowerCase());
  const intent = useMemo(
    () => publicationIntentForChannel(review, selectedChannel),
    [review, selectedChannel]);
  const reviewedPreflight = publicationPreflightMatchesIntent(review.preflight, intent) ? review.preflight : undefined;
  const versionEvidenceMatches = !review.versionPreflightSupported
    || Boolean(review.versionPreflight
      && review.versionPreflight.assignmentMode === versionSelection.mode
      && (versionSelection.mode === "automatic"
        || review.versionPreflight.requestedVersion === versionSelection.requestedVersion.trim()));
  const currentSelectionMatches = review.intent.action === intent.action
    && review.intent.slotName === intent.slotName
    && review.intent.expectedPublicationId === intent.expectedPublicationId
    && review.versionSelection.mode === versionSelection.mode
    && (versionSelection.mode === "automatic"
      || review.versionSelection.mode === "exact"
      && review.versionSelection.requestedVersion === versionSelection.requestedVersion);
  const closeOnEscape = !busy && review.phase !== "publishing" ? onCancel : null;
  useDialogFocus(dialogRef, closeOnEscape);

  useEffect(() => {
    if (!onReview || !editableReview || !channelIsValid || review.validationErrors.length > 0) return;
    if ((review.reviewPending && currentSelectionMatches) || (reviewedPreflight && versionEvidenceMatches)) return;
    void onReview(review, intent, versionSelection);
  }, [
    channelIsValid,
    editableReview,
    intent,
    onReview,
    currentSelectionMatches,
    review,
    reviewedPreflight,
    versionEvidenceMatches,
    versionSelection
  ]);

  const activeSlot = review.slots.find(slot => slot.slotName === selectedChannel);
  const isReplacement = reviewedPreflight
    ? reviewedPreflight.resolvedAction === "replace"
    : Boolean(activeSlot?.publication || activeSlot?.activePublicationId)
      || selectedChannel === review.policy.defaultSlotName;
  const changes = publicationChangesFor(review, activeSlot?.slotName ?? "");
  const preflightChanges = reviewedPreflight?.triggers ?? reviewedPreflight?.changes ?? [];
  const triggerSummary = reviewedPreflight
    ? preflightChanges.length
      ? preflightChanges.map(change => `${change.change} ${change.key} (${change.cardinality})`).join("; ")
      : "No trigger changes."
    : formatChangeCount(changes.triggers);
  const versionIssues = review.versionPreflight?.issues ?? [];
  const blocked = review.validationErrors.length > 0
    || !channelIsValid
    || review.reviewPending
    || !reviewedPreflight
    || !reviewedPreflight.canActivate
    || review.versionPreflightSupported && !versionEvidenceMatches
    || review.versionPreflight?.isReady === false;
  const statusMessage = publicationStatusMessage(review, blocked);

  return (
    <div className="wf-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="wf-dialog wf-publication-review"
        role="dialog"
        aria-modal="true"
        aria-labelledby="publication-review-title"
        aria-describedby="publication-review-status"
        tabIndex={-1}
      >
        <form onSubmit={event => {
          event.preventDefault();
          void onPublish(intent, versionSelection);
        }}>
          <header className="wf-dialog-heading wf-publication-header">
            <div>
              <span>Workflow publication</span>
              <h3 id="publication-review-title">
                {review.phase === "success"
                  ? "Publication complete"
                  : review.phase === "partialFailure"
                    ? "Publication needs attention"
                    : "Review and publish"}
              </h3>
              <p>Confirm where this captured workflow state will become active.</p>
            </div>
          </header>

          <div className="wf-publication-body">
            <output
              id="publication-review-status"
              className="wf-publication-status"
              data-phase={review.phase}
              aria-live="polite"
            >
              {statusMessage}
            </output>

            {review.phase === "success" && review.published ? (
              <PublicationSuccess review={review} />
            ) : review.phase === "partialFailure" ? (
              <PublicationRecovery review={review} />
            ) : review.phase === "savedFailure" ? (
              <PublicationSavedFailure review={review} />
            ) : (
              <>
                <section className="wf-publication-decision" aria-label="Publication decision">
                  <label className="wf-form-field">
                    <span>Publication channel</span>
                    <select
                      aria-label="Publication channel"
                      value={channelMode === "create" ? createChannelValue : existingChannel}
                      disabled={!editableReview || busy}
                      onChange={event => {
                        if (event.target.value === createChannelValue) {
                          setChannelMode("create");
                          setNewChannel("");
                        } else {
                          setChannelMode("existing");
                          setExistingChannel(event.target.value);
                        }
                      }}
                    >
                      {channelNames.map(channel => (
                        <option key={channel} value={channel}>
                          {channel === review.policy.defaultSlotName ? `${channel} (normal)` : channel}
                        </option>
                      ))}
                      <option value={createChannelValue}>Create new channel…</option>
                    </select>
                    <small>
                      <strong>{review.policy.defaultSlotName}</strong> is the normal publication channel.
                      Named channels such as <strong>canary</strong> remain separately addressable.
                    </small>
                  </label>

                  {channelMode === "create" ? (
                    <label className="wf-form-field">
                      <span>New channel name</span>
                      <input
                        aria-label="New publication channel"
                        value={newChannel}
                        disabled={!editableReview || busy}
                        onChange={event => setNewChannel(event.target.value)}
                        placeholder="canary"
                      />
                      {!newChannel.trim() ? <small role="alert">Enter a channel name.</small> : null}
                      {newChannel.trim().toLowerCase() === review.policy.defaultSlotName.toLowerCase()
                        ? <small role="alert">Choose {review.policy.defaultSlotName} from the existing channels.</small>
                        : null}
                    </label>
                  ) : null}

                  <dl className="wf-publication-summary">
                    <DecisionFact
                      label="Effect"
                      value={isReplacement
                        ? `Replace the current publication in ${selectedChannel || "this channel"}`
                        : `Create a separate publication channel named ${selectedChannel || "…"}`}
                    />
                    <DecisionFact
                      label="Version"
                      value={versionSelection.mode === "exact"
                        ? review.versionPreflight?.resolvedVersion || versionSelection.requestedVersion || "Enter an exact version"
                        : review.versionPreflight?.resolvedVersion
                          ? `${review.versionPreflight.resolvedVersion} · assigned automatically by version policy`
                          : "Assigned automatically by version policy"}
                    />
                    <DecisionFact
                      label="Readiness"
                      value={review.reviewPending
                        ? "Checking current policy and target…"
                        : reviewedPreflight?.canActivate && review.versionPreflight?.isReady !== false
                          ? "Ready to publish"
                          : "Not ready"}
                    />
                    <DecisionFact label="Compared with" value={publicationBaselineFor(review, selectedChannel)} />
                  </dl>

                  {review.exactVersionSupported ? (
                    <details className="wf-publication-disclosure">
                      <summary>Edit version</summary>
                      <div className="wf-publication-disclosure-body">
                        <label className="wf-publication-version-option">
                          <input
                            type="radio"
                            name="publication-version-mode"
                            checked={versionSelection.mode === "automatic"}
                            disabled={!editableReview || busy}
                            onChange={() => setVersionSelection({ mode: "automatic" })}
                          />
                          <span><strong>Automatic</strong><small>Foundation assigns the next version according to policy.</small></span>
                        </label>
                        <label className="wf-publication-version-option">
                          <input
                            type="radio"
                            name="publication-version-mode"
                            checked={versionSelection.mode === "exact"}
                            disabled={!editableReview || busy}
                            onChange={() => setVersionSelection({
                              mode: "exact",
                              requestedVersion: versionSelection.mode === "exact"
                                ? versionSelection.requestedVersion
                                : ""
                            })}
                          />
                          <span><strong>Exact semantic version</strong><small>Must be unused and newer than the latest promoted version.</small></span>
                        </label>
                        {versionSelection.mode === "exact" ? (
                          <label className="wf-form-field">
                            <span>Exact version</span>
                            <input
                              aria-label="Exact semantic version"
                              value={versionSelection.requestedVersion}
                              disabled={!editableReview || busy}
                              onChange={event => setVersionSelection({
                                mode: "exact",
                                requestedVersion: event.target.value
                              })}
                              placeholder="2.1.0 or 2.1.0-rc.1"
                            />
                          </label>
                        ) : null}
                      </div>
                    </details>
                  ) : null}
                </section>

                <section className="wf-publication-change-card" aria-labelledby="publication-changes-title">
                  <div>
                    <h4 id="publication-changes-title">Captured changes</h4>
                    <p>{compactChangeSummary(changes)}</p>
                  </div>
                  <p className="wf-publication-baseline">Baseline: {publicationBaselineFor(review, selectedChannel)}</p>
                </section>

                <p className="wf-dialog-note">
                  The current captured editor state is included and saved when you choose Publish.
                  Opening this review did not save, promote, or publish it.
                </p>

                {review.validationErrors.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Publication blocked before mutation</strong>
                    <ul>{review.validationErrors.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}</ul>
                  </div>
                ) : null}

                {versionIssues.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Version is not ready</strong>
                    <ul>{versionIssues.map(issue => <li key={`${issue.code}-${issue.message}`}>{issue.message}</li>)}</ul>
                  </div>
                ) : null}

                {reviewedPreflight?.conflicts.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Publication channel conflicts</strong>
                    <ul>
                      {reviewedPreflight.conflicts.map(conflict => (
                        <li key={`${conflict.publicationId}-${conflict.key}`}>
                          Conflict with {conflict.slotName}: {conflict.key}
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}

                {review.failureMessage ? <p className="wf-publication-recovery" role="alert">{review.failureMessage}</p> : null}

                <details className="wf-publication-disclosure">
                  <summary>Changes details</summary>
                  <dl className="wf-publication-detail-grid">
                    <ChangeSummary label="Activities" value={changes.activities} />
                    <ChangeSummary label="Inputs" value={changes.inputs} />
                    <ChangeSummary label="Outputs" value={changes.outputs} />
                    <div><dt>Triggers</dt><dd>{triggerSummary}</dd></div>
                  </dl>
                </details>

                <details className="wf-publication-disclosure">
                  <summary>Advanced details</summary>
                  <dl className="wf-publication-detail-grid">
                    <DecisionFact
                      label="Policy"
                      value={reviewedPreflight
                        ? `${reviewedPreflight.policySource}${reviewedPreflight.policyRevision == null ? "" : ` · revision ${reviewedPreflight.policyRevision}`}`
                        : "Awaiting authoritative review"}
                    />
                    <DecisionFact label="Resolved action" value={reviewedPreflight?.resolvedAction ?? "Awaiting authoritative review"} />
                    <DecisionFact label="Internal slot" value={(reviewedPreflight?.slotName ?? selectedChannel) || "—"} />
                    <DecisionFact label="Preflight token" value={reviewedPreflight?.preflightToken ?? "—"} />
                  </dl>
                  <div className="wf-publication-claims">
                    <strong>Authoritative trigger claims</strong>
                    {reviewedPreflight?.claims.length
                      ? <ul>{reviewedPreflight.claims.map(claim => <li key={`${claim.key}-${claim.cardinality}`}>{claim.key} ({claim.cardinality})</li>)}</ul>
                      : <p>No trigger claims.</p>}
                  </div>
                </details>
              </>
            )}
          </div>

          <footer className="wf-dialog-actions wf-publication-footer">
            <button type="button" onClick={onCancel} disabled={busy}>
              {review.phase === "review" || review.phase === "validationBlocked" ? "Cancel" : "Close"}
            </button>
            {review.phase === "success" ? (
              <button type="button" className="wf-primary-action" onClick={onOpenPublishedExecutable}>
                Open published executable
              </button>
            ) : review.phase === "partialFailure" ? (
              <button type="submit" disabled={busy || review.reviewPending}>
                Retry publication
              </button>
            ) : review.phase === "savedFailure" ? (
              review.validationErrors.length === 0
                ? <button type="submit" disabled={busy}>Retry publication</button>
                : null
            ) : (
              <button type="submit" disabled={busy || blocked}>
                {busy && review.phase === "publishing" ? "Publishing…" : "Publish"}
              </button>
            )}
          </footer>
        </form>
      </section>
    </div>
  );
}

function PublicationSuccess({ review }: { review: PublicationReviewState }) {
  return (
    <section className="wf-publication-outcome wf-publication-outcome-success" aria-labelledby="publication-success-title">
      <div className="wf-publication-outcome-mark" aria-hidden="true">✓</div>
      <div>
        <h4 id="publication-success-title">Workflow is published</h4>
        <p>
          Version <strong>{review.proposedVersion}</strong> is active in Publication channel{" "}
          <strong>{review.published?.slotName}</strong>.
        </p>
      </div>
      <details className="wf-publication-disclosure">
        <summary>Published details</summary>
        <dl className="wf-publication-detail-grid">
          <DecisionFact label="Executable" value={review.published?.artifactId ?? "—"} />
          <DecisionFact label="Source Reference" value={review.published?.sourceReferenceId ?? "—"} />
          <DecisionFact label="Promoted version ID" value={review.promotedVersionId ?? "—"} />
        </dl>
      </details>
    </section>
  );
}

function PublicationRecovery({ review }: { review: PublicationReviewState }) {
  return (
    <section className="wf-publication-outcome" aria-labelledby="publication-recovery-title">
      <h4 id="publication-recovery-title">The version was retained, but the channel was not activated</h4>
      <p>{review.failureMessage}</p>
      <dl className="wf-publication-detail-grid">
        <DecisionFact label="Retained version" value={review.proposedVersion} />
        <DecisionFact label="Publication channel" value={review.intent.slotName || review.policy.defaultSlotName} />
      </dl>
    </section>
  );
}

function PublicationSavedFailure({ review }: { review: PublicationReviewState }) {
  return (
    <section className="wf-publication-outcome" aria-labelledby="publication-saved-failure-title">
      <h4 id="publication-saved-failure-title">No version or publication was created</h4>
      <p>{review.failureMessage}</p>
      {review.validationErrors.length ? (
        <div className="wf-publication-risks" role="alert">
          <ul>{review.validationErrors.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}</ul>
        </div>
      ) : null}
    </section>
  );
}

function DecisionFact({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

function ChangeSummary({ label, value }: { label: string; value: PublicationChangeCount }) {
  return <div><dt>{label}</dt><dd>{formatChangeCount(value)}</dd></div>;
}

function formatChangeCount(value: PublicationChangeCount) {
  return `${value.added} added, ${value.changed} changed, ${value.removed} removed`;
}

function compactChangeSummary(changes: PublicationReviewState["changes"]) {
  const counts = [
    changes.activities.added + changes.activities.changed + changes.activities.removed,
    changes.inputs.added + changes.inputs.changed + changes.inputs.removed,
    changes.outputs.added + changes.outputs.changed + changes.outputs.removed,
    changes.triggers.added + changes.triggers.changed + changes.triggers.removed
  ];
  const total = counts.reduce((sum, count) => sum + count, 0);
  if (total === 0) return "No structural changes detected against this channel.";
  return [
    `${counts[0]} activity ${counts[0] === 1 ? "change" : "changes"}`,
    `${counts[1]} input ${counts[1] === 1 ? "change" : "changes"}`,
    `${counts[2]} output ${counts[2] === 1 ? "change" : "changes"}`,
    `${counts[3]} trigger ${counts[3] === 1 ? "change" : "changes"}`
  ].join(" · ");
}

function publicationStatusMessage(review: PublicationReviewState, blocked: boolean) {
  if (review.phase === "success") return "Publication completed successfully.";
  if (review.phase === "validationBlocked") return "Resolve the blocking validation before publishing.";
  if (review.phase === "savedFailure") return "The captured draft was saved, but no version or publication was created.";
  if (review.phase === "partialFailure") return `Promoted version ${review.proposedVersion} was retained and can be retried.`;
  if (review.phase === "publishing") {
    const messages = {
      saving: "Saving the captured workflow state…",
      promoting: "Promoting the saved workflow version…",
      preflight: "Checking publication policy and trigger conflicts…",
      publishing: "Activating the published executable…"
    };
    return review.progressStep ? messages[review.progressStep] : "Publishing…";
  }
  if (review.reviewPending) return "Checking the selected Publication channel and version…";
  if (blocked) return "Review the highlighted issue before publishing.";
  return "Ready to publish. Nothing changes until you choose Publish.";
}
