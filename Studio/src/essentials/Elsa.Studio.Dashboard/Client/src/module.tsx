import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { DashboardPage } from "./DashboardPage";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  const component = () => <DashboardPage api={api} />;
  api.navigation.add({ id: "dashboard", label: "Dashboard", path: "/dashboard", order: 0, iconColor: "var(--studio-accent)" });
  for (const path of ["/", "/dashboard", "/overview"]) api.routes.add({ id: `dashboard:${path}`, label: "Dashboard", path, component });
}
