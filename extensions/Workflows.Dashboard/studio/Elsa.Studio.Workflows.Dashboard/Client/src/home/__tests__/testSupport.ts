import { vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

export function stubApi(): ElsaStudioModuleApi {
  const makeRegistry = <T,>() => {
    const items: T[] = [];
    return { add: (item: T) => items.push(item), list: () => items, compose: () => [], slot: { id: "test", kind: "test", owner: { kind: "host", id: "host" } } };
  };
  const widgets = makeRegistry();
  const notFound = async () => {
    const error = new Error("missing") as Error & { status: number };
    error.status = 404;
    throw error;
  };
  return {
    host: { baseUrl: "https://studio.example", hostVersion: "1", sdkVersion: "1", http: {} },
    backend: {
      baseUrl: "https://backend.example",
      http: { getJson: notFound, putJson: vi.fn(async () => ({})), deleteJson: vi.fn(async () => ({})) }
    },
    runtime: { hostId: "test", enabledFeatures: ["workflows"] },
    navigation: makeRegistry(),
    routes: makeRegistry(),
    dashboardWidgets: widgets,
    homeWidgets: widgets,
    homeSections: makeRegistry()
  } as unknown as ElsaStudioModuleApi;
}

export async function waitUntil(predicate: () => boolean) {
  for (let attempt = 0; attempt < 40; attempt += 1) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Condition was not met before the test timeout.");
}
