import { describe, expect, it } from "vitest";
import type { StudioEndpointContext } from "../sdk";
import { builtInThemeDefinitions, getTheme, getThemeNames, isMaterialTheme, materialThemeIds } from "../app/themes/presets";
import type { ThemeMaterialMode } from "../app/themes/presets";
import { applyMaterialVariables } from "../app/components/ThemeProvider";
import { createCustomThemeFrom, findSelectableTheme, getSelectableThemes, normalizeThemeStore, saveTheme, setBuiltInThemeEnabled, validateThemeDefinition } from "../app/themes/themeStoreApi";

const retiredThemeIds = ["black-glass", "stone", "blueprint", "brass-instrument"] as const;

describe("theme presets", () => {
  it("keeps every built-in a read-only definition", () => {
    expect(getThemeNames()).toEqual(builtInThemeDefinitions.map(({ id, name }) => ({ id, name })));
    expect(builtInThemeDefinitions.every(theme => theme.source === "built-in")).toBe(true);
  });

  it("registers no built-in material theme", () => {
    expect(materialThemeIds).toEqual([]);
    expect(builtInThemeDefinitions.some(theme => isMaterialTheme(theme.id))).toBe(false);
  });

  it.each(retiredThemeIds)("no longer ships the retired %s theme", themeId => {
    expect(getTheme(themeId)).toBeUndefined();
    expect(isMaterialTheme(themeId)).toBe(false);
  });

  it("duplicates built-ins into custom draft themes", () => {
    const copy = createCustomThemeFrom(builtInThemeDefinitions[0], "meridian-copy", "Meridian Copy");

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

  it("hides admin-disabled built-in themes from the selectable picker list", () => {
    const store = normalizeThemeStore({ disabledBuiltInThemeIds: ["drift", "not-a-built-in", ...retiredThemeIds] });
    const drift = store.themes.find(theme => theme.id === "drift");

    // The definition stays listed (so the Theme Builder can re-enable it) but is not selectable.
    expect(drift?.enabled).toBe(false);
    expect(getSelectableThemes(store).some(theme => theme.id === "drift")).toBe(false);
    expect(findSelectableTheme(store, "drift").id).not.toBe("drift");
    // Unknown and retired ids are dropped rather than persisted back.
    expect(store.disabledBuiltInThemeIds).toEqual(["drift"]);
  });

  it.each(retiredThemeIds)("lands a user whose stored theme is the retired %s on the default theme", themeId => {
    expect(findSelectableTheme(normalizeThemeStore(), themeId).id).toBe("meridian");
    expect(findSelectableTheme(normalizeThemeStore({ defaultThemeId: "atelier" }), themeId).id).toBe("atelier");
  });

  it.each(retiredThemeIds)("falls back to the first built-in when the store default is the retired %s", themeId => {
    const store = normalizeThemeStore({ defaultThemeId: themeId });

    expect(store.defaultThemeId).toBe("meridian");
    expect(store.themes.some(theme => theme.id === themeId)).toBe(false);
    expect(findSelectableTheme(store, null).id).toBe("meridian");
  });

  it("keeps a custom copy of a retired material theme valid, textures and all", () => {
    const copy = createCustomThemeFrom(builtInThemeDefinitions[0], "stone-copy", "Stone Copy");
    copy.modes.dark.material = {
      textureAssets: { surface: "/studio/assets/stone-slate-tile.png" },
      textureSize: 390,
      cssVariables: { "--studio-material-finish": "slate", "--studio-material-depth": "0.78" }
    };
    const store = normalizeThemeStore({ themes: [{ ...copy, enabled: true, published: true }] });

    expect(validateThemeDefinition(copy).valid).toBe(true);
    expect(findSelectableTheme(store, "stone-copy").modes.dark.material).toEqual(copy.modes.dark.material);
    // A custom id never switches the material treatment on, so the missing tile is never requested.
    expect(isMaterialTheme("stone-copy")).toBe(false);
  });

  it("sends built-in visibility changes to the visibility endpoint and normalizes the result", async () => {
    let requested: { url: string; body: unknown } | null = null;
    const context = {
      http: {
        putJson: async (url: string, body: unknown) => {
          requested = { url, body };
          return { themes: [], defaultThemeId: "meridian", assets: [], disabledBuiltInThemeIds: ["drift"] };
        }
      }
    };

    const store = await setBuiltInThemeEnabled(context as unknown as StudioEndpointContext, "drift", false);

    expect(requested).toEqual({ url: "/_elsa/theme-store/themes/drift/visibility", body: { enabled: false } });
    expect(store.themes.find(theme => theme.id === "drift")?.enabled).toBe(false);
    expect(store.themes.filter(theme => theme.enabled).length).toBeGreaterThan(0);
  });

  it("normalizes store-shaped mutation responses with built-in themes", async () => {
    const custom = createCustomThemeFrom(builtInThemeDefinitions[0], "custom-theme", "Custom Theme");
    const context = {
      http: {
        putJson: async () => ({ themes: [custom], defaultThemeId: "schematic", assets: [] })
      }
    };

    const store = await saveTheme(context as unknown as StudioEndpointContext, custom);

    expect(store.themes.some(theme => theme.id === "schematic")).toBe(true);
    expect(store.themes.some(theme => theme.id === "custom-theme")).toBe(true);
    expect(store.defaultThemeId).toBe("schematic");
  });
});

describe("applyMaterialVariables", () => {
  function apply(material: ThemeMaterialMode | undefined) {
    const root = document.createElement("html");
    applyMaterialVariables(root, material);
    return root;
  }

  it("drives the CSS --studio-material-texture the recipes actually read", () => {
    // Material recipes read a single `--studio-material-texture`; the theme's
    // primary (`surface`) asset must reach it, not just the per-name alias.
    const root = apply({
      textureAssets: { surface: "/studio/assets/custom-slate-tile.png" },
      textureSize: 390
    });

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/custom-slate-tile.png")');
    expect(root.style.getPropertyValue("--studio-material-texture-size")).toBe("390px 390px");
    // Legacy per-name alias remains for backwards compatibility.
    expect(root.style.getPropertyValue("--studio-material-surface-texture")).toBe('url("/studio/assets/custom-slate-tile.png")');
  });

  it("delivers a custom theme's texture to the CSS variable end to end", () => {
    const custom = createCustomThemeFrom(builtInThemeDefinitions[0], "custom-texture", "Custom Texture");
    custom.modes.light.material = {
      textureAssets: { surface: "/studio/assets/custom-vellum-tile.png" },
      textureSize: 256,
      cssVariables: { "--studio-material-finish": "vellum" }
    };

    const root = apply(custom.modes.light.material);

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/custom-vellum-tile.png")');
    expect(root.style.getPropertyValue("--studio-material-texture-size")).toBe("256px 256px");
    expect(root.style.getPropertyValue("--studio-material-finish")).toBe("vellum");
  });

  it("falls back to the first texture asset when no surface key is present", () => {
    const root = apply({ textureAssets: { grain: "/studio/assets/custom-grain-tile.png" } });

    expect(root.style.getPropertyValue("--studio-material-texture")).toBe('url("/studio/assets/custom-grain-tile.png")');
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
    ["a same-origin absolute url", "url(/studio/assets/custom-slate-tile.png)"],
    ["a quoted same-origin url", 'url("/studio/assets/custom-slate-tile.png")'],
    ["a bundler-relative url", "url(../../assets/materials/custom-slate-tile.png)"]
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
