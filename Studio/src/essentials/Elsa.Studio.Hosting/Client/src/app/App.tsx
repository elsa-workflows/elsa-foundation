import { StudioPageHeader } from "./ui";
import { getStudioBranding } from "./runtime";
import React, { lazy, Suspense, useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import {
  Activity,
  Blocks,
  ChevronDown,
  ChevronRight,
  ChevronUp,
  CircleHelp,
  Ellipsis,
  ExternalLink,
  FileText,
  Gauge,
  GitBranch,
  Github,
  House,
  ListChecks,
  Maximize2,
  Minimize2,
  Paintbrush,
  PackageCheck,
  PackagePlus,
  PackageSearch,
  PanelLeftClose,
  PanelLeftOpen,
  PlayCircle,
  RefreshCw,
  Search,
  Server,
  Shield,
  Settings,
  SlidersHorizontal
} from "lucide-react";
import type {
  ElsaStudioModuleApi,
  StudioDiagnosticsWidgetContribution,
  StudioDiagnosticsWidgetProps,
  StudioDiagnosticsWidgetState,
  StudioModulesResponse,
  StudioNavigationContribution,
  StudioNavigationPlacement,
  StudioNavigationSection,
  StudioAgentPromptRequest,
  StudioPanelContribution
} from "../sdk";
import { AuthProvider, AnonymousAuthProvider, evaluateBackendRequirements, isHomeRunDetailPath, recordHomeRunDetailOpened, requestStudioNavigation, studioBackendCapabilitiesChangedEvent, useAuthContext } from "../sdk";
import { createStudioRegistry, findFeatureAreaForPath } from "./registry";
import type { AuthProviderManager } from "../sdk";
import { getStudioRuntimeConfig, getStudioRuntimeSettings } from "./runtime";
import { StudioAuthBoundary, createStudioAuthManager, createStudioEndpointContext } from "./auth/studioAuth";
import { loadStudioModules } from "./loader";
import { ThemeProvider, useNavMode, useWorkspaceStyle } from "./components/ThemeProvider";
import { ThemeSwitcher } from "./components/ThemeSwitcher";
import { UserMenu } from "./components/UserMenu";
import { defaultThemeStoreCapabilities, getThemeStoreCapabilities, type ThemeStoreCapabilities } from "./themes/themeStoreApi";
import { QueryProvider } from "./providers/QueryProvider";
import { useStudioManifest } from "./hooks/useStudioManifest";
import { getHealthErrorMessage } from "./hooks/useHostHealth";
import { DialogHost } from "./ui/dialog/DialogHost";
import { SkeletonRows } from "./ui/feedback/FeedbackStates";
import { registerBuiltInSettingEditors } from "./ui/shared";
import { tabElementIds, useTablistKeyboard } from "./ui/layout/Tabs";
import { ModuleManagementPage } from "./modules/ModuleManagementPage";
import { PackageFeedsPage } from "./modules/PackageFeedsPage";
import { ThemeBuilderPage } from "./modules/ThemeBuilderPage";
import { registerBuiltInPropertyEditors } from "./propertyEditors";
import {
  compareNavigationItems,
  filterAccessibleNavigation,
  getChildNavigationItems,
  getNavigationPlacement,
  getNavigationSection as resolveNavigationSection,
  getTopLevelNavigationItems as resolveTopLevelNavigationItems,
  getTopLevelNavigationItemsByPlacement,
  readMoreToolsOpen,
  writeMoreToolsOpen
} from "./navigation/placement";
import { useShellNavigationAccess } from "./navigation/useShellNavigationAccess";
import {
  appearanceChromeStorageKeys,
  appearanceResetEventName
} from "./themes/appearance";
import {
  getSettingsSectionGroups,
  hasSettingsSections,
  isSettingsSurfacePath,
  resolveSettingsSection
} from "./settings/settingsSections";
import { isDashboardPath, isNavigationItemActive } from "./navigation/active";
import elsaLogo from "../assets/images/icon.png";
import { WeaverSurface } from "./weaver";
import "./styles.css";
import "./themes/workspaceStyles.css";
import { AgentLauncher, createAgentClient, type AgentSessionIndicatorSession } from "./agent";
import "./agent/agent.css";
import "./weaver/weaver.css";
import { BackendConnections, PreparedBackend } from "./backends/connections";
import { BackendConnectionNotice, BackendSwitcher, backendNavigation } from "./backends/BackendsPage";
import { HostAuthContext } from "./backends/HostAuthContext";

const DevDisclosurePage = import.meta.env.DEV
  ? lazy(() => import("./ui/disclosure/DisclosureFixturePage").then(module => ({ default: module.DisclosureFixturePage })))
  : null;
const SettingsRoute = lazy(() => import("./settings/SettingsRoute").then(module => ({ default: module.SettingsRoute })));
const BackendsPage = lazy(() => import("./backends/BackendsPage").then(module => ({default:module.BackendsPage})));

type LoadState = "loading" | "ready" | "failed";
type NavigationSection = StudioNavigationSection;
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
  themes: "#0f766e"
} as const;

const builtInNavigation: StudioNavigationContribution[] = [
  { id: "modules", label: "Modules", path: "/modules", activePathPrefix: "/modules", order: 10, iconColor: navIconColors.modules, placement: "admin" },
  { id: "package-feeds", label: "Package feeds", path: "/package-feeds", activePathPrefix: "/package-feeds", order: 30, iconColor: navIconColors.feeds, placement: "admin" },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", activePathPrefix: "/diagnostics", order: 60, iconColor: navIconColors.diagnostics, placement: "more" }
];

