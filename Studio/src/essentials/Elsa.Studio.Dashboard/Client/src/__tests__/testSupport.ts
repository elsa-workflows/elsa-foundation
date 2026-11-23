import { flushSync } from "react-dom";
import { expect, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";
import { homeLayoutLocalStoragePrefix } from "../homeLayout";

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
      http: {
        getJson: notFound,
        putJson: vi.fn(async (_url, body) => ({
          source: "user",
          namespace: "home-layout",
          schemaVersion: 1,
          revision: "rev-1",
          value: (body as { value: unknown }).value
        })),
        deleteJson: vi.fn(async () => ({}))
      }
    },
    runtime: { hostId: "test", dashboard: { defaultRefreshIntervalMs: 300_000, widgetTimeoutMs: 10_000 }, enabledFeatures: ["workflows"] },
    navigation: makeRegistry(),
    routes: makeRegistry(),
    dashboardWidgets: widgets,
    homeWidgets: widgets,
    dashboardRecordLinks: makeRegistry(),
    settingEditors: makeRegistry()
  } as unknown as ElsaStudioModuleApi;
}

export function seedHomeLayout(api: ElsaStudioModuleApi, widgetIds: string[], subjectId = "anonymous") {
  const scope = {
    studioHostId: api.runtime.hostId ?? "default",
    backendBaseUrl: api.backend.baseUrl,
    subjectId,
    tenantId: "default"
  };
  localStorage.setItem(`${homeLayoutLocalStoragePrefix}${encodeURIComponent(JSON.stringify(scope))}`, JSON.stringify({
    source: "local",
    namespace: "home-layout",
    schemaVersion: 1,
    value: { widgets: widgetIds.map(id => ({ id, size: "wide", visible: true })) }
  }));
}

export async function flush() {
  await new Promise(resolve => setTimeout(resolve, 0));
  await new Promise(resolve => setTimeout(resolve, 0));
}

export async function waitUntil(predicate: () => boolean) {
  for (let attempt = 0; attempt < 40; attempt += 1) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Condition was not met before the test timeout.");
}

export async function beginNamedDraft(name: string) {
  await waitUntil(() => document.querySelector('[role="dialog"] input[required]') !== null);
  const dialog = document.querySelector<HTMLElement>('[role="dialog"]')!;
  const input = dialog.querySelector<HTMLInputElement>("input[required]")!;
  const create = dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  expect(create.disabled).toBe(true);
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, name);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
  flushSync(() => create.click());
  expect(document.querySelector('[role="dialog"]')).toBeNull();
}

export function selectResourceAction(container: HTMLElement, label: string, action: string) {
  const menu = container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  expect(menu).toBeTruthy();
  flushSync(() => menu!.click());
  const item = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find(button => button.textContent?.trim() === action);
  expect(item).toBeTruthy();
  flushSync(() => item!.click());
}
