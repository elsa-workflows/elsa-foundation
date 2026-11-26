import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../activityIconLoad", () => ({
  loadActivityIconGlyphs: () => Promise.resolve({
    ActivityIconGlyph: () => {
      throw new Error("glyph crashed");
    }
  })
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

describe("renderActivityIcon error boundary", () => {
  it("keeps the placeholder when the glyph component throws", async () => {
    const { renderActivityIcon } = await import("../activityIconView");
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => undefined);

    flushSync(() => root.render(<span data-testid="icon">{renderActivityIcon("Rocket", "activity")}</span>));

    await vi.waitFor(() => {
      expect(container.querySelector("[data-testid='icon'] svg")).toBeTruthy();
      expect(container.querySelectorAll("[data-testid='icon'] rect")).toHaveLength(4);
    });
    expect(container.textContent).toBe("");
    errorSpy.mockRestore();
  });
});
