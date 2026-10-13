import React, { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, EyeOff, RefreshCw, Settings } from "lucide-react";
import { OverflowMenu, selectSettingEditor } from "@elsa-workflows/studio-ui";
import { normalizeHomeWidgetSize, type ElsaStudioModuleApi, type StudioDashboardWidgetContribution, type StudioDashboardWidgetSize } from "@elsa-workflows/studio-sdk";
import { useDashboardWidget } from "./useDashboardWidgets";

const sizeLabels: Record<StudioDashboardWidgetSize, string> = {
  small: "S",
  medium: "M",
  wide: "Wide",
  full: "L"
};

export function WidgetFrame({ api, widget, size, settings, refreshIntervalMs, globalRefreshVersion, active, cacheScopeKey, pinned, editing, onMove, onHide, onSize, onSettings, onSnapshot }: {
  api: ElsaStudioModuleApi;
  widget: StudioDashboardWidgetContribution;
  size: StudioDashboardWidgetSize;
  settings: unknown;
  refreshIntervalMs: number;
  globalRefreshVersion: number;
  active: boolean;
  cacheScopeKey: string;
  pinned: boolean;
  editing: boolean;
  onMove(delta: -1 | 1): void;
  onHide(): void;
  onSize(size: StudioDashboardWidgetSize): void;
  onSettings(value: unknown): void;
  onSnapshot?(snapshot: unknown): void;
}) {
  const runtime = useDashboardWidget(widget, settings, refreshIntervalMs, api.runtime.dashboard?.widgetTimeoutMs ?? 10_000, cacheScopeKey, active);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [bodyResetVersion, setBodyResetVersion] = useState(0);
  useEffect(() => { if (globalRefreshVersion > 0) void runtime.refresh(); }, [globalRefreshVersion]); // eslint-disable-line react-hooks/exhaustive-deps
  const snapshot = runtime.state.status === "ready" || runtime.state.status === "refreshing" ? runtime.state.snapshot : undefined;
  useEffect(() => { if (snapshot !== undefined) onSnapshot?.(snapshot); }, [onSnapshot, snapshot]);
  const bodyResetKey = React.useMemo(
    () => ({ widget, cacheScopeKey, settings, size, updatedAt: "updatedAt" in runtime.state ? runtime.state.updatedAt : undefined, bodyResetVersion }),
    [bodyResetVersion, cacheScopeKey, runtime.state, settings, size, widget]
  );
  const retryBody = () => {
    setBodyResetVersion(version => version + 1);
    if (widget.load) void runtime.refresh();
  };
  const bodyVisible =
    runtime.state.status === "ready" ||
    runtime.state.status === "refreshing" ||
    (runtime.state.status === "idle" && !widget.load);

  return <article className="dashboard-widget" data-size={size} data-editing={editing ? "true" : undefined} aria-labelledby={`${widget.id}-title`} aria-busy={runtime.state.status === "loading" || runtime.state.status === "refreshing"}>
    <header className="dashboard-widget-header">
      <div>
        <h3 id={`${widget.id}-title`}>
          {editing ? <span className="home-widget-handle" aria-hidden="true">⠿</span> : null}
          {widget.title}
          {pinned ? <span className="dashboard-widget-pinned">Pinned</span> : null}
        </h3>
        {widget.description ? <p>{widget.description}</p> : null}
      </div>
      {editing ? (
        <div className="dashboard-widget-actions" aria-label={`${widget.title} controls`}>
          <button type="button" onClick={() => onMove(-1)} aria-label={`Move ${widget.title} earlier`}><ChevronLeft size={15} /></button>
          <button type="button" onClick={() => onMove(1)} aria-label={`Move ${widget.title} later`}><ChevronRight size={15} /></button>
          <select aria-label={`Size of ${widget.title}`} value={size} onChange={event => onSize(normalizeHomeWidgetSize(event.target.value))}>
            {widget.supportedSizes.map(option => <option key={option} value={option}>{sizeLabels[option] ?? option}</option>)}
          </select>
          {widget.settings ? <button type="button" onClick={() => setSettingsOpen(true)} aria-label={`Settings for ${widget.title}`}><Settings size={15} /></button> : null}
          {widget.load ? <button type="button" onClick={() => void runtime.refresh()} aria-label={`Refresh ${widget.title}`}><RefreshCw size={15} /></button> : null}
          {!pinned ? <button type="button" onClick={onHide} aria-label={widget.id === "home.get-started" ? `Dismiss ${widget.title}` : `Hide ${widget.title}`}><EyeOff size={15} /></button> : null}
        </div>
      ) : (
        <OverflowMenu
          ariaLabel={`${widget.title} actions`}
          items={[
            ...(widget.load ? [{ id: "refresh", label: "Refresh", onSelect: () => { void runtime.refresh(); } }] : []),
            ...(widget.settings ? [{ id: "options", label: "Options…", onSelect: () => setSettingsOpen(true) }] : []),
            ...widget.supportedSizes.map(option => ({
              id: `size-${option}`,
              label: `Size ${sizeLabels[option] ?? option}`,
              onSelect: () => onSize(option)
            })),
            ...(!pinned ? [{ id: "remove", label: widget.id === "home.get-started" ? "Dismiss" : "Remove", destructive: true, onSelect: onHide }] : [])
          ]}
        />
      )}
    </header>
    <div className="dashboard-widget-body">
      {runtime.state.status === "loading" ? <div role="status">Loading…</div> : null}
      {runtime.state.status === "error" || runtime.state.status === "timedOut" ? <div role="alert">{runtime.state.error} <button type="button" onClick={() => void runtime.refresh()}>Retry</button></div> : null}
      {bodyVisible ? (
        <WidgetBodyBoundary title={widget.title} resetKey={bodyResetKey} onRetry={retryBody}>
          <React.Suspense fallback={<div role="status">Loading…</div>}>
            <DashboardWidgetBody widget={widget} snapshot={snapshot} settings={settings} size={size} />
          </React.Suspense>
        </WidgetBodyBoundary>
      ) : null}
    </div>
    {runtime.state.status === "ready" || runtime.state.status === "refreshing" ? <footer>
      Updated {formatTime(runtime.state.updatedAt)}
      {runtime.state.nextRefreshAt ? ` · next ${formatTime(runtime.state.nextRefreshAt)}` : ""}
      {runtime.state.staleAt ? ` · stale ${formatTime(runtime.state.staleAt)}` : ""}
    </footer> : null}
    {settingsOpen && widget.settings ? <WidgetSettings api={api} widget={widget} value={settings} onSave={value => { onSettings(value); setSettingsOpen(false); }} onClose={() => setSettingsOpen(false)} /> : null}
  </article>;
}

