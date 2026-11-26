import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../activityIconLoad", () => ({
  loadActivityIconGlyphs: () => Promise.reject(new Error("Failed to fetch dynamically imported module"))
}));

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("renderActivityIcon", () => {
  it("keeps the placeholder when the glyph chunk fails to load", async () => {
    const { loadActivityIconRegistry, renderActivityIcon } = await import("../activityIconView");

    await expect(loadActivityIconRegistry()).rejects.toThrow("Failed to fetch dynamically imported module");

    flushSync(() => root.render(<span data-testid="icon">{renderActivityIcon("terminal")}</span>));

    await vi.waitFor(() => {
      expect(container.querySelector("[data-testid='icon'] svg")).toBeTruthy();
      expect(container.querySelectorAll("[data-testid='icon'] rect")).toHaveLength(4);
    });
    expect(container.textContent).toBe("");
  });
});
