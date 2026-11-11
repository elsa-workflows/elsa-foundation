import React, { useCallback, useRef } from "react";
import { Scale } from "lucide-react";
import { type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { WorkspaceTabPanels } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/WorkspaceTabPanels";
import { WorkspaceTabStrip } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/WorkspaceTabStrip";
import { resolveTabTitle } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/model";
import { workspaceStorageKey, type WorkspaceScope } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/storage";
import { useWorkspaceTabs } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/workspace/useWorkspaceTabs";
import { DecisionsPage } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionsPage";
import { newDecisionXml } from "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionXml";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/components.css";
import "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const xml = newDecisionXml();
const createdAt = "2026-11-05T10:00:00Z";
const asset = { id: "approval", name: "Approval", description: "Purchase approval", revision: "r1", sourceHash: "hash1", xml, isSensitive: false, createdAt, updatedAt: createdAt };
const published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml, sourceHash: "hash1", publishedAt: createdAt };
const created: { current: typeof asset | null } = { current: null };
const scope: WorkspaceScope = { origin: window.location.origin, backendId: "fixture", backendBaseUrl: "http://api.test", subject: "ada", tenantId: null };
window.localStorage.removeItem(workspaceStorageKey(scope));

function createApi(): ElsaStudioModuleApi {
  const getJson = async (path: string): Promise<unknown> => {
    if (path.endsWith("/versions") || path.endsWith("/cases") || path.endsWith("/runs")) return { items: path.includes("/approval/") && path.endsWith("/versions") ? [published] : [] };
    if (path.endsWith("/versions/v1")) return published;
    if (path.endsWith("/decisions")) return { items: created.current ? [asset, created.current] : [asset] };
    if (path.endsWith("/created") && created.current) return created.current;
    return asset;
  };
  const postJson = async (_path: string, body: unknown) => {
    const input = body as { name?: string; description?: string; xml?: string; isSensitive?: boolean };
    created.current = { ...asset, id: "created", name: input.name ?? "Created", description: input.description, xml: input.xml ?? xml, isSensitive: Boolean(input.isSensitive) };
    return created.current;
  };
  return {
    backend: { baseUrl: "https://server.example/tenant", http: { getJson, postJson, putJson: async () => asset, deleteJson: async () => undefined } },
    backendSession: {
      capabilities: { capabilities: [{ id: "elsa.api.decisions", contractVersion: "1", links: [{ rel: "decisions", href: "decisions" }] }] },
      identity: { status: "authenticated", subject: "user", tenantId: "tenant", roles: [], permissions: ["decisions:read", "decisions:manage", "decisions:publish", "decisions:evaluate", "decisions:test", "decisions:history", "decisions:source"] }
    },
    runtime: { identity: { subject: "user", tenantId: "tenant" } },
    dialogs: { confirm: async () => true, prompt: async () => "Imported", alert: async () => undefined }
  } as unknown as ElsaStudioModuleApi;
}

const api = createApi();
const resourceKeyOf = (location: string) => {
  const url = new URL(location, window.location.origin);
  const id = url.searchParams.get("decision");
  return id ? `decision:${id}:${url.searchParams.get("version") ?? "draft"}` : url.searchParams.has("new") ? "decision-editor:new" : null;
};

export function DecisionWorkspaceTabsFixture() {
  const contentRef = useRef<HTMLElement>(null);
  const workspace = useWorkspaceTabs({ scope, qualifyPath: path => path, resourceKeyOf, titleOf: () => "Decisions", contentRef });
  const renderScreen = useCallback((location: string) => {
    const url = new URL(location, window.location.origin);
    return <DecisionsPage api={api} navigate={workspace.navigate} assetId={url.searchParams.get("decision")} versionId={url.searchParams.get("version")} isNew={url.searchParams.has("new")} />;
  }, [workspace.navigate]);
  const tabViews = workspace.state.tabs.map(tab => {
    const title = resolveTabTitle(tab, workspace.screenKeyOf(tab.location), "Decisions");
    return { id: tab.id, title: title.title, detail: title.detail, icon: <Scale size={14} />, pinned: tab.pinned, guarded: Boolean(tab.guarded) && tab.id !== workspace.state.activeId };
  });
  return (
    <div>
      <WorkspaceTabStrip
        baseId="ws"
        tabs={tabViews}
        activeId={workspace.state.activeId}
        notice={workspace.notice}
        onDismissNotice={workspace.dismissNotice}
        onActivate={workspace.activate}
        onClose={workspace.close}
        onCloseOthers={workspace.closeOthers}
        onCloseAll={workspace.closeAll}
        onNewTab={() => workspace.open("/decisions", { newTab: true })}
        onPin={workspace.setPinned}
        onMove={workspace.move}
      />
      <main ref={contentRef}>
        <WorkspaceTabPanels
          baseId="ws"
          tabs={workspace.state.tabs}
          activeId={workspace.state.activeId}
          renderScreen={renderScreen}
          titleOf={() => "Decisions"}
          setTitle={workspace.setTitle}
          isActive={workspace.isActive}
          markPendingWork={workspace.markPendingWork}
          onCloseTab={workspace.close}
        />
      </main>
    </div>
  );
}
