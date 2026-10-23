import React, { lazy, Suspense } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { registerCoreHomeWidgets } from "./widgets/registerCoreHomeWidgets";
import "./styles.css";

const HomePage = lazy(() => import("./HomePage").then(module => ({ default: module.HomePage })));

export function register(api: ElsaStudioModuleApi) {
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
}
