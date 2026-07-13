import React, { useEffect, useMemo, useState } from "react";
import { LayoutDashboard, RefreshCw } from "lucide-react";
import { useAuthSession, usePermissions, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { createPreferenceScope, DashboardPreferenceStore, reconcileDashboardPreferences, type DashboardPreferenceDocument, type DashboardRefreshInterval } from "./dashboardPreferences";
import { WidgetFrame } from "./WidgetFrame";
import { clearDashboardWidgetCache, retainDashboardWidgetCache } from "./useDashboardWidgets";

const refreshOptions: Array<[DashboardRefreshInterval, string]> = [[0, "Off"], [60_000, "1 minute"], [300_000, "5 minutes"], [900_000, "15 minutes"], [1_800_000, "30 minutes"]];

export function DashboardPage({ api }: { api: ElsaStudioModuleApi }) {
  const session = useAuthSession();
  const permissions = usePermissions();
  const available = useMemo(() => api.dashboardWidgets.list().filter(widget => !widget.permissions?.length || permissions.hasAll(widget.permissions)), [api, permissions]);
  const scope = useMemo(() => createPreferenceScope(api, session), [api, session]);
  const cacheScopeKey = useMemo(() => JSON.stringify(scope), [scope]);
  const store = useMemo(() => new DashboardPreferenceStore(api, scope), [api, scope]);
  const pinnedWidgetIds = useMemo(() => api.runtime.dashboard?.pinnedWidgetIds ?? [], [api]);
  const [document, setDocument] = useState<DashboardPreferenceDocument | null>(null);
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const [settingsResetWidgetIds, setSettingsResetWidgetIds] = useState<string[]>([]);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [active, setActive] = useState(!globalThis.document.hidden);

  useEffect(() => { let live = true; setDocument(null); setPreferenceError(null); setSettingsResetWidgetIds([]); void store.load().then(value => {
    if (!live) return;
    const resetIds: string[] = [];
    const reconciled = { ...value, value: reconcileDashboardPreferences(value.value, available, pinnedWidgetIds, id => resetIds.push(id)) };
    setDocument(reconciled);
    setSettingsResetWidgetIds(resetIds);
    if (resetIds.length) void store.save(reconciled).then(saved => { if (live) setDocument(saved); }).catch(() => { if (live) setPreferenceError("Repaired widget settings could not be saved."); });
  }).catch(() => { if (live) setPreferenceError("Dashboard preferences could not be loaded."); }); return () => { live = false; }; }, [available, pinnedWidgetIds, store]);
  useEffect(() => { const changed = () => setActive(!globalThis.document.hidden); globalThis.document.addEventListener("visibilitychange", changed); return () => globalThis.document.removeEventListener("visibilitychange", changed); }, []);
  useEffect(() => () => clearDashboardWidgetCache(cacheScopeKey), [cacheScopeKey]);
  useEffect(() => retainDashboardWidgetCache(cacheScopeKey, new Set(available.map(widget => widget.id))), [available, cacheScopeKey]);

  if (!document) return <section className="dashboard-page"><div role="status">Loading Dashboard…</div></section>;
  const update = (mutate: (value: DashboardPreferenceDocument["value"]) => DashboardPreferenceDocument["value"]) => {
    const next = { ...document, value: mutate(document.value) }; setDocument(next); setPreferenceError(null); void store.save(next).then(saved => setDocument(current => current === next ? saved : current)).catch(() => setPreferenceError("Dashboard preferences could not be saved."));
  };
  const known = new Map(available.map(widget => [widget.id, widget]));
  const visible = document.value.widgets.filter(entry => entry.visible && known.has(entry.id));
  const hidden = document.value.widgets.filter(entry => !entry.visible && known.has(entry.id));
  const patchEntry = (id: string, patch: Record<string, unknown>) => update(value => ({ ...value, widgets: value.widgets.map(entry => entry.id === id ? { ...entry, ...patch } : entry) }));
  const move = (id: string, delta: -1 | 1) => update(value => { const widgets = [...value.widgets]; const from = widgets.findIndex(entry => entry.id === id); const to = Math.max(0, Math.min(widgets.length - 1, from + delta)); if (from >= 0 && from !== to) widgets.splice(to, 0, widgets.splice(from, 1)[0]); return { ...value, widgets }; });

  return <section className="dashboard-page">
    <header className="dashboard-page-header"><div><h2>Dashboard</h2><p>Prioritized work and operational summaries from Studio modules.</p></div><div>
      <label>Refresh <select value={document.value.refreshIntervalMs} onChange={event => update(value => ({ ...value, refreshIntervalMs: Number(event.target.value) as DashboardRefreshInterval }))}>{refreshOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label><input type="checkbox" checked={document.value.autoAddNewWidgets} onChange={event => update(value => ({ ...value, autoAddNewWidgets: event.target.checked }))} /> Add new widgets automatically</label>
      <button type="button" onClick={() => setRefreshVersion(value => value + 1)}><RefreshCw size={15} /> Refresh all</button>
    </div></header>
    {hidden.length ? <div className="dashboard-hidden"><span>Hidden widgets</span>{hidden.map(entry => <button key={entry.id} type="button" onClick={() => patchEntry(entry.id, { visible: true })}>Restore {known.get(entry.id)!.title}</button>)}</div> : null}
    {preferenceError ? <div role="status">{preferenceError}</div> : null}
    {settingsResetWidgetIds.length ? <div role="status">Settings were reset for {settingsResetWidgetIds.map(id => known.get(id)?.title ?? id).join(", ")} because the saved version is no longer valid. <button type="button" onClick={() => setSettingsResetWidgetIds([])}>Dismiss</button></div> : null}
    <div className="dashboard-grid">{visible.length ? visible.map(entry => { const widget = known.get(entry.id)!; return <WidgetFrame key={entry.id} api={api} widget={widget} size={entry.size} settings={entry.settings ?? widget.settings?.defaults} refreshIntervalMs={document.value.refreshIntervalMs} globalRefreshVersion={refreshVersion} active={active} cacheScopeKey={cacheScopeKey} pinned={pinnedWidgetIds.includes(entry.id)} onMove={delta => move(entry.id, delta)} onHide={() => patchEntry(entry.id, { visible: false })} onSize={size => patchEntry(entry.id, { size })} onSettings={settings => patchEntry(entry.id, { settings, settingsSchemaVersion: widget.settings?.schemaVersion })} />; }) : <div className="dashboard-empty"><LayoutDashboard size={22} /><span>No dashboard widgets are available.</span></div>}</div>
  </section>;
}
