import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import type { StudioAiContributionApi, StudioPanelContribution } from "../sdk";
import { BottomPanel } from "../app/App";

afterEach(() => {
  window.localStorage.clear();
});

describe("bottom panel", () => {
  it("reveals and expands the AI panel when a prompt action is dispatched", async () => {
    window.localStorage.setItem("elsa-studio-active-bottom-panel", "console");
    window.localStorage.setItem("elsa-studio-bottom-panel-collapsed", "true");

    const panels: StudioPanelContribution[] = [
      { id: "console", title: "Console", component: () => <div>Console panel</div> },
      { id: "weaver-chat", title: "Weaver", component: () => <div>Weaver panel</div> }
    ];
    const { ai, dispatchPrompt } = fakeAi();

    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    flushSync(() => root.render(<BottomPanel panels={panels} ai={ai} />));

    const section = host.querySelector(".bottom-panel") as HTMLElement;
    expect(section.classList.contains("collapsed")).toBe(true);
    expect(tabFor(host, "Weaver").classList.contains("active")).toBe(false);

    flushSync(() => dispatchPrompt());

    expect(section.classList.contains("collapsed")).toBe(false);
    const weaverTab = tabFor(host, "Weaver");
    expect(weaverTab.classList.contains("active")).toBe(true);
    expect(weaverTab.querySelector(".bottom-panel-tab-pulse")).toBeTruthy();
    expect(host.textContent).toContain("Weaver panel");

    // A repeat reveal of the same panel remounts the pulse overlay so the animation replays.
    const firstPulse = weaverTab.querySelector(".bottom-panel-tab-pulse");
    flushSync(() => dispatchPrompt());
    expect(weaverTab.querySelector(".bottom-panel-tab-pulse")).not.toBe(firstPulse);

    root.unmount();
    host.remove();
  });

  it("ignores dispatched prompts when no panel hosts an AI surface", () => {
    const panels: StudioPanelContribution[] = [
      { id: "console", title: "Console", component: () => <div>Console panel</div> }
    ];
    const { ai, dispatchPrompt } = fakeAi([]);

    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    flushSync(() => root.render(<BottomPanel panels={panels} ai={ai} />));

    expect(() => flushSync(() => dispatchPrompt())).not.toThrow();
    expect(tabFor(host, "Console").classList.contains("active")).toBe(true);

    root.unmount();
    host.remove();
  });
});

function fakeAi(panelSurfaceIds: string[] = ["weaver-chat"]) {
  const listeners = new Set<() => void>();
  const ai = {
    surfaces: {
      list: () => panelSurfaceIds.map(id => ({ id, title: id, placement: "panel" as const }))
    },
    onPrompt(listener: () => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  } as unknown as StudioAiContributionApi;

  return {
    ai,
    dispatchPrompt: () => listeners.forEach(listener => listener())
  };
}

function tabFor(host: HTMLElement, label: string) {
  const tab = Array.from(host.querySelectorAll<HTMLButtonElement>(".bottom-panel-tab-list button"))
    .find(button => button.textContent === label);
  if (!tab) throw new Error(`Tab '${label}' not found.`);
  return tab;
}
