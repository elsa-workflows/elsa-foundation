import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
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
  Paintbrush,
  PackagePlus,
  PackageSearch,
  RefreshCw,
  Search
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
import { requestStudioNavigation } from "../sdk";
import { createStudioRegistry, findFeatureAreaForPath } from "./registry";
import type { AuthProviderManager } from "../sdk";
import { getStudioRuntimeConfig, getStudioRuntimeSettings } from "./runtime";
import { StudioAuthBoundary, createStudioAuthManager, createStudioEndpointContext } from "./auth/studioAuth";
import { loadStudioModules } from "./loader";
import { ThemeProvider } from "./components/ThemeProvider";
import { ThemeSwitcher } from "./components/ThemeSwitcher";
import { defaultThemeStoreCapabilities, getThemeStoreCapabilities, type ThemeStoreCapabilities } from "./themes/themeStoreApi";
import { QueryProvider } from "./providers/QueryProvider";
import { useStudioManifest } from "./hooks/useStudioManifest";
import { getHealthErrorMessage } from "./hooks/useHostHealth";
import { DialogHost } from "./ui/dialog/DialogHost";
import { registerBuiltInSettingEditors } from "./ui/shared";
import { tabElementIds, useTablistKeyboard } from "./ui/layout/Tabs";
import { ModuleManagementPage } from "./modules/ModuleManagementPage";
import { PackageFeedsPage } from "./modules/PackageFeedsPage";
import { ExtensionBuilderPage } from "./modules/ExtensionBuilderPage";
import { ThemeBuilderPage } from "./modules/ThemeBuilderPage";
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
type DiagnosticsWidgetBoundaryProps = {
  children: React.ReactNode;
  title: string;
};
type DiagnosticsWidgetBoundaryState = {
  error: string | null;
};

// Single source of truth for the categorical nav-icon accent colours. Both the built-in
// navigation entries and getDefaultNavIconColor() (which matches contributed modules by id
// substring) resolve against this map so the palette is defined exactly once.
// (The HostHealthRegistry/HostHealthEntry types that #192 added here alongside this map now live in
// hooks/useHostHealth.ts after the #189 Query migration, so they are intentionally not re-added.)
const navIconColors = {
  dashboard: "#0ea5e9",
  weather: "#14b8a6",
  diagnostics: "#10b981",
  modules: "#8b5cf6",
  feeds: "#f59e0b",
  themes: "#0f766e",
  extensionBuilder: "#ec4899"
} as const;

const builtInNavigation: StudioNavigationContribution[] = [
  { id: "extension-builder", label: "Extension Builder", path: "/extension-builder", order: 40, iconColor: navIconColors.extensionBuilder },
  { id: "modules", label: "Modules", path: "/modules", order: 80, iconColor: navIconColors.modules },
  { id: "package-feeds", label: "Package feeds", path: "/package-feeds", order: 90, iconColor: navIconColors.feeds },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", activePathPrefix: "/diagnostics", order: 900, iconColor: navIconColors.diagnostics }
];

const themeBuilderNavigation: StudioNavigationContribution = {
  id: "theme-builder",
  label: "Theme Builder",
  path: "/theme-builder",
  order: 85,
  iconColor: navIconColors.themes
};

const ModulesChangedEventName = "elsa-studio:modules-changed";
const bottomPanelHeightStorageKey = "elsa-studio-bottom-panel-height";
const activeBottomPanelStorageKey = "elsa-studio-active-bottom-panel";
const bottomPanelCollapsedStorageKey = "elsa-studio-bottom-panel-collapsed";
const bottomPanelMaximizedStorageKey = "elsa-studio-bottom-panel-maximized";
const defaultBottomPanelHeight = 260;
const minBottomPanelHeight = 140;
const maxBottomPanelHeight = 560;
const minWorkspaceHeight = 280;

