import React, { useEffect, useMemo, useState } from "react";
import { Activity, Boxes, ExternalLink, FileText, Gauge, Github, LayoutDashboard, Search, ShieldCheck } from "lucide-react";
import type {
  ElsaStudioModuleApi,
  StudioModulesResponse,
  StudioNavigationContribution,
  StudioPanelContribution
} from "../sdk";
import { createStudioRegistry } from "./registry";
import { loadStudioModules } from "./loader";
import { ThemeProvider } from "./components/ThemeProvider";
import { ThemeSwitcher } from "./components/ThemeSwitcher";
import elsaLogo from "../assets/images/icon.png";
import "./styles.css";

type LoadState = "loading" | "ready" | "failed";

const builtInNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Overview", path: "/", order: 0 },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics/modules", order: 900 }
];

const bottomPanelHeightStorageKey = "elsa-studio-bottom-panel-height";
const activeBottomPanelStorageKey = "elsa-studio-active-bottom-panel";
const defaultBottomPanelHeight = 260;
const minBottomPanelHeight = 140;
const maxBottomPanelHeight = 560;
const minWorkspaceHeight = 280;

function AppContent() {
  const [state, setState] = useState<LoadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [api, setApi] = useState<ElsaStudioModuleApi | null>(null);
  const [path, setPath] = useState(normalizePath(window.location.pathname));

  useEffect(() => {
    const onPopState = () => setPath(normalizePath(window.location.pathname));
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  useEffect(() => {
    let disposed = false;

    async function boot() {
      try {
        const response = await fetch("/_elsa/studio/modules");
        if (!response.ok) {
          throw new Error(`Manifest request failed with ${response.status}.`);
        }

        const manifestResponse = (await response.json()) as StudioModulesResponse;
        const registry = createStudioRegistry({
          hostVersion: manifestResponse.hostVersion,
          sdkVersion: manifestResponse.sdkVersion,
          http: {
            async getJson<T>(url, init) {
              const result = await fetch(url, init);
              if (!result.ok) {
                throw new Error(`Request failed with ${result.status}.`);
              }
              return (await result.json()) as T;
            }
          }
        });

        for (const diagnostic of manifestResponse.diagnostics) {
          registry.diagnostics.add(diagnostic);
        }

        await loadStudioModules(manifestResponse.modules, registry, {
          hostVersion: manifestResponse.hostVersion,
          sdkVersion: manifestResponse.sdkVersion
        });

        if (!disposed) {
          setApi(registry);
          setState("ready");
        }
      } catch (e) {
        if (!disposed) {
          setError(e instanceof Error ? e.message : String(e));
          setState("failed");
        }
      }
    }

    void boot();

    return () => {
      disposed = true;
    };
  }, []);

  const routes = useMemo(() => api?.routes.list() ?? [], [api, state]);
  const navigation = useMemo(
    () => [...builtInNavigation, ...(api?.navigation.list() ?? [])]
      .filter((item, index, items) => items.findIndex(candidate => candidate.id === item.id) === index)
      .sort((a, b) => (a.order ?? 500) - (b.order ?? 500)),
    [api, state]
  );
  const panels = useMemo(
    () => (api?.panels.list() ?? []).sort((a, b) => (a.order ?? 500) - (b.order ?? 500)),
    [api, state]
  );

  if (state === "loading") {
    return (
      <ShellFrame navigation={navigation} panels={[]} path={path} title="Loading modules" onNavigate={navigateTo}>
        <div className="empty-state">Loading Studio modules...</div>
      </ShellFrame>
    );
  }

  if (state === "failed") {
    return (
      <ShellFrame navigation={navigation} panels={[]} path={path} title="Startup failed" onNavigate={navigateTo}>
        <div className="error-state">{error}</div>
      </ShellFrame>
    );
  }

  const activeRoute = routes.find(route => route.path === path);
  const ActiveComponent = activeRoute?.component;
  const pageTitle = navigation.find(item => item.path === path)?.label ?? activeRoute?.label ?? "Studio";

  return (
    <ShellFrame navigation={navigation} panels={panels} path={path} title={pageTitle} onNavigate={navigateTo}>
      {path === "/" ? <Home api={api!} /> : null}
      {path === "/diagnostics/modules" ? <Diagnostics api={api!} /> : null}
      {ActiveComponent ? <ActiveComponent /> : null}
      {!ActiveComponent && path !== "/" && path !== "/diagnostics/modules" ? (
        <div className="empty-state">No Studio route is registered for {path}.</div>
      ) : null}
    </ShellFrame>
  );
}

function ShellFrame({
  navigation,
  panels,
  path,
  title,
  onNavigate,
  children
}: {
  navigation: StudioNavigationContribution[];
  panels: StudioPanelContribution[];
  path: string;
  title: string;
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  return (
    <div className="studio-shell">
      <aside className="sidebar">
        <a className="brand" href="/" onClick={event => { event.preventDefault(); onNavigate("/"); }}>
          <span className="brand-mark" aria-hidden="true">
            <img src={elsaLogo} alt="" />
          </span>
          <span>
            <strong>Elsa Studio</strong>
            <small>Foundation</small>
          </span>
        </a>

        <label className="sidebar-search">
          <Search size={16} />
          <input aria-label="Search modules" placeholder="Search modules" />
        </label>

        <nav className="nav-section" aria-label="Studio">
          <span className="nav-heading">Workspace</span>
          {navigation.map(item => (
            <a
              key={item.id}
              className={path === item.path ? "active" : ""}
              href={item.path}
              onClick={event => {
                event.preventDefault();
                onNavigate(item.path);
              }}
            >
              <NavIcon id={item.id} />
              {item.label}
            </a>
          ))}
        </nav>

        <div className="sidebar-footer">
          <ShieldCheck size={16} />
          <span>Trusted same-origin modules</span>
        </div>
      </aside>

      <div className="main-frame">
        <header className="topbar">
          <div>
            <span className="breadcrumb">Studio / {title}</span>
            <h1>{title}</h1>
          </div>
          <div className="topbar-actions">
            <a href="https://github.com/elsa-workflows/elsa-foundation-studio" aria-label="GitHub">
              <Github size={18} />
            </a>
            <a href="https://github.com/elsa-workflows/elsa-foundation-designer" aria-label="Designer source">
              <ExternalLink size={18} />
            </a>
            <ThemeSwitcher />
          </div>
        </header>
        <main className="content">{children}</main>
      </div>
      {panels.length > 0 ? <BottomPanel panels={panels} /> : null}
    </div>
  );
}

function BottomPanel({ panels }: { panels: StudioPanelContribution[] }) {
  const [height, setHeight] = useState(getInitialBottomPanelHeight);
  const [activePanelId, setActivePanelId] = useState(getInitialActiveBottomPanelId);
  const activePanel = panels.find(panel => panel.id === activePanelId) ?? panels[0];
  const ActivePanelComponent = activePanel.component;

  useEffect(() => {
    if (!panels.some(panel => panel.id === activePanelId)) {
      setActivePanelId(panels[0].id);
    }
  }, [activePanelId, panels]);

  useEffect(() => {
    window.localStorage.setItem(bottomPanelHeightStorageKey, String(height));
  }, [height]);

  useEffect(() => {
    if (activePanel?.id) {
      window.localStorage.setItem(activeBottomPanelStorageKey, activePanel.id);
    }
  }, [activePanel?.id]);

  useEffect(() => {
    const handleResize = () => setHeight(current => clampBottomPanelHeight(current));
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, []);

  function startResize(event: React.PointerEvent<HTMLDivElement>) {
    event.preventDefault();

    const startY = event.clientY;
    const startHeight = height;

    document.body.classList.add("bottom-panel-resizing");

    const handlePointerMove = (moveEvent: PointerEvent) => {
      setHeight(clampBottomPanelHeight(startHeight + startY - moveEvent.clientY));
    };

    const stopResize = () => {
      document.body.classList.remove("bottom-panel-resizing");
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    };

    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  }

  function handleResizeKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === "ArrowUp") {
      event.preventDefault();
      setHeight(current => clampBottomPanelHeight(current + 16));
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      setHeight(current => clampBottomPanelHeight(current - 16));
    } else if (event.key === "Home") {
      event.preventDefault();
      setHeight(minBottomPanelHeight);
    } else if (event.key === "End") {
      event.preventDefault();
      setHeight(clampBottomPanelHeight(maxBottomPanelHeight));
    }
  }

  return (
    <section className="bottom-panel" style={{ "--bottom-panel-height": `${height}px` } as React.CSSProperties}>
      <div
        className="bottom-panel-resize-handle"
        role="separator"
        aria-label="Resize bottom panel"
        aria-orientation="horizontal"
        aria-valuemin={minBottomPanelHeight}
        aria-valuemax={getMaxBottomPanelHeight()}
        aria-valuenow={height}
        tabIndex={0}
        onPointerDown={startResize}
        onKeyDown={handleResizeKeyDown}
      />
      <div className="bottom-panel-tabs" role="tablist" aria-label="Bottom panels">
        {panels.map(panel => (
          <button
            key={panel.id}
            type="button"
            role="tab"
            aria-selected={panel.id === activePanel.id}
            className={panel.id === activePanel.id ? "active" : ""}
            onClick={() => setActivePanelId(panel.id)}
          >
            {panel.title}
          </button>
        ))}
      </div>
      <div className="bottom-panel-content" role="tabpanel" aria-label={activePanel.title}>
        <ActivePanelComponent />
      </div>
    </section>
  );
}

function Home({ api }: { api: ElsaStudioModuleApi }) {
  const widgets = api.dashboardWidgets.list().sort((a, b) => (a.order ?? 500) - (b.order ?? 500));
  const diagnostics = api.diagnostics.list();
  const loaded = diagnostics.filter(x => x.status === "loaded").length;
  const available = diagnostics.filter(x => x.status === "available").length;
  const failed = diagnostics.filter(x => x.status === "failed").length;

  return (
    <section className="dashboard-view">
      <div className="dashboard-metrics">
        <MetricCard title="Available modules" value={available} icon={<Boxes size={20} />} />
        <MetricCard title="Loaded modules" value={loaded} icon={<Activity size={20} />} />
        <MetricCard title="Failed modules" value={failed} icon={<Gauge size={20} />} />
      </div>

      <div className="section-header">
        <div>
          <h2>Dashboard</h2>
          <p>Runtime widgets contributed through the Studio SDK.</p>
        </div>
      </div>

      <div className="widget-grid">
        {widgets.length === 0 ? (
          <div className="empty-state">
            <LayoutDashboard size={22} />
            <span>No dashboard widgets are registered.</span>
          </div>
        ) : null}
        {widgets.map(widget => {
          const Widget = widget.component;
          return <Widget key={widget.id} />;
        })}
      </div>
    </section>
  );
}

function Diagnostics({ api }: { api: ElsaStudioModuleApi }) {
  return (
    <section>
      <div className="section-header">
        <div>
          <h2>Module diagnostics</h2>
          <p>Load status for server-discovered Studio modules.</p>
        </div>
      </div>
      <div className="diagnostics-list">
        {api.diagnostics.list().map((diagnostic, index) => (
          <div className="diagnostic-row" key={`${diagnostic.moduleId}-${diagnostic.status}-${index}`}>
            <strong>{diagnostic.moduleId}</strong>
            <span data-status={diagnostic.status}>{diagnostic.status}</span>
            <p>{diagnostic.reason}</p>
          </div>
        ))}
      </div>
    </section>
  );
}

function MetricCard({ title, value, icon }: { title: string; value: number; icon: React.ReactNode }) {
  return (
    <div className="metric-card">
      <span className="metric-icon">{icon}</span>
      <span>{title}</span>
      <strong>{value}</strong>
    </div>
  );
}

function NavIcon({ id }: { id: string }) {
  if (id.includes("dashboard")) {
    return <LayoutDashboard size={18} />;
  }

  if (id.includes("weather")) {
    return <Activity size={18} />;
  }

  if (id.includes("diagnostics")) {
    return <FileText size={18} />;
  }

  return <Gauge size={18} />;
}

function normalizePath(path: string) {
  if (path.length > 1 && path.endsWith("/")) {
    return path.slice(0, -1);
  }

  return path;
}

function getMaxBottomPanelHeight() {
  if (typeof window === "undefined") {
    return maxBottomPanelHeight;
  }

  return Math.max(minBottomPanelHeight, Math.min(maxBottomPanelHeight, window.innerHeight - minWorkspaceHeight));
}

function clampBottomPanelHeight(value: unknown) {
  const number = Number(value);
  if (!Number.isFinite(number)) {
    return defaultBottomPanelHeight;
  }

  return Math.min(getMaxBottomPanelHeight(), Math.max(minBottomPanelHeight, Math.round(number)));
}

function getInitialBottomPanelHeight() {
  if (typeof window === "undefined") {
    return defaultBottomPanelHeight;
  }

  return clampBottomPanelHeight(window.localStorage.getItem(bottomPanelHeightStorageKey) ?? defaultBottomPanelHeight);
}

function getInitialActiveBottomPanelId() {
  if (typeof window === "undefined") {
    return "";
  }

  return window.localStorage.getItem(activeBottomPanelStorageKey) ?? "";
}

function navigateTo(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function App() {
  return (
    <ThemeProvider>
      <AppContent />
    </ThemeProvider>
  );
}
