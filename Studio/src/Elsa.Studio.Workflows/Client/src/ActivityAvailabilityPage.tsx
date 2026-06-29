import React, { useEffect, useMemo, useState } from "react";
import { Activity, AlertTriangle, EyeOff, ListChecks, Save, Search, Shield, SlidersHorizontal } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  useActivityAvailabilityDiagnostics,
  useActivityAvailabilitySettings,
  useSaveActivityAvailabilitySettings
} from "./api/workflows";
import {
  createAvailabilityDraft,
  getActivityDisplayName,
  getAvailabilityActivityEntries,
  getAvailabilityModePayload,
  getAvailabilityStateClass,
  getAvailabilityStateLabel,
  getAvailabilityStateName,
  getUnresolvedAvailabilityEntries,
  toggleListValue,
  type AvailabilityDraft
} from "./activityAvailability";
import "./activityAvailability.css";

export function ActivityAvailabilityPage({ context }: { context: StudioEndpointContext }) {
  const settingsQuery = useActivityAvailabilitySettings(context);
  const diagnosticsQuery = useActivityAvailabilityDiagnostics(context);
  const saveMutation = useSaveActivityAvailabilitySettings(context);

  const settings = settingsQuery.data ?? null;
  const diagnostics = diagnosticsQuery.data ?? null;
  const loading = settingsQuery.isLoading || diagnosticsQuery.isLoading;
  const saving = saveMutation.isPending;

  const [draft, setDraft] = useState<AvailabilityDraft>(() => createAvailabilityDraft(settings));
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<string | null>(null);

  // Reseed the editable draft whenever the persisted settings change (initial load + after save).
  useEffect(() => {
    setDraft(createAvailabilityDraft(settings));
  }, [settings]);

  const activityEntries = useMemo(() => getAvailabilityActivityEntries(diagnostics), [diagnostics]);
  const unresolvedEntries = useMemo(() => getUnresolvedAvailabilityEntries(diagnostics), [diagnostics]);
  const hostSets = diagnostics?.sets ?? [];

  const filteredEntries = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return activityEntries;
    return activityEntries.filter(entry =>
      getActivityDisplayName(entry).toLowerCase().includes(term) || (entry.activityTypeKey ?? "").toLowerCase().includes(term)
    );
  }, [activityEntries, search]);

  const selectedActivityTypes = new Set(draft.activityTypes);
  const selectedSets = new Set(draft.sets);
  const hostBlockedCount = activityEntries.filter(entry => getAvailabilityStateName(entry.state) === "BlockedByHostBaseline").length;
  const hiddenCount = activityEntries.filter(entry => getAvailabilityStateName(entry.state) === "HiddenByManagementSettings").length;

  const error = saveMutation.error ?? settingsQuery.error ?? diagnosticsQuery.error;
  const errorMessage = error instanceof Error ? error.message : error ? "Activity availability could not be loaded." : null;

  const updateMode = (mode: AvailabilityDraft["mode"]) => setDraft(current => ({ ...current, mode }));
  const toggleActivityType = (activityTypeKey: string) =>
    setDraft(current => ({ ...current, activityTypes: toggleListValue(current.activityTypes, activityTypeKey) }));
  const toggleSet = (setName: string) => setDraft(current => ({ ...current, sets: toggleListValue(current.sets, setName) }));

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
    <div className="availability-shell">
      <div className="availability-header">
        <div>
          <h2><ListChecks size={18} /> Activity availability</h2>
          <p>Control which activities can be added to new workflows. Host baseline rules always take precedence.</p>
        </div>
        <button type="button" className="availability-save" onClick={save} disabled={loading || saving}>
          <Save size={15} />
          {saving ? "Saving…" : "Save"}
        </button>
      </div>

      {errorMessage && <div className="availability-error">{errorMessage}</div>}
      {status && !errorMessage && <div className="availability-status">{status}</div>}

      <div className="availability-mode" role="group" aria-label="Activity availability mode">
        <button type="button" className={draft.mode === "AllExcept" ? "active" : ""} onClick={() => updateMode("AllExcept")} disabled={loading || saving}>
          <EyeOff size={15} />
          <span><strong>All except</strong><em>Show everything except the selected activities</em></span>
        </button>
        <button type="button" className={draft.mode === "Only" ? "active" : ""} onClick={() => updateMode("Only")} disabled={loading || saving}>
          <Shield size={15} />
          <span><strong>Only</strong><em>Show only the selected activities</em></span>
        </button>
      </div>

      <div className="availability-counts">
        <span><Shield size={14} /> {hostBlockedCount} host blocked</span>
        <span><EyeOff size={14} /> {hiddenCount} management hidden</span>
        <span><AlertTriangle size={14} /> {unresolvedEntries.length} unresolved</span>
      </div>

      <div className="availability-body">
        {hostSets.length > 0 && (
          <section className="availability-section">
            <h3><SlidersHorizontal size={15} /> Sets</h3>
            <div className="availability-set-list">
              {hostSets.map(set => (
                <label className="availability-set-option" key={set.name}>
                  <input type="checkbox" checked={selectedSets.has(set.name)} disabled={loading || saving} onChange={() => toggleSet(set.name)} />
                  <span>{set.name}</span>
                  <code>{(set.activityTypeKeys ?? []).length}</code>
                </label>
              ))}
            </div>
          </section>
        )}

        <section className="availability-section availability-section-grow">
          <div className="availability-section-head">
            <h3><Activity size={15} /> Activities</h3>
            <div className="availability-search">
              <Search size={14} />
              <input type="search" value={search} placeholder="Filter activities…" onChange={event => setSearch(event.target.value)} />
            </div>
          </div>
          <div className="availability-activity-list">
            {loading && activityEntries.length === 0 && <p className="availability-muted">Loading availability…</p>}
            {!loading && activityEntries.length === 0 && <p className="availability-muted">No availability diagnostics reported.</p>}
            {!loading && activityEntries.length > 0 && filteredEntries.length === 0 && <p className="availability-muted">No activities match the filter.</p>}
            {filteredEntries.map(entry => {
              const state = getAvailabilityStateName(entry.state);
              const hostBlocked = state === "BlockedByHostBaseline";
              const key = entry.activityTypeKey ?? entry.activityDefinitionId ?? "";
              return (
                <label className={`availability-activity-option ${hostBlocked ? "disabled" : ""}`} key={key}>
                  <input
                    type="checkbox"
                    checked={selectedActivityTypes.has(key)}
                    disabled={loading || saving || hostBlocked}
                    onChange={() => toggleActivityType(key)}
                  />
                  <span className="availability-activity-main">
                    <strong>{getActivityDisplayName(entry)}</strong>
                    <code>{entry.activityTypeKey}</code>
                  </span>
                  <em className={`availability-state ${getAvailabilityStateClass(entry.state)}`}>{getAvailabilityStateLabel(entry.state)}</em>
                </label>
              );
            })}
          </div>
        </section>

        {unresolvedEntries.length > 0 && (
          <section className="availability-section">
            <h3><AlertTriangle size={15} /> Unresolved references</h3>
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
    </div>
  );
}
