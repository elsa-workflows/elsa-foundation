import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityDefinitionVersionManagementView,
  ActivityDefinitionVersionView,
  RecommendedActivityDefinition
} from "../activityDefinitionTypes";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";
import {
  useActivityDefinitionVersions,
  useFullActivityDefinitionVersion
} from "../api/activityDesign";
import { useActivityVersionDiff } from "../api/activityVersionChange";
import {
  analyzeActivityVersionChange,
  createActivityVersionChangePrecondition,
  type ActivityVersionChangePrecondition,
  type ActivityVersionChangeScope
} from "./activityVersionChangeModel";
import { useDialogFocus } from "./useDialogFocus";
import versionChangeStylesUrl from "./ActivityVersionChangeDialog.css?url&no-inline";

const versionChangeStylesHref = import.meta.env.PROD && versionChangeStylesUrl.startsWith("/")
  ? new URL(versionChangeStylesUrl.slice(1), import.meta.url).href
  : versionChangeStylesUrl;

export interface ActivityVersionChangeApplyRequest {
  precondition: ActivityVersionChangePrecondition;
  targetVersionId: string;
  scope: ActivityVersionChangeScope;
}

export function ActivityVersionChangeDialog({
  context,
  draft,
  occurrence,
  current,
  recommendation,
  onApply,
  onCancel
}: {
  context: StudioEndpointContext;
  draft: WorkflowDraft;
  occurrence: ActivityNode;
  current: ActivityDefinitionVersionView;
  recommendation?: RecommendedActivityDefinition | null;
  onApply(request: ActivityVersionChangeApplyRequest): Promise<void>;
  onCancel(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [cursorHistory, setCursorHistory] = useState<Array<string | null>>([]);
  const [targetVersionId, setTargetVersionId] = useState<string | null>(null);
  const [scope, setScope] = useState<ActivityVersionChangeScope>("occurrence");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [precondition] = useState(() => createActivityVersionChangePrecondition(draft, occurrence));
  const versions = useActivityDefinitionVersions(context, {
    definitionId: current.definition.definitionId,
    limit: 25,
    cursor
  });
  const activeTargets = useMemo(
    () => (versions.data?.items ?? []).filter(item =>
      item.version.versionId !== current.versionId
      && item.version.lifecycle.toLowerCase() === "active"),
    [current.versionId, versions.data?.items]
  );
  const target = useFullActivityDefinitionVersion(context, targetVersionId);
  const diff = useActivityVersionDiff(context, current.versionId, targetVersionId);
  const impact = target.data && diff.data
    ? analyzeActivityVersionChange(draft, occurrence, current, target.data, diff.data, scope)
    : null;

  useDialogFocus(dialogRef, busy ? null : onCancel);

  useEffect(() => {
    if (targetVersionId && activeTargets.some(item => item.version.versionId === targetVersionId)) return;
    const preferred = activeTargets.find(item => item.version.versionId === recommendation?.versionId)
      ?? activeTargets[0];
    setTargetVersionId(preferred?.version.versionId ?? null);
  }, [activeTargets, recommendation?.versionId, targetVersionId]);

  const canApply = Boolean(
    target.data
    && diff.data
    && diff.data.from.versionId === current.versionId
    && diff.data.to.versionId === target.data.versionId
    && impact?.occurrenceIds.length
  );

  return (<>
    <link rel="stylesheet" href={versionChangeStylesHref} />
    <div className="wf-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="wf-dialog wf-version-change-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="activity-version-change-title"
        tabIndex={-1}
      >
        <form onSubmit={event => {
          event.preventDefault();
          if (!targetVersionId || !canApply) return;
          setBusy(true);
          setError("");
          void onApply({ precondition, targetVersionId, scope })
            .catch(cause => setError(cause instanceof Error ? cause.message : String(cause)))
            .finally(() => setBusy(false));
        }}>
          <header className="wf-dialog-heading">
            <div>
              <span>Current workflow draft</span>
              <h3 id="activity-version-change-title">Review exact Activity Definition Version change</h3>
            </div>
          </header>

          <dl className="wf-version-change-facts">
            <div><dt>Definition</dt><dd>{current.definition.definitionId}</dd></div>
            <div><dt>Selected occurrence</dt><dd>{occurrence.nodeId}</dd></div>
            <div><dt>Current exact version</dt><dd>v{current.version} · {current.versionId}</dd></div>
            <div><dt>Target exact version</dt><dd>{target.data ? `v${target.data.version} · ${target.data.versionId}` : "Select an active version"}</dd></div>
          </dl>

          <fieldset disabled={busy || versions.isPending}>
            <legend>Active target version</legend>
            {activeTargets.length ? activeTargets.map(item => (
              <VersionChoice
                key={item.version.versionId}
                item={item}
                checked={targetVersionId === item.version.versionId}
                recommended={item.version.versionId === recommendation?.versionId}
                onChange={() => setTargetVersionId(item.version.versionId)}
              />
            )) : (
              <p className="wf-muted">{versions.isPending ? "Loading active versions…" : "No other active version is available."}</p>
            )}
            <div className="wf-version-page-actions">
              <button
                type="button"
                disabled={busy || cursorHistory.length === 0}
                onClick={() => {
                  const previous = cursorHistory[cursorHistory.length - 1] ?? null;
                  setCursorHistory(history => history.slice(0, -1));
                  setCursor(previous);
                }}
              >
                Previous versions
              </button>
              <button
                type="button"
                disabled={busy || !versions.data?.continuation}
                onClick={() => {
                  setCursorHistory(history => [...history, cursor]);
                  setCursor(versions.data?.continuation ?? null);
                }}
              >
                More versions
              </button>
            </div>
          </fieldset>

          <fieldset disabled={busy}>
            <legend>Apply scope</legend>
            <label>
              <input type="radio" name="version-change-scope" checked={scope === "occurrence"} onChange={() => setScope("occurrence")} />
              This occurrence only
            </label>
            <label>
              <input type="radio" name="version-change-scope" checked={scope === "matching"} onChange={() => setScope("matching")} />
              All matching occurrences in this workflow draft
            </label>
          </fieldset>

          {diff.isPending || target.isPending ? <p role="status">Loading authoritative compatibility evidence…</p> : null}
          {diff.data && impact ? (
            <>
              <section className="wf-version-change-section" aria-labelledby="version-compatibility-title">
                <h4 id="version-compatibility-title">Compatibility and public contract</h4>
                <dl>
                  <div><dt>Compatibility</dt><dd>{diff.data.compatibility}</dd></div>
                  <div><dt>Required bump</dt><dd>{diff.data.requiredBump}</dd></div>
                  <div><dt>Behavior changed</dt><dd>{diff.data.behaviorChanged ? "Yes" : "No"}</dd></div>
                  <div><dt>Contract changes</dt><dd>{diff.data.summary.breaking} breaking, {diff.data.summary.additive} additive, {diff.data.summary.nonBehavioral} non-behavioral</dd></div>
                  <div><dt>Occurrences</dt><dd>{impact.occurrenceIds.length}</dd></div>
                </dl>
                {diff.data.changes.length ? (
                  <ul className="wf-version-change-list">
                    {diff.data.changes.map(change => (
                      <li key={change.changeId}>
                        <strong>{change.subject.memberKind ?? change.area}{change.subject.referenceKey ? ` · ${change.subject.referenceKey}` : ""}</strong>
                        <span>{change.message}</span>
                      </li>
                    ))}
                  </ul>
                ) : <p>No public-contract changes.</p>}
              </section>

              <ImpactSection
                title="Binding impact"
                preserved={impact.preservedBindingKeys}
                unresolved={impact.unresolvedBindingKeys}
                empty="No authored input bindings on the selected occurrence."
              />
              <ImpactSection
                title="Outcome and connection impact"
                preserved={impact.preservedOutcomeKeys}
                unresolved={impact.unresolvedOutcomeKeys}
                empty="No outgoing outcome connections on the selected scope."
              />
              <p className="wf-dialog-note">
                Bindings and connections are retained only by stable reference key. Removed or incompatible members remain attached as explicit unresolved authoring work; Studio never guesses a replacement.
              </p>
              <p className="wf-dialog-note">
                This operation updates draft {draft.id} only. Published workflow artifacts and every other draft remain immutable.
              </p>
            </>
          ) : null}

          {versions.isError || target.isError || diff.isError ? (
            <p className="wf-publication-recovery" role="alert">Authorized exact-version evidence could not be loaded. Try the review again.</p>
          ) : null}
          {error ? <p className="wf-publication-recovery" role="alert">{error}</p> : null}

          <div className="wf-dialog-actions">
            <button type="button" disabled={busy} onClick={onCancel}>Cancel</button>
            <button type="submit" disabled={busy || !canApply}>
              {busy ? "Applying…" : `Apply to ${scope === "occurrence" ? "this occurrence" : `${impact?.occurrenceIds.length ?? 0} occurrences`}`}
            </button>
          </div>
        </form>
      </section>
    </div>
  </>);
}

function VersionChoice({
  item,
  checked,
  recommended,
  onChange
}: {
  item: ActivityDefinitionVersionManagementView;
  checked: boolean;
  recommended: boolean;
  onChange(): void;
}) {
  return (
    <label className="wf-version-choice">
      <input type="radio" name="target-activity-version" checked={checked} onChange={onChange} />
      <span>
        <strong>v{item.version.version}</strong>
        <small>{item.version.versionId} · Active{recommended ? " · Recommended" : ""}</small>
      </span>
    </label>
  );
}

function ImpactSection({
  title,
  preserved,
  unresolved,
  empty
}: {
  title: string;
  preserved: string[];
  unresolved: string[];
  empty: string;
}) {
  return (
    <section className="wf-version-change-section">
      <h4>{title}</h4>
      {!preserved.length && !unresolved.length ? <p>{empty}</p> : (
        <dl>
          <div><dt>Retained by stable key</dt><dd>{preserved.length ? preserved.join(", ") : "None"}</dd></div>
          <div><dt>Unresolved after apply</dt><dd>{unresolved.length ? unresolved.join(", ") : "None"}</dd></div>
        </dl>
      )}
    </section>
  );
}
