import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { DisclosureFixturePage } from "../DisclosureFixturePage";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
});

describe("DisclosureFixturePage", () => {
  it("renders every disclosure primitive for visual QA", () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    flushSync(() => root.render(<DisclosureFixturePage />));
    cleanup = () => {
      flushSync(() => root.unmount());
      host.remove();
    };

    expect(host.querySelector(".studio-add-option")).not.toBeNull();
    expect(host.querySelector(".studio-reveal-toggle")).not.toBeNull();
    expect(host.querySelector(".studio-advanced-section")).not.toBeNull();
    expect(host.querySelector(".studio-technical-details")).not.toBeNull();
    expect(host.querySelector(".studio-expression-toggle")).not.toBeNull();
    expect(host.querySelector(".studio-peer-view-switch")).not.toBeNull();
    expect(host.querySelector(".studio-overflow-menu")).not.toBeNull();
    expect(host.querySelector("[aria-label='Disclosure kit fixture']")).not.toBeNull();
  });
});
