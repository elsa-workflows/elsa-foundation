// @vitest-environment node

import { readdirSync, readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const activityRoot = new URL("../../../../../../../Essentials/src/Activities/", import.meta.url);

describe("built-in activity icon declarations", () => {
  it("remain available in the pinned lucide dynamic imports", async () => {
    const iconNames = new Set<string>();
    for (const entry of readdirSync(activityRoot, { recursive: true })) {
      if (!entry.endsWith(".cs")) continue;
      const source = readFileSync(new URL(entry.replaceAll("\\", "/"), activityRoot), "utf8");
      for (const [, attributes] of source.matchAll(/\[Activity\(([^\]]*)\)\]/gs)) {
        const icon = attributes.match(/\bIcon\s*=\s*"([^"]+)"/s)?.[1];
        if (icon) iconNames.add(icon);
      }
    }

    const { default: dynamicIcons } = await import("lucide-react/dynamicIconImports");
    const missing = [...iconNames].filter(icon => !Object.hasOwn(dynamicIcons, toLucideKebabName(icon)));

    expect(iconNames.size).toBeGreaterThan(0);
    expect(missing).toEqual([]);
  });
});

function toLucideKebabName(exportName: string): string {
  return exportName
    .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
    .replace(/([A-Z])([A-Z][a-z])/g, "$1-$2")
    .replace(/([a-zA-Z])(\d+)/g, "$1-$2")
    .toLowerCase();
}
