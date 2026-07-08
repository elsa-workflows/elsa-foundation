import { describe, expect, it } from "vitest";
import { getTheme, getThemeNames, isMaterialTheme, materialThemeIds } from "../app/themes/presets";

describe("theme presets", () => {
  it("includes the hot pink theme", () => {
    expect(getTheme("hot-pink")?.name).toBe("Hot Pink");
    expect(getThemeNames()).toContainEqual({ id: "hot-pink", name: "Hot Pink" });
  });

  it("includes the black glass theme", () => {
    const theme = getTheme("black-glass");

    expect(theme?.name).toBe("Black Glass");
    expect(theme?.description).toContain("Smoked-glass");
    expect(theme?.dark.primary).toBe("oklch(0.78 0.16 235)");
    expect(theme?.dark.background).toBe("oklch(0.13 0.014 245)");
    expect(getThemeNames()).toContainEqual({ id: "black-glass", name: "Black Glass" });
  });

  it("includes the material themes", () => {
    expect(materialThemeIds).toEqual(["stone", "paper", "blueprint", "ceramic", "carbon", "brass-instrument"]);

    expect(getThemeNames()).toEqual(
      expect.arrayContaining([
        { id: "stone", name: "Stone" },
        { id: "paper", name: "Paper" },
        { id: "blueprint", name: "Blueprint" },
        { id: "ceramic", name: "Ceramic" },
        { id: "carbon", name: "Carbon" },
        { id: "brass-instrument", name: "Brass Instrument" },
      ])
    );

    expect(isMaterialTheme("stone")).toBe(true);
    expect(isMaterialTheme("black-glass")).toBe(false);
  });
});