function AppContent({ authManager }: { authManager: AuthProviderManager | null }) {
  const [state, setState] = useState<LoadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [api, setApi] = useState<ElsaStudioModuleApi | null>(null);
  const [path, setPath] = useState(normalizePath(window.location.pathname));
  const [moduleRegistryRevision, setModuleRegistryRevision] = useState(0);
  const [themeCapabilities, setThemeCapabilities] = useState<ThemeStoreCapabilities>(defaultThemeStoreCapabilities);
  const [assistantOpen, setAssistantOpen] = useState(false);
  const [agentSessions, setAgentSessions] = useState<AgentSessionIndicatorSession[]>([]);
  const [incomingPrompt, setIncomingPrompt] = useState<{ id: string; message: string; requestId?: string } | null>(null);
  const acceptedPathRef = useRef(normalizePath(window.location.pathname));
  const acceptedLocationRef = useRef(currentRelativeLocation());
  const runtimeConfig = getStudioRuntimeConfig();
  const shellBaseUrl = window.location.origin;
  const backendBaseUrl = resolveRuntimeBaseUrl(runtimeConfig.backendBaseUrl, shellBaseUrl);
  // The shell endpoint context (host origin) routed through the authenticated HTTP client: the manifest
  // query fetches `/_elsa/studio/modules` through this, so the boot document carries the bearer token
  // (when a user provider is configured). Anonymous boot — no provider — falls back to the plain SDK client.
  // No management-key header rides any request: after ADR 0037 / #248 the browser holds no host management key.
  const shellContext = useMemo(
    () => createStudioEndpointContext(shellBaseUrl, authManager),
    [authManager, shellBaseUrl]
  );

  useEffect(() => {
    const onPopState = () => {
      const nextPath = normalizePath(window.location.pathname);
      const nextLocation = currentRelativeLocation();
      if (nextPath !== acceptedPathRef.current && !requestStudioNavigation(acceptedLocationRef.current, nextLocation)) {
        window.history.pushState({}, "", acceptedLocationRef.current);
        return;
      }
      acceptedPathRef.current = nextPath;
      acceptedLocationRef.current = nextLocation;
      setPath(nextPath);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const navigateTo = useCallback((nextPath: string) => {
    if (!requestStudioNavigation(acceptedLocationRef.current, nextPath)) return;
    window.history.pushState({}, "", nextPath);
    acceptedPathRef.current = normalizePath(window.location.pathname);
    acceptedLocationRef.current = currentRelativeLocation();
    setPath(acceptedPathRef.current);
    window.dispatchEvent(new PopStateEvent("popstate"));
  }, []);

  // Cross-surface prompt dispatch (e.g. an "Explain this workflow" action) opens the Weaver dock and forwards the prompt.
  useEffect(() => {
    if (!api) return;
    return api.ai.onPrompt(prompt => {
      setAssistantOpen(true);
      setIncomingPrompt({ id: `prompt-${Date.now()}`, message: prompt.message, requestId: prompt.requestId });
    });
  }, [api]);

  // The boot manifest is fetched through TanStack Query, using the authenticated shell HTTP client so
  // the manifest request carries auth + management-key headers; the effect below turns manifest data
  // into a loaded registry (module loading + contribution registration are side effects, not cacheable data).
  const manifestQuery = useStudioManifest(shellContext.http);

  useEffect(() => {
    let disposed = false;

    async function loadThemeCapabilities() {
      const capabilities = await getThemeStoreCapabilities(shellContext);
      if (!disposed) {
        setThemeCapabilities(capabilities);
      }
    }

    void loadThemeCapabilities();
    window.addEventListener(ModulesChangedEventName, loadThemeCapabilities);
    return () => {
      disposed = true;
      window.removeEventListener(ModulesChangedEventName, loadThemeCapabilities);
    };
  }, [shellContext]);

  // A modules-changed event re-fetches the manifest and bumps the revision so the loader effect re-runs
  // even when the refetched manifest is structurally identical (a package may have been reconciled
  // in place). Preserves the original "rebuild the registry on module changes" behavior.
  useEffect(() => {
    const onModulesChanged = () => {
      void manifestQuery.refetch();
      setModuleRegistryRevision(revision => revision + 1);
    };
    window.addEventListener(ModulesChangedEventName, onModulesChanged);
    return () => window.removeEventListener(ModulesChangedEventName, onModulesChanged);
  }, [manifestQuery]);

  useEffect(() => {
    if (manifestQuery.isPending) {
      setState("loading");
      return;
    }

    if (manifestQuery.isError) {
      setError(manifestQuery.error instanceof Error ? manifestQuery.error.message : String(manifestQuery.error));
      setState("failed");
      return;
    }

    const manifestResponse = manifestQuery.data;
    if (!manifestResponse) return;

    let disposed = false;

    async function loadRegistry(manifest: StudioModulesResponse) {
      try {
        setState("loading");
        // Route both endpoint contexts through the authenticated HTTP client when a user provider is
        // configured, so backend/host requests attach a bearer token and refresh-retry on 401. When no
        // provider is configured this yields the plain SDK client (anonymous path). No management-key
        // header rides any request (ADR 0037 / #248). The manifest document itself was already fetched
        // (through the same authenticated shell context) by useStudioManifest; here we only turn it into
        // a loaded registry.
        const backendContext = createStudioEndpointContext(backendBaseUrl, authManager);
        const registry = createStudioRegistry({
          hostVersion: manifest.hostVersion,
          sdkVersion: manifest.sdkVersion,
          // The Studio host serves its own gated management surface (module management, feature management,
          // console-stream); those requests carry the user bearer via the authenticated host context. The
          // hub credential is the user-JWT factory (see createStudioEndpointContext).
          ...createStudioEndpointContext(shellBaseUrl, authManager)
        }, {
          backendBaseUrl,
          backendHttp: backendContext.http,
          runtime: getStudioRuntimeSettings(runtimeConfig, authManager?.getSession())
        });
        registerBuiltInSettingEditors(registry);

        for (const diagnostic of manifest.diagnostics) {
          registry.diagnostics.add(diagnostic);
        }

        await loadStudioModules(manifest.modules, registry, {
          hostVersion: manifest.hostVersion,
          sdkVersion: manifest.sdkVersion
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

    void loadRegistry(manifestResponse);

    return () => {
      disposed = true;
    };
    // runtimeConfig reads the stable window.__ELSA_STUDIO_RUNTIME__ global (constant for the page lifetime), so it is
    // intentionally not a dependency — listing it would re-run boot on every render for a value that never changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authManager, backendBaseUrl, manifestQuery.data, manifestQuery.isError, manifestQuery.error, manifestQuery.isPending, moduleRegistryRevision, shellBaseUrl]);

  const routes = useMemo(() => api?.routes.list() ?? [], [api, state]);
  const navigation = useMemo(
    () => getStudioNavigation(api?.navigation.list() ?? [], { includeThemeBuilder: themeCapabilities.managementEnabled }),
    [api, state, themeCapabilities.managementEnabled]
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
      <ThemeProvider storeContext={shellContext}>
        <ShellFrame navigation={navigation} panels={[]} path={path} title="Loading modules" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl}>
          <div className="empty-state">Loading Studio modules...</div>
        </ShellFrame>
      </ThemeProvider>
    );
  }

  if (state === "failed") {
    return (
      <ThemeProvider storeContext={shellContext}>
        <ShellFrame navigation={navigation} panels={[]} path={path} title="Startup failed" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl}>
          <div className="error-state">{error}</div>
        </ShellFrame>
      </ThemeProvider>
    );
  }

  const themeBuilderPath = themeCapabilities.managementEnabled && path === "/theme-builder";
  const activeRoute = findRouteForPath(routes, path);
  const ActiveComponent = activeRoute?.component;
  const owningFeatureArea = findFeatureAreaForPath(featureAreas, path);
  const pageTitle = navigation.find(item => isNavigationItemActive(item, path))?.label ?? activeRoute?.label ?? owningFeatureArea?.title ?? "Studio";

  return (
    <ThemeProvider storeContext={shellContext}>
      <ShellFrame
        navigation={navigation}
        panels={panels}
        path={path}
        title={pageTitle}
        onNavigate={navigateTo}
        backendBaseUrl={backendBaseUrl}
        assistantAction={<AgentLauncher open={assistantOpen} sessions={agentSessions} onClick={() => setAssistantOpen(current => !current)} />}
        themeAction={themeCapabilities.pickerEnabled ? <ThemeSwitcher /> : null}
      >
        {path === "/extension-builder" ? <ExtensionBuilderPage api={api!} /> : null}
        {path === "/modules" ? <ModuleManagementPage api={api!} /> : null}
        {themeBuilderPath ? <ThemeBuilderPage api={api!} /> : null}
        {path === "/package-feeds" ? <PackageFeedsPage api={api!} /> : null}
        {path === "/diagnostics" ? <Diagnostics api={api!} /> : null}
        {path === "/diagnostics/modules" ? <ModuleDiagnostics api={api!} /> : null}
        {ActiveComponent ? <ActiveComponent navigate={navigateTo} /> : null}
        {!ActiveComponent && path !== "/extension-builder" && path !== "/modules" && !themeBuilderPath && path !== "/package-feeds" && path !== "/diagnostics" && path !== "/diagnostics/modules" ? (
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
    </ThemeProvider>
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

export function ShellFrame({
  navigation,
  panels,
  path,
  title,
  backendBaseUrl,
  assistantAction,
  themeAction,
  onNavigate,
  children
}: {
  navigation: StudioNavigationContribution[];
  panels: StudioPanelContribution[];
  path: string;
  title: string;
  backendBaseUrl: string;
  assistantAction?: React.ReactNode;
  themeAction?: React.ReactNode;
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  const [navQuery, setNavQuery] = useState("");
  const childrenByParentId = new Map<string, StudioNavigationContribution[]>();
  for (const item of navigation) {
    if (!item.parentId) {
      continue;
    }

    const children = childrenByParentId.get(item.parentId) ?? [];
    children.push(item);
    childrenByParentId.set(item.parentId, children);
  }

  const query = navQuery.trim().toLowerCase();
  const matchesQuery = (item: StudioNavigationContribution) => item.label.toLowerCase().includes(query);
  // A parent stays visible when it matches directly or when any of its children match, so a filtered
  // child never becomes orphaned. Children are filtered independently in the render below.
  const itemVisible = (item: StudioNavigationContribution) =>
    !query || matchesQuery(item) || (childrenByParentId.get(item.id) ?? []).some(matchesQuery);

  const navigationSections = [
    { id: "workspace", label: "Workspace", items: getTopLevelNavigationItems(navigation, "workspace").filter(itemVisible) },
    { id: "settings", label: "Settings", items: getTopLevelNavigationItems(navigation, "settings").filter(itemVisible) }
  ].filter(section => section.items.length > 0);
  const hasNavResults = navigationSections.length > 0;

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
          <Search size={16} aria-hidden="true" />
          <input
            type="search"
            aria-label="Search modules"
            placeholder="Search modules"
            value={navQuery}
            onChange={event => setNavQuery(event.target.value)}
            onKeyDown={event => {
              if (event.key === "Escape" && navQuery) {
                event.preventDefault();
                setNavQuery("");
              }
            }}
          />
        </label>

        {query && !hasNavResults ? (
          <p className="sidebar-search-empty" role="status">No modules match "{navQuery.trim()}".</p>
        ) : null}

        {navigationSections.map(section => (
          <nav key={section.id} className="nav-section" aria-label={section.label}>
            <span className="nav-heading">{section.label}</span>
            {section.items.map(item => {
              const childItems = (childrenByParentId.get(item.id) ?? [])
                .filter(child => getNavigationSection(child) === section.id)
                // When the parent matched the search directly, keep all of its children; otherwise only
                // the children that match, so a filtered group doesn't reveal unrelated siblings.
                .filter(child => !query || matchesQuery(item) || matchesQuery(child));
              const hasActiveChild = childItems.some(child => isNavigationItemActive(child, path));
              const itemActive = isNavigationItemActive(item, path);
              return (
                <div className="nav-item-group" key={item.id}>
                  <a
                    className={[itemActive ? "active" : "", hasActiveChild ? "has-active-child" : ""].filter(Boolean).join(" ")}
                    href={item.path}
                    aria-current={itemActive ? "page" : undefined}
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
                      {childItems.map(child => {
                        const childActive = isNavigationItemActive(child, path);
                        return (
                          <a
                            key={child.id}
                            className={childActive ? "active nav-child" : "nav-child"}
                            href={child.path}
                            aria-current={childActive ? "page" : undefined}
                            onClick={event => {
                              event.preventDefault();
                              onNavigate(child.path);
                            }}
                          >
                            {child.label}
                          </a>
                        );
                      })}
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
            {themeAction}
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
  // `activePanel` is `panels.find(...) ?? panels[0]`, so it is always a member of `panels`.
  const activePanelIndex = panels.indexOf(activePanel);
  const ActivePanelComponent = activePanel.component;
  const tabsBaseId = useId();
  const panelKeyDown = useTablistKeyboard(
    panels.map(panel => panel.id),
    activePanel.id,
    id => {
      setActivePanelId(id);
      setCollapsed(false);
    }
  );

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
        <div className="bottom-panel-tab-list" role="tablist" aria-label="Bottom panels" onKeyDown={panelKeyDown}>
          {panels.map((panel, index) => {
            const isActive = panel.id === activePanel.id;
            const ids = tabElementIds(tabsBaseId, index);
            return (
              <button
                key={panel.id}
                id={ids.tabId}
                data-tab-id={panel.id}
                type="button"
                role="tab"
                aria-selected={isActive}
                aria-controls={ids.panelId}
                tabIndex={isActive ? 0 : -1}
                className={isActive ? "active" : ""}
                onClick={() => {
                  setActivePanelId(panel.id);
                  setCollapsed(false);
                }}
              >
                {panel.title}
              </button>
            );
          })}
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
      <div
        className="bottom-panel-content"
        role="tabpanel"
        id={tabElementIds(tabsBaseId, activePanelIndex).panelId}
        aria-labelledby={tabElementIds(tabsBaseId, activePanelIndex).tabId}
        // When collapsed the content is visually hidden; `inert` also removes it from the tab order and
        // the accessibility tree so keyboard/AT users don't land on off-screen controls (React 19).
        inert={collapsed}
      >
        <ActivePanelComponent />
      </div>
    </section>
  );
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

  if (id.includes("theme")) {
    return <Paintbrush size={18} />;
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
  if (id.includes("dashboard") || id === "home") return navIconColors.dashboard;
  if (id.includes("weather")) return navIconColors.weather;
  if (id.includes("diagnostics")) return navIconColors.diagnostics;
  if (id.includes("modules")) return navIconColors.modules;
  if (id.includes("theme")) return navIconColors.themes;
  if (id.includes("feeds")) return navIconColors.feeds;
  if (id.includes("extension-builder")) return navIconColors.extensionBuilder;
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

export function getStudioNavigation(moduleNavigation: StudioNavigationContribution[], options: { includeThemeBuilder?: boolean } = {}) {
  const hostNavigation = options.includeThemeBuilder ? [...builtInNavigation, themeBuilderNavigation] : builtInNavigation;
  return [...hostNavigation, ...moduleNavigation]
    .filter((item, index, items) => items.findIndex(candidate => candidate.id === item.id) === index)
    .sort((a, b) => (a.order ?? 500) - (b.order ?? 500));
}

export function getNavigationSection(item: Pick<StudioNavigationContribution, "id" | "path">): NavigationSection {
  const settingsPaths = new Set(["/modules", "/theme-builder", "/package-feeds", "/features", "/extension-builder"]);
  if (settingsPaths.has(item.path) || item.id === "modules" || item.id === "theme-builder" || item.id === "package-feeds" || item.id === "feature-management") {
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

function currentRelativeLocation() {
  return `${window.location.pathname}${window.location.search}${window.location.hash}`;
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

export function App() {
  // Create the auth manager once per shell instance: null means no provider is configured, so the shell
  // boots anonymously (no login, no bearer token). When configured, the same manager instance backs both
  // the session provider (via StudioAuthBoundary) and the authenticated HTTP client passed to AppContent.
  // The identity endpoints (`/_elsa/identity/*`) live on the backend, not the Studio host, so the manager
  // must resolve them against the backend base URL — using the Studio origin makes bootstrap hit the SPA
  // fallback (index.html) and the login flow stalls at "Signing in…".
  const authManager = useMemo(() => {
    const config = getStudioRuntimeConfig();
    const backendBaseUrl = resolveRuntimeBaseUrl(config.backendBaseUrl, window.location.origin);
    return createStudioAuthManager(config, backendBaseUrl);
  }, []);

  // AuthProvider is mounted outermost so the login/redirect gate wraps the entire shell — including theming,
  // data fetching, and dialogs — and so an unauthenticated session never renders backend-bound UI.
  return (
    <StudioAuthBoundary manager={authManager}>
      <QueryProvider>
        <AppContent authManager={authManager} />
        <DialogHost />
      </QueryProvider>
    </StudioAuthBoundary>
  );
}
