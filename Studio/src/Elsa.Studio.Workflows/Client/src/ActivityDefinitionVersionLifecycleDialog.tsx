import { useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionManagementView,
  ActivityDefinitionVersionLifecycle,
  ActivityDefinitionVersionLifecycleAction,
  ActivityDefinitionVersionManagementView,
  ActivityRecommendationDecision
} from "./activityDefinitionTypes";
import { isActivityDefinitionEnumValue } from "./activityDefinitionTypes";
import {
  activityDesignKeys,
  getActivityDefinition,
  getActivityDefinitionVersion
} from "./api/activityDesign";
import {
  changeActivityDefinitionVersionLifecycle,
  getActivityDefinitionRevocationEvidence,
  listAllActivityDefinitionVersions,
  setActivityDefinitionRecommendation
} from "./api/activityDefinitionLifecycle";
import { ActivityDefinitionManagementDialogShell } from "./ActivityDefinitionManagementDialogShell";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";

export function ActivityDefinitionVersionLifecycleDialog({
  context,
  definition,
  version,
  action,
  onClose,
  onApplied
}: {
  context: StudioEndpointContext;
  definition: ActivityDefinitionManagementView;
  version: ActivityDefinitionVersionManagementView;
  action: ActivityDefinitionVersionLifecycleAction;
  onClose(): void;
  onApplied(): void;
}) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [recommendationDisposition, setRecommendationDisposition] = useState<"Replace" | "Clear">("Replace");
  const [replacementVersionId, setReplacementVersionId] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [reviewed, setReviewed] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef, submitting ? null : onClose);

  const candidatesQuery = useQuery({
    queryKey: [...activityDesignKeys.definitionVersions({ definitionId: definition.definition.definitionId, limit: 100 }), "lifecycle-candidates"],
    queryFn: ({ signal }) => listAllActivityDefinitionVersions(context, definition.definition.definitionId, signal),
    retry: false
  });
  const evidenceQuery = useQuery({
    queryKey: [...activityDesignKeys.fullDefinitionVersion(version.version.versionId), "usage-evidence"],
    queryFn: ({ signal }) => getActivityDefinitionRevocationEvidence(context, version.version.versionId, signal),
    enabled: action === "revoke",
    retry: false
  });
  const replacements = useMemo(() => (candidatesQuery.data ?? []).filter(item =>
    item.version.versionId !== version.version.versionId &&
    isActivityDefinitionEnumValue(item.version.lifecycle, "Active")
  ), [candidatesQuery.data, version.version.versionId]);
  const effectiveReplacementId = replacementVersionId || replacements[0]?.version.versionId || "";
  const invalidatesRecommendation = version.isRecommended && (action === "retire" || action === "revoke");
  const expectedTargetLifecycle = version.version.lifecycle as ActivityDefinitionVersionLifecycle;
  const targetLifecycle = action === "restore" ? "Active" : action === "retire" ? "Retired" : action === "revoke" ? "Revoked" : expectedTargetLifecycle;
  const evidenceReady = action !== "revoke" || Boolean(
    evidenceQuery.data?.directDependencies.consistency.isAuthoritative &&
    evidenceQuery.data.inboundUsage.consistency.asOf
  );
  const dispositionReady = !invalidatesRecommendation ||
    recommendationDisposition === "Clear" ||
    Boolean(effectiveReplacementId);
  const canSubmit = !submitting &&
    reason.trim().length > 0 &&
    reviewed &&
    dispositionReady &&
    evidenceReady &&
    (action !== "revoke" || confirmation === version.version.version);

  const recommendationDecision = (): ActivityRecommendationDecision | null => {
    if (!invalidatesRecommendation) return null;
    if (recommendationDisposition === "Clear") return {
      expectedDefinitionHeadVersionId: definition.lifecycle.head?.versionId ?? null,
      expectedRecommendedVersionId: definition.lifecycle.recommendation?.versionId ?? null,
      disposition: "Clear"
    };
    return {
      expectedDefinitionHeadVersionId: definition.lifecycle.head?.versionId ?? null,
      expectedRecommendedVersionId: definition.lifecycle.recommendation?.versionId ?? null,
      disposition: "Replace",
      replacementVersionId: effectiveReplacementId,
      expectedReplacementLifecycle: "Active"
    };
  };

  const submit = async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    try {
      if (action === "recommend") {
        const result = await setActivityDefinitionRecommendation(context, definition.definition.definitionId, {
          expectedDefinitionHeadVersionId: definition.lifecycle.head?.versionId ?? null,
          expectedRecommendedVersionId: definition.lifecycle.recommendation?.versionId ?? null,
          recommendedVersionId: version.version.versionId,
          expectedRecommendedVersionLifecycle: expectedTargetLifecycle,
          reason: reason.trim()
        });
        if (result.definitionId !== definition.definition.definitionId ||
            result.headVersionId !== (definition.lifecycle.head?.versionId ?? null) ||
            result.recommendedVersionId !== version.version.versionId) {
          throw new Error("The recommendation response did not match the exact reviewed definition state.");
        }
      } else {
        const result = await changeActivityDefinitionVersionLifecycle(context, version.version.versionId, action, {
          expectedLifecycle: expectedTargetLifecycle,
          reason: reason.trim(),
          recommendationDecision: recommendationDecision()
        });
        if (result.versionId !== version.version.versionId || result.lifecycle !== targetLifecycle) {
          throw new Error("The lifecycle response did not match the exact reviewed version transition.");
        }
      }
      await queryClient.invalidateQueries({ queryKey: activityDesignKeys.all });
      onApplied();
    } catch (cause) {
      if (isAmbiguousFailure(cause) && await reconcilesAppliedState(
        context,
        definition,
        version,
        action,
        targetLifecycle,
        recommendationDecision()
      )) {
        await queryClient.invalidateQueries({ queryKey: activityDesignKeys.all });
        onApplied();
        return;
      }
      setError(lifecycleMutationError(cause));
    } finally {
      setSubmitting(false);
    }
  };

  return <ActivityDefinitionManagementDialogShell
    dialogRef={dialogRef}
    title={dialogTitle(action)}
    kicker="Reviewed exact-version mutation"
    submitting={submitting}
    onClose={onClose}
  >
    <dl className="ad-review-facts" aria-label="Exact lifecycle binding">
      <div><dt>Definition</dt><dd><code>{definition.definition.definitionId}</code></dd></div>
      <div><dt>Version</dt><dd>{version.version.version} · <code>{version.version.versionId}</code></dd></div>
      <div><dt>Expected lifecycle</dt><dd>{expectedTargetLifecycle}</dd></div>
      <div><dt>Expected head</dt><dd><code>{definition.lifecycle.head?.versionId ?? "No head"}</code></dd></div>
      <div><dt>Current recommendation</dt><dd><code>{definition.lifecycle.recommendation?.versionId ?? "No recommendation"}</code></dd></div>
    </dl>

    <section className="ad-impact-group is-warning" aria-labelledby="lifecycle-impact-title">
      <h3 id="lifecycle-impact-title">Consequences</h3>
      <ul>{consequences(action).map(item => <li key={item}>{item}</li>)}</ul>
    </section>

    {invalidatesRecommendation ? <fieldset className="ad-choice-grid">
      <legend>Required recommendation decision</legend>
      <label><input type="radio" name="recommendation-disposition" checked={recommendationDisposition === "Replace"} onChange={() => setRecommendationDisposition("Replace")} /> Replace with another active version</label>
      {recommendationDisposition === "Replace" ? candidatesQuery.isPending
        ? <div className="ad-inline-status" role="status">Loading exact active replacement versions…</div>
        : candidatesQuery.isError
          ? <div className="ad-inline-error" role="alert">Replacement versions could not be confirmed. Choose no recommendation or retry.</div>
          : replacements.length
            ? <label className="ad-dialog-field"><span>Exact replacement</span><select value={effectiveReplacementId} onChange={event => setReplacementVersionId(event.target.value)}>{replacements.map(item => <option key={item.version.versionId} value={item.version.versionId}>{item.version.version} · {item.version.versionId}</option>)}</select></label>
            : <div className="ad-inline-status" role="status">No other active version is available.</div>
        : null}
      <label><input type="radio" name="recommendation-disposition" checked={recommendationDisposition === "Clear"} onChange={() => setRecommendationDisposition("Clear")} /> Continue with no recommended version</label>
    </fieldset> : null}

    {action === "revoke" ? <section className="ad-impact-group is-warning" aria-labelledby="revocation-evidence-title">
      <h3 id="revocation-evidence-title">Dependency and usage evidence</h3>
      {evidenceQuery.isPending ? <div className="ad-inline-status" role="status">Loading authoritative direct dependencies and bounded as-of usage…</div> : null}
      {evidenceQuery.isError ? <div className="ad-inline-error" role="alert">Dependency and usage evidence is unavailable. Revocation remains disabled.</div> : null}
      {evidenceQuery.data ? evidenceQuery.data.directDependencies.consistency.isAuthoritative && evidenceQuery.data.inboundUsage.consistency.asOf
        ? <><dl className="ad-review-facts"><div><dt>Direct dependency authority</dt><dd>{evidenceQuery.data.directDependencies.consistency.kind}</dd></div><div><dt>Exact direct dependencies</dt><dd>{evidenceQuery.data.directDependencies.items.length}</dd></div><div><dt>Usage snapshot as of</dt><dd>{formatTimestamp(evidenceQuery.data.inboundUsage.consistency.asOf)}</dd></div><div><dt>Visible inbound uses</dt><dd>{evidenceQuery.data.inboundUsage.items.length}</dd></div><div><dt>Usage evidence kind</dt><dd>{evidenceQuery.data.inboundUsage.consistency.kind}</dd></div><div><dt>Usage sequence</dt><dd>{evidenceQuery.data.inboundUsage.consistency.asOfSequence ?? "Not supplied"}</dd></div></dl><p>The API distinguishes authoritative immutable direct dependencies from the rebuildable inbound/transitive usage projection. Counts are bounded to the exact visible as-of snapshot.</p></>
        : <div className="ad-inline-error" role="alert">The API did not return authoritative direct dependencies plus an as-of usage snapshot. Revocation remains disabled.</div> : null}
      <div className="ad-inline-error" role="alert"><strong>Terminal Host Policy consequence</strong><span> Revoked versions cannot be selected for new placements or used as closed dependencies. Immutable artifacts, identities, dependency evidence, and Runtime Evidence are preserved; no occurrence is upgraded automatically.</span></div>
      <label className="ad-dialog-field"><span>Type semantic version <code>{version.version.version}</code> to confirm</span><input value={confirmation} onChange={event => setConfirmation(event.target.value)} autoComplete="off" /></label>
    </section> : null}

    <label className="ad-dialog-field"><span>Reason <small>Required and recorded by the server</small></span><textarea value={reason} onChange={event => setReason(event.target.value)} rows={3} /></label>
    <label className="ad-contract-check"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> I reviewed the exact identities, lifecycle preconditions, recommendation decision, and consequences above.</label>
    {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
    <footer><button type="button" onClick={onClose} disabled={submitting}>Cancel</button><button type="button" className="ad-primary-action" onClick={() => void submit()} disabled={!canSubmit}>{submitting ? "Applying and reconciling…" : submitLabel(action)}</button></footer>
  </ActivityDefinitionManagementDialogShell>;
}