const themeBuilderNavigation: StudioNavigationContribution = {
  id: "theme-builder",
  label: "Theme builder",
  path: "/theme-builder",
  activePathPrefix: "/theme-builder",
  order: 20,
  iconColor: navIconColors.themes,
  placement: "admin"
};

const appearanceNavigation: StudioNavigationContribution = {
  id: "appearance",
  label: "Appearance",
  path: "/settings/appearance",
  activePathPrefix: "/settings/appearance",
  order: 10,
  iconColor: navIconColors.themes,
  placement: "settings"
};

const ModulesChangedEventName = "elsa-studio:modules-changed";
const sidebarCollapsedStorageKey = appearanceChromeStorageKeys.sidebarCollapsed;
const bottomPanelHeightStorageKey = appearanceChromeStorageKeys.bottomPanelHeight;
const activeBottomPanelStorageKey = appearanceChromeStorageKeys.activeBottomPanel;
const bottomPanelCollapsedStorageKey = appearanceChromeStorageKeys.bottomPanelCollapsed;
const bottomPanelMaximizedStorageKey = appearanceChromeStorageKeys.bottomPanelMaximized;
const defaultBottomPanelHeight = 260;
const minBottomPanelHeight = 140;
const maxBottomPanelHeight = 560;
const minWorkspaceHeight = 280;

