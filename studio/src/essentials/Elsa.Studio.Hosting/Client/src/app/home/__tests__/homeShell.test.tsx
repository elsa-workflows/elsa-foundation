import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { AnonymousAuthProvider, createEndpointContext, registerHomeWidget, type ElsaStudioModuleApi, type StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { findRouteForPath } from "../../App";
import { loadStudioModules } from "../../loader";
import { createStudioRegistry } from "../../registry";
import { registerHome } from "../registerHome";
import { seedHomeLayout } from "./testSupport";

/**
 * The shell owns Home (#5768): a flavor without the Dashboard module still serves `/` as Home, and a widget from any
 * other module renders there. The registry, loader and route lookup are the ones the shell boots with.
 */
describe("Home in the Studio shell", () => {
  afterEach(() => {
    localStorage.clear();
    document.body.innerHTML = "";
  });

  it("serves Home at / and renders a widget a non-Dashboard module contributes, with no Dashboard module loaded", async () => {
    const api = await bootShell([{ id: "Acme.Weather", register: moduleApi => registerHomeWidget(moduleApi, {
      id: "acme.weather",
      moduleId: "Acme.Weather",
      title: "Weather",
      defaultSize: "M",
      load: async () => ({ forecast: "Sunny" }),
      component: ({ snapshot }) => <p>Forecast: {(snapshot as { forecast?: string } | undefined)?.forecast}</p>
    }) }]);

    expect(api.runtime.enabledFeatures).not.toContain("Elsa.Studio.Dashboard");
    // Widgets outside the built-in default layout appear once a user adds them; this user has.
    seedHomeLayout(api, ["acme.weather"]);
    const container = renderRoute(api, "/");

    await waitUntil(() => container.textContent?.includes("Forecast: Sunny") === true);
    expect(container.querySelector("h2")?.textContent).toBe("Home");
    expect(container.querySelector('article.dashboard-widget[data-widget-id="acme.weather"] h3')?.textContent).toBe("Weather");
  });

  it("serves an empty Home in the bare shell", async () => {
    const api = await bootShell([]);
    const container = renderRoute(api, "/");

    await waitUntil(() => container.querySelector(".dashboard-empty") !== null);
    expect(container.querySelector("h2")?.textContent).toBe("Home");
    expect(container.textContent).toContain("No installed module adds content to Home yet.");
    expect(api.navigation.list().filter(item => item.placement === "main").map(item => item.label)).toEqual(["Home"]);
  });

  it("tells a user whose backend lacks the capability that the installed widgets need it", async () => {
    const api = await bootShell([workflowWidgetModule], { capabilities: { capabilities: [] } });
    const container = renderRoute(api, "/");

    await waitUntil(() => container.querySelector(".dashboard-empty") !== null);
    expect(api.homeWidgets.list()).toEqual([]);
    expect(container.textContent).toContain("The installed Home widgets need a backend capability, a module feature or a permission that this session lacks.");
    expect(container.textContent).not.toContain("No installed module adds content to Home yet.");
  });

  it("reports failed backend discovery when the installed widgets cannot be admitted", async () => {
    const api = await bootShell([workflowWidgetModule], { capabilities: null, discoveryError: "The backend at https://backend.example/ did not answer." });
    const container = renderRoute(api, "/");

    await waitUntil(() => container.querySelector(".dashboard-empty") !== null);
    expect(container.textContent).toContain("Home content is unavailable because the backend's capabilities could not be discovered.");
    expect(container.textContent).toContain("The backend at https://backend.example/ did not answer.");
    expect(container.textContent).not.toContain("No installed module adds content to Home yet.");
  });

  it("keeps /dashboard and /overview on Home for older links", async () => {
    const api = await bootShell([]);
    for (const path of ["/dashboard", "/overview"])
      expect(findRouteForPath(api.routes.list(), path)?.id).toBe(`home:${path}`);
  });
});

/** A module whose Home widget needs a backend capability, as the workflow dashboard's widgets do. */
const workflowWidgetModule = { id: "Acme.Workflows", register: (moduleApi: ElsaStudioModuleApi) => registerHomeWidget(moduleApi, {
  id: "acme.runs",
  moduleId: "Acme.Workflows",
  title: "Runs",
  requiresBackend: [{ id: "elsa.api.runtime", contractVersion: "1" }],
  component: () => <p>Runs body</p>
}) };

async function bootShell(
  modules: Array<{ id: string; register(api: ElsaStudioModuleApi): void }>,
  backendSession?: Pick<StudioBackendSession, "capabilities" | "discoveryError">
) {
  const enabledFeatures = modules.map(module => module.id);
  const api = createStudioRegistry({ hostVersion: "1.0.0", sdkVersion: "1.0.0", ...createEndpointContext("https://studio.example/") }, {
    backendBaseUrl: "https://backend.example/",
    backendSession: backendSession as StudioBackendSession | undefined,
    runtime: { hostId: "test", enabledFeatures }
  });
  await loadStudioModules(
    modules.map(module => ({ id: module.id, displayName: module.id, version: "1.0.0", entry: `/${module.id}.js`, styles: [], requiredHostVersion: "^1.0.0", requiredSdkVersion: "^1.0.0", capabilities: [] })),
    api,
    {
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      importModule: async entry => ({ register: modules.find(module => entry.includes(module.id))!.register })
    }
  );
  registerHome(api);
  return api;
}

function renderRoute(api: ElsaStudioModuleApi, path: string) {
  const route = findRouteForPath(api.routes.list(), path);
  if (!route) throw new Error(`No route for ${path}`);
  const Route = route.component;
  const container = document.createElement("div");
  document.body.append(container);
  flushSync(() => createRoot(container).render(<AnonymousAuthProvider><Route navigate={() => undefined} /></AnonymousAuthProvider>));
  return container;
}

/** Home is a lazy route, so the first render waits on a real module import: poll on wall-clock time. */
async function waitUntil(predicate: () => boolean, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error("Condition was not met before the test timeout.");
    await new Promise(resolve => setTimeout(resolve, 10));
  }
}
