import React, { useEffect } from "react";
import {
  Activity,
  FileText,
  Gauge,
  GitBranch,
  Github,
  LayoutDashboard,
  Paintbrush,
  PackagePlus,
  PackageSearch,
  PanelLeftClose,
  Search
} from "lucide-react";
import { ExternalLink } from "lucide-react";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { getNavigationSection, getTopLevelNavigationItems } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import elsaLogo from "../../src/apps/Elsa.Studio.Web/Client/src/assets/images/icon.png";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";

const navigation: StudioNavigationContribution[] = [
  { id: "dashboard", label: "Dashboard", path: "/dashboard", order: 0 },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20 },
  { id: "workflows-definitions", label: "Workflow Definitions", path: "/workflows/definitions", parentId: "workflows" },
  { id: "workflows-activity-definitions", label: "Activity Definitions", path: "/workflows/activity-definitions", parentId: "workflows" },
  { id: "workflows-executables", label: "Executables", path: "/workflows/executables", parentId: "workflows" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", parentId: "workflows" },
  { id: "workflows-runtime-diagnostics", label: "Runtime Diagnostics", path: "/workflows/runtime-diagnostics", parentId: "workflows" },
  { id: "workflows-activity-availability", label: "Activity Availability", path: "/workflows/activity-availability", parentId: "workflows" },
  { id: "secrets", label: "Secrets", path: "/security/secrets", order: 45 },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", order: 900 },
  { id: "modules", label: "Modules", path: "/modules", order: 80 },
  { id: "theme-builder", label: "Theme Builder", path: "/theme-builder", order: 85 },
  { id: "package-feeds", label: "Package feeds", path: "/package-feeds", order: 90 },
  { id: "feature-management", label: "Features", path: "/features", order: 130 },
  { id: "extension-builder", label: "Extension Builder", path: "/extension-builder", order: 50, section: "settings" }
];

writeAppearancePreferences({ themeId: "classic", mode: "light", navigation: "left" });

export function NavBeforeFixture() {
  const childrenByParentId = new Map<string, StudioNavigationContribution[]>();
  for (const item of navigation) {
    if (!item.parentId) continue;
    const children = childrenByParentId.get(item.parentId) ?? [];
    children.push(item);
    childrenByParentId.set(item.parentId, children);
  }
  const byOrder = (a: StudioNavigationContribution, b: StudioNavigationContribution) => (a.order ?? 1000) - (b.order ?? 1000);
  const sections = [
    { id: "workspace" as const, label: "Workspace", items: getTopLevelNavigationItems(navigation, "workspace").sort(byOrder) },
    { id: "settings" as const, label: "Settings", items: getTopLevelNavigationItems(navigation, "settings").sort(byOrder) }
  ].filter(section => section.items.length > 0);

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", "light");
    document.documentElement.setAttribute("data-theme-appearance", "light");
  }, []);

  return (
    <ThemeProvider>
      <div className="studio-shell">
        <aside className="sidebar">
          <div className="sidebar-top">
            <a className="brand" href="/">
              <span className="brand-mark" aria-hidden="true">
                <img src={elsaLogo} alt="" />
              </span>
              <span className="nav-label">
                <strong>Elsa Studio</strong>
                <small>Foundation</small>
              </span>
            </a>
            <button type="button" className="sidebar-collapse-toggle" aria-label="Collapse navigation" aria-expanded="true">
              <PanelLeftClose size={16} />
            </button>
          </div>
          <label className="sidebar-search">
            <Search size={16} aria-hidden="true" />
            <input type="search" aria-label="Search modules" placeholder="Search modules" defaultValue="" />
          </label>
          {sections.map(section => (
            <nav key={section.id} className="nav-section" aria-label={section.label}>
              <span className="nav-heading">{section.label}</span>
              {section.items.map(item => {
                const childItems = (childrenByParentId.get(item.id) ?? [])
                  .filter(child => getNavigationSection(child) === section.id);
                const itemActive = item.path === "/workflows/definitions" || item.id === "workflows";
                return (
                  <div className="nav-item-group" key={item.id}>
                    <a className={itemActive ? "active has-active-child" : ""} href={item.path} aria-current={itemActive ? "page" : undefined} aria-label={item.label}>
                      <span className="nav-icon-tile" style={{ "--nav-icon-color": legacyIconColor(item.id) } as React.CSSProperties} aria-hidden="true">
                        <LegacyNavIcon id={item.id} />
                      </span>
                      <span className="nav-label">{item.label}</span>
                    </a>
                    {childItems.length > 0 ? (
                      <div className="nav-children">
                        {childItems.map(child => (
                          <a
                            key={child.id}
                            className={child.path === "/workflows/definitions" ? "active nav-child" : "nav-child"}
                            href={child.path}
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
              <small>backend.example</small>
            </span>
          </div>
        </aside>
        <div className="main-frame">
          <header className="topbar">
            <div>
              <span className="breadcrumb">Studio / Workflows</span>
              <h1>Workflows</h1>
            </div>
            <div className="topbar-actions">
              <a href="https://github.com/elsa-workflows/elsa-foundation-studio" aria-label="GitHub">
                <Github size={18} />
              </a>
              <a href="https://github.com/elsa-workflows/elsa-foundation-designer" aria-label="Designer source">
                <ExternalLink size={18} />
              </a>
            </div>
          </header>
          <main className="content">
            <section style={{ padding: 24 }}>
              <h2>Workflows</h2>
              <p>Navigation before placement metadata.</p>
            </section>
          </main>
        </div>
      </div>
    </ThemeProvider>
  );
}

function legacyIconColor(id: string) {
  if (id.includes("dashboard")) return "#0ea5e9";
  if (id.includes("workflow")) return "#0ea5e9";
  if (id.includes("diagnostics")) return "#10b981";
  if (id.includes("modules")) return "#8b5cf6";
  if (id.includes("theme")) return "#0f766e";
  if (id.includes("feeds")) return "#f59e0b";
  if (id.includes("secret")) return "#64748b";
  return "var(--primary)";
}

function LegacyNavIcon({ id }: { id: string }) {
  if (id === "dashboard" || id.includes("dashboard")) return <LayoutDashboard size={18} />;
  if (id.includes("weather")) return <Activity size={18} />;
  if (id.includes("diagnostics")) return <FileText size={18} />;
  if (id.includes("theme")) return <Paintbrush size={18} />;
  if (id.includes("modules")) return <PackageSearch size={18} />;
  if (id.includes("feeds")) return <PackagePlus size={18} />;
  if (id.includes("workflow")) return <GitBranch size={18} />;
  return <Gauge size={18} />;
}