function AppContent({ authManager,backendSession,connections }: { authManager: AuthProviderManager | null;backendSession:PreparedBackend;connections:BackendConnections }) {
  const [state, setState] = useState<LoadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [api, setApi] = useState<ElsaStudioModuleApi | null>(null);
  const [path, setPath] = useState(normalizePath(window.location.pathname));
  const [moduleRegistryRevision, setModuleRegistryRevision] = useState(0);
  const [themeCapabilities, setThemeCapabilities] = useState<ThemeStoreCapabilities>(defaultThemeStoreCapabilities);
  const [assistantOpen, setAssistantOpen] = useState(false);
  const [agentSessions, setAgentSessions] = useState<AgentSessionIndicatorSession[]>([]);
  const [incomingPrompt, setIncomingPrompt] = useState<(StudioAgentPromptRequest & { id: string }) | null>(null);
  const acceptedPathRef = useRef(normalizePath(window.location.pathname));
  const acceptedLocationRef = useRef(currentRelativeLocation());
  const runtimeConfig = getStudioRuntimeConfig();
  const shellBaseUrl = window.location.origin;
  const backendBaseUrl = backendSession.profile.baseUrl;
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
      const qualified = connections.qualifyPath(currentRelativeLocation());
      if (qualified !== currentRelativeLocation()) window.history.replaceState(window.history.state,"",qualified);
      const nextPath = normalizePath(window.location.pathname);
      const nextLocation = currentRelativeLocation();
      if (nextPath !== acceptedPathRef.current && !requestStudioNavigation(acceptedLocationRef.current, nextLocation)) {
        window.history.pushState({}, "", acceptedLocationRef.current);
        return;
      }
      acceptedPathRef.current = nextPath;
      acceptedLocationRef.current = nextLocation;
      setPath(nextPath);
      if (isHomeRunDetailPath(nextPath)) recordHomeRunDetailOpened();
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const navigateTo = useCallback((nextPath: string) => {
    if (!requestStudioNavigation(acceptedLocationRef.current, nextPath)) return;
    window.history.pushState({}, "", connections.qualifyPath(nextPath));
    acceptedPathRef.current = normalizePath(window.location.pathname);
    acceptedLocationRef.current = currentRelativeLocation();
    setPath(acceptedPathRef.current);
    if (isHomeRunDetailPath(acceptedPathRef.current)) recordHomeRunDetailOpened();
    window.dispatchEvent(new PopStateEvent("popstate"));
  }, [connections]);

  // Cross-surface prompt dispatch (e.g. an "Explain this workflow" action) opens the Weaver dock and forwards the prompt.
  useEffect(() => {
    if (!api) return;
    return api.agent.onPrompt(prompt => {
      setAssistantOpen(true);
      setIncomingPrompt({ ...prompt, id: `prompt-${Date.now()}-${Math.random()}` });
    });
  }, [api]);

  const consumeIncomingPrompt = useCallback((id: string) => {
    // The shell outlives dock and registry remounts. Retaining an attempted request here
    // would submit it again as fresh intent when a new assistant lifetime becomes ready.
    setIncomingPrompt(current => current?.id === id ? null : current);
  }, []);

  // Probe the agent provider once so provider-gated affordances (e.g. the workflow "Risks" action) can be
  // disabled before the user opens the Weaver dock. "unavailable"/"disabled" mean the provider can deliver
  // nothing; a failed probe is treated the same. The Weaver dock refreshes this on its own bootstrap.
  useEffect(() => {
    if (!api) return;
    let disposed = false;
    if (evaluateBackendRequirements(api.backendSession,[{id:"elsa.api.agent",contractVersion:"1"}]).state !== "available") {
      api.agent.providerAvailability.set(false);
      return;
    }
    void createAgentClient(api.backend).bootstrap()
      .then(result => {
        if (!disposed) api.agent.providerAvailability.set(result.enabled && result.providerStatus !== "unavailable" && result.providerStatus !== "disabled");
      })
      .catch(() => {
        if (!disposed) api.agent.providerAvailability.set(false);
      });
    return () => { disposed = true; };
  }, [api]);

  // The boot manifest is fetched through TanStack Query, using the authenticated shell HTTP client so
  // the manifest request carries auth + management-key headers; the effect below turns manifest data
  // into a loaded registry (module loading + contribution registration are side effects, not cacheable data).
  const manifestQuery = useStudioManifest(shellContext.http);
  useEffect(() => {
    const refresh = (event:Event) => {
      if ((event as CustomEvent<{key:string}>).detail?.key !== backendSession.key) return;
      void backendSession.refreshCapabilities().catch(() => undefined).finally(() => setModuleRegistryRevision(value => value+1));
    };
    window.addEventListener(studioBackendCapabilitiesChangedEvent,refresh);
    return () => window.removeEventListener(studioBackendCapabilitiesChangedEvent,refresh);
  },[backendSession]);

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
        const backendContext = backendSession.context;
        const enabledFeatures = [...new Set((manifest.modules ?? []).flatMap(module => [module.id, ...(module.capabilities ?? [])]))];
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
          backendContext,
          backendSession,
          hostSession: connections.hostIdentity,
          runtime: {
            ...getStudioRuntimeSettings(runtimeConfig, backendSession.identity),
            enabledFeatures
          }
        });
        registerBuiltInSettingEditors(registry);

        for (const diagnostic of manifest.diagnostics) {
          registry.diagnostics.add(diagnostic);
        }

        await loadStudioModules(manifest.modules, registry, {
          hostVersion: manifest.hostVersion,
          sdkVersion: manifest.sdkVersion
        });
        registerBuiltInContributions(registry);
        registry.navigation.add(backendNavigation);
        registry.routes.add({id:"backends",path:"/settings/backends",label:"Backends",component:({navigate}) => <Suspense fallback={<SkeletonRows rows={3} />}><BackendsPage connections={connections} navigate={navigate} featureManagementAvailable={registry.routes.list({includeUnavailable:true}).some(route => route.id === "feature-management")} /></Suspense>});

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
  }, [authManager, backendBaseUrl, backendSession, connections, manifestQuery.data, manifestQuery.isError, manifestQuery.error, manifestQuery.isPending, moduleRegistryRevision, shellBaseUrl]);

  const routes = useMemo(() => api?.routes.list({includeUnavailable:true}) ?? [], [api, state]);
  const enabledFeatures = useMemo(() => {
    const features = new Set<string>();
    for (const module of manifestQuery.data?.modules ?? []) {
      features.add(module.id);
      for (const capability of module.capabilities ?? []) {
        features.add(capability);
      }
    }
    return features;
  }, [manifestQuery.data]);
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
  const access = useShellNavigationAccess(enabledFeatures);
  const settingsGroups = getSettingsSectionGroups(navigation, access);
  const settingsSurface = isSettingsSurfacePath(path, navigation);

  if (state === "loading") {
    return (
      <ThemeProvider storeContext={shellContext}>
        <ShellFrame navigation={[backendNavigation,...navigation]} panels={[]} path={path} title="Loading modules" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl} backendAction={<BackendSwitcher connections={connections} onNavigate={navigateTo} />} enabledFeatures={enabledFeatures} accountAction={<UserMenu />}>
          <BackendConnectionNotice connections={connections} />
          <SkeletonRows rows={8} label="Loading Studio modules" />
        </ShellFrame>
      </ThemeProvider>
    );
  }

  if (state === "failed") {
    return (
      <ThemeProvider storeContext={shellContext}>
        <ShellFrame navigation={[backendNavigation,...navigation]} panels={[]} path={path} title="Startup failed" onNavigate={navigateTo} backendBaseUrl={backendBaseUrl} backendAction={<BackendSwitcher connections={connections} onNavigate={navigateTo} />} enabledFeatures={enabledFeatures} accountAction={<UserMenu />}>
          <div className="error-state">{error}</div>
          <BackendConnectionNotice connections={connections} />
          <Suspense fallback={null}><BackendsPage connections={connections} navigate={navigateTo} /></Suspense>
        </ShellFrame>
      </ThemeProvider>
    );
  }

  const activeRoute = findRouteForPath(routes, path);
  const ActiveComponent = activeRoute?.component;
  const routeAvailability = evaluateBackendRequirements(backendSession,activeRoute?.requiresBackend);
  const owningFeatureArea = findFeatureAreaForPath(featureAreas, path);
  const settingsSection = settingsSurface ? resolveSettingsSection(path, settingsGroups) : undefined;
  const pageTitle = settingsSection?.label
    ?? navigation.find(item => isNavigationItemActive(item, path, navigation))?.label
    ?? activeRoute?.label
    ?? owningFeatureArea?.title
    ?? (settingsSurface ? "Settings" : "Studio");

  return (
    <ThemeProvider storeContext={shellContext}>
      <ShellFrame
        navigation={navigation}
        panels={panels}
        path={path}
        title={pageTitle}
        onNavigate={navigateTo}
        backendBaseUrl={backendBaseUrl}
        backendAction={<BackendSwitcher connections={connections} onNavigate={navigateTo} />}
        enabledFeatures={enabledFeatures}
        assistantAction={evaluateBackendRequirements(backendSession,[{id:"elsa.api.agent",contractVersion:"1"}]).state === "available" ? <AgentLauncher open={assistantOpen} sessions={agentSessions} onClick={() => setAssistantOpen(current => !current)} /> : null}
        themeAction={themeCapabilities.pickerEnabled ? <ThemeSwitcher /> : null}
        accountAction={<UserMenu />}
      >
        <BackendConnectionNotice connections={connections} />
        {settingsSurface ? (
          <Suspense fallback={<SkeletonRows rows={8} label="Loading settings" />}>
            {/* Legacy /modules, /package-feeds and /theme-builder URLs must stay on this surface so the host-control gate applies. */}
            <SettingsRoute
              path={path}
              navigation={navigation}
              enabledFeatures={enabledFeatures}
              onNavigate={navigateTo}
              renderSection={section => renderHostSettingsSection(section, api!, routes, navigateTo, themeCapabilities.managementEnabled)}
            />
          </Suspense>
        ) : null}
        {path === "/diagnostics" ? <Diagnostics api={api!} navigation={filterAccessibleNavigation(navigation, access)} onNavigate={navigateTo} /> : null}
        {path === "/diagnostics/modules" ? <ModuleDiagnostics api={api!} /> : null}
        {DevDisclosurePage && path === "/dev/disclosure" ? (
          <Suspense fallback={null}>
            <DevDisclosurePage />
          </Suspense>
        ) : null}
        {!settingsSurface && ActiveComponent ? routeAvailability.state === "available" ? <ActiveComponent navigate={navigateTo} /> : <div className="empty-state" role="status">{routeAvailability.reason}<button type="button" onClick={() => navigateTo("/settings/backends")}>Choose backend</button></div> : null}
        {!settingsSurface && !ActiveComponent && path !== "/diagnostics" && path !== "/diagnostics/modules" && !(DevDisclosurePage && path === "/dev/disclosure") ? (
          <div className="empty-state">
            {owningFeatureArea
              ? `${owningFeatureArea.title} owns ${path}, but no route component is registered for it.`
              : `No Studio route is registered for ${path}.`}
          </div>
        ) : null}
      </ShellFrame>
      {assistantOpen ? (
        <WeaverSurface api={api!} surface={{ route: path }} variant="dock" onClose={() => setAssistantOpen(false)} onSessionIndicatorChange={setAgentSessions} incomingPrompt={incomingPrompt} onPromptConsumed={consumeIncomingPrompt} />
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
  accountAction,
  backendAction,
  enabledFeatures,
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
  accountAction?: React.ReactNode;
  backendAction?: React.ReactNode;
  enabledFeatures?: ReadonlySet<string> | readonly string[];
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  const [navQuery, setNavQuery] = useState("");
  const workspaceStyle = useWorkspaceStyle();
  const [navCollapsedChoice, setNavCollapsedChoice] = useState<boolean | null>(() => {
    try {
      const saved = window.localStorage.getItem(sidebarCollapsedStorageKey);
      return saved === "true" ? true : saved === "false" ? false : null;
    } catch { return null; }
  });
  const navCollapsed = navCollapsedChoice ?? workspaceStyle === "focus";
  const [moreToolsOpen, setMoreToolsOpen] = useState(() => readMoreToolsOpen());
  const [topMoreToolsOpen, setTopMoreToolsOpen] = useState(false);
  const [dismissedMoreToolsQuery, setDismissedMoreToolsQuery] = useState<string | null>(null);
  const moreToolsSectionRef = useRef<HTMLElement>(null);
  const moreToolsButtonRef = useRef<HTMLButtonElement>(null);
  // The top navigation mode turns the sidebar into a menubar and moves backend status into a
  // status bar; the icon-rail collapse has no meaning there, so it is ignored (not forgotten).
  const topNav = useNavMode() === "top";
  const backendHost = new URL(backendBaseUrl).host;
  const access = useShellNavigationAccess(enabledFeatures);
  const visibleNavigation = filterAccessibleNavigation(navigation, access);

  useEffect(() => {
    if (navCollapsedChoice === null) window.localStorage.removeItem(sidebarCollapsedStorageKey);
    else window.localStorage.setItem(sidebarCollapsedStorageKey, String(navCollapsedChoice));
  }, [navCollapsedChoice]);

  useEffect(() => {
    if (navCollapsed) setNavQuery("");
  }, [navCollapsed]);

  useEffect(() => {
    writeMoreToolsOpen(moreToolsOpen);
  }, [moreToolsOpen]);

  useEffect(() => {
    const onAppearanceReset = () => setNavCollapsedChoice(null);
    window.addEventListener(appearanceResetEventName, onAppearanceReset);
    return () => window.removeEventListener(appearanceResetEventName, onAppearanceReset);
  }, []);

  function toggleNavCollapsed() {
    setNavCollapsedChoice(() => {
      const next = !navCollapsed;
      if (next) {
        // The search box is hidden while collapsed, so drop any active filter rather than
        // leaving the nav invisibly filtered.
        setNavQuery("");
      }
      return next;
    });
  }

  const childrenByParentId = new Map<string, StudioNavigationContribution[]>();
  for (const item of visibleNavigation) {
    if (!item.parentId) {
      continue;
    }

    const children = childrenByParentId.get(item.parentId) ?? [];
    children.push(item);
    childrenByParentId.set(item.parentId, children);
  }

  const query = navQuery.trim().toLowerCase();
  const previousPathRef = useRef(path);
  useLayoutEffect(() => {
    if (previousPathRef.current === path) return;
    previousPathRef.current = path;
    setTopMoreToolsOpen(false);
    setDismissedMoreToolsQuery(query || null);
  }, [path, query]);

  const navigateFromTopNavigation = (destination: string) => {
    setTopMoreToolsOpen(false);
    setDismissedMoreToolsQuery(null);
    setNavQuery("");
    onNavigate(destination);
  };
  const navigateFromMainNavigation = topNav ? navigateFromTopNavigation : onNavigate;
  const matchesQuery = (item: StudioNavigationContribution) => item.label.toLowerCase().includes(query);
  // A parent stays visible when it matches directly or when any of its children match, so a filtered
  // child never becomes orphaned. Children are filtered independently in the render below.
  const itemVisible = (item: StudioNavigationContribution) =>
    !query || matchesQuery(item) || (childrenByParentId.get(item.id) ?? []).some(matchesQuery);

  const mainItems = getTopLevelNavigationItemsByPlacement(visibleNavigation, "main").filter(itemVisible);
  const moreItems = getTopLevelNavigationItemsByPlacement(visibleNavigation, "more").filter(itemVisible);
  const settingsItems = getTopLevelNavigationItemsByPlacement(visibleNavigation, "settings").filter(itemVisible);
  const adminItems = getTopLevelNavigationItemsByPlacement(visibleNavigation, "admin").filter(itemVisible);
  const settingsGroups = getSettingsSectionGroups(visibleNavigation);
  const searchableSettingsItems = [...settingsItems, ...adminItems];
  const showSettingsEntry = hasSettingsSections(settingsGroups) && (!query || "settings".includes(query));
  const settingsActive = isSettingsSurfacePath(path, navigation);
  const moreToolsExpanded = topNav
    ? topMoreToolsOpen || Boolean(query && query !== dismissedMoreToolsQuery)
    : moreToolsOpen || Boolean(query);
  const dismissTopMoreTools = useCallback((returnFocus = false) => {
    setTopMoreToolsOpen(false);
    if (query) setDismissedMoreToolsQuery(query);
    if (returnFocus) moreToolsButtonRef.current?.focus();
  }, [query]);
  const hasNavResults = mainItems.length + moreItems.length + searchableSettingsItems.length + (showSettingsEntry ? 1 : 0) > 0;

  useEffect(() => {
    if (!topNav || !moreToolsExpanded) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.key !== "Escape") return;
      event.preventDefault();
      dismissTopMoreTools(true);
    };
    const handlePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Node && moreToolsSectionRef.current?.contains(event.target)) return;
      dismissTopMoreTools();
    };

    window.addEventListener("keydown", handleKeyDown);
    window.addEventListener("pointerdown", handlePointerDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("pointerdown", handlePointerDown);
    };
  }, [dismissTopMoreTools, moreToolsExpanded, topNav]);

  const branding = getStudioBranding();
  return (
    <div className={navCollapsed && !topNav ? "studio-shell sidebar-collapsed" : "studio-shell"}>
      <aside className="sidebar">
        <div className="sidebar-top">
          <a className="brand" href="/" onClick={event => { event.preventDefault(); navigateFromMainNavigation("/"); }}>
            <span className="brand-mark" aria-hidden="true">
              <img src={branding.logoUrl ?? elsaLogo} alt="" onError={event => { event.currentTarget.onerror = null; event.currentTarget.src = elsaLogo; }} />
            </span>
            <span className="nav-label">
              <strong>{branding.productName}</strong>
              <small>{branding.subtitle}</small>
            </span>
          </a>
          <button
            type="button"
            className="sidebar-collapse-toggle"
            aria-label={navCollapsed ? "Expand navigation" : "Collapse navigation"}
            aria-expanded={!navCollapsed}
            title={navCollapsed ? "Expand navigation" : "Collapse navigation"}
            onClick={toggleNavCollapsed}
          >
            {navCollapsed ? <PanelLeftOpen size={16} /> : <PanelLeftClose size={16} />}
          </button>
        </div>

        <label className="sidebar-search">
          <Search size={16} aria-hidden="true" />
          <input
            type="search"
            aria-label="Search modules"
            placeholder="Search modules"
            value={navQuery}
            onChange={event => {
              setDismissedMoreToolsQuery(null);
              setNavQuery(event.target.value);
            }}
            onKeyDown={event => {
              if (event.key !== "Escape") return;
              if (topNav && moreToolsExpanded) {
                event.preventDefault();
                dismissTopMoreTools(true);
              } else if (navQuery) {
                event.preventDefault();
                setNavQuery("");
              }
            }}
          />
        </label>

        {query && !hasNavResults ? (
          <p className="sidebar-search-empty" role="status">No modules match "{navQuery.trim()}".</p>
        ) : null}

        {mainItems.length > 0 ? (
          <nav className="nav-section" aria-label="Main">
            {mainItems.map(item => (
              <NavItemGroup
                key={item.id}
                item={item}
                childItems={childItemsFor(item, visibleNavigation, query, matchesQuery)}
                items={visibleNavigation}
                path={path}
                collapsed={navCollapsed}
                onNavigate={navigateFromMainNavigation}
              />
            ))}
          </nav>
        ) : null}

        {moreItems.length > 0 ? (
          <nav
            ref={moreToolsSectionRef}
            className="nav-section nav-section-more"
            aria-label="More tools"
            onBlur={event => {
              if (!topNav) return;
              const nextTarget = event.relatedTarget;
              if (nextTarget instanceof Node && event.currentTarget.contains(nextTarget)) return;
              dismissTopMoreTools();
            }}
          >
            <button
              ref={moreToolsButtonRef}
              type="button"
              className={["nav-disclosure", moreToolsExpanded ? "open" : ""].filter(Boolean).join(" ")}
              aria-expanded={moreToolsExpanded}
              aria-controls="studio-nav-more-tools"
              title={navCollapsed ? "More tools" : undefined}
              onClick={() => {
                if (!topNav) {
                  setMoreToolsOpen(current => !current);
                  return;
                }
                if (moreToolsExpanded) dismissTopMoreTools();
                else {
                  setTopMoreToolsOpen(true);
                  setDismissedMoreToolsQuery(null);
                }
              }}
            >
              <span className="nav-icon-tile" aria-hidden="true">
                <Ellipsis size={18} />
              </span>
              <span className="nav-label">More tools</span>
              {moreToolsExpanded ? <ChevronDown size={14} aria-hidden="true" /> : <ChevronRight size={14} aria-hidden="true" />}
            </button>
            {moreToolsExpanded ? (
              <div id="studio-nav-more-tools" className="nav-disclosure-panel">
                {moreItems.map(item => (
                  <NavItemGroup
                    key={item.id}
                    item={item}
                    childItems={childItemsFor(item, visibleNavigation, query, matchesQuery)}
                    items={visibleNavigation}
                    path={path}
                    collapsed={navCollapsed}
                    onNavigate={topNav ? navigateFromTopNavigation : onNavigate}
                  />
                ))}
              </div>
            ) : null}
          </nav>
        ) : null}

        {query && searchableSettingsItems.length > 0 ? (
          <nav className="nav-section" aria-label="Settings">
            <span className="nav-heading">Settings</span>
            {searchableSettingsItems.map(item => (
              <NavItemGroup
                key={item.id}
                item={item}
                childItems={childItemsFor(item, visibleNavigation, query, matchesQuery)}
                items={visibleNavigation}
                path={path}
                collapsed={navCollapsed}
                onNavigate={navigateFromMainNavigation}
              />
            ))}
          </nav>
        ) : showSettingsEntry ? (
          <nav className="nav-section" aria-label="Settings">
            <a
              className={settingsActive ? "active" : ""}
              href="/settings"
              aria-current={settingsActive ? "page" : undefined}
              aria-label="Settings"
              title={navCollapsed ? "Settings" : undefined}
              onClick={event => {
                event.preventDefault();
                navigateFromMainNavigation("/settings");
              }}
            >
              <span className="nav-icon-tile" style={{ "--nav-icon-color": navIconColors.themes } as NavIconTileStyle} aria-hidden="true">
                <Settings size={18} />
              </span>
              <span className="nav-label">Settings</span>
            </a>
          </nav>
        ) : null}

        <div className="sidebar-footer" role="group" aria-label="Environment and account">
          {backendAction ?? <div className="sidebar-environment" role="group" aria-label={`Configured backend endpoint: ${backendHost}`} title="Configured backend endpoint; connectivity is not measured">
            <span className="sidebar-environment-icon" aria-hidden="true"><Server size={16} /></span>
            <span className="sidebar-environment-copy">
              <strong>Backend API</strong>
              <small>{backendHost}</small>
            </span>
          </div>}
          <div className="sidebar-account">{topNav ? null : accountAction}</div>
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
            {branding.showHelpLink ? <a href={branding.helpUrl ?? "https://github.com/elsa-workflows/elsa-foundation"} aria-label={branding.helpUrl ? "Help" : "GitHub"}>
              {branding.helpUrl ? <CircleHelp size={18} /> : <Github size={18} />}
            </a> : null}
            {branding.showPrivacyLink ? <a href={branding.privacyUrl ?? "https://docs.elsaworkflows.io"} aria-label={branding.privacyUrl ? "Privacy" : "Documentation"}>
              {branding.privacyUrl ? <Shield size={18} /> : <ExternalLink size={18} />}
            </a> : null}
            {themeAction}
            {accountAction}
          </div>
        </header>
        <main className="content">{children}</main>
      </div>
      {panels.length > 0 ? <BottomPanel panels={panels} /> : null}
      {topNav ? (
        <footer className="studio-statusbar" aria-label="Environment status">
          {backendAction ?? <span title="Configured backend endpoint; connectivity is not measured"><span className="sidebar-status-dot" aria-hidden="true" />Backend API · {backendHost}</span>}
          <span>Studio / {title}</span>
        </footer>
      ) : null}
    </div>
  );
}

