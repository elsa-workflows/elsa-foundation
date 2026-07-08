import { describe, expect, it } from "vitest";
import type { StudioEndpointContext } from "../sdk";
import { builtInThemeDefinitions, getTheme, getThemeNames, isMaterialTheme, materialThemeIds } from "../app/themes/presets";
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
      { id: "brass-instrument", name: "Brass Instrument" }
    ]));
    expect(getTheme("black-glass")?.source).toBe("built-in");
    expect(getTheme("black-glass")?.modes.dark.material?.cssVariables?.["--studio-material-finish"]).toBe("glass");
    expect(materialThemeIds).toEqual(["stone", "paper", "blueprint", "ceramic", "carbon", "brass-instrument"]);
    expect(isMaterialTheme("stone")).toBe(true);
    expect(isMaterialTheme("black-glass")).toBe(false);
  });

  it("keeps material theme modes visually distinct", () => {
    for (const themeId of materialThemeIds) {
      const theme = getTheme(themeId);

      expect(theme?.light.background, themeId).not.toBe(theme?.dark.background);
      expect(theme?.light.card, themeId).not.toBe(theme?.dark.card);
      expect(theme?.light.foreground, themeId).not.toBe(theme?.dark.foreground);
      expect(theme?.modes.light.material?.textureAssets?.surface, themeId).not.toBe(theme?.modes.dark.material?.textureAssets?.surface);
    }

    const blackGlass = getTheme("black-glass");

    expect(blackGlass?.light.background).not.toBe(blackGlass?.dark.background);
    expect(blackGlass?.light.foreground).not.toBe(blackGlass?.dark.foreground);
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
