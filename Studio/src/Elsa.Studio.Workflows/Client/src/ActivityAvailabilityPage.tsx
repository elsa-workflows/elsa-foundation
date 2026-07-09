import React, { useEffect, useMemo, useState } from "react";
import { Activity, AlertTriangle, EyeOff, ListChecks, Save, Search, Shield, SlidersHorizontal, X } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  useActivityAvailabilityDiagnostics,
  useActivityAvailabilitySettings,
  useSaveActivityAvailabilitySettings,
  useWorkflowActivities
} from "./api/workflows";
import {
  computeEffectiveAvailability,
  createAvailabilityDraft,
  getAvailabilityActivityDescription,
  getAvailabilityActivityEntries,
  getAvailabilityActivityName,
  getAvailabilityActivityTypeLabel,
  getAvailabilityLayerLabel,
  getAvailabilityModePayload,
  getAvailabilityRuleKey,
  getAvailabilityStateClass,
  getAvailabilityStateLabel,
  getAvailabilityStateName,
  getUnresolvedAvailabilityEntries,
  groupAvailabilityEntriesByCategory,
  isAvailabilityDraftDirty,
  isHostBlockedEntry,
  isRuleTargetEnabled,
  setRuleTargetEnabled,
  summarizeAvailabilityArguments,
  type AvailabilityDraft,
  type EffectiveActivityAvailability
} from "./activityAvailability";
import { formatTypeName } from "./activityProperties";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem } from "./workflowTypes";
import { resolveActivityIcon } from "./workflowAdapter";
import { renderActivityIcon } from "./workflowFormatting";
import "./activityAvailability.css";

/** Unique row identity for React keys and details-panel selection (rule keys can collide across versions). */
function getEntryRowId(entry: ActivityAvailabilityDiagnosticEntry): string {
  return `${entry.activityTypeKey ?? ""}|${entry.activityDefinitionId ?? ""}`;
}

