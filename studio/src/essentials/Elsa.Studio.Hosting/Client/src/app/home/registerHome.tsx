import React, { lazy, Suspense } from "react";
import type { ElsaStudioModuleApi } from "../../sdk";

const HomePage = lazy(() => import("./HomePage").then(module => ({ default: module.HomePage })));

/** The paths the shell serves Home at. `/` is the landing page; the others keep older links working. */
export const homePaths = ["/", "/dashboard", "/overview"] as const;

/**
 * Registers the shell's Home: the main navigation entry and its routes. Every Studio flavor has it; modules add
 * content through `api.homeWidgets` (via `registerHomeWidget`) and `api.homeSections`.
 */
export function registerHome(api: ElsaStudioModuleApi) {
  const component = () => (
    <Suspense fallback={<section className="dashboard-page home-page"><div role="status">Loading Home…</div></section>}>
      <HomePage api={api} />
    </Suspense>
  );
  api.navigation.add({ id: "home", label: "Home", path: "/dashboard", order: 10, iconColor: "var(--studio-accent)", placement: "main" });
  for (const path of homePaths) api.routes.add({ id: `home:${path}`, label: "Home", path, component });
}
