import { useState } from "react";
import { FlaskConical, Package, ShieldAlert } from "lucide-react";
import type { ElsaStudioModuleApi, StudioSettingDescriptor } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type {
  DraftFeature,
  FeatureRelationshipGroup,
  FeatureRelationshipLink,
  FeatureRelationships
} from "./featureModel";
import { CategoryChips, FeatureBadge, SourceBadge } from "./FeatureBadges";
import { FeatureSettingsDialog, FeatureSettingsSummary } from "./FeatureSettings";

export function FeatureInspector({
  moduleApi,
  feature,
  relationships,
  pending,
  disabled,
  onNavigate,
  onToggle,
  onSettingChange
}: {
  moduleApi: ElsaStudioModuleApi;
  feature: DraftFeature | null;
  relationships: FeatureRelationships | null;
  pending: boolean;
  disabled: boolean;
  onNavigate(featureId: string): void;
  onToggle(feature: DraftFeature): void;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
}) {
  const [settingsDialogFeatureId, setSettingsDialogFeatureId] = useState<string | null>(null);

  if (!feature) {
    return <aside className="feature-management-inspector"><p className="feature-management-muted">Select a feature.</p></aside>;
  }

  const displayName = feature.displayName || feature.id;
  const editSettingsDisabled = disabled || !feature.enabled;
  const settingsDialogOpen = settingsDialogFeatureId === feature.id;

  return (
    <aside className="feature-management-inspector">
      <div className="feature-management-inspector-heading">
        <div>
          <span className="feature-management-inspector-badges">
            <SourceBadge sourceKind={feature.sourceKind} />
            <FeatureBadge tone={feature.enabled ? "enabled" : "neutral"}>{feature.enabled ? "Enabled" : "Disabled"}</FeatureBadge>
            {pending ? <FeatureBadge tone="info">Pending changes</FeatureBadge> : null}
            {feature.experimental ? <FeatureBadge tone="experimental"><FlaskConical size={11} />Experimental</FeatureBadge> : null}
            {feature.advanced ? <FeatureBadge tone="advanced"><ShieldAlert size={11} />Advanced</FeatureBadge> : null}
          </span>
          <h3>{displayName}</h3>
        </div>
        <StudioButton onClick={() => onToggle(feature)} disabled={disabled}>
          {feature.enabled ? "Disable" : "Enable"}
        </StudioButton>
      </div>

      {feature.description ? <p>{feature.description}</p> : null}
      {feature.readError ? <div className="feature-management-warning">{feature.readError}</div> : null}
      {feature.enabled && feature.runtimeAvailable === false ? <div className="feature-management-warning">Enabled in configuration; no runtime descriptor is available.</div> : null}
      {feature.enabled && feature.availability?.status === "dormant" ? <div className="feature-management-warning">Dormant: {feature.availability.reasons.map(reason => reason.reason).join(" ")}</div> : null}

      {relationships ? <FeatureRelationshipsPanel relationships={relationships} onNavigate={onNavigate} /> : null}

      <section className="feature-management-inspector-section feature-management-technical-section" aria-label="Feature technical details">
        <FeatureMetadata feature={feature} />
      </section>

      {feature.settings.length > 0 ? (
        <section className="feature-management-inspector-section feature-management-settings-panel" aria-label="Feature settings">
          <div className="feature-management-section-heading">
            <h4>Settings</h4>
            <StudioButton
              onClick={() => setSettingsDialogFeatureId(feature.id)}
              disabled={editSettingsDisabled}
              title={!feature.enabled ? "Enable the feature before editing settings." : undefined}
            >
              Edit settings
            </StudioButton>
          </div>
          <FeatureSettingsSummary feature={feature} />
        </section>
      ) : null}

      {settingsDialogOpen ? (
        <FeatureSettingsDialog
          moduleApi={moduleApi}
          feature={feature}
          disabled={editSettingsDisabled}
          onSettingChange={onSettingChange}
          onClose={() => setSettingsDialogFeatureId(null)}
        />
      ) : null}
    </aside>
  );
}

/**
 * Metadata that is not already visible in the inspector heading. Status, source, and the
 * advanced/experimental flags render once as badges above the title, and the display name
 * and description live in the heading, so this list intentionally does not repeat them.
 */
