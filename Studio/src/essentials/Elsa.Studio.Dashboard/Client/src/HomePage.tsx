import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { House, Plus, RefreshCw } from "lucide-react";
import {
  homeWidgetIds,
  useAuthSession,
  usePermissions,
  type ElsaStudioModuleApi,
  type StudioDashboardWidgetContribution
} from "@elsa-workflows/studio-sdk";
import { homeLayoutPermissions } from "./hostControl";
import { useHomeDialogFocus } from "./useHomeDialogFocus";
import {
  HomeLayoutStore,
  createHomeLayoutScope,
  isHomeWidgetFeatureEnabled,
  reconcileHomeLayout,
  type HomeLayoutDocument,
  type HomeWidgetLayoutEntry
} from "./homeLayout";
import { WidgetFrame } from "./WidgetFrame";
import { clearDashboardWidgetCache, retainDashboardWidgetCache } from "./useDashboardWidgets";
import type { GetStartedSnapshot } from "./widgets/GetStartedWidget";
import { getStartedSteps } from "./widgets/GetStartedWidget";

export function HomePage({ api }: { api: ElsaStudioModuleApi }) {
  const session = useAuthSession();
  const permissions = usePermissions();
  const registered = useMemo(() => (api.homeWidgets ?? api.dashboardWidgets).list(), [api]);
  const enabledFeatures = api.runtime.enabledFeatures;
  const available = useMemo(
    () => registered.filter(widget =>
      isHomeWidgetFeatureEnabled(widget, enabledFeatures) &&
      (!widget.permissions?.length || permissions.hasAll(widget.permissions))),
    [enabledFeatures, permissions, registered]
  );
  const excludedWidgetIds = useMemo(
    () => new Set(registered.filter(widget =>
      !isHomeWidgetFeatureEnabled(widget, enabledFeatures) ||
      (widget.permissions?.length && !permissions.hasAll(widget.permissions))
    ).map(widget => widget.id)),
    [enabledFeatures, permissions, registered]
  );
  const scope = useMemo(() => createHomeLayoutScope(api, session), [api, session]);
  const cacheScopeKey = useMemo(() => JSON.stringify(scope), [scope]);
  const store = useMemo(() => new HomeLayoutStore(api, scope), [api, scope]);
  const pinnedWidgetIds = useMemo(() => api.runtime.dashboard?.pinnedWidgetIds ?? [], [api]);
  const canSaveDefault = permissions.has(homeLayoutPermissions.manageDefaults) || permissions.has("*");
  const [document, setDocument] = useState<HomeLayoutDocument | null>(null);
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [pickerQuery, setPickerQuery] = useState("");
  const [liveMessage, setLiveMessage] = useState("");
  const [moveFocus, setMoveFocus] = useState<{ id: string; label: string } | null>(null);
  const pageRef = useRef<HTMLElement>(null);
  const pickerRef = useRef<HTMLElement>(null);
  const closePicker = useCallback(() => { setPickerOpen(false); setPickerQuery(""); }, []);
  useHomeDialogFocus(pickerRef, pickerOpen ? closePicker : null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [refreshIntervalMs, setRefreshIntervalMs] = useState(api.runtime.dashboard?.defaultRefreshIntervalMs ?? 300_000);
  const [active, setActive] = useState(!globalThis.document.hidden);
  const [getStartedSnapshot, setGetStartedSnapshot] = useState<GetStartedSnapshot | null>(null);

  useEffect(() => {
    let live = true;
    setDocument(null);
    setPreferenceError(null);
    void store.load().then(value => {
      if (!live) return;
      setDocument({ ...value, value: reconcileHomeLayout(value.value, available, excludedWidgetIds) });
    }).catch(() => { if (live) setPreferenceError("Home layout could not be loaded."); });
    return () => { live = false; };
  }, [available, excludedWidgetIds, store]);

  useEffect(() => {
    const changed = () => setActive(!globalThis.document.hidden);
    globalThis.document.addEventListener("visibilitychange", changed);
    return () => globalThis.document.removeEventListener("visibilitychange", changed);
  }, []);
  useEffect(() => () => clearDashboardWidgetCache(cacheScopeKey), [cacheScopeKey]);
  useEffect(() => retainDashboardWidgetCache(cacheScopeKey, new Set(available.map(widget => widget.id))), [available, cacheScopeKey]);
  useEffect(() => {
    if (!document || !getStartedSnapshot) return;
    const entry = document.value.widgets.find(item => item.id === homeWidgetIds.getStarted);
    if (!entry?.visible || entry.options?.keepVisible === true) return;
    if (!getStartedSteps(getStartedSnapshot).every(step => step.done)) return;
    const next = {
      ...document,
      source: "user" as const,
      value: {
        widgets: document.value.widgets.map(item => item.id === homeWidgetIds.getStarted
          ? { ...item, visible: false, options: { ...item.options, autoHidden: true } }
          : item)
      }
    };
    setDocument(next);
    void store.save(next).catch(() => setPreferenceError("Home layout could not be saved."));
  }, [document, getStartedSnapshot, store]);
  useEffect(() => {
    if (!moveFocus) return;
    const root = pageRef.current ?? globalThis.document;
    const button = root.querySelector<HTMLButtonElement>(
      `[data-widget-id="${moveFocus.id}"] button[aria-label="${moveFocus.label}"]`
    );
    button?.focus();
  }, [moveFocus]);

  if (!document) return <section className="dashboard-page home-page"><div role="status">Loading Home…</div></section>;

  const known = new Map(available.map(widget => [widget.id, widget]));
  const update = (mutate: (value: HomeLayoutDocument["value"]) => HomeLayoutDocument["value"]) => {
    const next = { ...document, source: "user" as const, value: mutate(document.value) };
    setDocument(next);
    setPreferenceError(null);
    void store.save(next).then(saved => setDocument(current => current === next ? saved : current)).catch(() => setPreferenceError("Home layout could not be saved."));
  };
  const visible = document.value.widgets.filter(entry => entry.visible && known.has(entry.id));
  const hidden = document.value.widgets.filter(entry => !entry.visible && known.has(entry.id));
  const missing = available.filter(widget => !document.value.widgets.some(entry => entry.id === widget.id));
  const patchEntry = (id: string, patch: Partial<HomeWidgetLayoutEntry>) =>
    update(value => ({ widgets: value.widgets.map(entry => entry.id === id ? { ...entry, ...patch } : entry) }));
  const move = (id: string, delta: -1 | 1) => update(value => {
    const widgets = [...value.widgets];
    const visibleIndexes = widgets.flatMap((entry, index) => entry.visible && known.has(entry.id) ? [index] : []);
    const fromIndex = widgets.findIndex(entry => entry.id === id);
    const from = visibleIndexes.indexOf(fromIndex);
    const to = Math.max(0, Math.min(visibleIndexes.length - 1, from + delta));
    if (from >= 0 && from !== to) {
      const toIndex = visibleIndexes[to];
      [widgets[fromIndex], widgets[toIndex]] = [widgets[toIndex], widgets[fromIndex]];
    }
    const title = known.get(id)?.title ?? id;
    const visibleAfter = widgets.filter(entry => entry.visible && known.has(entry.id));
    const position = visibleAfter.findIndex(entry => entry.id === id) + 1;
    if (from >= 0 && from !== to && position > 0) {
      setLiveMessage(`${title} moved to position ${position} of ${visibleAfter.length}`);
      setMoveFocus({ id, label: delta < 0 ? `Move ${title} earlier` : `Move ${title} later` });
    }
    return { widgets };
  });
  const addWidget = (widget: StudioDashboardWidgetContribution) => {
    update(value => {
      const existing = value.widgets.find(entry => entry.id === widget.id);
      if (existing) {
        return {
          widgets: value.widgets.map(entry => entry.id === widget.id
            ? { ...entry, visible: true, options: { ...entry.options, keepVisible: true } }
            : entry)
        };
      }
      return {
        widgets: [...value.widgets, { id: widget.id, size: widget.defaultSize, visible: true, options: { keepVisible: true } }]
      };
    });
    setPickerOpen(false);
    setPickerQuery("");
  };
  const reset = () => {
    void store.reset().then(value => {
      setDocument({ ...value, value: reconcileHomeLayout(value.value, available, excludedWidgetIds) });
      setEditing(false);
    }).catch(() => setPreferenceError("Home layout could not be reset."));
  };
  const saveDefault = () => {
    void store.saveDefault(document).then(() => setPreferenceError(null)).catch(() => setPreferenceError("The server default could not be saved."));
  };

  const addable = [...hidden.map(entry => known.get(entry.id)!), ...missing]
    .filter((widget, index, items) => items.findIndex(item => item.id === widget.id) === index);
  const filteredAddable = addable.filter(widget => {
    const query = pickerQuery.trim().toLowerCase();
    return !query || widget.title.toLowerCase().includes(query) || widget.id.toLowerCase().includes(query);
  });

  return (
    <section ref={pageRef} className={`dashboard-page home-page${editing ? " home-page--editing" : ""}`}>
      <header className="dashboard-page-header">
        <div>
          <h2>Home</h2>
          <p>{editing ? "Add, remove and reorder widgets. Changes save as you go." : "What to know and do next."}</p>
        </div>
        <div>
          <label>Refresh <select value={refreshIntervalMs} onChange={event => setRefreshIntervalMs(Number(event.target.value))}>
            {([[0, "Off"], [60_000, "1 minute"], [300_000, "5 minutes"], [900_000, "15 minutes"], [1_800_000, "30 minutes"]] as const)
              .map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select></label>
          <button type="button" onClick={() => setRefreshVersion(value => value + 1)}><RefreshCw size={15} /> Refresh all</button>
          {editing ? (
            <>
              <button type="button" onClick={() => { setPickerOpen(true); setPickerQuery(""); }}><Plus size={15} /> Add widget</button>
              <button type="button" onClick={reset}>Reset to default</button>
              {canSaveDefault && !store.anonymous ? <button type="button" onClick={saveDefault}>Save as server default</button> : null}
              <button type="button" onClick={() => { setEditing(false); closePicker(); }}>Done</button>
            </>
          ) : <button type="button" onClick={() => setEditing(true)}>Edit home</button>}
        </div>
      </header>
      <div className="visually-hidden" aria-live="polite">{liveMessage}</div>
      {preferenceError ? <div role="status">{preferenceError}</div> : null}
      <div className="dashboard-grid">
        {visible.length ? visible.map(entry => {
          const widget = known.get(entry.id)!;
          return (
            <WidgetFrame
              key={entry.id}
              api={api}
              widget={widget}
              size={entry.size}
              settings={entry.options ?? widget.settings?.defaults}
              refreshIntervalMs={refreshIntervalMs}
              globalRefreshVersion={refreshVersion}
              active={active}
              cacheScopeKey={cacheScopeKey}
              pinned={pinnedWidgetIds.includes(entry.id)}
              editing={editing}
              onMove={delta => move(entry.id, delta)}
              onHide={() => patchEntry(entry.id, { visible: false, options: { ...entry.options, dismissed: true } })}
              onSize={size => patchEntry(entry.id, { size })}
              onSettings={settings => patchEntry(entry.id, { options: settings as Record<string, unknown> })}
              onSnapshot={widget.id === homeWidgetIds.getStarted
                ? snapshot => setGetStartedSnapshot(snapshot as GetStartedSnapshot)
                : undefined}
            />
          );
        }) : (
          <div className="dashboard-empty">
            <House size={22} />
            <span>No Home widgets are visible.</span>
            <button type="button" onClick={() => { setEditing(true); setPickerOpen(true); }}>Add widget</button>
          </div>
        )}
        {editing ? (
          <button type="button" className="home-add-slot" onClick={() => { setPickerOpen(true); setPickerQuery(""); }}>
            ＋ Add widget
          </button>
        ) : null}
      </div>
      {pickerOpen ? (
        <div className="dashboard-settings-backdrop">
          <section
            ref={pickerRef}
            role="dialog"
            aria-modal="true"
            aria-label="Add widget"
            className="dashboard-settings-dialog"
            tabIndex={-1}
          >
            <h4>Add widget</h4>
            <label>
              <span>Search</span>
              <input aria-label="Search widgets" value={pickerQuery} onChange={event => setPickerQuery(event.target.value)} />
            </label>
            <AddWidgetGroup title="Core" widgets={filteredAddable.filter(widget => widget.core)} hidden={hidden} onAdd={addWidget} />
            <AddWidgetGroup title="From modules" widgets={filteredAddable.filter(widget => !widget.core)} hidden={hidden} onAdd={addWidget} />
            {!filteredAddable.length ? <p>No widgets match.</p> : null}
            <div><button type="button" onClick={closePicker}>Close</button></div>
          </section>
        </div>
      ) : null}
    </section>
  );
}

function AddWidgetGroup({
  title,
  widgets,
  hidden,
  onAdd
}: {
  title: string;
  widgets: StudioDashboardWidgetContribution[];
  hidden: HomeWidgetLayoutEntry[];
  onAdd(widget: StudioDashboardWidgetContribution): void;
}) {
  if (!widgets.length) return null;
  return (
    <section>
      <h5>{title}</h5>
      <ul className="home-add-widget-list">
        {widgets.map(widget => {
          const hiddenEntry = hidden.find(entry => entry.id === widget.id);
          const reason = hiddenEntry?.options?.autoHidden
            ? "Hidden: all steps done"
            : hiddenEntry?.options?.dismissed
              ? "Dismissed"
              : hiddenEntry
                ? "Hidden"
                : undefined;
          return (
            <li key={widget.id}>
              <button type="button" onClick={() => onAdd(widget)}>
                {widget.title}
                {reason ? <span> {reason}</span> : null}
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

export { HomePage as DashboardPage };
