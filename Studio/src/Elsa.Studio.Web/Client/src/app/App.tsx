import React, { useEffect, useMemo, useState } from "react";
import {
  Activity,
  ChevronDown,
  ChevronUp,
  Hammer,
  ExternalLink,
  FileText,
  Gauge,
  GitBranch,
  Github,
  LayoutDashboard,
  Maximize2,
  Minimize2,
  PackagePlus,
  PackageSearch,
  Search,
  ShieldCheck
} from "lucide-react";
import type {
  ElsaStudioModuleApi,
  StudioDiagnosticsWidgetContribution,
  StudioDiagnosticsWidgetProps,
  StudioDiagnosticsWidgetState,
  StudioModulesResponse,
  StudioNavigationContribution,
  StudioPanelContribution
} from "../sdk";
import { createStudioRegistry, findFeatureAreaForPath } from "./registry";
import { createEndpointContext } from "../sdk";
import { getStudioRuntimeConfig, type StudioRuntimeConfig } from "./runtime";
import { loadStudioModules } from "./loader";
import { ThemeProvider } from "./components/ThemeProvider";
import { ThemeSwitcher } from "./components/ThemeSwitcher";
import { QueryProvider } from "./providers/QueryProvider";
import { ModuleManagementPage } from "./modules/ModuleManagementPage";
import { PackageFeedsPage } from "./modules/PackageFeedsPage";
import { ExtensionBuilderPage } from "./modules/ExtensionBuilderPage";
import { registerBuiltInPropertyEditors } from "./propertyEditors";
import elsaLogo from "../assets/images/icon.png";
import { AgentLauncher, createWorkflowAgentContextProvider, type AgentSessionIndicatorSession, workflowAgentCapabilities, workflowPromptStarters } from "./agent";
import { WeaverSurface } from "./weaver";
import "./styles.css";
import "./agent/agent.css";
import "./weaver/weaver.css";

type LoadState = "loading" | "ready" | "failed";
type NavigationSection = "workspace" | "settings";
type NavIconTileStyle = React.CSSProperties & { "--nav-icon-color": string };
type HostHealthStatus = "checking" | "ok" | "attention" | "unavailable";
type DiagnosticsWidgetBoundaryProps = {
  children: React.ReactNode;
  title: string;
};
type DiagnosticsWidgetBoundaryState = {
  error: string | null;
};

interface HostHealthRegistry {
  modules: Array<{ status?: string; diagnostics?: Array<{ status?: string }> }>;
  diagnostics?: Array<{ status?: string }>;
}

interface HostHealthEntry {
  status: HostHealthStatus;
  attention: number;
  detail: string;
}

const builtInNavigation: StudioNavigationContribution[] = [
  { id: "dashboard", label: "Dashboard", path: "/dashboard", order: 0, iconColor: "#0ea5e9" },
  { id: "extension-builder", label: "Extension Builder", path: "/extension-builder", order: 40, iconColor: "#ec4899" },
  { id: "modules", label: "Modules", path: "/modules", order: 80, iconColor: "#8b5cf6" },
  { id: "package-feeds", label: "Package feeds", path: "/package-feeds", order: 90, iconColor: "#f59e0b" },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", activePathPrefix: "/diagnostics", order: 900, iconColor: "#10b981" }
];

const ModulesChangedEventName = "elsa-studio:modules-changed";
const bottomPanelHeightStorageKey = "elsa-studio-bottom-panel-height";
const activeBottomPanelStorageKey = "elsa-studio-active-bottom-panel";
const bottomPanelCollapsedStorageKey = "elsa-studio-bottom-panel-collapsed";
const bottomPanelMaximizedStorageKey = "elsa-studio-bottom-panel-maximized";
const defaultBottomPanelHeight = 260;
const minBottomPanelHeight = 140;
const maxBottomPanelHeight = 560;
const minWorkspaceHeight = 280;