function registerBuiltInContributions(api: ElsaStudioModuleApi) {
  registerBuiltInPropertyEditors(api);
}

function BottomPanel({ panels }: { panels: StudioPanelContribution[] }) {
  const initialMaximized = getInitialBoolean(bottomPanelMaximizedStorageKey, false);
  const [height, setHeight] = useState(getInitialBottomPanelHeight);
  const [activePanelId, setActivePanelId] = useState(getInitialActiveBottomPanelId);
  const [collapsed, setCollapsed] = useState(() => !initialMaximized && getInitialBoolean(bottomPanelCollapsedStorageKey, true));
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
    if (activePanelId && !panels.some(panel => panel.id === activePanelId)) {
      setActivePanelId("");
    }
  }, [activePanelId, panels]);

  useEffect(() => {
    if (height === defaultBottomPanelHeight) window.localStorage.removeItem(bottomPanelHeightStorageKey);
    else window.localStorage.setItem(bottomPanelHeightStorageKey, String(height));
  }, [height]);

  useEffect(() => {
    if (activePanelId && activePanel?.id) {
      window.localStorage.setItem(activeBottomPanelStorageKey, activePanel.id);
    } else window.localStorage.removeItem(activeBottomPanelStorageKey);
  }, [activePanelId, activePanel?.id]);

  useEffect(() => {
    if (collapsed) window.localStorage.removeItem(bottomPanelCollapsedStorageKey);
    else window.localStorage.setItem(bottomPanelCollapsedStorageKey, "false");
  }, [collapsed]);

  useEffect(() => {
    if (maximized) window.localStorage.setItem(bottomPanelMaximizedStorageKey, "true");
    else window.localStorage.removeItem(bottomPanelMaximizedStorageKey);
  }, [maximized]);

  useEffect(() => {
    const onAppearanceReset = () => {
      setCollapsed(true);
      setMaximized(false);
      setHeight(defaultBottomPanelHeight);
      setActivePanelId("");
    };
    window.addEventListener(appearanceResetEventName, onAppearanceReset);
    return () => window.removeEventListener(appearanceResetEventName, onAppearanceReset);
  }, []);

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

