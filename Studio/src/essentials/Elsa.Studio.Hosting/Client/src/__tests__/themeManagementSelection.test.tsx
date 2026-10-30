import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "../sdk";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { AppearancePage } from "../app/settings/AppearancePage";
import { ThemeBuilderPage } from "../app/modules/ThemeBuilderPage";
import { builtInThemeDefinitions, cloneThemeDefinition } from "../app/themes/presets";

let container: HTMLDivElement;
let root: Root;

async function update(action: () => void) {
  flushSync(action);
  for (let index = 0; index < 3; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

beforeEach(() => {
  localStorage.clear();
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await update(() => root.unmount());
  container.remove();
  document.documentElement.removeAttribute("style");
  for (const name of ["data-theme", "data-theme-mode", "data-theme-appearance", "data-theme-layout", "data-nav-mode"]) document.documentElement.removeAttribute(name);
});

describe("theme management with an empty selectable default", () => {
  it("explains an empty DirectoryOnly selection while keeping colour-mode and navigation controls usable", async () => {
    const context = { http: { getJson: async () => ({ themes: [], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] }) } } as unknown as StudioEndpointContext;
    await update(() => root.render(<ThemeProvider storeContext={context}><AppearancePage onNavigate={() => {}} /></ThemeProvider>));

    expect(container.textContent).toContain("No themes are available for selection.");
    expect(container.textContent).not.toContain("Classic is the default");
    expect(container.querySelector('[role="radiogroup"][aria-label="Theme"]')).toBeNull();
    expect(container.querySelectorAll('[role="radiogroup"][aria-label="Colour mode"] [role="radio"]').length).toBeGreaterThan(0);
    expect(container.querySelectorAll('[role="radiogroup"][aria-label="Navigation"] [role="radio"]').length).toBeGreaterThan(0);
  });

  it("selects an existing management-inventory entry after deleting a custom theme when no theme is selectable", async () => {
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    let themes = [custom];
    const wire = () => ({ themes, themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] });
    const remove = vi.fn(async () => { themes = []; return wire(); });
    const context = { http: { getJson: async () => wire(), deleteJson: remove } } as unknown as StudioEndpointContext;
    const api = { host: context, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    await update(() => root.render(<ThemeProvider storeContext={context}><ThemeBuilderPage api={api} /></ThemeProvider>));
    const customButton = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-list button")).find(button => button.textContent?.includes("Managed Theme"));
    expect(customButton).toBeTruthy();
    await update(() => customButton!.click());
    expect(container.querySelector(".theme-builder-detail h3")?.textContent).toBe("Managed Theme");
    const deleteButton = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-actions button")).find(button => button.textContent?.trim() === "Delete");
    expect(deleteButton?.disabled).toBe(false);
    await update(() => deleteButton!.click());

    expect(remove).toHaveBeenCalledWith("/_elsa/theme-store/themes/managed-theme");
    expect(container.querySelector(".theme-builder-detail h3")?.textContent).toBe("Linen");
    expect(container.querySelectorAll(".theme-builder-list button.active")).toHaveLength(1);
    expect(container.textContent).not.toContain("Loading Theme Store definitions.");
    expect(container.textContent).toContain("Theme deleted.");
  });
});
