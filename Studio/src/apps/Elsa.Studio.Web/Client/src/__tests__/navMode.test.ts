// @vitest-environment node

import { describe, expect, it } from "vitest";
import { getThemeNavMode, isNavModePreference, navModePreferences, resolveNavMode } from "../app/themes/navMode";
import { builtInThemeDefinitions } from "../app/themes/presets";
import { studioThemeLayouts } from "../sdk/themeLayout";
import { read, ruleBody } from "./themeTestUtils";

const stylesCss = read("../app/styles.css");
const layoutsCss = read("../app/themes/layouts.css").replace(/\/\*[\s\S]*?\*\//g, "");
// The desktop media block closes at the first column-zero brace; the status bar rules follow it.
const desktopEnd = layoutsCss.indexOf("\n}\n");
const desktopCss = layoutsCss.slice(0, desktopEnd);
const everyWidthCss = layoutsCss.slice(desktopEnd);

const topNav = 'html[data-nav-mode="top"]';
const selectors = (css: string) =>
  [...css.matchAll(/html\[[^{]+(?=\{)/g)].flatMap(match => match[0].split(",").map(selector => selector.trim()));
const zIndex = (body: string) => Number(body.match(/z-index:\s*(\d+);/)?.[1]);

describe("navigation mode resolution", () => {
  it.each(studioThemeLayouts)("lets Left and Top override the %s layout, and Theme default follow it", layout => {
    expect(resolveNavMode(layout, "left")).toBe("left");
    expect(resolveNavMode(layout, "top")).toBe("top");
    expect(resolveNavMode(layout, "theme")).toBe(layout === "workbench" ? "top" : "left");
  });

  it("defaults every built-in theme to the sidebar except Schematic", () => {
    expect(builtInThemeDefinitions.filter(theme => getThemeNavMode(theme.layout) === "top").map(theme => theme.id)).toEqual(["schematic"]);
  });

  it("accepts only the three preferences from storage", () => {
    expect(navModePreferences.every(isNavModePreference)).toBe(true);
    expect([null, "", "bottom", "workbench"].some(isNavModePreference)).toBe(false);
  });
});

describe("navigation mode stylesheet", () => {
  it("keys the menubar and status bar on the navigation mode, never on a theme layout", () => {
    const menubarParts = /\.(sidebar-top|sidebar-search|sidebar-collapse-toggle|nav-children|nav-item-group|nav-icon-tile|studio-statusbar)\b/;

    expect(selectors(layoutsCss).filter(selector => selector.includes("data-theme-layout") && menubarParts.test(selector))).toEqual([]);
    expect(selectors(layoutsCss).some(selector => selector.startsWith('html[data-theme-layout="workbench"] .sidebar'))).toBe(false);
    expect(selectors(layoutsCss).filter(selector => selector.includes("data-theme-layout") && selector.includes("data-nav-mode"))).toEqual([]);
  });

  // A layout that insets its main frame states the inset once; the start side follows the navigation.
  it("keeps the editorial desk margin on the sheet's start side only when no sidebar provides it", () => {
    const editorial = 'html[data-theme-layout="editorial"]';

    expect(ruleBody(layoutsCss, `${editorial} .studio-shell`)).toMatch(/--shell-frame-inset:\s*10px;/);
    expect(ruleBody(layoutsCss, `${editorial} .main-frame`)).toMatch(
      /margin:\s*var\(--shell-frame-inset\) var\(--shell-frame-inset\) var\(--shell-frame-inset\) var\(--shell-frame-inset-start, 0\);/);
    expect(ruleBody(layoutsCss, `${topNav} .studio-shell`)).toMatch(/--shell-frame-inset-start:\s*var\(--shell-frame-inset, 0\);/);
  });

  it("confines the menubar to desktop widths and renders the status bar at every width", () => {
    const everyWidth = selectors(everyWidthCss);

    expect(selectors(desktopCss)).toContain(`${topNav} .sidebar`);
    expect(selectors(desktopCss).some(selector => selector.includes(".studio-statusbar"))).toBe(false);
    expect(everyWidth.length).toBeGreaterThan(0);
    expect(everyWidth.every(selector => selector.startsWith(`${topNav} .studio-statusbar`))).toBe(true);
  });

  // Floating and editorial restyle `.sidebar` at the same specificity, so source order decides.
  it.each(["floating", "editorial"])("places the menubar rules after the %s layout's sidebar rules", layout => {
    const layoutSidebar = desktopCss.lastIndexOf(`html[data-theme-layout="${layout}"] .sidebar`);

    expect(layoutSidebar).toBeGreaterThan(-1);
    expect(desktopCss.indexOf(`${topNav} .sidebar {`)).toBeGreaterThan(layoutSidebar);
  });

  it("lifts the menubar above the sticky topbar so dropdowns clear it, and lets a long navigation wrap", () => {
    const menubar = ruleBody(layoutsCss, `${topNav} .sidebar`);

    expect(menubar).toMatch(/position:\s*relative;/);
    expect(zIndex(menubar)).toBeGreaterThan(zIndex(ruleBody(stylesCss, ".topbar")));
    expect(menubar).toMatch(/flex-wrap:\s*wrap;/);
    expect(menubar).toMatch(/overflow:\s*visible;/);
  });
});
