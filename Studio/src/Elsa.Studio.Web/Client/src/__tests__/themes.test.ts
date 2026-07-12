import { describe, expect, it } from "vitest";
import type { StudioEndpointContext } from "../sdk";
import { builtInThemeDefinitions, getSupportedThemeModes, getTheme, getThemeNames, isMaterialTheme, materialThemeIds, resolveThemeMode, supportsThemeMode } from "../app/themes/presets";
import type { ThemeMaterialMode } from "../app/themes/presets";
import { applyMaterialVariables } from "../app/components/ThemeProvider";
import { createCustomThemeFrom, findSelectableTheme, normalizeThemeStore, saveTheme, validateThemeDefinition } from "../app/themes/themeStoreApi";

describe("theme presets", () => {
  it("includes the hot pink theme", () => {
    expect(getTheme("hot-pink")?.name).toBe("Hot Pink");
    expect(getThemeNames()).toContainEqual({ id: "hot-pink", name: "Hot Pink" });
  });

  it("represents material themes as read-only built-in definitions", () => {
    expect(getThemeNames()).toEqual(expect.arrayContaining([
      { id: "black-glass", name: "Black Glass" },
      { id: "stone", name: "Stone" },
      { id: "paper", name: "Paper" },
      { id: "blueprint", name: "Blueprint" },
      { id: "ceramic", name: "Ceramic" },
      { id: "carbon", name: "Carbon" },
      { id: "brass-instrument", name: "Brass Instrument" },
      { id: "walnut-workshop", name: "Walnut Workshop" }
    ]));
    expect(getTheme("black-glass")?.source).toBe("built-in");
    expect(getTheme("black-glass")?.modes.dark.material?.cssVariables?.["--studio-material-finish"]).toBe("glass");
    expect(materialThemeIds).toEqual(["stone", "paper", "blueprint", "ceramic", "carbon", "brass-instrument", "walnut-workshop"]);
    expect(isMaterialTheme("stone")).toBe(true);
    expect(isMaterialTheme("black-glass")).toBe(false);
  });

  it("keeps material theme modes visually distinct", () => {
    for (const themeId of materialThemeIds) {
      const theme = getTheme(themeId);
      if (!theme || !supportsThemeMode(theme, "dark")) {
        continue;
      }

      expect(theme?.light.background, themeId).not.toBe(theme?.dark.background);
      expect(theme?.light.card, themeId).not.toBe(theme?.dark.card);
      expect(theme?.light.foreground, themeId).not.toBe(theme?.dark.foreground);
      expect(theme?.modes.light.material?.textureAssets?.surface, themeId).not.toBe(theme?.modes.dark.material?.textureAssets?.surface);
    }

    const blackGlass = getTheme("black-glass");

    expect(blackGlass?.light.background).not.toBe(blackGlass?.dark.background);
    expect(blackGlass?.light.foreground).not.toBe(blackGlass?.dark.foreground);
  });

  it("treats Paper as a light-only material theme", () => {
    const paper = getTheme("paper");

    expect(paper).toBeDefined();
    expect(getSupportedThemeModes(paper!)).toEqual(["light"]);
    expect(supportsThemeMode(paper!, "dark")).toBe(false);
    expect(resolveThemeMode(paper!, "dark")).toBe("light");
  });

  it("treats Brass Instrument as a dark-only material theme", () => {
    const brass = getTheme("brass-instrument");

    expect(brass).toBeDefined();
    expect(getSupportedThemeModes(brass!)).toEqual(["dark"]);
    expect(supportsThemeMode(brass!, "light")).toBe(false);
    expect(resolveThemeMode(brass!, "light")).toBe("dark");
  });

  it("treats Walnut Workshop as a light-only material theme", () => {
    const walnut = getTheme("walnut-workshop");

    expect(walnut).toBeDefined();
    expect(getSupportedThemeModes(walnut!)).toEqual(["light"]);
    expect(walnut?.modes.light.material?.textureAssets?.surface).toBe("/studio/assets/walnut-workshop-tile.png");
    expect(walnut?.modes.light.material?.cssVariables?.["--studio-material-finish"]).toBe("walnut-inlay");
    expect(walnut?.modes.light.material?.cssVariables?.["--studio-material-depth"]).toBe("0.32");
    expect(walnut?.modes.dark.background).not.toBe(walnut?.modes.light.background);
    expect(resolveThemeMode(walnut!, "dark")).toBe("light");
  });

  it("duplicates built-ins into custom draft themes", () => {
    const copy = createCustomThemeFrom(builtInThemeDefinitions[0], "black-glass-copy", "Black Glass Copy");

    expect(copy.source).toBe("custom");
    expect(copy.published).toBe(false);
    expect(copy.enabled).toBe(false);
    expect(copy.modes.dark.primary).toBe(builtInThemeDefinitions[0].modes.dark.primary);
  });

  it("only exposes published and enabled themes as selectable", () => {
    const custom = createCustomThemeFrom(builtInThemeDefinitions[0], "custom-theme", "Custom Theme");
    const store = normalizeThemeStore({
      defaultThemeId: "custom-theme",
      themes: [{ ...custom, published: true, enabled: false }]
    });

    expect(findSelectableTheme(store, "custom-theme").id).toBe(builtInThemeDefinitions[0].id);
  });

  it("rejects arbitrary css token values", () => {
    const theme = createCustomThemeFrom(builtInThemeDefinitions[0], "unsafe-theme", "Unsafe Theme");
    theme.modes.light.background = "url(https://example.test/texture.png)";

    expect(validateThemeDefinition(theme).valid).toBe(false);
    expect(validateThemeDefinition(theme).issues.some(issue => issue.message.includes("arbitrary CSS"))).toBe(true);
  });

  it("normalizes store-shaped mutation responses with built-in themes", async () => {
    const custom = createCustomThemeFrom(builtInThemeDefinitions[0], "custom-theme", "Custom Theme");
    const context = {
      http: {
        putJson: async () => ({ themes: [custom], defaultThemeId: "black-glass", assets: [] })
      }
    };

    const store = await saveTheme(context as unknown as StudioEndpointContext, custom);

    expect(store.themes.some(theme => theme.id === "black-glass")).toBe(true);
    expect(store.themes.some(theme => theme.id === "custom-theme")).toBe(true);
    expect(store.defaultThemeId).toBe("black-glass");
  });
});