export function Diagnostics({
  api,
  navigation,
  onNavigate
}: {
  api: ElsaStudioModuleApi;
  navigation: StudioNavigationContribution[];
  onNavigate: (path: string) => void;
}) {
  const widgets = api.diagnosticsWidgets.list().sort(compareOrderedContributions);
  const destinations = getDiagnosticDestinations(navigation);

  return (
    <section className="studio-page diagnostics-view">
      <section className="diagnostics-index" aria-labelledby="diagnostics-index-heading">
        <StudioPageHeader headingLevel={2} title="Diagnostic tasks" titleId="diagnostics-index-heading"
          description="Choose a tool for the task you need to complete." />
        {destinations.length === 0 ? (
          <p className="diagnostics-index-empty" role="status">No diagnostic tool links are available to you in this app.</p>
        ) : (
          <ul className="diagnostics-task-list">
            {destinations.map(destination => (
              <li key={destination.id}>
                <a
                  className="diagnostics-task-link"
                  href={destination.path}
                  onClick={event => {
                    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                    event.preventDefault();
                    onNavigate(destination.path);
                  }}
                >
                  <span className="diagnostics-task-content">
                    <span className="diagnostics-task-title">{destination.label}</span>
                    <span className="diagnostics-task-description">{describeDiagnosticDestination(destination)}</span>
                  </span>
                  <ChevronRight size={16} aria-hidden="true" />
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="diagnostics-widgets" aria-labelledby="diagnostics-widgets-heading">
        <header className="diagnostics-section-heading">
          <h2 id="diagnostics-widgets-heading">Diagnostics widgets</h2>
          <p>Runtime and operational widgets contributed through the Studio SDK.</p>
        </header>
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
    </section>
  );
}

function getDiagnosticDestinations(navigation: StudioNavigationContribution[]) {
  const navigationById = new Map(navigation.map(item => [item.id, item]));
  const diagnosticsRegistered = navigationById.has("diagnostics");

  function isDiagnosticsDescendant(item: StudioNavigationContribution) {
    if (!diagnosticsRegistered) return false;

    const visited = new Set([item.id]);
    let parentId = item.parentId;
    while (parentId) {
      if (parentId === "diagnostics") return true;
      if (visited.has(parentId)) return false;
      visited.add(parentId);
      parentId = navigationById.get(parentId)?.parentId;
    }
    return false;
  }

  return navigation.filter(item => {
    if (item.id === "diagnostics" || item.path === "/diagnostics") return false;
    const isRuntimeEvidenceSettings = item.id === "workflows-runtime-diagnostics" && item.path === "/workflows/runtime-diagnostics";
    return isRuntimeEvidenceSettings || isDiagnosticsDescendant(item);
  });
}

function describeDiagnosticDestination(item: StudioNavigationContribution) {
  if (item.id === "open-telemetry" && item.path === "/diagnostics/open-telemetry") {
    return "Inspect distributed traces. Metrics and OpenTelemetry Logs are not available in this experience.";
  }
  if (item.id === "structured-logs" && item.path === "/diagnostics/structured-logs") {
    return "Inspect application logs and follow the live structured log stream.";
  }
  if (item.id === "console" && item.path === "/diagnostics/console") {
    return "Inspect live console output.";
  }
  if (item.id === "workflows-runtime-diagnostics" && item.path === "/workflows/runtime-diagnostics") {
    return "Review runtime evidence capture policy settings.";
  }
  return "Open this destination.";
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

function childItemsFor(
  item: StudioNavigationContribution,
  navigation: StudioNavigationContribution[],
  query: string,
  matchesQuery: (item: StudioNavigationContribution) => boolean
) {
  return getChildNavigationItems(navigation, item)
    .filter(child => !query || matchesQuery(item) || matchesQuery(child));
}

function NavItemGroup({
  item,
  childItems,
  items,
  path,
  collapsed,
  onNavigate
}: {
  item: StudioNavigationContribution;
  childItems: StudioNavigationContribution[];
  items: readonly StudioNavigationContribution[];
  path: string;
  collapsed: boolean;
  onNavigate: (path: string) => void;
}) {
  const hasActiveChild = childItems.some(child => isNavigationItemActive(child, path, items));
  const itemActive = isNavigationItemActive(item, path, items);
  return (
    <div className="nav-item-group">
      <a
        className={[itemActive ? "active" : "", hasActiveChild ? "has-active-child" : ""].filter(Boolean).join(" ")}
        href={item.path}
        aria-current={itemActive ? "page" : undefined}
        aria-label={item.label}
        title={collapsed ? item.label : undefined}
        onClick={event => {
          event.preventDefault();
          onNavigate(item.path);
        }}
      >
        <NavIconTile item={item} />
        <span className="nav-label">{item.label}</span>
      </a>
      {childItems.length > 0 ? (
        <div className="nav-children">
          {childItems.map(child => {
            const childActive = isNavigationItemActive(child, path, items);
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
}

function NavIcon({ id, icon: Icon }: Pick<StudioNavigationContribution, "id" | "icon">) {
  // A contribution that names its own icon wins; the id-substring matches below are the host's defaults.
  if (Icon) {
    return <Icon size={18} />;
  }

  if (id === "home" || id.includes("dashboard")) {
    return <House size={18} />;
  }

  if (id.includes("weather")) {
    return <Activity size={18} />;
  }

  if (id.includes("diagnostics")) {
    return <FileText size={18} />;
  }

  if (id.includes("run") || id.includes("instance")) {
    return <PlayCircle size={18} />;
  }

  if (id.includes("task")) {
    return <ListChecks size={18} />;
  }

  if (id === "workflows-executables") {
    return <PackageCheck size={18} />;
  }

  if (id === "workflows-activity-definitions") {
    return <Blocks size={18} />;
  }

  if (id === "workflows-activity-availability") {
    return <SlidersHorizontal size={18} />;
  }

  if (id.includes("workflow")) {
    return <GitBranch size={18} />;
  }

  if (id.includes("theme") || id.includes("appearance")) {
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
      <NavIcon id={item.id} icon={item.icon} />
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
  return "var(--primary)";
}

export { isDashboardPath, isExactSegmentPrefix, isNavigationItemActive } from "./navigation/active";

export function getStudioNavigation(moduleNavigation: StudioNavigationContribution[], options: { includeThemeBuilder?: boolean } = {}) {
  const hostNavigation = [
    appearanceNavigation,
    ...(options.includeThemeBuilder ? [themeBuilderNavigation] : []),
    ...builtInNavigation
  ];
  return [...hostNavigation, ...moduleNavigation]
    .filter((item, index, items) => items.findIndex(candidate => candidate.id === item.id) === index)
    .sort(compareNavigationItems);
}

function renderHostSettingsSection(
  section: StudioNavigationContribution,
  api: ElsaStudioModuleApi,
  routes: Array<{ path: string; component: React.ComponentType<{ navigate: (path: string) => void }> }>,
  navigate: (path: string) => void,
  themeBuilderEnabled: boolean
) {
  if (section.id === "modules" || section.path === "/modules") {
    return <ModuleManagementPage api={api} />;
  }
  if ((section.id === "theme-builder" || section.path === "/theme-builder") && themeBuilderEnabled) {
    return <ThemeBuilderPage api={api} />;
  }
  if (section.id === "package-feeds" || section.path === "/package-feeds") {
    return <PackageFeedsPage api={api} />;
  }
  const route = findRouteForPath(routes, section.path);
  if (route?.component) {
    const Component = route.component;
    return <Component navigate={navigate} />;
  }
  return null;
}

export function getNavigationSection(item: Pick<StudioNavigationContribution, "id" | "path" | "section">): NavigationSection {
  return resolveNavigationSection(item);
}

export function getNavigationPlacementForItem(item: Pick<StudioNavigationContribution, "id" | "path" | "section" | "placement">): StudioNavigationPlacement {
  return getNavigationPlacement(item);
}

export function getTopLevelNavigationItems(navigation: StudioNavigationContribution[], section: NavigationSection) {
  return resolveTopLevelNavigationItems(navigation, section);
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

function getInitialBoolean(storageKey: string, fallback: boolean) {
  if (typeof window === "undefined") {
    return fallback;
  }

  const value = window.localStorage.getItem(storageKey);
  return value === null ? fallback : value === "true";
}

export function App() {
  useEffect(() => {
    const branding = getStudioBranding();
    document.title = branding.productName;
    if (branding.faviconUrl) {
      const icon = document.querySelector<HTMLLinkElement>('link[rel="icon"]') ?? document.createElement("link");
      icon.rel = "icon";
      icon.href = branding.faviconUrl;
      document.head.append(icon);
    }
  }, []);
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
      <StableHostApplication authManager={authManager} />
    </StudioAuthBoundary>
  );
}

function StableHostApplication({authManager}:{authManager:AuthProviderManager|null}) {
  const {session} = useAuthContext();
  const scope = JSON.stringify([session.status,session.subject,session.tenantId]);
  const initial = useRef(scope);
  useEffect(() => {if (initial.current !== scope) window.location.reload();},[scope]);
  if (initial.current !== scope) return <p role="status">Your Studio identity changed. Reloading connections…</p>;
  return <QueryProvider><BackendApplication authManager={authManager} /><DialogHost /></QueryProvider>;
}

function BackendApplication({authManager}:{authManager:AuthProviderManager|null}) {
  const hostAuth = useAuthContext();
  const [connections] = useState(() => new BackendConnections(getStudioRuntimeConfig(),createStudioEndpointContext(window.location.origin,authManager),authManager,hostAuth.session));
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  useEffect(() => {void connections.initialize();},[connections]);
  if (!snapshot.active) return <ThemeProvider storeContext={connections.hostContext}><ShellFrame navigation={[backendNavigation,appearanceNavigation]} panels={[]} path="/settings/backends" title="Backends" backendBaseUrl={resolveRuntimeBaseUrl(getStudioRuntimeConfig().backendBaseUrl,window.location.origin)} backendAction={<BackendSwitcher connections={connections} onNavigate={path => window.location.assign(path)} />} onNavigate={path => window.location.assign(path)} accountAction={<UserMenu />}><BackendConnectionNotice connections={connections} /><Suspense fallback={<SkeletonRows rows={3} />}><BackendsPage connections={connections} navigate={path => window.location.assign(path)} /></Suspense></ShellFrame></ThemeProvider>;
  const content = <AppContent authManager={authManager} backendSession={snapshot.active} connections={connections} />;
  return <HostAuthContext.Provider value={hostAuth}>{snapshot.active.manager === authManager ? content : snapshot.active.manager ? <AuthProvider manager={snapshot.active.manager}>{content}</AuthProvider> : <AnonymousAuthProvider>{content}</AnonymousAuthProvider>}</HostAuthContext.Provider>;
}