async function reconcilesAppliedState(
  context: StudioEndpointContext,
  definition: ActivityDefinitionManagementView,
  version: ActivityDefinitionVersionManagementView,
  action: ActivityDefinitionVersionLifecycleAction,
  targetLifecycle: string,
  decision: ActivityRecommendationDecision | null
) {
  try {
    const [currentDefinition, currentVersion] = await Promise.all([
      getActivityDefinition(context, definition.definition.definitionId),
      getActivityDefinitionVersion(context, version.version.versionId)
    ]);
    if (action === "recommend") {
      return currentDefinition.lifecycle.head?.versionId === definition.lifecycle.head?.versionId &&
        currentDefinition.lifecycle.recommendation?.versionId === version.version.versionId;
    }
    if (currentVersion.lifecycle !== targetLifecycle) return false;
    if (!decision) return true;
    const expectedRecommendation = decision.disposition === "Clear" ? null : decision.replacementVersionId ?? null;
    return (currentDefinition.lifecycle.recommendation?.versionId ?? null) === expectedRecommendation;
  } catch {
    return false;
  }
}

function isAmbiguousFailure(cause: unknown) {
  return !(cause instanceof StudioHttpError);
}

function lifecycleMutationError(cause: unknown) {
  if (cause instanceof StudioHttpError) {
    if (cause.status === 409) return "The definition head, recommendation, lifecycle, or usage state changed after review. Reload and review the transition again.";
    if (cause.status === 401 || cause.status === 403) return "This lifecycle action is not authorized. No hidden identity or usage count is disclosed.";
    if (cause.status === 404) return "The exact definition or version is no longer available. No lifecycle change was confirmed.";
  }
  return "The lifecycle change could not be confirmed or reconciled. The reviewed state remains unchanged in Studio; reload before retrying.";
}

