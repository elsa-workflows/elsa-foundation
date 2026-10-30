import React, { useEffect, useMemo, useRef, useState } from "react";
import { describeActivationSource, type PublicationIntent } from "../api/publishing";
import {
  publicationBaselineFor,
  publicationBlockCause,
  publicationBlockedMessage,
  publicationChangesFor,
  publicationChannelOccupancy,
  publicationConflictKeyDetail,
  publicationConflictLabel,
  publicationIntentForChannel,
  publicationPreflightMatchesIntent,
  uniquePublicationConflicts,
  type PublicationChangeCount,
  type PublicationChannelOccupancy,
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
  const channelValidationMessage = channelIsValid
    ? undefined
    : channelMode === "create" && selectedChannel.toLowerCase() === review.policy.defaultSlotName.toLowerCase()
      ? `Choose ${review.policy.defaultSlotName} from the existing channels.`
      : "Enter a channel name before publishing.";
  const intent = useMemo(
    () => publicationIntentForChannel(review, selectedChannel),
    [review, selectedChannel]);
  const reviewedPreflight = publicationPreflightMatchesIntent(review.preflight, intent) ? review.preflight : undefined;
  const versionEvidenceMatches = !review.versionPreflightSupported
    || Boolean(review.versionPreflight
      && review.versionPreflight.assignmentMode === versionSelection.mode
      && (versionSelection.mode === "automatic"
        || review.versionPreflight.requestedVersion === versionSelection.requestedVersion.trim()));
  const matchingVersionPreflight = versionEvidenceMatches ? review.versionPreflight : undefined;
  const currentSelectionMatches = review.intent.action === intent.action
    && review.intent.slotName === intent.slotName
    && review.intent.expectedPublicationId === intent.expectedPublicationId
    && review.versionSelection.mode === versionSelection.mode
    && (versionSelection.mode === "automatic"
      || review.versionSelection.mode === "exact"
      && review.versionSelection.requestedVersion.trim() === versionSelection.requestedVersion.trim());
  const closeOnEscape = !busy && review.phase !== "publishing" ? onCancel : null;
  useDialogFocus(dialogRef, closeOnEscape);

  useEffect(() => {
    if (!onReview || !editableReview || !channelIsValid || review.validationErrors.length > 0) return;
    if (review.reviewFailed && currentSelectionMatches) return;
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
  const occupancy = publicationChannelOccupancy(review, selectedChannel);
  const changes = publicationChangesFor(review, activeSlot?.slotName ?? "");
  const preflightChanges = reviewedPreflight?.triggers ?? reviewedPreflight?.changes ?? [];
  const triggerCounts = preflightChanges.reduce((counts, change) => {
    counts[change.change]++;
    return counts;
  }, { added: 0, removed: 0, retained: 0 });
  const triggerSummary = reviewedPreflight
    ? Object.entries(triggerCounts).filter(([, count]) => count > 0)
      .map(([kind, count]) => `${count} ${kind}`).join(", ") || "No trigger changes."
    : "Not checked";
  const summaryChanges = changes
    ? { ...changes, triggers: { added: reviewedPreflight ? triggerCounts.added : 0, removed: reviewedPreflight ? triggerCounts.removed : 0, changed: 0 } }
    : null;
  const versionIssues = matchingVersionPreflight?.issues ?? [];
  // A host predating elsa-foundation#1659 can still block without reporting why: no targetSlotOwner
  // and no conflicts. Say so honestly instead of rendering neither blocker and leaving the author
  // pointed at nothing.
  const blockCause = reviewedPreflight ? publicationBlockCause(reviewedPreflight) : null;
  const causeNeutralBlockMessage = reviewedPreflight && blockCause === "unknown"
    ? publicationBlockedMessage(reviewedPreflight)
    : undefined;
  const blocked = review.validationErrors.length > 0
    || !channelIsValid
    || review.reviewPending
    || !reviewedPreflight
    || !reviewedPreflight.canActivate
    || review.versionPreflightSupported && !versionEvidenceMatches
    || matchingVersionPreflight?.isReady === false;
  const checkingVersionEvidence = Boolean(onReview)
    && editableReview
    && channelIsValid
    && review.validationErrors.length === 0
    && !review.reviewFailed
    && !review.failureMessage
    && review.versionPreflightSupported
    && !versionEvidenceMatches
    && reviewedPreflight?.canActivate !== false;
  const statusMessage = publicationStatusMessage(review, blocked, causeNeutralBlockMessage, checkingVersionEvidence);
  const submitDisabled = busy || (review.phase === "partialFailure"
    ? Boolean(review.reviewPending)
    : review.phase === "savedFailure"
      ? review.validationErrors.length > 0
      : blocked);

  useEffect(() => {
    if (review.phase !== "success" && review.phase !== "partialFailure" && review.phase !== "savedFailure") return;
    dialogRef.current?.querySelector<HTMLElement>(".wf-publication-outcome h4")?.focus();
  }, [review.phase]);

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
          if (submitDisabled) return;
          void onPublish(intent, versionSelection);
        }}>
          <header className="wf-dialog-heading wf-publication-header">
            <div>
              <h3 id="publication-review-title">
                {review.phase === "success"
                  ? "Published"
                  : review.phase === "partialFailure" || review.phase === "savedFailure"
                    ? "Couldn’t publish"
                  : "Publish workflow"}
              </h3>
              {review.phase === "success" || review.phase === "partialFailure" || review.phase === "savedFailure"
                ? null
                : <p>
                    {(reviewedPreflight?.resolvedAction
                      ? reviewedPreflight.resolvedAction === "replace"
                      : occupancy.kind === "publication")
                      ? <>Update the published workflow{selectedChannel !== review.policy.defaultSlotName
                          ? <> in <strong>{selectedChannel}</strong></>
                          : " with your latest changes"}.</>
                      : selectedChannel && selectedChannel !== review.policy.defaultSlotName
                      ? <>Publish your changes to <strong>{selectedChannel}</strong>.</>
                      : "Make your latest changes available to run."}
                  </p>}
            </div>
          </header>

          <div className="wf-publication-body">
            <output
              id="publication-review-status"
              className="wf-publication-status"
              data-phase={review.phase}
              data-visible={review.phase !== "success"
                && review.phase !== "partialFailure"
                && review.phase !== "savedFailure"
                && (review.phase === "publishing" || busy || Boolean(review.reviewPending) || blocked)}
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
                {review.slotsUnavailableReason ? (
                  <p className="wf-dialog-note" role="note">{review.slotsUnavailableReason}</p>
                ) : null}
                {versionSelection.mode === "exact" ? (
                  <p className="wf-publication-selection">
                    <strong>Exact version:</strong>{" "}
                    {versionSelection.requestedVersion.trim() || "Enter a version in Publishing options."}
                  </p>
                ) : null}

                {channelValidationMessage ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Publication channel required</strong>
                    <p>{channelValidationMessage}</p>
                  </div>
                ) : null}

                {review.validationErrors.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Fix these issues before publishing</strong>
                    <ul>{review.validationErrors.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}</ul>
                  </div>
                ) : null}

                <PublicationWarnings warnings={review.validationWarnings} />

                {versionIssues.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Choose a valid version</strong>
                    <ul>{versionIssues.map(issue => <li key={`${issue.code}-${issue.message}`}>{issue.message}</li>)}</ul>
                  </div>
                ) : null}

                {reviewedPreflight?.targetSlotOwner ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>This channel is managed elsewhere</strong>
                    <p>
                      {selectedChannel || reviewedPreflight.slotName} is managed by{" "}
                      {describeActivationSource(reviewedPreflight.targetSlotOwner)}.
                      Choose another channel to publish from Studio.
                    </p>
                  </div>
                ) : null}

                {reviewedPreflight?.conflicts.length ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>These triggers are already in use</strong>
                    <ul>
                      {[...new Set(reviewedPreflight.conflicts.map(conflict => conflict.slotName))].map(channel => {
                        const channelConflicts = uniquePublicationConflicts(
                          reviewedPreflight.conflicts.filter(conflict => conflict.slotName === channel));
                        return (
                          <li key={channel}>
                            {channelConflicts.length} {channelConflicts.length === 1 ? "trigger conflicts" : "triggers conflict"} with channel <strong>{channel}</strong>.
                            <ul>
                              {channelConflicts.map(conflict => (
                                <li key={conflict.key}>
                                  <div>{publicationConflictLabel(conflict)}</div>
                                  <small className="wf-publication-selection">{publicationConflictKeyDetail(conflict)}</small>
                                </li>
                              ))}
                            </ul>
                          </li>
                        );
                      })}
                    </ul>
                  </div>
                ) : null}

                {causeNeutralBlockMessage ? (
                  <div className="wf-publication-risks" role="alert">
                    <strong>Publication blocked</strong>
                    <p>{causeNeutralBlockMessage}</p>
                  </div>
                ) : null}

                {review.failureMessage ? (
                  <div className="wf-publication-recovery" role="alert">
                    <p>{review.failureMessage}</p>
                    {review.reviewFailed && onReview ? (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void onReview(review, intent, versionSelection)}
                      >
                        Retry review
                      </button>
                    ) : null}
                  </div>
                ) : review.reviewFailed ? (
                  <div className="wf-publication-recovery" role="alert">
                    <p>Couldn’t check this workflow. Try again.</p>
                    {onReview ? (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void onReview(review, intent, versionSelection)}
                      >
                        Retry review
                      </button>
                    ) : null}
                  </div>
                ) : null}

                <div className="wf-publication-overview">
                  {versionSelection.mode === "automatic" ? <p className="wf-publication-selection">Version <strong>{matchingVersionPreflight?.resolvedVersion || review.proposedVersion}</strong></p> : null}
                  <p className="wf-publication-change-summary"><strong>Changes:</strong> {summaryChanges ? compactChangeSummary(summaryChanges, !!reviewedPreflight) : comparisonUnavailableMessage(occupancy, selectedChannel)}</p>
                  {!reviewedPreflight || !changes ? <p className="wf-publication-change-summary">{reviewedPreflight ? `Trigger changes: ${triggerSummary}` : "Trigger changes have not been checked."}</p> : null}
                </div>

                <details className="wf-publication-disclosure">
                  <summary>Review changes</summary>
                  <div className="wf-publication-disclosure-body">
                    <p className="wf-publication-baseline">
                      Compared with <strong>{publicationBaselineFor(review, selectedChannel)}</strong>
                    </p>
                    <dl className="wf-publication-detail-grid">
                      <ChangeSummary label="Activities" value={changes?.activities} />
                      <ChangeSummary label="Inputs" value={changes?.inputs} />
                      <ChangeSummary label="Outputs" value={changes?.outputs} />
                      <div><dt>Triggers</dt><dd>{triggerSummary}</dd></div>
                    </dl>
                  </div>
                </details>

                <details className="wf-publication-disclosure">
                  <summary>Publishing options</summary>
                  <div className="wf-publication-disclosure-body wf-publication-decision">
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
                      <small>Use a named channel for a separate publication of this workflow.</small>
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
                          aria-invalid={!channelIsValid}
                        />
                      </label>
                    ) : null}

                    {review.exactVersionSupported ? (
                      <fieldset className="wf-publication-version-options" disabled={!editableReview || busy}>
                        <legend>Version assignment</legend>
                        {versionSelection.mode === "automatic" ? (
                          <p className="wf-publication-selection">
                            Next version: <strong>{matchingVersionPreflight?.resolvedVersion || review.proposedVersion}</strong>
                          </p>
                        ) : null}
                        <label className="wf-publication-version-option">
                          <input
                            type="radio"
                            name="publication-version-mode"
                            checked={versionSelection.mode === "automatic"}
                            onChange={() => setVersionSelection({ mode: "automatic" })}
                          />
                          <span><strong>Automatic</strong><small>Use the version policy.</small></span>
                        </label>
                        <label className="wf-publication-version-option">
                          <input
                            type="radio"
                            name="publication-version-mode"
                            checked={versionSelection.mode === "exact"}
                            onChange={() => setVersionSelection({
                              mode: "exact",
                              requestedVersion: versionSelection.mode === "exact"
                                ? versionSelection.requestedVersion
                                : ""
                            })}
                          />
                          <span><strong>Exact version</strong><small>Must be unused and newer than the latest promoted version.</small></span>
                        </label>
                        {versionSelection.mode === "exact" ? (
                          <label className="wf-form-field">
                            <span>Exact semantic version</span>
                            <input
                              aria-label="Exact semantic version"
                              value={versionSelection.requestedVersion}
                              disabled={!editableReview || busy}
                              aria-invalid={versionIssues.length > 0}
                              onChange={event => setVersionSelection({
                                mode: "exact",
                                requestedVersion: event.target.value
                              })}
                              placeholder="2.1.0 or 2.1.0-rc.1"
                            />
                          </label>
                        ) : null}
                      </fieldset>
                    ) : null}
                  </div>
                </details>
                <details className="wf-publication-disclosure wf-publication-technical">
                  <summary>Technical details</summary>
                  <dl className="wf-publication-detail-grid">
                    <DecisionFact label="Trigger changes" value={preflightChanges.length
                      ? preflightChanges.map(change => `${change.change} ${change.key} (${change.cardinality})`).join("; ")
                      : "No trigger changes."} />
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
                  {reviewedPreflight?.conflicts.length ? <div className="wf-publication-claims">
                    <strong>Trigger conflict identifiers</strong>
                    <ul>{reviewedPreflight.conflicts.map(conflict => <li key={`${conflict.publicationId}-${conflict.key}`}>
                      {publicationConflictLabel(conflict)} · <code>{publicationConflictKeyDetail(conflict)}</code>
                      {" "}· channel {conflict.slotName} · publication <code>{conflict.publicationId}</code>
                    </li>)}</ul>
                  </div> : null}
                </details>
              </>
            )}
          </div>

          <footer className="wf-dialog-actions wf-publication-footer">
            <button
              type="button"
              className={review.phase === "success" ? "wf-primary-action" : undefined}
              onClick={onCancel}
              disabled={busy}
            >
              {review.phase === "review" || review.phase === "validationBlocked" ? "Cancel" : "Close"}
            </button>
            {review.phase === "success" ? (
              <button type="button" onClick={onOpenPublishedExecutable}>
                View published workflow
              </button>
            ) : review.phase === "partialFailure" ? (
              <button type="submit" disabled={submitDisabled}>
                Retry publication
              </button>
            ) : review.phase === "savedFailure" ? (
              review.validationErrors.length === 0
                ? <button type="submit" disabled={submitDisabled}>Retry publication</button>
                : null
            ) : (
              <button type="submit" disabled={submitDisabled}>
                {busy && review.phase === "publishing" ? "Publishing…" : "Publish"}
              </button>
            )}
          </footer>
        </form>
      </section>
    </div>
  );
}

