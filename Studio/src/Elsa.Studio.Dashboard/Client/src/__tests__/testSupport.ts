import { vi } from "vitest";
import type { ElsaStudioModuleApi, StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";

export function widget(overrides: Partial<StudioDashboardWidgetContribution> = {}): StudioDashboardWidgetContribution {
  return {
    id: "workflows.runs",
    moduleId: "Elsa.Studio.Workflows.Dashboard",
    title: "Workflow runs",
    defaultVisible: true,
    defaultSize: "wide",
    supportedSizes: ["medium", "wide"],
    settings: {
      schemaVersion: 1,
      defaults: { range: "7d" },
      descriptors: [],
      validate: value => (value as { range?: string })?.range === "7d" ? value as { range: string } : null
    },
    component: () => null,
    ...overrides
  };
}

export function stubApi(): ElsaStudioModuleApi {
  const makeRegistry = <T,>() => {
    const items: T[] = [];
    return { add: (item: T) => items.push(item), list: () => items, compose: () => [], slot: { id: "test", kind: "test", owner: { kind: "host", id: "host" } } };
  };
  const notFound = async () => {
    const error = new Error("missing") as Error & { status: number };
    error.status = 404;
    throw error;
  };
  return {
    host: { baseUrl: "https://studio.example", hostVersion: "1", sdkVersion: "1", http: {} },
    backend: { baseUrl: "https://backend.example", http: { getJson: notFound, putJson: vi.fn(async (_url, body) => ({ namespace: "dashboard", schemaVersion: 1, value: (body as { value: unknown }).value })) } },
    runtime: { hostId: "test", dashboard: { defaultRefreshIntervalMs: 300_000, widgetTimeoutMs: 10_000 } },
    navigation: makeRegistry(), routes: makeRegistry(), dashboardWidgets: makeRegistry(), settingEditors: makeRegistry()
  } as unknown as ElsaStudioModuleApi;
}

export async function flush() {
  await new Promise(resolve => setTimeout(resolve, 0));
  await new Promise(resolve => setTimeout(resolve, 0));
}

export async function waitUntil(predicate: () => boolean) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Condition was not met before the test timeout.");
}
