import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  allThemeModes,
  builtInThemeDefinitions,
  getSupportedThemeModes,
  getTheme,
  getThemeColorScheme,
  getThemeModeDefinition,
  resolveThemeMode,
  type ThemeMode,
  type ThemeModeDefinition
} from "../app/themes/presets";
import { foundationThemeIds } from "../app/themes/foundationThemes";
import {
  createCustomThemeFrom,
  normalizeThemeStore,
  validateThemeDefinition,
  withOptionalMode,
  withoutOptionalMode,
  withStyleField
} from "../app/themes/themeStoreApi";
import { ThemeProvider, useTheme } from "../app/components/ThemeProvider";
import { ThemeSwitcher } from "../app/components/ThemeSwitcher";
import { ShellFrame } from "../app/App";
import { getStudioThemeLayout } from "../sdk";

/** WCAG 2 contrast ratio between two `oklch(L C H)` colours, via OKLab → linear sRGB. */
function contrast(first: string, second: string) {
  const [a, b] = [luminance(first), luminance(second)].sort((x, y) => y - x);
  return (a + 0.05) / (b + 0.05);
}

function luminance(color: string) {
  const match = color.match(/^oklch\(([\d.]+) ([\d.]+) ([\d.]+)\)$/);
  if (!match) throw new Error(`Expected an oklch() colour, received ${color}`);
  const [lightness, chroma, hue] = match.slice(1).map(Number);
  const radians = (hue * Math.PI) / 180;
  const okA = chroma * Math.cos(radians);
  const okB = chroma * Math.sin(radians);
  const l = (lightness + 0.3963377774 * okA + 0.2158037573 * okB) ** 3;
  const m = (lightness - 0.1055613458 * okA - 0.0638541728 * okB) ** 3;
  const s = (lightness - 0.0894841775 * okA - 1.291485548 * okB) ** 3;
  const clamp = (value: number) => Math.min(1, Math.max(0, value));
  const red = clamp(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s);
  const green = clamp(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s);
  const blue = clamp(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s);
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

const signatureModes = foundationThemeIds.flatMap(id =>
  allThemeModes.map(mode => [id, mode, getThemeModeDefinition(getTheme(id)!, mode)!] as const));

describe("signature themes", () => {
  it("lead the built-in list with Meridian as the out-of-box default", () => {
    expect(builtInThemeDefinitions.slice(0, foundationThemeIds.length).map(theme => theme.id)).toEqual([...foundationThemeIds]);
    expect(foundationThemeIds[0]).toBe("meridian");
    expect(normalizeThemeStore().defaultThemeId).toBe("meridian");
  });

  it.each(foundationThemeIds)("%s defines all four modes, its typography and shape, and validates", id => {
    const theme = getTheme(id)!;

    expect(getSupportedThemeModes(theme)).toEqual(["light", "dark", "dim", "high-contrast"]);
    expect(theme.typography?.sans).toBeTruthy();
    expect(theme.typography?.mono).toBeTruthy();
    expect(theme.shape?.radius).toMatch(/px$/);
    expect(validateThemeDefinition(theme).issues.filter(issue => issue.severity === "error")).toEqual([]);
  });

  // Body text needs 4.5:1 (WCAG AA); High contrast holds itself to 7:1 (AAA) throughout.
  it.each(signatureModes)("%s %s meets its contrast floor", (_id, mode, colors) => {
    const floor = mode === "high-contrast" ? 7 : 4.5;
    const pairs: [keyof ThemeModeDefinition, keyof ThemeModeDefinition][] = [
      ["foreground", "background"],
      ["cardForeground", "card"],
      ["mutedForeground", "card"],
      ["mutedForeground", "background"],
      ["mutedForeground", "muted"],
      ["primaryForeground", "primary"],
      ["sidebarForeground", "sidebar"],
      ["sidebarActiveForeground", "sidebarActive"],
      ["successForeground", "success"],
      ["warningForeground", "warning"],
      ["dangerForeground", "danger"]
    ];

    for (const [text, surface] of pairs) {
      expect(contrast(colors[text] as string, colors[surface] as string), `${text} on ${surface}`).toBeGreaterThanOrEqual(floor);
    }
  });

  it("gives each signature theme its layout, and every other built-in the classic one", () => {
    expect(foundationThemeIds.map(id => getTheme(id)!.layout)).toEqual(["classic", "floating", "workbench", "editorial", "classic", "classic", "classic"]);
    expect(builtInThemeDefinitions.slice(foundationThemeIds.length).every(theme => theme.layout === undefined)).toBe(true);
  });

  it.each(foundationThemeIds)("%s high contrast is black-grounded with a bright accent", id => {
    const colors = getThemeModeDefinition(getTheme(id)!, "high-contrast")!;

    expect(colors.background).toBe("oklch(0 0 0)");
    expect(contrast(colors.foreground, colors.background)).toBeGreaterThanOrEqual(18);
    expect(contrast(colors.primary, colors.background)).toBeGreaterThanOrEqual(10);
    expect(contrast(colors.border, colors.background)).toBeGreaterThanOrEqual(7);
  });
});

describe("theme modes", () => {
  it("maps dim and high contrast onto the dark colour scheme", () => {
    expect(allThemeModes.map(getThemeColorScheme)).toEqual(["light", "dark", "dark", "dark"]);
  });

  it("offers only light and dark for themes that define no optional modes", () => {
    expect(getSupportedThemeModes(getTheme("stone")!)).toEqual(["light", "dark"]);
  });

  it.each<[string, ThemeMode, ThemeMode]>([
    ["stone", "dim", "dark"],
    ["stone", "high-contrast", "dark"],
    ["brass-instrument", "high-contrast", "dark"],
    ["brass-instrument", "light", "dark"],
    ["meridian", "high-contrast", "high-contrast"]
  ])("resolves %s with a %s preference to %s, staying in the same scheme when it can", (id, preferred, expected) => {
    expect(resolveThemeMode(getTheme(id)!, preferred)).toBe(expected);
  });

  it("carries optional modes, typography and shape into a custom duplicate", () => {
    const copy = createCustomThemeFrom(getTheme("atelier")!, "atelier-copy", "Atelier Copy");

    expect(copy.modes.highContrast).toEqual(getTheme("atelier")!.modes.highContrast);
    expect(copy.typography?.display).toContain("Instrument Serif");
    expect(copy.shape?.radius).toBe("9px");
    expect(getSupportedThemeModes(copy)).toEqual(["light", "dark", "dim", "high-contrast"]);
  });
});

describe("Theme Builder edits", () => {
  const custom = () => createCustomThemeFrom(getTheme("stone")!, "stone-custom", "Stone Custom");

  it("adds an optional mode seeded from the dark palette and offers it", () => {
    const theme = withOptionalMode(custom(), "high-contrast");

    expect(theme.modes.highContrast).toEqual(theme.modes.dark);
    expect(theme.modes.highContrast).not.toBe(theme.modes.dark);
    expect(getSupportedThemeModes(theme)).toEqual(["light", "dark", "high-contrast"]);
    expect(validateThemeDefinition(theme).valid).toBe(true);
  });

  it("removes an optional mode and stops offering it, leaving a valid theme", () => {
    const theme = withoutOptionalMode(withOptionalMode(withOptionalMode(custom(), "dim"), "high-contrast"), "dim");

    expect(theme.modes.dim).toBeUndefined();
    expect(getSupportedThemeModes(theme)).toEqual(["light", "dark", "high-contrast"]);
    expect(validateThemeDefinition(theme).valid).toBe(true);
  });

  it("sets typography and shape fields, and drops a section once it is emptied", () => {
    const withFont = withStyleField(custom(), "typography", "sans", "\"Manrope Variable\", sans-serif");

    expect(withFont.typography).toEqual({ sans: "\"Manrope Variable\", sans-serif" });
    expect(withStyleField(withFont, "typography", "sans", "  ").typography).toBeUndefined();
  });
});

describe("theme validation of appearance", () => {
  const base = () => createCustomThemeFrom(getTheme("meridian")!, "custom-appearance", "Custom Appearance");
  const errorsAt = (theme: ReturnType<typeof base>, path: string) =>
    validateThemeDefinition(theme).issues.filter(issue => issue.severity === "error" && issue.path.startsWith(path));

  it("rejects a supported mode the theme does not define", () => {
    const theme = base();
    delete theme.modes.dim;

    expect(errorsAt(theme, "supportedModes")).not.toEqual([]);
  });

  it("validates optional mode palettes like the required ones", () => {
    const theme = base();
    theme.modes.highContrast!.background = "url(https://evil.test/x.png)";

    expect(errorsAt(theme, "modes.highContrast.background")).not.toEqual([]);
  });

  it.each([
    ["typography.sans", (theme: ReturnType<typeof base>) => { theme.typography = { sans: "x; background: red" }; }],
    ["typography.display", (theme: ReturnType<typeof base>) => { theme.typography = { display: "url(https://evil.test/font.woff2)" }; }],
    ["shape.radius", (theme: ReturnType<typeof base>) => { theme.shape = { radius: "calc(100vw)" }; }],
    ["layout", (theme: ReturnType<typeof base>) => { (theme as { layout?: string }).layout = "sidebar-on-the-right"; }]
  ])("rejects unsafe %s", (path, mutate) => {
    const theme = base();
    mutate(theme);

    expect(errorsAt(theme, path)).not.toEqual([]);
  });
});

describe("ThemeProvider and ThemeSwitcher", () => {
  let container: HTMLDivElement;
  let root: Root;
  let api: ReturnType<typeof useTheme> | undefined;

  function Probe() {
    api = useTheme();
    return null;
  }

  /**
   * Runs an update synchronously, then lets the provider's effects settle. Mounting takes two
   * passes (read stored preferences, then apply them to <html>), so yield a few macrotasks.
   */
  async function act(update: () => void) {
    flushSync(update);
    for (let tick = 0; tick < 3; tick++) {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }

  async function render() {
    await act(() => root.render(
      <ThemeProvider>
        <Probe />
        <ThemeSwitcher />
        <ShellFrame navigation={[]} panels={[]} path="/" title="Dashboard" backendBaseUrl="https://backend.example/" onNavigate={() => {}}>
          <div />
        </ShellFrame>
      </ThemeProvider>
    ));
  }

  /** Opens the theme menu from the keyboard, as Radix does for Enter on its trigger. */
  async function openThemeMenu() {
    const trigger = container.querySelector<HTMLButtonElement>('button[aria-label="Select theme"]')!;
    await act(() => trigger.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
  }

  const modeItems = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));

  beforeEach(() => {
    localStorage.clear();
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(() => root.unmount());
    container.remove();
    document.body.removeAttribute("style");
    for (const attribute of ["data-theme", "data-theme-mode", "data-theme-appearance", "data-theme-layout", "style"]) {
      document.documentElement.removeAttribute(attribute);
    }
    api = undefined;
  });

  it("renders Meridian by default and applies its typography and shape", async () => {
    await render();
    const html = document.documentElement;

    expect(html.getAttribute("data-theme")).toBe("meridian");
    expect(html.style.getPropertyValue("--font-sans")).toContain("Geist Variable");
    expect(html.style.getPropertyValue("--radius")).toBe("7px");
  });

  it("keeps data-theme-mode as the colour scheme and exposes the exact mode as data-theme-appearance", async () => {
    await render();
    await act(() => api!.setMode("high-contrast"));
    const html = document.documentElement;

    expect(html.getAttribute("data-theme-mode")).toBe("dark");
    expect(html.getAttribute("data-theme-appearance")).toBe("high-contrast");
    expect(html.style.getPropertyValue("--background")).toBe("oklch(0 0 0)");
    expect(localStorage.getItem("elsa-studio-theme-mode")).toBe("high-contrast");
  });

  it("remembers the preferred mode across a theme that lacks it", async () => {
    await render();
    await act(() => api!.setMode("dim"));
    await act(() => api!.setTheme("stone"));

    expect(api!.mode).toBe("dark");
    expect(api!.preferredMode).toBe("dim");
    // Stone sets no typography, so Meridian's stack must not linger on <html>.
    expect(document.documentElement.style.getPropertyValue("--font-sans")).toBe("");

    await act(() => api!.setTheme("drift"));

    expect(api!.mode).toBe("dim");
    expect(document.documentElement.getAttribute("data-theme-appearance")).toBe("dim");
  });

  it("offers all four modes in the picker and applies the one picked, keeping the menu open", async () => {
    await render();
    await openThemeMenu();

    expect(modeItems().map(item => item.textContent)).toEqual(["Light", "Dark", "Dim", "High contrast"]);
    expect(modeItems().some(item => item.hasAttribute("data-disabled"))).toBe(false);

    await act(() => modeItems()[3].click());

    expect(document.documentElement.getAttribute("data-theme-appearance")).toBe("high-contrast");
    expect(modeItems()[3].getAttribute("aria-checked")).toBe("true");
  });

  it("disables the modes the current theme does not define", async () => {
    await render();
    await act(() => api!.setTheme("stone"));
    await openThemeMenu();
    const [, , dim, highContrast] = modeItems();

    expect(dim.hasAttribute("data-disabled")).toBe(true);
    expect(highContrast.hasAttribute("data-disabled")).toBe(true);

    await act(() => dim.click());

    expect(api!.preferredMode).not.toBe("dim");
  });

  it("publishes the theme's layout on <html>, falling back to classic", async () => {
    await render();
    expect(getStudioThemeLayout()).toBe("classic");

    await act(() => api!.setTheme("atelier"));
    expect(document.documentElement.getAttribute("data-theme-layout")).toBe("editorial");

    await act(() => api!.setTheme("stone"));
    expect(getStudioThemeLayout()).toBe("classic");
  });

  it("gives the workbench layout a status bar and ignores the icon-rail collapse", async () => {
    localStorage.setItem("elsa-studio-sidebar-collapsed", "true");
    await render();
    const shell = () => container.querySelector(".studio-shell")!;

    expect(container.querySelector(".studio-statusbar")).toBeNull();

    await act(() => api!.setTheme("schematic"));

    expect(container.querySelector(".studio-statusbar")?.textContent).toContain("backend.example");
    expect(container.querySelector(".studio-statusbar")?.textContent).toContain("Studio / Dashboard");
    expect(shell().classList.contains("sidebar-collapsed")).toBe(false);
  });
});
