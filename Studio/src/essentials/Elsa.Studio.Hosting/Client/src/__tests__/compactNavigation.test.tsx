import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "../sdk";
import { SettingsShell } from "../app/settings/SettingsShell";
import { getSettingsSectionGroups } from "../app/settings/settingsSections";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { ThemeBuilderPage } from "../app/modules/ThemeBuilderPage";
import { builtInThemeDefinitions, cloneThemeDefinition } from "../app/themes/presets";

let container: HTMLDivElement;
let root: Root;
let compact = false;
const changes = new Set<() => void>();
async function update(action: () => void) {
  flushSync(action);
  for (let i = 0; i < 3; i++) await new Promise(resolve => setTimeout(resolve, 0));
}
async function resize(value: boolean) {
  compact = value;
  await update(() => { for (const change of changes) change(); });
}
beforeEach(() => {
  localStorage.clear(); compact = false; changes.clear();
  vi.stubGlobal("matchMedia", (query: string) => ({
    get matches() { return query === "(max-width: 800px)" && compact; },
    media: query,
    addEventListener: (_: string, change: () => void) => { if (query === "(max-width: 800px)") changes.add(change); },
    removeEventListener: (_: string, change: () => void) => changes.delete(change)
  }));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(async () => {
  await update(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); localStorage.clear();
});

it("keeps the current Settings form mounted and moves navigation focus across breakpoints", async () => {
  const groups = getSettingsSectionGroups([
    { id: "appearance", label: "Appearance with a long section name", path: "/settings/appearance", placement: "settings" },
    { id: "modules", label: "Modules", path: "/modules", placement: "admin" }
  ], { canReadAdmin: true });
  const navigate = vi.fn();
  await update(() => root.render(<SettingsShell groups={groups} activeId="appearance" onNavigate={navigate}><input aria-label="Unsaved setting" defaultValue="Draft value" /></SettingsShell>));
  const field = container.querySelector<HTMLInputElement>("input")!;
  container.querySelector<HTMLElement>('[aria-current="page"]')!.focus();
  await resize(true);
  const select = container.querySelector<HTMLSelectElement>("select")!;
  expect(select.value).toBe("appearance"); expect(document.activeElement).toBe(select);
  expect(container.querySelector("input")).toBe(field); expect(field.value).toBe("Draft value");
  await update(() => { select.value = "modules"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(navigate).toHaveBeenCalledWith("/settings/modules");
  await resize(false);
  expect(document.activeElement).toBe(container.querySelector('[aria-current="page"]'));
  field.focus(); await resize(true); expect(document.activeElement).toBe(field);
  expect(container.querySelector("input")).toBe(field);
});

it("preserves a dirty theme and content focus while changing between catalog and current-theme selection", async () => {
  const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "compact-custom", name: "A long custom theme name for narrow selection", source: "custom", enabled: true, published: true };
  const context = { http: { getJson: async () => ({ themes: [custom], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: custom.id, assets: [] }) } } as unknown as StudioEndpointContext;
  const api = { host: context, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
  await update(() => root.render(<ThemeProvider storeContext={context}><ThemeBuilderPage api={api} /></ThemeProvider>));
  const choice = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-list button")).find(button => button.textContent?.includes(custom.name))!;
  await update(() => choice.click());
  const field = Array.from(container.querySelectorAll<HTMLInputElement>(".theme-builder-detail input")).find(input => !input.disabled && input.type === "text")!;
  const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
  await update(() => { set.call(field, "Retained dirty theme"); field.dispatchEvent(new Event("input", { bubbles: true })); });
  field.focus(); await resize(true);
  expect(container.querySelector(".theme-builder-list")).toBeNull();
  expect(container.querySelector<HTMLSelectElement>(".theme-builder-select select")!.value).toBe(custom.id);
  expect(container.querySelector(".theme-builder-detail input")).toBe(field); expect(field.value).toBe("Retained dirty theme"); expect(document.activeElement).toBe(field);
  const select = container.querySelector<HTMLSelectElement>(".theme-builder-select select")!; select.focus(); await resize(false);
  expect(document.activeElement).toBe(container.querySelector(".theme-builder-list button.active"));
  expect(field.value).toBe("Retained dirty theme");
});
