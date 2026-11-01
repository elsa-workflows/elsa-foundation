import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ThemeProvider, useTheme } from "../app/components/ThemeProvider";
import { getTheme, getThemeModeDefinition } from "../app/themes/presets";
import { appearanceResetStorageKeys } from "../app/themes/appearance";
import { normalizeThemeStore, withoutOptionalMode } from "../app/themes/themeStoreApi";
import type { StudioEndpointContext } from "../sdk";

let container: HTMLDivElement;
let root: Root;
let appearance: ReturnType<typeof useTheme>;
const html = () => document.documentElement;

function Probe() {
  appearance = useTheme();
  return <input aria-label="Unsaved property" defaultValue="Original" />;
}

async function update(action: () => void) {
  flushSync(action);
  for (let index = 0; index < 3; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

async function render(context?: StudioEndpointContext) {
  await update(() => root.render(<ThemeProvider storeContext={context}><Probe /></ThemeProvider>));
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
  for (const name of Array.from(html().attributes).map(attribute => attribute.name)) {
    if (name.startsWith("data-theme") || name === "data-nav-mode" || name === "data-workspace-style" || name === "data-color-scheme" || name === "style") html().removeAttribute(name);
  }
});

describe("independent workspace appearance", () => {
  it("applies Sketchbook, preserves an unsaved edit across palette changes and restores the selection", async () => {
    await render();
    await update(() => appearance.setNavModePreference("top"));
    await update(() => appearance.applyPreset("builtin:sketchbook"));
    expect(html().getAttribute("data-theme")).toBe("builtin:sketchbook");
    expect(html().getAttribute("data-workspace-style")).toBe("sketchbook");
    expect(html().getAttribute("data-theme-appearance")).toBe("light");
    const input = container.querySelector("input")!;
    input.value = "Keep this workflow edit";
    const display = html().style.getPropertyValue("--font-display");
    await update(() => appearance.setColorScheme("drift"));
    await update(() => appearance.setModePreference("dim"));
    expect(input.value).toBe("Keep this workflow edit");
    expect(html().style.getPropertyValue("--font-display")).toBe(display);
    expect(html().getAttribute("data-workspace-style")).toBe("sketchbook");
    await update(() => root.render(<div />));
    await render();
    expect(html().getAttribute("data-workspace-style")).toBe("sketchbook");
    expect(html().getAttribute("data-color-scheme")).toBe("drift");
    expect(html().getAttribute("data-theme-appearance")).toBe("dim");
    expect(html().getAttribute("data-nav-mode")).toBe("top");
  });

  it.each(["builtin:porcelain", "builtin:atelier", "builtin:graphite"] as const)("applies %s as an explicit complete preset and restores it after remount", async id => {
    await render();
    await update(() => appearance.setNavModePreference("top"));
    await update(() => appearance.applyPreset(id));
    const mode = id === "builtin:graphite" ? "dark" : "light";
    expect(html().getAttribute("data-theme")).toBe(id);
    expect(html().getAttribute("data-color-scheme")).toBe(id);
    expect(html().getAttribute("data-workspace-style")).toBe("minimal");
    expect(html().getAttribute("data-theme-appearance")).toBe(mode);
    expect(html().getAttribute("data-nav-mode")).toBe("top");
    expect(html().style.getPropertyValue("--studio-nav-bg")).toBe(getThemeModeDefinition(getTheme(id)!, mode)!.sidebar);
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe("@theme");
    await update(() => root.render(<div />));
    await render();
    expect(html().getAttribute("data-theme")).toBe(id);
    expect(html().getAttribute("data-workspace-style")).toBe("minimal");
    expect(html().getAttribute("data-theme-appearance")).toBe(mode);
  });

  it("changes the palette without changing a minimal preset's mode, geometry or edited input", async () => {
    await render();
    await update(() => appearance.applyPreset("builtin:porcelain"));
    const input = container.querySelector("input")!;
    input.value = "Keep this edit";
    const radius = html().style.getPropertyValue("--radius");
    await update(() => appearance.setColorScheme("builtin:graphite"));
    expect(html().getAttribute("data-theme-appearance")).toBe("light");
    expect(html().getAttribute("data-theme")).toBe("builtin:porcelain");
    expect(html().getAttribute("data-color-scheme")).toBe("builtin:graphite");
    expect(html().style.getPropertyValue("--radius")).toBe(radius);
    expect(input.value).toBe("Keep this edit");
  });

  it("previews a custom minimal treatment without persisting or overriding its editable theme defaults", async () => {
    await render();
    const before = { ...localStorage };
    await update(() => appearance.previewTheme({ ...getTheme("builtin:porcelain")!, id: "custom-minimal", source: "custom", layout: "floating", shape: { radius: "18px" }, typography: { sans: "Custom Sans" } }));
    expect(html().getAttribute("data-workspace-style")).toBe("minimal");
    expect(html().getAttribute("data-theme-layout")).toBe("floating");
    expect(html().style.getPropertyValue("--radius")).toBe("18px");
    expect(html().style.getPropertyValue("--font-sans")).toBe("Custom Sans");
    expect({ ...localStorage }).toEqual(before);
    await update(() => appearance.setTheme("atelier"));
    expect(html().hasAttribute("data-workspace-style")).toBe(false);
    expect(html().getAttribute("data-theme-layout")).toBe("editorial");
  });

  it("does not apply a hidden complete preset", async () => {
    const context = { http: { getJson: async () => normalizeThemeStore({ disabledBuiltInThemeIds: ["builtin:graphite"] }) } } as unknown as StudioEndpointContext;
    await render(context);
    await vi.waitFor(() => expect(appearance.store.disabledBuiltInThemeIds).toContain("builtin:graphite"));
    const before = { ...localStorage };
    await update(() => appearance.applyPreset("builtin:graphite"));
    expect(appearance.currentTheme.id).toBe("linen");
    expect({ ...localStorage }).toEqual(before);
  });

  it("keeps a host palette default unpersisted so a later host default can apply", async () => {
    window.__ELSA_STUDIO_RUNTIME__ = { appearance: { paletteId: "drift" } };
    const context = { http: { getJson: async () => normalizeThemeStore({ defaultThemeId: "meridian" }) } } as unknown as StudioEndpointContext;
    try {
      await render(context);
      await vi.waitFor(() => expect(appearance.store.defaultThemeId).toBe("meridian"));
      await vi.waitFor(() => expect(html().getAttribute("data-color-scheme")).toBe("drift"));
      expect(localStorage.getItem("elsa-studio-color-scheme")).toBeNull();
      window.__ELSA_STUDIO_RUNTIME__ = { appearance: { paletteId: "signal" } };
      await update(() => root.render(<div />));
      await render(context);
      await vi.waitFor(() => expect(appearance.store.defaultThemeId).toBe("meridian"));
      await vi.waitFor(() => expect(html().getAttribute("data-color-scheme")).toBe("signal"));
      expect(localStorage.getItem("elsa-studio-color-scheme")).toBeNull();
    } finally { delete window.__ELSA_STUDIO_RUNTIME__; }
  });

  it("uses another palette while retaining Drift's layout, typography and preset identity", async () => {
    localStorage.setItem("elsa-studio-theme", "drift");
    await render();
    const font = html().style.getPropertyValue("--font-sans");
    await update(() => appearance.setColorScheme("meridian"));

    expect(html().getAttribute("data-theme")).toBe("drift");
    expect(html().getAttribute("data-color-scheme")).toBe("meridian");
    expect(html().getAttribute("data-theme-layout")).toBe("floating");
    expect(html().style.getPropertyValue("--font-sans")).toBe(font);
    expect(html().style.getPropertyValue("--primary")).toBe(getThemeModeDefinition(getTheme("meridian")!, appearance.mode)!.primary);
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe("meridian");
  });

  it("preserves palette, requested mode and explicit navigation through style changes", async () => {
    await render();
    await update(() => {
      appearance.setColorScheme("signal");
      appearance.setMode("dim");
      appearance.setNavModePreference("top");
    });
    for (const style of ["quiet", "focus", "open"] as const) {
      await update(() => appearance.setWorkspaceStyle(style));
      expect(html().getAttribute("data-workspace-style")).toBe(style);
      expect(html().getAttribute("data-color-scheme")).toBe("signal");
      expect(html().getAttribute("data-theme-appearance")).toBe("dim");
      expect(html().getAttribute("data-nav-mode")).toBe("top");
    }
    expect(localStorage.getItem("elsa-studio-workspace-style")).toBe("open");
    expect(localStorage.getItem("elsa-studio-theme")).toBeNull();
  });

  it("restores independent overrides and ignores an invalid workspace style", async () => {
    localStorage.setItem("elsa-studio-theme", "drift");
    localStorage.setItem("elsa-studio-workspace-style", "unknown-style");
    localStorage.setItem("elsa-studio-color-scheme", "classic");
    await render();
    expect(html().hasAttribute("data-workspace-style")).toBe(false);
    expect(html().getAttribute("data-theme-layout")).toBe("floating");
    expect(html().getAttribute("data-color-scheme")).toBe("classic");
  });

  it("keeps the same edited input and its focus during live appearance changes", async () => {
    await render();
    const input = container.querySelector("input")!;
    input.value = "Unsaved draft";
    input.focus();
    input.setSelectionRange(2, 5);
    for (const style of ["quiet", "focus", "open"] as const) {
      await update(() => { appearance.setWorkspaceStyle(style); appearance.setColorScheme("dusk"); });
      expect(container.querySelector("input")).toBe(input);
      expect(input.value).toBe("Unsaved draft");
      expect(document.activeElement).toBe(input);
      expect([input.selectionStart, input.selectionEnd]).toEqual([2, 5]);
    }
  });

  it("protects and restores a delayed custom color scheme identity", async () => {
    const custom = { ...getTheme("meridian")!, id: "custom-ocean", name: "Custom Ocean", source: "custom" as const };
    let resolveStore!: (value: unknown) => void;
    const pending = new Promise(resolve => { resolveStore = resolve; });
    localStorage.setItem("elsa-studio-color-scheme", custom.id);
    const context = { http: { getJson: () => pending } } as unknown as StudioEndpointContext;
    await render(context);
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe(custom.id);
    await update(() => resolveStore({ ...normalizeThemeStore(), themes: [custom] }));
    expect(html().getAttribute("data-color-scheme")).toBe(custom.id);
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe(custom.id);
  });

  it("does not overwrite an unresolved custom scheme when its store fails", async () => {
    localStorage.setItem("elsa-studio-color-scheme", "custom-offline");
    await render({ http: { getJson: async () => { throw new Error("offline"); } } } as unknown as StudioEndpointContext);
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe("custom-offline");
    expect(html().getAttribute("data-color-scheme")).toBe("linen");
  });

  it("clears a definitively removed palette so the base theme supplies its colors", async () => {
    localStorage.setItem("elsa-studio-theme", "drift");
    localStorage.setItem("elsa-studio-color-scheme", "custom-removed");
    await render({ http: { getJson: async () => normalizeThemeStore() } } as unknown as StudioEndpointContext);
    // Store reconciliation writes preferences before the follow-up render applies DOM colors.
    await vi.waitFor(() => {
      expect(localStorage.getItem("elsa-studio-color-scheme")).toBe("@theme");
      expect(html().getAttribute("data-theme")).toBe("drift");
      expect(html().getAttribute("data-color-scheme")).toBe("drift");
    });
  });

  it("previews a full preset transiently without rewriting independent preferences", async () => {
    await render();
    await update(() => { appearance.setWorkspaceStyle("focus"); appearance.setColorScheme("meridian"); });
    await update(() => appearance.previewTheme(getTheme("atelier")!));
    expect(html().getAttribute("data-theme-layout")).toBe("editorial");
    expect(html().getAttribute("data-color-scheme")).toBe("atelier");
    expect(html().hasAttribute("data-workspace-style")).toBe(false);
    expect(localStorage.getItem("elsa-studio-workspace-style")).toBe("focus");
    expect(localStorage.getItem("elsa-studio-color-scheme")).toBe("meridian");
    await update(() => appearance.resetAppearance());
    expect(html().getAttribute("data-theme")).toBe("linen");
    expect(appearanceResetStorageKeys.every(key => localStorage.getItem(key) === null)).toBe(true);
  });

  it("retains the requested optional mode across a palette that lacks it", async () => {
    const base = getTheme("meridian")!;
    const twoMode = { ...withoutOptionalMode(withoutOptionalMode(base, "dim"), "high-contrast"), id: "two-mode", name: "Two mode", source: "custom" as const };
    await render({ http: { getJson: async () => ({ ...normalizeThemeStore(), themes: [twoMode] }) } } as unknown as StudioEndpointContext);
    await update(() => { appearance.setMode("dim"); appearance.setColorScheme("two-mode"); });
    expect(appearance.mode).toBe("dark");
    expect(appearance.preferredMode).toBe("dim");
    await update(() => appearance.setColorScheme("classic"));
    expect(appearance.mode).toBe("dim");
  });
});