function AppContent() {
  const [state, setState] = useState<LoadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [api, setApi] = useState<ElsaStudioModuleApi | null>(null);
  const [path, setPath] = useState(normalizePath(window.location.pathname));
  const [moduleRegistryRevision, setModuleRegistryRevision] = useState(0);
  const [assistantOpen, setAssistantOpen] = useState(false);
  const [agentSessions, setAgentSessions] = useState<AgentSessionIndicatorSession[]>([]);
  const [incomingPrompt, setIncomingPrompt] = useState<{ id: string; message: string } | null>(null);
  const runtimeConfig = getStudioRuntimeConfig();
  const shellBaseUrl = window.location.origin;
  const backendBaseUrl = resolveRuntimeBaseUrl(runtimeConfig.backendBaseUrl, shellBaseUrl);
  const backendHeaders = createBackendHeaders(runtimeConfig);

  useEffect(() => {
    const onPopState = () => setPath(normalizePath(window.location.pathname));
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  useEffect(() => {
    const onModulesChanged = () => setModuleRegistryRevision(revision => revision + 1);
    window.addEventListener(ModulesChangedEventName, onModulesChanged);
    return () => window.removeEventListener(ModulesChangedEventName, onModulesChanged);
  }, []);

  // Cross-surface prompt dispatch (e.g. an "Explain this workflow" action) opens the Weaver dock and forwards the prompt.
  useEffect(() => {
    if (!api) return;
    return api.ai.onPrompt(prompt => {
      setAssistantOpen(true);
      setIncomingPrompt({ id: `prompt-${Date.now()}`, message: prompt.message });
    });
  }, [api]);

  useEffect(() => {
    let disposed = false;

    async function boot() {
      try {
        setState("loading");
        const response = await fetch("/_elsa/studio/modules");
        if (!response.ok) {
          throw new Error(`Manifest request failed with ${response.status}.`);
        }

        const manifestResponse = (await response.json()) as StudioModulesResponse;
        const registry = createStudioRegistry({
          hostVersion: manifestResponse.hostVersion,
          sdkVersion: manifestResponse.sdkVersion,
          ...createEndpointContext(shellBaseUrl)
        }, backendBaseUrl, backendHeaders);

        for (const diagnostic of manifestResponse.diagnostics) {
          registry.diagnostics.add(diagnostic);
        }

        await loadStudioModules(manifestResponse.modules, registry, {
          hostVersion: manifestResponse.hostVersion,
          sdkVersion: manifestResponse.sdkVersion
        });
        registerBuiltInAgentContributions(registry);

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
  }, [backendBaseUrl, moduleRegistryRevision, shellBaseUrl]);

  const routes = useMemo(() => api?.routes.list() ?? [], [api, state]);
  const navigation = useMemo(
    () => getStudioNavigation(api?.navigation.list() ?? []),
    [api, state]
  );
  const panels = useMemo(
    () => (api?.panels.list() ?? []).sort((a, b) => (a.order ?? 500) - (b.order ?? 500)),
    [api, state]
  );
  const featureAreas = useMemo(
    () => (api?.featureAreas.list() ?? []).sort((a, b) => (a.order ?? 500) - (b.order ?? 500)),
    [api, state]
  );

  if (state === "loading") {
    return (
      <ShellFrame navigation={navigation} panels={[]} path={path} title="Loading modules" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl}>
        <div className="empty-state">Loading Studio modules...</div>
      </ShellFrame>
    );
  }

  if (state === "failed") {
    return (
      <ShellFrame navigation={navigation} panels={[]} path={path} title="Startup failed" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl}>
        <div className="error-state">{error}</div>
      </ShellFrame>
    );
  }

  const dashboardPath = isDashboardPath(path);
  const activeRoute = dashboardPath ? undefined : findRouteForPath(routes, path);
  const ActiveComponent = activeRoute?.component;
  const owningFeatureArea = findFeatureAreaForPath(featureAreas, path);
  const pageTitle = dashboardPath ? "Dashboard" : navigation.find(item => isNavigationItemActive(item, path))?.label ?? activeRoute?.label ?? owningFeatureArea?.title ?? "Studio";

  return (
    <>
      <ShellFrame
        navigation={navigation}
        panels={panels}
        path={path}
        title={pageTitle}
        onNavigate={navigateTo}
        backendBaseUrl={backendBaseUrl}
        assistantAction={<AgentLauncher open={assistantOpen} sessions={agentSessions} onClick={() => setAssistantOpen(current => !current)} />}
      >
        {dashboardPath ? <Dashboard api={api!} /> : null}
        {path === "/extension-builder" ? <ExtensionBuilderPage api={api!} /> : null}
        {path === "/modules" ? <ModuleManagementPage api={api!} /> : null}
        {path === "/package-feeds" ? <PackageFeedsPage api={api!} /> : null}
        {path === "/diagnostics" ? <Diagnostics api={api!} /> : null}
        {path === "/diagnostics/modules" ? <ModuleDiagnostics api={api!} /> : null}
        {ActiveComponent ? <ActiveComponent /> : null}
        {!ActiveComponent && !dashboardPath && path !== "/extension-builder" && path !== "/modules" && path !== "/package-feeds" && path !== "/diagnostics" && path !== "/diagnostics/modules" ? (
          <div className="empty-state">
            {owningFeatureArea
              ? `${owningFeatureArea.title} owns ${path}, but no route component is registered for it.`
              : `No Studio route is registered for ${path}.`}
          </div>
        ) : null}
      </ShellFrame>
      {assistantOpen ? (
        <WeaverSurface api={api!} surface={{ route: path }} variant="dock" onClose={() => setAssistantOpen(false)} onSessionIndicatorChange={setAgentSessions} incomingPrompt={incomingPrompt} />
      ) : null}
    </>
  );
}

export function findRouteForPath<TRoute extends { path: string }>(routes: TRoute[], path: string) {
  return routes.find(route => route.path === path) ?? routes.find(route => routeMatchesPath(route.path, path));
}

function routeMatchesPath(routePath: string, path: string) {
  const routeSegments = routePath.split("/").filter(Boolean);
  const pathSegments = path.split("/").filter(Boolean);
  if (routeSegments.length !== pathSegments.length) return false;

  return routeSegments.every((segment, index) =>
    segment.startsWith(":") || segment === pathSegments[index]);
}

function createBackendHeaders(runtimeConfig: StudioRuntimeConfig): HeadersInit | undefined {
  const moduleManagementApiKey = runtimeConfig.backendModuleManagementApiKey?.trim();
  return moduleManagementApiKey ? { "X-Elsa-Module-Management-Key": moduleManagementApiKey } : undefined;
}

function ShellFrame({
  navigation,
  panels,
  path,
  title,
  backendBaseUrl,
  assistantAction,
  onNavigate,
  children
}: {
  navigation: StudioNavigationContribution[];
  panels: StudioPanelContribution[];
  path: string;
  title: string;
  backendBaseUrl: string;
  assistantAction?: React.ReactNode;
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  const childrenByParentId = new Map<string, StudioNavigationContribution[]>();
  for (const item of navigation) {
    if (!item.parentId) {
      continue;
    }

    const children = childrenByParentId.get(item.parentId) ?? [];
    children.push(item);
    childrenByParentId.set(item.parentId, children);
  }

  const navigationSections = [
    { id: "workspace", label: "Workspace", items: getTopLevelNavigationItems(navigation, "workspace") },
    { id: "settings", label: "Settings", items: getTopLevelNavigationItems(navigation, "settings") }
  ].filter(section => section.items.length > 0);

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

        {navigationSections.map(section => (
          <nav key={section.id} className="nav-section" aria-label={section.label}>
            <span className="nav-heading">{section.label}</span>
            {section.items.map(item => {
              const childItems = (childrenByParentId.get(item.id) ?? []).filter(child => getNavigationSection(child) === section.id);
              const hasActiveChild = childItems.some(child => isNavigationItemActive(child, path));
              return (
                <div className="nav-item-group" key={item.id}>
                  <a
                    className={[isNavigationItemActive(item, path) ? "active" : "", hasActiveChild ? "has-active-child" : ""].filter(Boolean).join(" ")}
                    href={item.path}
                    onClick={event => {
                      event.preventDefault();
                      onNavigate(item.path);
                    }}
                  >
                    <NavIconTile item={item} />
                    {item.label}
                  </a>
                  {childItems.length > 0 ? (
                    <div className="nav-children">
                      {childItems.map(child => (
                        <a
                          key={child.id}
                          className={isNavigationItemActive(child, path) ? "active nav-child" : "nav-child"}
                          href={child.path}
                          onClick={event => {
                            event.preventDefault();
                            onNavigate(child.path);
                          }}
                        >
                          {child.label}
                        </a>
                      ))}
                    </div>
                  ) : null}
                </div>
              );
            })}
          </nav>
        ))}

        <div className="sidebar-footer" aria-label="Backend API status">
          <span className="sidebar-status-dot" aria-hidden="true" />
          <span>
            <strong>Backend API</strong>
            <small>{new URL(backendBaseUrl).host}</small>
          </span>
        </div>
      </aside>

      <div className="main-frame">
        <header className="topbar">
          <div>
            <span className="breadcrumb">Studio / {title}</span>
            <h1>{title}</h1>
          </div>
          <div className="topbar-actions">
            {assistantAction}
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

function registerBuiltInAgentContributions(api: ElsaStudioModuleApi) {
  registerBuiltInPropertyEditors(api);
  api.agent.contextProviders.add(createWorkflowAgentContextProvider());
  for (const capability of workflowAgentCapabilities) {
    api.agent.capabilities.add(capability);
  }
  for (const promptStarter of workflowPromptStarters) {
    api.agent.promptStarters.add(promptStarter);
  }
}

function BottomPanel({ panels }: { panels: StudioPanelContribution[] }) {
  const initialMaximized = getInitialBoolean(bottomPanelMaximizedStorageKey, false);
  const [height, setHeight] = useState(getInitialBottomPanelHeight);
  const [activePanelId, setActivePanelId] = useState(getInitialActiveBottomPanelId);
  const [collapsed, setCollapsed] = useState(() => !initialMaximized && getInitialBottomPanelCollapsed());
  const [maximized, setMaximized] = useState(() => initialMaximized);
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
    window.localStorage.setItem(bottomPanelCollapsedStorageKey, String(collapsed));
  }, [collapsed]);

  useEffect(() => {
    window.localStorage.setItem(bottomPanelMaximizedStorageKey, String(maximized));
  }, [maximized]);

  useEffect(() => {
    document.body.classList.toggle("bottom-panel-maximized", maximized);
    return () => document.body.classList.remove("bottom-panel-maximized");
  }, [maximized]);

  useEffect(() => {
    if (!maximized) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMaximized(false);
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [maximized]);

  useEffect(() => {
    if (maximized) {
      return;
    }

    const handleResize = () => setHeight(current => clampBottomPanelHeight(current));
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, [maximized]);

  function startResize(event: React.PointerEvent<HTMLDivElement>) {
    event.preventDefault();
    setCollapsed(false);
    setMaximized(false);

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
      setCollapsed(false);
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

  function toggleCollapsed() {
    setCollapsed(current => {
      const next = !current;
      if (next) {
        setMaximized(false);
      }
      return next;
    });
  }

  function toggleMaximized() {
    setMaximized(current => {
      const next = !current;
      if (next) {
        setCollapsed(false);
      }
      return next;
    });
  }

  return (
    <section
      className={`bottom-panel ${collapsed ? "collapsed" : ""} ${maximized ? "maximized" : ""}`}
      style={{ "--bottom-panel-height": `${height}px` } as React.CSSProperties}
      aria-label="Bottom panel"
    >
      {!collapsed && !maximized ? (
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
      ) : null}
      <div className="bottom-panel-tabs">
        <div className="bottom-panel-tab-list" role="tablist" aria-label="Bottom panels">
          {panels.map(panel => (
            <button
              key={panel.id}
              type="button"
              role="tab"
              aria-selected={panel.id === activePanel.id}
              className={panel.id === activePanel.id ? "active" : ""}
              onClick={() => {
                setActivePanelId(panel.id);
                setCollapsed(false);
              }}
            >
              {panel.title}
            </button>
          ))}
        </div>
        <div className="bottom-panel-actions" aria-label="Bottom panel controls">
          <button
            type="button"
            className="bottom-panel-action-button"
            aria-label={collapsed ? "Expand bottom panel" : "Collapse bottom panel"}
            title={collapsed ? "Expand" : "Collapse"}
            onClick={toggleCollapsed}
          >
            {collapsed ? <ChevronUp size={15} /> : <ChevronDown size={15} />}
          </button>
          <button
            type="button"
            className="bottom-panel-action-button"
            aria-label={maximized ? "Restore bottom panel" : "Maximize bottom panel"}
            title={maximized ? "Restore" : "Maximize"}
            onClick={toggleMaximized}
          >
            {maximized ? <Minimize2 size={15} /> : <Maximize2 size={15} />}
          </button>
        </div>
      </div>
      <div className="bottom-panel-content" role="tabpanel" aria-label={activePanel.title} aria-hidden={collapsed}>
        <ActivePanelComponent />
      </div>
    </section>
  );
}

export function Dashboard({ api }: { api: ElsaStudioModuleApi }) {
  const widgets = api.dashboardWidgets.list().sort(compareOrderedContributions);

  return (
    <section className="dashboard-view">
      <HostHealthStrip api={api} />

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

function HostHealthStrip({ api }: { api: ElsaStudioModuleApi }) {
  const [studio, setStudio] = useState<HostHealthEntry>(checkingHostHealth("Checking Studio host."));
  const [server, setServer] = useState<HostHealthEntry>(checkingHostHealth("Checking Server host."));
  const [backend, setBackend] = useState<HostHealthEntry>(checkingHostHealth("Checking backend API."));

  useEffect(() => {
    let disposed = false;

    async function load() {
      const [studioHealth, serverHealth, backendHealth] = await Promise.all([
        readHostHealth(api.host.http.getJson<HostHealthRegistry>("/_elsa/module-management/registry"), "Studio"),
        readHostHealth(api.backend.http.getJson<HostHealthRegistry>("/_elsa/module-management/registry"), "Server"),
        readBackendHealth(api.backend.baseUrl)
      ]);

      if (!disposed) {
        setStudio(studioHealth);
        setServer(serverHealth);
        setBackend(backendHealth);
      }
    }

    void load();

    return () => {
      disposed = true;
    };
  }, [api]);

  const unavailableHosts = [studio, server].filter(host => host.status === "unavailable").length;
  const attention = studio.attention + server.attention + unavailableHosts;
  const attentionStatus: HostHealthStatus = studio.status === "checking" || server.status === "checking"
    ? "checking"
    : attention === 0
      ? "ok"
      : "attention";

  return (
    <div className="host-health-strip" aria-label="Host health">
      <HostHealthTile title="Studio host" value={labelForHostStatus(studio.status)} detail={studio.detail} status={studio.status} icon={<ShieldCheck size={18} />} />
      <HostHealthTile title="Server host" value={labelForHostStatus(server.status)} detail={server.detail} status={server.status} icon={<ShieldCheck size={18} />} />
      <HostHealthTile title="Backend API" value={backend.status === "ok" ? "Connected" : labelForHostStatus(backend.status)} detail={backend.detail} status={backend.status} icon={<Activity size={18} />} />
      <HostHealthTile title="Attention" value={attentionStatus === "checking" ? "Checking" : String(attention)} detail={attention === 0 ? "No host issues reported." : "Open Modules to review host-scoped issues."} status={attentionStatus} icon={<Gauge size={18} />} />
    </div>
  );
}

function HostHealthTile({
  title,
  value,
  detail,
  status,
  icon
}: {
  title: string;
  value: string;
  detail: string;
  status: HostHealthStatus;
  icon: React.ReactNode;
}) {
  return (
    <div className="host-health-tile" data-status={status}>
      <span className="host-health-icon" aria-hidden="true">{icon}</span>
      <span className="host-health-title">{title}</span>
      <strong>{value}</strong>
      <span className="host-health-detail">{detail}</span>
    </div>
  );
}

async function readHostHealth(registryRequest: Promise<HostHealthRegistry>, hostLabel: string): Promise<HostHealthEntry> {
  try {
    const registry = await registryRequest;
    const attention = countRegistryAttention(registry);

    return {
      status: attention === 0 ? "ok" : "attention",
      attention,
      detail: attention === 0 ? `${hostLabel} is reachable.` : `${attention} item${attention === 1 ? "" : "s"} need review.`
    };
  } catch (e) {
    return {
      status: "unavailable",
      attention: 0,
      detail: `${hostLabel} module registry is unavailable: ${getHealthErrorMessage(e)}`
    };
  }
}

async function readBackendHealth(baseUrl: string): Promise<HostHealthEntry> {
  try {
    await fetch(baseUrl, { cache: "no-store", mode: "no-cors" });
    return {
      status: "ok",
      attention: 0,
      detail: baseUrl
    };
  } catch (e) {
    return {
      status: "unavailable",
      attention: 0,
      detail: `${baseUrl} (${getHealthErrorMessage(e)})`
    };
  }
}

function checkingHostHealth(detail: string): HostHealthEntry {
  return { status: "checking", attention: 0, detail };
}

function getHealthErrorMessage(error: unknown) {
  return error instanceof Error && error.message.length > 0 ? error.message : "request failed";
}

function countRegistryAttention(registry: HostHealthRegistry) {
  const attentionStatuses = new Set(["failed", "incompatible", "disabled"]);
  const moduleStatuses = registry.modules.filter(module => module.status && attentionStatuses.has(module.status)).length;
  const moduleDiagnostics = registry.modules.flatMap(module => module.diagnostics ?? []).filter(diagnostic => diagnostic.status && attentionStatuses.has(diagnostic.status)).length;
  const hostDiagnostics = (registry.diagnostics ?? []).filter(diagnostic => diagnostic.status && attentionStatuses.has(diagnostic.status)).length;

  return moduleStatuses + moduleDiagnostics + hostDiagnostics;
}

function labelForHostStatus(status: HostHealthStatus) {
  if (status === "ok")
    return "OK";

  if (status === "attention")
    return "Review";

  if (status === "unavailable")
    return "Unavailable";

  return "Checking";
}

export function Diagnostics({ api }: { api: ElsaStudioModuleApi }) {
  const widgets = api.diagnosticsWidgets.list().sort(compareOrderedContributions);

  return (
    <section className="diagnostics-view">
      <div className="section-header">
        <div>
          <h2>Diagnostics</h2>
          <p>Runtime and operational widgets contributed through the Studio SDK.</p>
        </div>
      </div>

      <div className="widget-grid">
        {widgets.length === 0 ? (
          <div className="empty-state">
            <FileText size={22} />
            <span>No diagnostics widgets are registered.</span>
          </div>
        ) : null}
        {widgets.map(widget => (
          <DiagnosticsWidgetHost widget={widget} key={widget.id} />
        ))}
      </div>
    </section>
  );
}

function DiagnosticsWidgetHost({ widget }: { widget: StudioDiagnosticsWidgetContribution }) {
  const [state, setState] = useState<StudioDiagnosticsWidgetState>({ status: widget.load || widget.subscribe ? "loading" : "idle" });
  const Widget = widget.component as React.ComponentType<StudioDiagnosticsWidgetProps>;

  useEffect(() => {
    let disposed = false;
    let cleanup: void | (() => void);

    async function loadSnapshot() {
      if (!widget.load) {
        return;
      }

      try {
        setState(current => ({ ...current, status: "loading", error: undefined }));
        const snapshot = await widget.load();
        if (!disposed) {
          setState({ status: "ready", snapshot });
        }
      } catch (e) {
        if (!disposed) {
          setState({ status: "error", error: getHealthErrorMessage(e) });
        }
      }
    }

    void loadSnapshot();

    if (widget.subscribe) {
      try {
        setState(current => ({ ...current, status: "streaming", error: undefined }));
        cleanup = widget.subscribe(snapshot => {
          if (!disposed) {
            setState({ status: "streaming", snapshot });
          }
        });
      } catch (e) {
        setState({ status: "error", error: getHealthErrorMessage(e) });
      }
    }

    return () => {
      disposed = true;
      if (typeof cleanup === "function") {
        cleanup();
      }
    };
  }, [widget]);

  return (
    <DiagnosticsWidgetBoundary title={widget.title}>
      <Widget state={state} />
    </DiagnosticsWidgetBoundary>
  );
}

class DiagnosticsWidgetBoundary extends React.Component<DiagnosticsWidgetBoundaryProps, DiagnosticsWidgetBoundaryState> {
  state: DiagnosticsWidgetBoundaryState = { error: null };

  static getDerivedStateFromError(error: unknown): DiagnosticsWidgetBoundaryState {
    return { error: getHealthErrorMessage(error) };
  }

  render() {
    if (this.state.error) {
      return (
        <div className="error-state">
          <strong>{this.props.title}</strong>
          <span>{this.state.error}</span>
        </div>
      );
    }

    return this.props.children;
  }
}

function ModuleDiagnostics({ api }: { api: ElsaStudioModuleApi }) {
  return (
    <section>
      <div className="section-header">
        <div>
          <h2>Module load status</h2>
          <p>Server-discovered module activation diagnostics for administration.</p>
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

function compareOrderedContributions(a: { id: string; title?: string; order?: number }, b: { id: string; title?: string; order?: number }) {
  const orderComparison = (a.order ?? 500) - (b.order ?? 500);
  if (orderComparison !== 0) {
    return orderComparison;
  }

  return (a.title ?? a.id).localeCompare(b.title ?? b.id) || a.id.localeCompare(b.id);
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

  if (id.includes("workflow")) {
    return <GitBranch size={18} />;
  }

  if (id.includes("extension-builder")) {
    return <Hammer size={18} />;
  }

  if (id.includes("modules")) {
    return <PackageSearch size={18} />;
  }

  if (id.includes("feeds")) {
    return <PackagePlus size={18} />;
  }

  return <Gauge size={18} />;
}

function NavIconTile({ item }: { item: StudioNavigationContribution }) {
  return (
    <span
      className="nav-icon-tile"
      style={{ "--nav-icon-color": item.iconColor ?? getDefaultNavIconColor(item.id) } as NavIconTileStyle}
      aria-hidden="true"
    >
      <NavIcon id={item.id} />
    </span>
  );
}

function getDefaultNavIconColor(id: string) {
  if (id.includes("dashboard") || id === "home") {
    return "#0ea5e9";
  }

  if (id.includes("weather")) {
    return "#14b8a6";
  }

  if (id.includes("diagnostics")) {
    return "#10b981";
  }

  if (id.includes("modules")) {
    return "#8b5cf6";
  }

  if (id.includes("feeds")) {
    return "#f59e0b";
  }

  if (id.includes("extension-builder")) {
    return "#ec4899";
  }

  return "var(--primary)";
}

function isNavigationItemActive(item: StudioNavigationContribution, path: string) {
  if (item.id === "dashboard" && isDashboardPath(path)) return true;
  if (path === item.path) return true;
  if (!item.activePathPrefix) return false;
  return path === item.activePathPrefix || path.startsWith(`${item.activePathPrefix}/`);
}

export function isDashboardPath(path: string) {
  return path === "/" || path === "/dashboard" || path === "/overview";
}

export function getStudioNavigation(moduleNavigation: StudioNavigationContribution[]) {
  return [...builtInNavigation, ...moduleNavigation.filter(item => !isDashboardPath(item.path))]
    .filter((item, index, items) => items.findIndex(candidate => candidate.id === item.id) === index)
    .sort((a, b) => (a.order ?? 500) - (b.order ?? 500));
}

export function getNavigationSection(item: Pick<StudioNavigationContribution, "id" | "path">): NavigationSection {
  const settingsPaths = new Set(["/modules", "/package-feeds", "/features", "/extension-builder"]);
  if (settingsPaths.has(item.path) || item.id === "modules" || item.id === "package-feeds" || item.id === "feature-management") {
    return "settings";
  }

  return "workspace";
}

export function getTopLevelNavigationItems(navigation: StudioNavigationContribution[], section: NavigationSection) {
  const ids = new Set(navigation.map(item => item.id));
  return navigation.filter(item => getNavigationSection(item) === section && (!item.parentId || !ids.has(item.parentId)));
}

function normalizePath(path: string) {
  if (path.length > 1 && path.endsWith("/")) {
    return path.slice(0, -1);
  }

  return path;
}

function resolveRuntimeBaseUrl(value: string | undefined, fallbackBaseUrl: string) {
  return value ? new URL(value, fallbackBaseUrl).toString() : fallbackBaseUrl;
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

function getInitialBottomPanelCollapsed() {
  if (typeof window === "undefined") {
    return false;
  }

  const value = window.localStorage.getItem(bottomPanelCollapsedStorageKey);
  return value === null ? window.innerWidth <= 640 : value === "true";
}

function getInitialBoolean(storageKey: string, fallback: boolean) {
  if (typeof window === "undefined") {
    return fallback;
  }

  const value = window.localStorage.getItem(storageKey);
  return value === null ? fallback : value === "true";
}

function navigateTo(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function App() {
  return (
    <ThemeProvider>
      <QueryProvider>
        <AppContent />
      </QueryProvider>
    </ThemeProvider>
  );
}