/** Numeric-segment version compare, tolerant of missing/non-numeric parts ("1.2" vs "1.10"). */
function compareVersionStrings(left: string | undefined, right: string | undefined): number {
  const parse = (value: string | undefined) => (value ?? "").split(".").map(part => Number.parseInt(part, 10) || 0);
  const leftParts = parse(left);
  const rightParts = parse(right);
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index++) {
    const difference = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

function getSwitchLockTitle(effective: EffectiveActivityAvailability): string | undefined {
  if (effective.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (effective.lockedBy === "set-rule") return `Controlled by the "${effective.governingSet}" set rule`;
  return undefined;
}

function ActivityIconChip({ icon }: { icon: ReturnType<typeof resolveActivityIcon> }) {
  return <span className="wf-activity-icon" data-icon={icon} aria-hidden="true">{renderActivityIcon(icon)}</span>;
}

export function ActivityAvailabilityPage({ context }: { context: StudioEndpointContext }) {
  const settingsQuery = useActivityAvailabilitySettings(context);
  const diagnosticsQuery = useActivityAvailabilityDiagnostics(context);
  const catalogQuery = useWorkflowActivities(context);
  const saveMutation = useSaveActivityAvailabilitySettings(context);

  const settings = settingsQuery.data ?? null;
  const diagnostics = diagnosticsQuery.data ?? null;
  const loading = settingsQuery.isLoading || diagnosticsQuery.isLoading;
  const saving = saveMutation.isPending;

  const [draft, setDraft] = useState<AvailabilityDraft>(() => createAvailabilityDraft(settings));
  const [search, setSearch] = useState("");
  const [hiddenOnly, setHiddenOnly] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);

  // Reseed the editable draft whenever the persisted settings change (initial load + after save).
  useEffect(() => {
    setDraft(createAvailabilityDraft(settings));
  }, [settings]);

  const activityEntries = useMemo(() => getAvailabilityActivityEntries(diagnostics), [diagnostics]);
  const unresolvedEntries = useMemo(() => getUnresolvedAvailabilityEntries(diagnostics), [diagnostics]);
  const hostSets = useMemo(() => diagnostics?.sets ?? [], [diagnostics]);

  // Draft availability per rule key, layering set rules and the host baseline like the backend does.
  const effectiveAvailability = useMemo(
    () => computeEffectiveAvailability(activityEntries, hostSets, draft),
    [activityEntries, hostSets, draft]
  );
  const getEffective = (entry: ActivityAvailabilityDiagnosticEntry): EffectiveActivityAvailability =>
    effectiveAvailability.get(getAvailabilityRuleKey(entry)) ?? { enabled: true, lockedBy: null };

  // Catalog metadata (icons, versions, inputs/outputs) is best-effort enrichment: the catalog only
  // lists activities the saved policy exposes, so hidden entries simply render without it. The
  // catalog has one item per definition version; keep the newest per type key.
  const catalogByTypeKey = useMemo(() => {
    const map = new Map<string, ActivityCatalogItem>();
    for (const item of catalogQuery.data?.activities ?? []) {
      if (!item.activityTypeKey) continue;
      const existing = map.get(item.activityTypeKey);
      if (!existing || compareVersionStrings(item.version, existing.version) > 0) map.set(item.activityTypeKey, item);
    }
    return map;
  }, [catalogQuery.data]);

  const findCatalogItem = (entry: ActivityAvailabilityDiagnosticEntry) => catalogByTypeKey.get(entry.activityTypeKey ?? "");

  const filteredEntries = useMemo(() => {
    const term = search.trim().toLowerCase();
    const candidates = hiddenOnly
      ? activityEntries.filter(entry => !(effectiveAvailability.get(getAvailabilityRuleKey(entry))?.enabled ?? true))
      : activityEntries;
    if (!term) return candidates;
    return candidates.filter(entry => [
      getAvailabilityActivityName(entry),
      getAvailabilityActivityTypeLabel(entry),
      getAvailabilityActivityDescription(entry),
      entry.activityTypeKey,
      entry.category
    ].some(value => (value ?? "").toLowerCase().includes(term)));
  }, [activityEntries, search, hiddenOnly, effectiveAvailability]);

  const groups = useMemo(() => groupAvailabilityEntriesByCategory(filteredEntries), [filteredEntries]);
  const selectedEntry = selectedRowId ? activityEntries.find(entry => getEntryRowId(entry) === selectedRowId) ?? null : null;

  const hostBlockedCount = activityEntries.filter(isHostBlockedEntry).length;
  const hiddenCount = activityEntries.filter(entry => getAvailabilityStateName(entry.state) === "HiddenByManagementSettings").length;
  const dirty = useMemo(() => isAvailabilityDraftDirty(draft, settings), [draft, settings]);

  const error = saveMutation.error ?? settingsQuery.error ?? diagnosticsQuery.error;
  const errorMessage = error instanceof Error ? error.message : error ? "Activity availability could not be loaded." : null;

  const editDraft = (edit: (current: AvailabilityDraft) => AvailabilityDraft) => {
    setStatus(null);
    setDraft(edit);
  };
  const updateMode = (mode: AvailabilityDraft["mode"]) => editDraft(current => ({ ...current, mode }));
  const setActivityEnabled = (ruleKey: string, enabled: boolean) =>
    editDraft(current => setRuleTargetEnabled(current, "activityTypes", ruleKey, enabled));
  const setGroupEnabled = (ruleKeys: string[], enabled: boolean) =>
    editDraft(current => ruleKeys.reduce((next, key) => setRuleTargetEnabled(next, "activityTypes", key, enabled), current));
  const setSetEnabled = (setName: string, enabled: boolean) =>
    editDraft(current => setRuleTargetEnabled(current, "sets", setName, enabled));

  const save = () => {
    setStatus(null);
    saveMutation.mutate(
      {
        scope: settings?.scope ?? "host-default",
        mode: getAvailabilityModePayload(draft.mode),
        rules: { activityTypes: draft.activityTypes, sets: draft.sets }
      },
      { onSuccess: () => setStatus("Activity availability saved.") }
    );
  };

  return (
    <section className="wf-page availability-page">
      <div className="wf-page-header">
        <div>
          <div className="wf-kicker">Workflows</div>
          <h2><ListChecks size={18} /> Activity availability</h2>
          <p className="wf-muted">Control which activities can be added to new workflows. Host baseline rules always take precedence.</p>
        </div>
        <div className="wf-actions">
          {dirty && !saving && <span className="wf-chip availability-dirty">Unsaved changes</span>}
          <button type="button" className="availability-save" onClick={save} disabled={loading || saving || !dirty}>
            <Save size={15} />
            {saving ? "Saving…" : "Save"}
          </button>
        </div>
      </div>

      <div className="availability-body">
        {errorMessage && <div className="availability-banner availability-banner-error">{errorMessage}</div>}
        {status && !errorMessage && <div className="availability-banner availability-banner-success">{status}</div>}

        <div className="availability-mode" role="group" aria-label="Activity availability mode">
          <button type="button" className={draft.mode === "AllExcept" ? "active" : ""} onClick={() => updateMode("AllExcept")} disabled={loading || saving}>
            <EyeOff size={15} />
            <span><strong>All except</strong><em>Activities are available unless you turn them off</em></span>
          </button>
          <button type="button" className={draft.mode === "Only" ? "active" : ""} onClick={() => updateMode("Only")} disabled={loading || saving}>
            <Shield size={15} />
            <span><strong>Only</strong><em>Activities are hidden unless you turn them on</em></span>
          </button>
        </div>

        <div className="availability-counts">
          <span><Shield size={14} /> {hostBlockedCount} host blocked</span>
          <span><EyeOff size={14} /> {hiddenCount} management hidden</span>
          <span><AlertTriangle size={14} /> {unresolvedEntries.length} unresolved</span>
        </div>

        {hostSets.length > 0 && (
          <section className="availability-section">
            <h3 className="wf-section-label"><SlidersHorizontal size={14} /> Sets</h3>
            <div className="availability-set-list">
              {hostSets.map(set => (
                <label className="availability-set-option" key={set.name}>
                  <input
                    type="checkbox"
                    role="switch"
                    className="wf-switch-input"
                    aria-label={`Activities in the ${set.name} set available in new workflows`}
                    checked={isRuleTargetEnabled(draft, "sets", set.name)}
                    disabled={loading || saving}
                    onChange={event => setSetEnabled(set.name, event.target.checked)}
                  />
                  <span>{set.name}</span>
                  <code>{(set.activityTypeKeys ?? []).length}</code>
                </label>
              ))}
            </div>
          </section>
        )}

        <section className="availability-section availability-section-grow">
          <div className="availability-section-head">
            <h3 className="wf-section-label"><Activity size={14} /> Activities</h3>
            <div className="availability-section-tools">
              <button
                type="button"
                className={`availability-filter-toggle${hiddenOnly ? " active" : ""}`}
                aria-pressed={hiddenOnly}
                title="Show only activities that are turned off or blocked"
                onClick={() => setHiddenOnly(current => !current)}
              >
                <EyeOff size={13} />
                Hidden only
              </button>
              <div className="wf-search availability-search">
                <Search size={14} />
                <input type="search" value={search} placeholder="Filter activities…" onChange={event => setSearch(event.target.value)} />
              </div>
            </div>
          </div>
          <div className="availability-activity-browser">
            <div className="availability-activity-list">
              {loading && activityEntries.length === 0 && <p className="wf-muted">Loading availability…</p>}
              {!loading && activityEntries.length === 0 && <p className="wf-muted">No availability diagnostics reported.</p>}
              {!loading && activityEntries.length > 0 && filteredEntries.length === 0 && (
                <p className="wf-muted">{hiddenOnly && !search.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter."}</p>
              )}
              {groups.map(group => {
                const togglable = group.entries.filter(entry => getEffective(entry).lockedBy === null);
                const enabledCount = togglable.filter(entry => getEffective(entry).enabled).length;
                const allEnabled = togglable.length > 0 && enabledCount === togglable.length;
                const mixed = enabledCount > 0 && !allEnabled;
                return (
                  <div className="availability-group" key={group.category}>
                    <div className="availability-group-header">
                      <span className="availability-group-title">
                        {group.category}
                        <small>{group.entries.length}</small>
                      </span>
                      {togglable.length > 0 && (
                        <input
                          type="checkbox"
                          role="switch"
                          className="wf-switch-input"
                          aria-label={`Toggle the ${togglable.length} listed ${group.category} activities`}
                          checked={allEnabled}
                          ref={element => { if (element) element.indeterminate = mixed; }}
                          disabled={loading || saving}
                          onChange={() => setGroupEnabled(togglable.map(getAvailabilityRuleKey), !allEnabled)}
                        />
                      )}
                    </div>
                    {group.entries.map(entry => {
                      const ruleKey = getAvailabilityRuleKey(entry);
                      const rowId = getEntryRowId(entry);
                      const state = getAvailabilityStateName(entry.state);
                      const effective = getEffective(entry);
                      const locked = effective.lockedBy !== null;
                      const icon = resolveActivityIcon(findCatalogItem(entry));
                      const activityName = getAvailabilityActivityName(entry);
                      const activityTypeLabel = getAvailabilityActivityTypeLabel(entry);
                      const activityDescription = getAvailabilityActivityDescription(entry);
                      const selected = selectedRowId === rowId;
                      return (
                        <div className={`availability-activity-row${selected ? " selected" : ""}${locked ? " disabled" : ""}`} key={rowId}>
                          <button
                            type="button"
                            className="availability-activity-open"
                            aria-expanded={selected}
                            title="Show activity details"
                            onClick={() => setSelectedRowId(selected ? null : rowId)}
                          >
                            <ActivityIconChip icon={icon} />
                            <span className="availability-activity-main">
                              <span className="availability-activity-title-line">
                                <strong>{activityName}</strong>
                                {activityTypeLabel && <code title={entry.activityTypeKey ?? undefined}>{activityTypeLabel}</code>}
                              </span>
                              {activityDescription && <span className="availability-activity-description" title={activityDescription}>{activityDescription}</span>}
                            </span>
                          </button>
                          {state !== "Available" && (
                            <em className={`availability-state ${getAvailabilityStateClass(entry.state)}`}>{getAvailabilityStateLabel(entry.state)}</em>
                          )}
                          <input
                            type="checkbox"
                            role="switch"
                            className="wf-switch-input"
                            aria-label={`${activityName} available in new workflows`}
                            title={getSwitchLockTitle(effective)}
                            checked={effective.enabled}
                            disabled={loading || saving || locked}
                            onChange={event => setActivityEnabled(ruleKey, event.target.checked)}
                          />
                        </div>
                      );
                    })}
                  </div>
                );
              })}
            </div>
            {selectedEntry && (
              <ActivityAvailabilityDetails
                entry={selectedEntry}
                catalogItem={findCatalogItem(selectedEntry)}
                onClose={() => setSelectedRowId(null)}
              />
            )}
          </div>
        </section>

        {unresolvedEntries.length > 0 && (
          <section className="availability-section">
            <h3 className="wf-section-label"><AlertTriangle size={14} /> Unresolved references</h3>
            <div className="availability-unresolved-list">
              {unresolvedEntries.map(entry => (
                <span key={`${entry.layer}-${entry.referenceKind}-${entry.referenceName}`}>
                  <strong>{entry.referenceName}</strong>
                  <em>{getAvailabilityStateLabel(entry.state)}</em>
                </span>
              ))}
            </div>
          </section>
        )}
      </div>
    </section>
  );
}

function ActivityAvailabilityDetails({
  entry,
  catalogItem,
  onClose
}: {
  entry: ActivityAvailabilityDiagnosticEntry;
  catalogItem: ActivityCatalogItem | undefined;
  onClose(): void;
}) {
  const activityName = getAvailabilityActivityName(entry);
  const description = entry.description?.trim() || catalogItem?.description?.trim();
  const inputs = useMemo(() => summarizeAvailabilityArguments(catalogItem?.inputs), [catalogItem]);
  const outputs = useMemo(() => summarizeAvailabilityArguments(catalogItem?.outputs), [catalogItem]);
  const rawMetadata = useMemo(() => JSON.stringify({ diagnostic: entry, catalog: catalogItem }, null, 2), [entry, catalogItem]);
  const metadata: Array<[string, string | null | undefined]> = [
    ["Type", entry.activityTypeKey],
    ["Definition ID", entry.activityDefinitionId],
    ["Category", entry.category],
    ["Version", catalogItem?.version],
    ["Execution", catalogItem?.executionType],
    ["Policy layer", getAvailabilityLayerLabel(entry.layer)]
  ];

  return (
    <aside className="availability-details" aria-label={`${activityName} details`}>
      <header className="availability-details-header">
        <ActivityIconChip icon={resolveActivityIcon(catalogItem)} />
        <h4>{activityName}</h4>
        <button type="button" className="wf-icon-button availability-details-close" aria-label="Close details" onClick={onClose}>
          <X size={14} />
        </button>
      </header>
      <div className="availability-details-body">
        <p className="availability-details-status">
          <em className={`availability-state ${getAvailabilityStateClass(entry.state)}`}>{getAvailabilityStateLabel(entry.state)}</em>
          {entry.reason?.trim() && <span>{entry.reason}</span>}
        </p>
        {description && <p className="availability-details-description">{description}</p>}
        <dl className="availability-details-meta">
          {metadata.map(([label, value]) => value?.trim() ? (
            <React.Fragment key={label}>
              <dt>{label}</dt>
              <dd><code>{value}</code></dd>
            </React.Fragment>
          ) : null)}
        </dl>
        {inputs.length > 0 && <AvailabilityArgumentList title="Inputs" items={inputs} />}
        {outputs.length > 0 && <AvailabilityArgumentList title="Outputs" items={outputs} />}
        {!catalogItem && (
          <p className="wf-muted availability-details-note">
            Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy.
          </p>
        )}
        <details className="availability-details-raw">
          <summary>Raw metadata</summary>
          <pre>{rawMetadata}</pre>
        </details>
      </div>
    </aside>
  );
}

function AvailabilityArgumentList({ title, items }: { title: string; items: ReturnType<typeof summarizeAvailabilityArguments> }) {
  return (
    <section className="availability-details-arguments">
      <h5>{title}</h5>
      <ul>
        {items.map(item => (
          <li key={item.name} title={item.description || undefined}>
            <strong>{item.name}</strong>
            {item.typeName && <code>{formatTypeName(item.typeName)}</code>}
          </li>
        ))}
      </ul>
    </section>
  );
}