// Spec 261 FR-515: warnings are listed with text and an icon, and never disable Publish.
function PublicationWarnings({ warnings }: { warnings: string[] }) {
  if (!warnings?.length) return null;
  return (
    <section className="wf-publication-warnings" aria-labelledby="publication-warnings-title">
      <h4 id="publication-warnings-title">
        <span className="wf-publication-warnings-mark" aria-hidden="true">⚠</span> Warnings
      </h4>
      <p>These do not block publishing. Review them before you publish.</p>
      <ul>{warnings.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}</ul>
    </section>
  );
}

function PublicationSuccess({ review }: { review: PublicationReviewState }) {
  return (
    <section className="wf-publication-outcome wf-publication-outcome-success" aria-labelledby="publication-success-title">
      <div className="wf-publication-outcome-mark" aria-hidden="true">✓</div>
      <div>
        <h4 id="publication-success-title" tabIndex={-1}>
          Version {review.proposedVersion} is active in {review.published?.slotName}.
        </h4>
      </div>
      <details className="wf-publication-disclosure">
        <summary>Technical details</summary>
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
  const channel = review.intent.slotName || review.policy.defaultSlotName;
  return (
    <section className="wf-publication-outcome" aria-labelledby="publication-recovery-title">
      <h4 id="publication-recovery-title" tabIndex={-1}>
        Version {review.proposedVersion} was retained. Channel {channel} was not activated.
      </h4>
      {review.failureMessage ? <p>{review.failureMessage}</p> : null}
    </section>
  );
}

function PublicationSavedFailure({ review }: { review: PublicationReviewState }) {
  return (
    <section className="wf-publication-outcome" aria-labelledby="publication-saved-failure-title">
      <h4 id="publication-saved-failure-title" tabIndex={-1}>Draft saved</h4>
      <p>{review.failureMessage ?? "Your draft was saved, but no version was created or published."}</p>
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

function ChangeSummary({ label, value }: { label: string; value?: PublicationChangeCount }) {
  return <div><dt>{label}</dt><dd>{formatChangeCount(value)}</dd></div>;
}

function formatChangeCount(value?: PublicationChangeCount) {
  if (!value) return "Not compared";
  return [
    value.added ? `${value.added} added` : "",
    value.changed ? `${value.changed} changed` : "",
    value.removed ? `${value.removed} removed` : ""
  ].filter(Boolean).join(", ") || "No changes";
}

function comparisonUnavailableMessage(occupancy: PublicationChannelOccupancy, channel: string) {
  return occupancy.kind === "foreign"
    ? `Not compared: ${channel} is occupied by an activation from ${describeActivationSource(occupancy.slot)}, not by a Studio design version.`
    : "Not compared: the current publication in this channel is unknown on this backend.";
}

function compactChangeSummary(changes: PublicationReviewState["changes"], triggersChecked: boolean) {
  const categories = [
    { single: "activity", plural: "activities", value: changes.activities },
    { single: "input", plural: "inputs", value: changes.inputs },
    { single: "output", plural: "outputs", value: changes.outputs },
    { single: "trigger", plural: "triggers", value: changes.triggers }
  ];
  return categories.flatMap(({ single, plural, value }) => {
    const count = value.added + value.changed + value.removed;
    return count ? [`${count} ${count === 1 ? single : plural}`] : [];
  }).join(" · ") || (triggersChecked ? "No structural changes." : "No activity, input or output changes.");
}

function publicationStatusMessage(
  review: PublicationReviewState,
  blocked: boolean,
  causeNeutralBlockMessage?: string,
  checkingVersionEvidence = false
) {
  if (review.phase === "success" || review.phase === "partialFailure" || review.phase === "savedFailure") return "";
  if (review.phase === "validationBlocked") return "Fix the highlighted issues before publishing.";
  if (review.phase === "publishing") {
    const messages = {
      saving: "Saving changes…",
      promoting: "Creating a version…",
      preflight: "Checking for conflicts…",
      publishing: "Publishing…"
    };
    return review.progressStep ? messages[review.progressStep] : "Publishing…";
  }
  if (review.reviewPending || checkingVersionEvidence) return "Checking workflow…";
  if (blocked) return causeNeutralBlockMessage ?? "Review the highlighted issue before publishing.";
  return "Ready to publish.";
}
