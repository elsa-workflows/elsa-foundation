import React, { useEffect, useMemo, useState } from "react";
import type { AdminModulesResponse, ElsaAdminModuleApi } from "../sdk";
import { createAdminRegistry } from "./registry";
import { loadAdminModules } from "./loader";
import "./styles.css";

type LoadState = "loading" | "ready" | "failed";

export function App() {
  const [state, setState] = useState<LoadState>("loading");
  const [error, setError] = useState<string | null>(null);
  const [api, setApi] = useState<ElsaAdminModuleApi | null>(null);
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
        const response = await fetch("/_elsa/admin/modules");
        if (!response.ok) {
          throw new Error(`Manifest request failed with ${response.status}.`);
        }

        const manifestResponse = (await response.json()) as AdminModulesResponse;
        const registry = createAdminRegistry({
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

        await loadAdminModules(manifestResponse.modules, registry, {
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
    () => [...(api?.navigation.list() ?? []), { id: "diagnostics", label: "Diagnostics", path: "/diagnostics/modules", order: 900 }]
      .sort((a, b) => (a.order ?? 500) - (b.order ?? 500)),
    [api, state]
  );

  if (state === "loading") {
    return <ShellFrame navigation={[]} path={path} onNavigate={navigateTo}><div className="empty-state">Loading admin modules...</div></ShellFrame>;
  }

  if (state === "failed") {
    return <ShellFrame navigation={[]} path={path} onNavigate={navigateTo}><div className="error-state">{error}</div></ShellFrame>;
  }

  const activeRoute = routes.find(route => route.path === path);
  const ActiveComponent = activeRoute?.component;

  return (
    <ShellFrame navigation={navigation} path={path} onNavigate={navigateTo}>
      {path === "/" ? <Home api={api!} /> : null}
      {path === "/diagnostics/modules" ? <Diagnostics api={api!} /> : null}
      {ActiveComponent ? <ActiveComponent /> : null}
      {!ActiveComponent && path !== "/" && path !== "/diagnostics/modules" ? (
        <div className="empty-state">No admin route is registered for {path}.</div>
      ) : null}
    </ShellFrame>
  );
}

function ShellFrame({
  navigation,
  path,
  onNavigate,
  children
}: {
  navigation: Array<{ id: string; label: string; path: string }>;
  path: string;
  onNavigate: (path: string) => void;
  children: React.ReactNode;
}) {
  return (
    <div className="admin-shell">
      <aside className="sidebar">
        <a className="brand" href="/" onClick={event => { event.preventDefault(); onNavigate("/"); }}>
          <span className="brand-mark">E</span>
          <span>Elsa Admin</span>
        </a>
        <nav>
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
              {item.label}
            </a>
          ))}
        </nav>
      </aside>
      <main className="content">{children}</main>
    </div>
  );
}

function Home({ api }: { api: ElsaAdminModuleApi }) {
  const widgets = api.dashboardWidgets.list().sort((a, b) => (a.order ?? 500) - (b.order ?? 500));

  return (
    <section>
      <header className="page-header">
        <h1>Admin modules</h1>
        <p>Installed modules contribute routes, widgets, and extension points through the admin SDK.</p>
      </header>
      <div className="widget-grid">
        {widgets.length === 0 ? <div className="empty-state">No dashboard widgets are registered.</div> : null}
        {widgets.map(widget => {
          const Widget = widget.component;
          return <Widget key={widget.id} />;
        })}
      </div>
    </section>
  );
}

function Diagnostics({ api }: { api: ElsaAdminModuleApi }) {
  return (
    <section>
      <header className="page-header">
        <h1>Module diagnostics</h1>
        <p>Load status for server-discovered admin modules.</p>
      </header>
      <div className="diagnostics-list">
        {api.diagnostics.list().map((diagnostic, index) => (
          <div className="diagnostic-row" key={`${diagnostic.moduleId}-${diagnostic.status}-${index}`}>
            <strong>{diagnostic.moduleId}</strong>
            <span>{diagnostic.status}</span>
            <p>{diagnostic.reason}</p>
          </div>
        ))}
      </div>
    </section>
  );
}

function normalizePath(path: string) {
  if (path.length > 1 && path.endsWith("/")) {
    return path.slice(0, -1);
  }

  return path;
}

function navigateTo(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}