function consequences(action: ActivityDefinitionVersionLifecycleAction) {
  if (action === "recommend") return [
    "New picker selections will resolve to this exact active version.",
    "Existing placed occurrences and immutable artifacts remain pinned and unchanged."
  ];
  if (action === "retire") return [
    "The version disappears from new picker selection.",
    "Already closed artifacts remain pinned and usable according to Host Policy.",
    "No occurrence is upgraded automatically."
  ];
  if (action === "restore") return [
    "The retired version becomes active and eligible for an explicit recommendation.",
    "Restoration does not recommend it automatically."
  ];
  return [
    "Revocation is terminal and removes the version from direct selection and closed-dependency use.",
    "Immutable artifacts and Runtime Evidence remain preserved.",
    "No occurrence is upgraded automatically."
  ];
}

function dialogTitle(action: ActivityDefinitionVersionLifecycleAction) {
  return action === "recommend" ? "Move recommended version" : action === "retire" ? "Retire activity version" : action === "restore" ? "Restore activity version" : "Revoke activity version";
}

function submitLabel(action: ActivityDefinitionVersionLifecycleAction) {
  return action === "recommend" ? "Move recommendation" : action === "retire" ? "Retire exact version" : action === "restore" ? "Restore exact version" : "Revoke exact version permanently";
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
