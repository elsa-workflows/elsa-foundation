import React, { useEffect, useMemo, useState } from "react";
import { AlertTriangle, DatabaseZap, Save, ShieldCheck } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { useRuntimeDiagnosticsSettings, useSaveRuntimeDiagnosticsSettings } from "./api/runtime";
import type {
  RuntimeDiagnosticsEvidenceLevel,
  RuntimeDiagnosticsSettingsView,
  RuntimeDiagnosticsSubject,
  RuntimeDiagnosticsSubjectOverrides
} from "./workflowTypes";

const levels: RuntimeDiagnosticsEvidenceLevel[] = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"];
const levelRank = new Map(levels.map((level, index) => [level, index]));

const subjects: Array<{ id: RuntimeDiagnosticsSubject; label: string; detail: string }> = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];

interface RuntimeDiagnosticsDraft {
  defaultLevel: RuntimeDiagnosticsEvidenceLevel;
  subjectOverrides: RuntimeDiagnosticsSubjectOverrides;
}

export function RuntimeDiagnosticsSettingsPage({ context }: { context: StudioEndpointContext }) {
  const settingsQuery = useRuntimeDiagnosticsSettings(context);
  const saveMutation = useSaveRuntimeDiagnosticsSettings(context);
  const settings = settingsQuery.data ?? null;
  const loading = settingsQuery.isLoading;
  const saving = saveMutation.isPending;
  const canManage = settings?.permissions.canManage ?? false;
  const [draft, setDraft] = useState<RuntimeDiagnosticsDraft>(() => createDraft(settings));
  const [status, setStatus] = useState<string | null>(null);

  useEffect(() => {
    setDraft(createDraft(settings));
  }, [settings]);

  const error = saveMutation.error ?? settingsQuery.error;
  const errorMessage = error instanceof Error ? error.message : error ? "Runtime diagnostics settings could not be loaded." : null;
  const hostReasons = settings?.hostPolicy.limitationReasons ?? [];
  const effectiveReasons = settings?.effective.limitationReasons ?? [];
  const hasUnsavedChanges = settings ? !draftMatchesRequestedSettings(draft, settings) : false;
  const defaultLevelChanged = Boolean(settings && draft.defaultLevel !== settings.requested.defaultLevel);

  const allowedLevels = useMemo(() => levels.filter(level => isLevelSelectable(level, settings)), [settings]);
  const updateDefaultLevel = (defaultLevel: RuntimeDiagnosticsEvidenceLevel) => {
    setStatus(null);
    setDraft(current => ({ ...current, defaultLevel }));
  };
  const updateSubjectLevel = (subject: RuntimeDiagnosticsSubject, value: string) => {
    setStatus(null);
    setDraft(current => {
      const subjectOverrides = { ...current.subjectOverrides };
      if (value === "inherit") delete subjectOverrides[subject];
      else subjectOverrides[subject] = value as RuntimeDiagnosticsEvidenceLevel;
      return { ...current, subjectOverrides };
    });
  };

  const save = () => {
    setStatus(null);
    saveMutation.mutate(
      {
        scope: settings?.requested.scope ?? "host-default",
        defaultLevel: draft.defaultLevel,
        subjectOverrides: draft.subjectOverrides
      },
      { onSuccess: saved => setStatus(`Saved. New capture events now use ${formatLevel(saved.effective.defaultLevel)}.`) }
    );
  };

  return (
    <section className="wf-page runtime-diagnostics-page">
      <div className="wf-page-header">
        <div>
          <div className="wf-kicker">Workflows</div>
          <h2><DatabaseZap size={18} /> Runtime diagnostics</h2>
          <p className="wf-muted">Choose what evidence new runtime capture events retain. Evidence already captured is unchanged.</p>
        </div>
        <div className="wf-actions">
          <button type="button" onClick={save} disabled={loading || saving || !canManage || !hasUnsavedChanges}>
            <Save size={15} />
            {saving ? "Saving..." : "Save changes"}
          </button>
        </div>
      </div>

      <div className="runtime-diagnostics-body">
        {errorMessage && <div className="runtime-diagnostics-banner error">{errorMessage}</div>}
        {status && !errorMessage && <div className="runtime-diagnostics-banner success">{status}</div>}

        <section className="runtime-diagnostics-section">
          <div className="runtime-diagnostics-section-head">
            <div className="runtime-diagnostics-section-title">
              <h3 className="wf-section-label"><ShieldCheck size={14} /> Default capture</h3>
              <p>Choose the level Studio should request. Save changes to apply it; Host Policy may lower the level used by the runtime.</p>
            </div>
            {settings && (
              <div className="runtime-diagnostics-effective">
                <span>New capture events use</span>
                <strong>{formatLevel(settings.effective.defaultLevel)}</strong>
                {settings.requested.defaultLevel !== settings.effective.defaultLevel && (
                  <small>Saved request: {formatLevel(settings.requested.defaultLevel)}. Host Policy applies a lower level.</small>
                )}
              </div>
            )}
          </div>
          <LevelSelect
            value={draft.defaultLevel}
            allowedLevels={allowedLevels}
            disabled={loading || saving || !canManage}
            onChange={updateDefaultLevel}
          />
          {defaultLevelChanged && (
            <div className="runtime-diagnostics-pending" role="status">
              <strong>Unsaved request: {formatLevel(draft.defaultLevel)}</strong>
              <span>Save changes to apply this request to new capture events.</span>
            </div>
          )}
        </section>

        <section className="runtime-diagnostics-section runtime-diagnostics-section-grow">
          <div className="runtime-diagnostics-section-title">
            <h3 className="wf-section-label"><DatabaseZap size={14} /> Subject overrides</h3>
            <p>Request a different level for a specific kind of evidence. The runtime-applied level is shown after Host Policy.</p>
          </div>
          <div className="runtime-diagnostics-subjects">
            {subjects.map(subject => {
              const requested = draft.subjectOverrides[subject.id] ?? "inherit";
              const effective = settings ? effectiveSubjectLevel(settings, subject.id) : null;
              return (
                <label className="runtime-diagnostics-subject" key={subject.id}>
                  <span>
                    <strong>{subject.label}</strong>
                    <em>{subject.detail}</em>
                  </span>
                  <span className="runtime-diagnostics-subject-controls">
                    {effective && <code>Runtime applies: {formatLevel(effective)}</code>}
                    <select
                      value={requested}
                      disabled={loading || saving || !canManage}
                      onChange={event => updateSubjectLevel(subject.id, event.target.value)}
                    >
                      <option value="inherit">Inherit default</option>
                      {allowedLevels.map(level => <option value={level} key={level}>{formatLevel(level)}</option>)}
                    </select>
                  </span>
                </label>
              );
            })}
          </div>
        </section>

        {(hostReasons.length > 0 || effectiveReasons.length > 0 || !canManage) && (
          <section className="runtime-diagnostics-section">
            <h3 className="wf-section-label"><AlertTriangle size={14} /> Policy</h3>
            <div className="runtime-diagnostics-policy">
              {!canManage && <span>Current user can view settings but cannot change them.</span>}
              {settings && <span>Maximum allowed by Host Policy: {formatLevel(settings.hostPolicy.maximumLevel)}</span>}
              {effectiveReasons.map(reason => <span key={`effective-${reason}`}>{reason}</span>)}
              {hostReasons.map(reason => <span key={`host-${reason}`}>{reason}</span>)}
            </div>
          </section>
        )}
      </div>
    </section>
  );
}

