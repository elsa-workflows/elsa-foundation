import React, { lazy, Suspense } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { registerCoreHomeWidgets } from "./widgets/registerCoreHomeWidgets";
import "./styles.css";
import { createDashboardAuthoringController } from "./native/authoring";
import { registerDashboardWeaver } from "./native/weaverAuthoring";
export type { DashboardAuthoringController, DashboardAuthoringSnapshot, DashboardAuthoringProposal, DashboardAuthoringApplyResult } from "./native/authoring";

const HomePage = lazy(() => import("./HomePage").then(module => ({ default: module.HomePage })));
const NamedDashboardsRoute = lazy(() => import("./native/NamedDashboards").then(module => ({ default: module.NamedDashboardsRoute })));

export function register(api: ElsaStudioModuleApi) {
  const dashboardAuthoring = createDashboardAuthoringController(api.weaver?.clientId ?? crypto.randomUUID());
  registerDashboardWeaver(api, dashboardAuthoring);
  registerCoreHomeWidgets(api);
  const component = () => (
    <Suspense fallback={<section className="dashboard-page home-page"><div role="status">Loading Home…</div></section>}>
      <HomePage api={api} />
    </Suspense>
  );
  api.navigation.add({
    id: "home",
    label: "Home",
    path: "/dashboard",
    order: 10,
    iconColor: "var(--studio-accent)",
    placement: "main"
  });
  for (const path of ["/", "/dashboard", "/overview"]) {
    api.routes.add({ id: `home:${path}`, label: "Home", path, component });
  }
  const requiresBackend = [{ id: "elsa.api.studio-dashboard", contractVersion: "1" }];
  api.navigation.add({ id: "dashboards", label: "Dashboards", path: "/dashboards", order: 12, placement: "operate", requiresBackend });
  api.navigation.add({ id: "questions", label: "Questions", path: "/questions", order: 13, placement: "operate", requiresBackend });
  for (const kind of ["dashboard", "question"] as const) {
    const collection = `/${kind}s`;
    for (const [view, path] of [["list", collection], ["create", `${collection}/new`], ["detail", `${collection}/:resourceId`]] as const) {
      api.routes.add({ id: `saved-${kind}:${view}`, label: view === "list" ? `${kind === "dashboard" ? "Dashboards" : "Questions"}` : view === "create" ? `Create ${kind}` : `Saved ${kind}`, path, requiresBackend,
        workspace: view === "detail" ? { resourceKey: location => `${kind}:${location.pathname.split("/").pop()}` } : undefined,
        component: ({ navigate }) => <Suspense fallback={<section role="status">Loading {kind}…</section>}><NamedDashboardsRoute api={api} kind={kind} view={view} authoring={dashboardAuthoring} navigate={navigate} /></Suspense>
      });
    }
  }
}