function DashboardWidgetBody({ widget, snapshot, settings, size }: {
  widget: StudioDashboardWidgetContribution;
  snapshot: unknown;
  settings: unknown;
  size: StudioDashboardWidgetSize;
}) {
  if (widget.isEmpty?.(snapshot, settings)) {
    return (
      <div className="dashboard-widget-empty" role="status">
        <strong>{widget.emptyState?.title ?? "No data available"}</strong>
        {widget.emptyState?.description ? <span>{widget.emptyState.description}</span> : null}
      </div>
    );
  }

  const Body = widget.component;
  return <Body snapshot={snapshot} settings={settings} size={size} />;
}

class WidgetBodyBoundary extends React.Component<{
  children: React.ReactNode;
  title: string;
  resetKey: object;
  onRetry(): void;
}, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  componentDidUpdate(previous: Readonly<{ resetKey: object }>) {
    if (this.state.failed && previous.resetKey !== this.props.resetKey) {
      this.setState({ failed: false });
    }
  }

  render() {
    if (this.state.failed) {
      return (
        <div role="alert">
          {this.props.title} could not be rendered. <button type="button" onClick={this.props.onRetry}>Retry</button>
        </div>
      );
    }

    return this.props.children;
  }
}

function formatTime(value: number) { return new Date(value).toLocaleTimeString(); }

function WidgetSettings({ api, widget, value, onSave, onClose }: { api: ElsaStudioModuleApi; widget: StudioDashboardWidgetContribution; value: unknown; onSave(value: unknown): void; onClose(): void }) {
  const definition = widget.settings!;
  const [draft, setDraft] = useState<Record<string, unknown>>(() => ({ ...(value as Record<string, unknown>) }));
  return <div className="dashboard-settings-backdrop"><section role="dialog" aria-modal="true" aria-label={`${widget.title} settings`} className="dashboard-settings-dialog">
    <h4>{widget.title} settings</h4>
    {definition.descriptors.map(descriptor => {
      const Editor = selectSettingEditor(api, descriptor).component;
      return <label key={descriptor.name}><span>{descriptor.displayName}</span><Editor setting={descriptor} value={draft[descriptor.name]} onChange={next => setDraft(current => ({ ...current, [descriptor.name]: next }))} /></label>;
    })}
    <div><button type="button" onClick={onClose}>Cancel</button><button type="button" onClick={() => { const valid = definition.validate(draft); if (valid != null) onSave(valid); }}>Save</button></div>
  </section></div>;
}