function createDraft(settings: RuntimeDiagnosticsSettingsView | null): RuntimeDiagnosticsDraft {
  return {
    defaultLevel: settings?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...(settings?.requested.subjectOverrides ?? {}) }
  };
}

function draftMatchesRequestedSettings(draft: RuntimeDiagnosticsDraft, settings: RuntimeDiagnosticsSettingsView): boolean {
  if (draft.defaultLevel !== settings.requested.defaultLevel) return false;
  return subjects.every(({ id }) => draft.subjectOverrides[id] === settings.requested.subjectOverrides?.[id]);
}

function isLevelSelectable(level: RuntimeDiagnosticsEvidenceLevel, settings: RuntimeDiagnosticsSettingsView | null): boolean {
  if (!settings) return level !== "Payload";
  if (level === "Payload" && !settings.permissions.canEnableFullPayloads) return false;
  return (levelRank.get(level) ?? 0) <= (levelRank.get(settings.hostPolicy.maximumLevel) ?? 0);
}

function effectiveSubjectLevel(settings: RuntimeDiagnosticsSettingsView, subject: RuntimeDiagnosticsSubject): RuntimeDiagnosticsEvidenceLevel {
  return settings.effective.subjectOverrides?.[subject] ?? settings.effective.defaultLevel;
}

function formatLevel(level: RuntimeDiagnosticsEvidenceLevel): string {
  switch (level) {
    case "Off":
      return "Off";
    case "Metadata":
      return "Metadata only";
    case "DiagnosticSnapshot":
      return "Diagnostic snapshots";
    case "Payload":
      return "Full payloads";
  }
}

function LevelSelect({
  value,
  allowedLevels,
  disabled,
  onChange
}: {
  value: RuntimeDiagnosticsEvidenceLevel;
  allowedLevels: RuntimeDiagnosticsEvidenceLevel[];
  disabled: boolean;
  onChange: (level: RuntimeDiagnosticsEvidenceLevel) => void;
}) {
  return (
    <fieldset className="runtime-diagnostics-levels" disabled={disabled}>
      <legend>Requested default</legend>
      <div className="runtime-diagnostics-level-options">
        {allowedLevels.map(level => (
          <label
            className={value === level ? "active" : ""}
            key={level}
          >
            <span className="runtime-diagnostics-level-title">
              <input
                type="radio"
                name="runtime-diagnostics-default-level"
                value={level}
                checked={value === level}
                onChange={() => onChange(level)}
              />
              <strong>{formatLevel(level)}</strong>
            </span>
            <em>{levelDescription(level)}</em>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function levelDescription(level: RuntimeDiagnosticsEvidenceLevel): string {
  switch (level) {
    case "Off":
      return "Do not store runtime value evidence.";
    case "Metadata":
      return "Store capture metadata without value shape.";
    case "DiagnosticSnapshot":
      return "Store bounded, sanitized value shape and previews.";
    case "Payload":
      return "Store full payloads when host policy permits it.";
  }
}
