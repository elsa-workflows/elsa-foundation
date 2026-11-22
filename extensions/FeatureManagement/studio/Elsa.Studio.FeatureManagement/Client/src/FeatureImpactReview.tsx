import { useLayoutEffect, useRef } from "react";
import { StudioAlert, StudioDialog, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { getReloadImplication, normalizeType, type FeatureImpactEntry } from "./featureModel";
import { FeatureBadge } from "./FeatureBadges";

export function FeatureImpactReviewDialog({
  hostLabel,
  entries,
  dirty,
  applying,
  readOnly,
  error,
  onClose,
  onApply
}: {
  hostLabel: string;
  entries: FeatureImpactEntry[];
  dirty: boolean;
  applying: boolean;
  readOnly: boolean;
  error: string | null;
  onClose(): void;
  onApply(): void;
}) {
  const errorRef = useRef<HTMLDivElement | null>(null);

  useLayoutEffect(() => {
    if (error) {
      errorRef.current?.focus();
    }
  }, [error]);

  return (
    <StudioDialog open title="Review changes" onClose={onClose} dismissDisabled={applying} dismissOnOutsideClick={false}
      className="feature-management-impact-dialog" footer={<StudioDialogFooter onCancel={onClose} cancelDisabled={applying}
        confirmType="button" onConfirm={onApply} confirmLabel={applying ? "Applying…" : "Apply"} confirmDisabled={!dirty || applying || readOnly} />}>
        <div className="feature-management-dialog-body">
          {error ? (
            <div ref={errorRef} tabIndex={-1}>
              <StudioAlert tone="danger">{error}</StudioAlert>
            </div>
          ) : null}
          <p className="feature-management-impact-intro">
            This draft preview includes every changed feature for {hostLabel}, including features hidden by the current filters.
            It is not a validation result; the server validates the complete desired state when you apply it.
          </p>
          {entries.length > 0 ? (
            <div className="feature-management-impact-list">
              {entries.map(entry => <FeatureImpactCard key={entry.desired.id} entry={entry} />)}
            </div>
          ) : (
            <p className="feature-management-muted">No feature differences remain to review.</p>
          )}
        </div>

        <p className="feature-management-dialog-note">Reviewing {entries.length} changed feature{entries.length === 1 ? "" : "s"}.</p>
    </StudioDialog>
  );
}

function FeatureImpactCard({ entry }: { entry: FeatureImpactEntry }) {
  return (
    <article className="feature-management-impact-entry" data-impact-feature-id={entry.desired.id}>
      <div className="feature-management-impact-heading">
        <div>
          <h4>{entry.desired.displayName || entry.desired.id}</h4>
          <code>{entry.desired.id}</code>
        </div>
        <div className="feature-management-impact-kinds">
          {entry.direct ? <FeatureBadge tone="info">Direct</FeatureBadge> : null}
          {entry.automatic ? <FeatureBadge tone="violet">Automatic</FeatureBadge> : null}
        </div>
      </div>

      {entry.enabledChanged ? (
        <p className="feature-management-impact-state">
          Enabled: {entry.baseline.enabled ? "On" : "Off"} → {entry.desired.enabled ? "On" : "Off"}
        </p>
      ) : null}

      {entry.settingChanges.length > 0 ? (
        <section className="feature-management-impact-section" aria-labelledby={`feature-impact-settings-${normalizeType(entry.desired.id)}`}>
          <h5 id={`feature-impact-settings-${normalizeType(entry.desired.id)}`}>Changed settings</h5>
          <dl>
            {entry.settingChanges.map(setting => (
              <div key={setting.name}>
                <dt>{setting.displayName}</dt>
                <dd>{setting.before} <span aria-hidden="true">→</span> {setting.after}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : null}

      {entry.automatic ? (
        <section className="feature-management-impact-section" aria-labelledby={`feature-impact-paths-${normalizeType(entry.desired.id)}`}>
          <h5 id={`feature-impact-paths-${normalizeType(entry.desired.id)}`}>{entry.reasons.some(reason => reason.historical) ? "Earlier cascade" : "Required relationships"}</h5>
          {entry.reasons.length > 0 ? (
            <ul className="feature-management-impact-reasons">
              {entry.reasons.map(reason => (
                <li className="feature-management-impact-reason" key={`${reason.historical ? "history" : "current"}:${reason.rootId}:${reason.path.join("/")}`}>
                  <span>{reason.historical ? "Historical reason" : `Required from ${reason.rootId}`}</span>
                  <code>{reason.path.join(" → ")}</code>
                </li>
              ))}
            </ul>
          ) : (
            <p>No current required path is available for this automatic change.</p>
          )}
        </section>
      ) : null}

      <p className="feature-management-impact-reload">
        Reload implication: {getReloadImplication(entry)}
      </p>
    </article>
  );
}
