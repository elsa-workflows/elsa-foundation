import React from "react";
import type { ElsaAdminModuleApi } from "@elsa-workflows/admin-sdk";
import "./styles.css";

export function register(api: ElsaAdminModuleApi) {
  api.navigation.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/admin/dashboard",
    order: 100
  });

  api.routes.add({
    id: "dashboard-sample",
    label: "Dashboard",
    path: "/admin/dashboard",
    component: DashboardPage
  });

  api.dashboardWidgets.add({
    id: "dashboard-sample-health",
    title: "Module health",
    order: 100,
    component: ModuleHealthWidget
  });
}

export function DashboardPage() {
  return (
    <section>
      <header className="page-header">
        <h1>Dashboard sample</h1>
        <p>This frontend-only module contributed navigation, a route, and dashboard widgets.</p>
      </header>
      <div className="dashboard-sample-grid">
        <ModuleHealthWidget />
        <div className="admin-card">
          <h2>Registered routes</h2>
          <p className="metric">1</p>
          <p>The route was added by the dashboard module at runtime.</p>
        </div>
        <div className="admin-card">
          <h2>Backend endpoints</h2>
          <p className="metric">0</p>
          <p>This sample proves a frontend-only contribution.</p>
        </div>
      </div>
    </section>
  );
}

export function ModuleHealthWidget() {
  return (
    <div className="admin-card dashboard-sample-card">
      <h2>Dashboard module</h2>
      <p className="metric">Loaded</p>
      <p>Runtime registration completed through the admin SDK.</p>
    </div>
  );
}
