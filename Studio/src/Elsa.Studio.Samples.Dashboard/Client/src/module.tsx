import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  api.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: ModuleHealthWidget
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-route",
    title: "Route ownership",
    order: 110,
    component: RouteOwnershipWidget
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-backend",
    title: "Backend endpoints",
    order: 120,
    component: BackendEndpointsWidget
  });
}

export function ModuleHealthWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Dashboard module</h2>
      <p className="metric">Loaded</p>
      <p>Runtime widget registration completed through the Studio SDK.</p>
    </div>
  );
}

export function RouteOwnershipWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Route ownership</h2>
      <p className="metric">Host</p>
      <p>Dashboard navigation and routing are provided by Studio.</p>
    </div>
  );
}

export function BackendEndpointsWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Backend endpoints</h2>
      <p className="metric">0</p>
      <p>This widget is contributed by a frontend-only module.</p>
    </div>
  );
}
