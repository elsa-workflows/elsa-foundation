import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { FolderKanban, Scale } from "lucide-react";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences, type ThemeModePreference } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { WorkspaceTabStrip, type WorkspaceTabView } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/WorkspaceTabStrip";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const query = new URLSearchParams(location.search);
const appearance = (query.get("appearance") ?? "light") as ThemeModePreference;
writeAppearancePreferences({ themeId: "classic", mode: appearance === "dark" ? "dark" : "light", navigation: "left", workspaceStyle: "linen" });

const longWorkspaceTabTitle = "Picker-created policy narrow-dark-1731234567890";

const twoTabs: WorkspaceTabView[] = [
  { id: "workflow", title: "Decision picker narrow-dark-1731234567890", icon: <Scale size={14} />, pinned: false, guarded: true },
  { id: "policy", title: longWorkspaceTabTitle, icon: <FolderKanban size={14} />, pinned: false, guarded: false }
];

const manyTabs: WorkspaceTabView[] = [
  { id: "home", title: "Home", icon: <Scale size={14} />, pinned: true, guarded: false },
  { id: "orders", title: "Orders workflow", icon: <FolderKanban size={14} />, pinned: false, guarded: false },
  { id: "customers", title: "Customers", icon: <Scale size={14} />, pinned: false, guarded: false },
  { id: "invoices", title: "Invoices draft", icon: <FolderKanban size={14} />, pinned: false, guarded: true },
  { id: "reports", title: "Reports", icon: <Scale size={14} />, pinned: false, guarded: false },
  { id: "settings", title: "Workspace settings", icon: <FolderKanban size={14} />, pinned: false, guarded: false },
  { id: "workflow", title: "Decision picker narrow-dark-1731234567890", icon: <Scale size={14} />, pinned: false, guarded: true },
  { id: "policy", title: longWorkspaceTabTitle, icon: <FolderKanban size={14} />, pinned: false, guarded: false }
];

const initialTabs = query.get("tabs") === "many" ? manyTabs : twoTabs;

function WorkspaceTabStripFixture() {
  const [tabs, setTabs] = useState(initialTabs);
  const [activeId, setActiveId] = useState("policy");
  return (
    <main style={{ minHeight: "100vh", background: "var(--studio-bg)" }}>
      <WorkspaceTabStrip
        baseId="ws"
        tabs={tabs}
        activeId={activeId}
        notice={null}
        onDismissNotice={() => undefined}
        onActivate={setActiveId}
        onClose={id => {
          setTabs(current => {
            const next = current.filter(tab => tab.id !== id);
            if (id === activeId) setActiveId(next[next.length - 1]?.id ?? "");
            return next;
          });
        }}
        onCloseOthers={id => setTabs(current => current.filter(tab => tab.id === id))}
        onCloseAll={() => { setTabs([]); setActiveId(""); }}
        onNewTab={() => undefined}
        onPin={(id, pinned) => setTabs(current => current.map(tab => tab.id === id ? { ...tab, pinned } : tab))}
        onMove={(id, toIndex) => setTabs(current => {
          const from = current.findIndex(tab => tab.id === id);
          if (from < 0) return current;
          const next = [...current];
          const [moved] = next.splice(from, 1);
          next.splice(Math.max(0, Math.min(toIndex, next.length)), 0, moved);
          return next;
        })}
      />
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<ThemeProvider><WorkspaceTabStripFixture /></ThemeProvider>);
