import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { requestStudioNavigation, type ElsaStudioModuleApi, type StudioEndpointContext } from "../sdk";
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
  for (const name of ["data-theme", "data-theme-mode", "data-theme-appearance", "data-theme-layout", "data-nav-mode", "data-sidebar-color"]) document.documentElement.removeAttribute(name);
});

const notifications = { notify: vi.fn((_input: { level: string; title: string; message?: string }) => ({ id: "test", update() {}, dismiss() {} })), track: <T,>(work: Promise<T>) => work };
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

  it("names the top bar and keeps High contrast's own navigation on the sidebar color card", async () => {
    localStorage.setItem("elsa-studio-nav-mode", "top");
    localStorage.setItem("elsa-studio-theme-mode", "high-contrast");
    const context = { http: { getJson: async () => ({ themes: [], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] }) } } as unknown as StudioEndpointContext;
    await update(() => root.render(<ThemeProvider storeContext={context}><AppearancePage onNavigate={() => {}} /></ThemeProvider>));

    const options = Array.from(container.querySelectorAll<HTMLButtonElement>('[role="radiogroup"][aria-label="Navigation bar color"] [role="radio"]'));
    expect(container.textContent).toContain("Navigation bar color");
    expect(options.map(option => option.getAttribute("aria-label"))).toEqual(["Theme default", "Light", "Dark (default)"]);
    expect(options.every(option => option.disabled)).toBe(true);
  });

  it("selects an existing management-inventory entry after deleting a custom theme when no theme is selectable", async () => {
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    let themes = [custom];
    const wire = () => ({ themes, themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] });
    const remove = vi.fn(async () => { themes = []; return wire(); });
    const context = { http: { getJson: async () => wire(), deleteJson: remove } } as unknown as StudioEndpointContext;
    const api = { host: context, notifications, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
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
    expect(notifications.notify).toHaveBeenCalledWith({ level: "success", title: "Theme deleted." });
    expect(container.textContent).not.toContain("Theme deleted.");
  });

  it("reports an unsaved custom theme to workspace probes and closes but not to ordinary navigation", async () => {
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    const context = { http: { getJson: async () => ({ themes: [custom], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] }) } } as unknown as StudioEndpointContext;
    const api = { host: context, notifications, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    await update(() => root.render(<ThemeProvider storeContext={context}><ThemeBuilderPage api={api} /></ThemeProvider>));
    const customButton = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-list button")).find(button => button.textContent?.includes("Managed Theme"));
    await update(() => customButton!.click());
    expect(requestStudioNavigation("/theme-builder", "/theme-builder", "probe")).toBe(true);

    const field = Array.from(container.querySelectorAll<HTMLInputElement>(".theme-builder-detail input")).find(input => !input.disabled && input.type !== "file" && input.type !== "checkbox")!;
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    await update(() => { setValue.call(field, `${field.value}x`); field.dispatchEvent(new Event("input", { bubbles: true })); });

    expect(requestStudioNavigation("/theme-builder", "/theme-builder", "probe")).toBe(false);
    expect(requestStudioNavigation("/theme-builder", "/", "close")).toBe(false);
    expect(requestStudioNavigation("/theme-builder", "/")).toBe(true);
  });

  it("reports a Theme Store write in progress to workspace probes and closes until it finishes", async () => {
    const wire = { themes: [], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] };
    let release = () => {};
    const putJson = vi.fn((_url: string, theme: unknown) => new Promise(resolve => { release = () => resolve({ ...wire, themes: [theme] }); }));
    const context = { http: { getJson: async () => wire, putJson } } as unknown as StudioEndpointContext;
    const api = { host: context, notifications, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    await update(() => root.render(<ThemeProvider storeContext={context}><ThemeBuilderPage api={api} /></ThemeProvider>));
    const duplicate = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-actions button")).find(button => button.textContent?.trim() === "Duplicate")!;
    await update(() => duplicate.click());
    expect(putJson).toHaveBeenCalledTimes(1);
    expect(requestStudioNavigation("/theme-builder", "/theme-builder", "probe")).toBe(false);
    expect(requestStudioNavigation("/theme-builder", "/", "close")).toBe(false);

    await update(() => release());
    expect(notifications.notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", title: expect.stringContaining("Theme duplicated") }));
    expect(requestStudioNavigation("/theme-builder", "/theme-builder", "probe")).toBe(true);
  });

  it("keeps an unsaved theme edit, and loads once, when a background tab returns", async () => {
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    const wire = async () => ({ themes: [custom], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] });
    // The page gets its own context so only its Theme Store loads are counted, not the provider's.
    const getJson = vi.fn(wire);
    const context = { http: { getJson: wire } } as unknown as StudioEndpointContext;
    const api = { host: { http: { getJson } }, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    const page = (mode: "visible" | "hidden") => <ThemeProvider storeContext={context}><React.Activity mode={mode}><ThemeBuilderPage api={api} /></React.Activity></ThemeProvider>;
    // Hide the tab while the first load is still in flight.
    flushSync(() => root.render(page("visible")));
    flushSync(() => root.render(page("hidden")));
    await update(() => root.render(page("visible")));
    expect(getJson).toHaveBeenCalledTimes(1);
    const customButton = Array.from(container.querySelectorAll<HTMLButtonElement>(".theme-builder-list button")).find(button => button.textContent?.includes("Managed Theme"));
    await update(() => customButton!.click());

    const field = () => Array.from(container.querySelectorAll<HTMLInputElement>(".theme-builder-detail input")).find(input => !input.disabled && input.type !== "file" && input.type !== "checkbox")!;
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
    const edited = `${field().value}x`;
    await update(() => { setValue.call(field(), edited); field().dispatchEvent(new Event("input", { bubbles: true })); });

    await update(() => root.render(page("hidden")));
    await update(() => root.render(page("visible")));
    expect(field().value).toBe(edited);
    expect(requestStudioNavigation("/theme-builder", "/theme-builder", "probe")).toBe(false);
    expect(getJson).toHaveBeenCalledTimes(1);
  });

  it("shows a failed Theme Store load with Retry and retries when a background tab returns", async () => {
    let calls = 0;
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    const context = { http: { getJson: async () => { calls += 1; if (calls === 1) throw new Error("Theme Store unavailable"); return { themes: [custom], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] }; } } } as unknown as StudioEndpointContext;
    const api = { host: context, notifications, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    const page = (mode: "visible" | "hidden") => <ThemeProvider storeContext={context}><React.Activity mode={mode}><ThemeBuilderPage api={api} /></React.Activity></ThemeProvider>;
    await update(() => root.render(page("visible")));
    const failedLoads = calls;
    expect(container.textContent).toContain("Theme Store unavailable");
    expect(Array.from(container.querySelectorAll("button")).some(button => button.textContent?.includes("Retry"))).toBe(true);

    await update(() => root.render(page("hidden")));
    await update(() => root.render(page("visible")));
    expect(calls).toBeGreaterThan(failedLoads);
    expect(container.querySelector(".theme-builder-detail h3")?.textContent).toBeTruthy();
    const loaded = calls;
    await update(() => root.render(page("hidden")));
    await update(() => root.render(page("visible")));
    expect(calls).toBe(loaded);
  });

  it("does not repeat a Retry that is still loading when a background tab returns", async () => {
    const custom = { ...cloneThemeDefinition(builtInThemeDefinitions[0]), id: "managed-theme", name: "Managed Theme", source: "custom", enabled: true, published: true };
    const wire = { themes: [custom], themesMode: "DirectoryOnly", directoryThemeIds: [], defaultThemeId: "", assets: [] };
    let release = () => {};
    const getJson = vi.fn()
      .mockRejectedValueOnce(new Error("Theme Store unavailable"))
      .mockImplementationOnce(() => new Promise(resolve => { release = () => resolve(wire); }))
      .mockResolvedValue(wire);
    const context = { http: { getJson: async () => wire } } as unknown as StudioEndpointContext;
    const api = { host: { http: { getJson } }, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;
    const page = (mode: "visible" | "hidden") => <ThemeProvider storeContext={context}><React.Activity mode={mode}><ThemeBuilderPage api={api} /></React.Activity></ThemeProvider>;
    await update(() => root.render(page("visible")));
    const retry = Array.from(container.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent?.includes("Retry"))!;
    await update(() => retry.click());
    expect(getJson).toHaveBeenCalledTimes(2);

    await update(() => root.render(page("hidden")));
    await update(() => root.render(page("visible")));
    expect(getJson).toHaveBeenCalledTimes(2);
    await update(() => release());
    expect(container.querySelector(".theme-builder-detail h3")?.textContent).toBeTruthy();
  });
});
