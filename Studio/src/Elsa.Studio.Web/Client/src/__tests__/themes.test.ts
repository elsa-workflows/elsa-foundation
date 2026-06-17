import { describe, expect, it } from "vitest";
import { getTheme, getThemeNames } from "../app/themes/presets";

describe("theme presets", () => {
  it("includes the hot pink theme", () => {
    expect(getTheme("hot-pink")?.name).toBe("Hot Pink");
    expect(getThemeNames()).toContainEqual({ id: "hot-pink", name: "Hot Pink" });
  });
});
