import { useLayoutEffect, useRef } from "react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton } from "@elsa-workflows/studio-ui";
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
  const dialogRef = useRef<HTMLElement | null>(null);
  const errorRef = useRef<HTMLDivElement | null>(null);
  useDialogFocus(dialogRef, applying ? null : onClose);

  useLayoutEffect(() => {
    if (error) {
      errorRef.current?.focus();
    }
  }, [error]);

  return (
    <div className="feature-management-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="feature-management-settings-dialog feature-management-impact-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Feature impact review"
        aria-labelledby="feature-management-impact-title"
        tabIndex={-1}
      >
        <div className="feature-management-dialog-heading">
          <div>
            <span>{hostLabel} feature configuration</span>
            <h3 id="feature-management-impact-title">Review changes</h3>
          </div>
          <StudioButton onClick={onClose} disabled={applying} data-dialog-initial-focus>Close</StudioButton>
        </div>

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

        <div className="feature-management-dialog-actions">
          <span>Reviewing {entries.length} changed feature{entries.length === 1 ? "" : "s"}.</span>
          <div>
            <StudioButton onClick={onClose} disabled={applying}>Cancel</StudioButton>
            <StudioButton variant="primary" onClick={onApply} disabled={!dirty || applying || readOnly}>
              {applying ? "Applying" : "Apply"}
            </StudioButton>
          </div>
        </div>
      </section>
    </div>
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