describe("applyMaterialVariables", () => {
  function apply(material: ThemeMaterialMode | undefined) {
    const root = document.createElement("html");
    applyMaterialVariables(root, material);
    return root;
  }

  it("drives the CSS --studio-material-texture the recipes actually read", () => {
    // The recipes in tokens.css read a single `--studio-material-texture`; the preset's
    // primary (`surface`) asset must reach it, not just the per-name alias.
    const root = apply({
      textureAssets: { surface: "/studio/assets/stone-slate-tile.png" },
      textureSize: 390
    });

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/stone-slate-tile.png")');
    expect(root.style.getPropertyValue("--studio-material-texture-size")).toBe("390px 390px");
    // Legacy per-name alias remains for backwards compatibility.
    expect(root.style.getPropertyValue("--studio-material-surface-texture")).toBe('url("/studio/assets/stone-slate-tile.png")');
  });

  it("delivers a custom theme's texture to the CSS variable end to end", () => {
    const custom = createCustomThemeFrom(builtInThemeDefinitions[0], "custom-texture", "Custom Texture");
    custom.modes.light.material = {
      textureAssets: { surface: "/studio/assets/paper-vellum-tile.png" },
      textureSize: 256,
      cssVariables: { "--studio-material-finish": "vellum" }
    };

    const root = apply(custom.modes.light.material);

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/paper-vellum-tile.png")');
    expect(root.style.getPropertyValue("--studio-material-texture-size")).toBe("256px 256px");
    expect(root.style.getPropertyValue("--studio-material-finish")).toBe("vellum");
  });

  it("falls back to the first texture asset when no surface key is present", () => {
    const root = apply({ textureAssets: { grain: "/studio/assets/carbon-weave-tile.png" } });

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/carbon-weave-tile.png")');
  });

  it("escapes quotes and backslashes in texture urls", () => {
    const root = apply({ textureAssets: { surface: '/studio/assets/a"b\\c.png' } });

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/a\\"b\\\\c.png")');
  });

  it("only applies --studio-material-* cssVariables (allowlist safety)", () => {
    const root = apply({
      cssVariables: {
        "--studio-material-depth": "0.5",
        "--evil-var": "red"
      }
    });

    expect(root.style.getPropertyValue("--studio-material-depth")).toBe("0.5");
    expect(root.style.getPropertyValue("--evil-var")).toBe("");
  });
});

describe("material cssVariables validation", () => {
  function issuesFor(cssVariables: Record<string, string>) {
    const theme = createCustomThemeFrom(builtInThemeDefinitions[0], "material-value-theme", "Material Value Theme");
    theme.modes.light.material = { cssVariables };
    return validateThemeDefinition(theme).issues.filter(issue => issue.path.includes("material.cssVariables"));
  }

  it.each([
    ["a bare finish keyword", "frosted"],
    ["a numeric depth", "0.72"],
    ["a length pair", "390px 390px"],
    ["a color-mix function", "color-mix(in srgb, #65d8ff 16%, transparent)"],
    ["an oklch color", "oklch(0.78 0.13 230)"],
    ["a gradient stack", "radial-gradient(circle at 72% 18%, #1b79ff 13%, transparent 28rem), linear-gradient(135deg, #07111b 0%, #03070c 58%)"],
    ["a same-origin absolute url", "url(/studio/assets/stone-slate-tile.png)"],
    ["a quoted same-origin url", 'url("/studio/assets/stone-slate-tile.png")'],
    ["a bundler-relative url", "url(../../assets/materials/stone-slate-tile.png)"]
  ])("accepts %s", (_label, value) => {
    expect(issuesFor({ "--studio-material-surface": value })).toEqual([]);
  });

  it.each([
    ["a remote http url", "url(https://evil.test/x.png)"],
    ["a protocol-relative url", "url(//evil.test/x.png)"],
    ["a data uri", "url(data:image/png;base64,AAAA)"],
    ["a declaration breakout", "red; background: url(https://evil.test/x.png)"],
    ["a rule breakout", "red } html { background: red"],
    ["an at-rule injection", "@import url(https://evil.test/x.css)"],
    ["unbalanced parentheses", "linear-gradient(#fff"],
    ["a css escape sequence", "\\65 vil"]
  ])("rejects %s", (_label, value) => {
    expect(issuesFor({ "--studio-material-surface": value }).length).toBeGreaterThan(0);
  });
});