function FeatureMetadata({ feature }: { feature: DraftFeature }) {
  const packageName = feature.packageId
    ? [feature.packageId, feature.packageVersion].filter(Boolean).join(" ")
    : "Not package-backed";

  return (
    <details className="feature-management-metadata" aria-label="Feature metadata">
      <summary>Technical details</summary>
      <dl className="feature-management-detail-list">
        <div><dt>Technical name</dt><dd><code>{feature.id}</code></dd></div>
        <div><dt>Settings</dt><dd>{feature.settings.length}</dd></div>
        <div><dt>Runtime descriptor</dt><dd>{feature.runtimeAvailable === undefined ? "Not reported" : feature.runtimeAvailable ? "Available" : "Missing"}</dd></div>
        <div><dt>Operational availability</dt><dd>{!feature.enabled ? "Disabled" : feature.availability?.status ?? "Not reported"}</dd></div>
        <div className="feature-management-detail-wide"><dt>Categories</dt><dd><CategoryChips categories={feature.categories} /></dd></div>
        <div className="feature-management-detail-wide"><dt>Package</dt><dd>{feature.packageId ? <FeatureBadge tone="violet"><Package size={11} />{packageName}</FeatureBadge> : <span className="feature-management-muted">{packageName}</span>}</dd></div>
        {feature.manifestHash ? <div className="feature-management-detail-wide"><dt>Manifest hash</dt><dd><code>{feature.manifestHash}</code></dd></div> : null}
        {feature.manifestPath ? <div className="feature-management-detail-wide"><dt>Manifest path</dt><dd><code>{feature.manifestPath}</code></dd></div> : null}
      </dl>
    </details>
  );
}

function FeatureRelationshipsPanel({ relationships, onNavigate }: { relationships: FeatureRelationships; onNavigate(featureId: string): void }) {
  if (relationships.requires.direct.length === 0 && relationships.requires.indirect.length === 0
    && relationships.usedBy.direct.length === 0 && relationships.usedBy.indirect.length === 0) {
    return null;
  }

  return (
    <section className="feature-management-inspector-section feature-management-relationships" aria-label="Feature relationships">
      <h4>Dependencies and impact</h4>
      <FeatureRelationshipSection title="Requires" group={relationships.requires} onNavigate={onNavigate} />
      <FeatureRelationshipSection title="Used by" group={relationships.usedBy} onNavigate={onNavigate} />
    </section>
  );
}

function FeatureRelationshipSection({ title, group, onNavigate }: { title: string; group: FeatureRelationshipGroup; onNavigate(featureId: string): void }) {
  if (group.direct.length === 0 && group.indirect.length === 0) return null;

  return (
    <div className="feature-management-relationship-group">
      <h5>{title}</h5>
      {group.direct.length > 0 ? (
        <>
          <span className="feature-management-relationship-label">Direct</span>
          <ul>{group.direct.map(link => <FeatureRelationshipItem key={link.id} link={link} onNavigate={onNavigate} />)}</ul>
        </>
      ) : null}
      {group.indirect.length > 0 ? (
        <details>
          <summary>Indirect ({group.indirect.length})</summary>
          <ul>{group.indirect.map(link => <FeatureRelationshipItem key={link.id} link={link} onNavigate={onNavigate} />)}</ul>
        </details>
      ) : null}
    </div>
  );
}

function FeatureRelationshipItem({ link, onNavigate }: { link: FeatureRelationshipLink; onNavigate(featureId: string): void }) {
  const label = link.feature?.displayName || link.id;

  return (
    <li>
      {link.feature ? (
        <StudioButton className="feature-management-relationship-link" onClick={() => onNavigate(link.id)} aria-label={`Open ${label}`}>
          <span>{label}</span>
          <FeatureBadge tone={link.feature.enabled ? "enabled" : "neutral"}>{link.feature.enabled ? "Enabled" : "Disabled"}</FeatureBadge>
          {link.optional ? <FeatureBadge>Optional</FeatureBadge> : null}
        </StudioButton>
      ) : (
        <span className="feature-management-relationship-missing">
          <code>{link.id}</code>
          <FeatureBadge tone="danger">Missing from catalog</FeatureBadge>
          {link.optional ? <FeatureBadge>Optional</FeatureBadge> : null}
        </span>
      )}
    </li>
  );
}
