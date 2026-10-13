import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { DashboardPage } from "./DashboardPage";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  const component = () => <DashboardPage api={api} />;
  // Home is the existing dashboard/overview route. A later PR merges Home and the dashboard into
  // one customizable page; this contribution only relabels and places the current surface on main.
  api.navigation.add({
    id: "home",
    label: "Home",
    path: "/dashboard",
    order: 10,
    iconColor: "var(--studio-accent)",
    placement: "main"
  });
  for (const path of ["/", "/dashboard", "/overview"]) api.routes.add({ id: `dashboard:${path}`, label: "Home", path, component });
}
