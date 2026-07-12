import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SyntaxPicker } from "../ActivityPropertiesPanel";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.unstubAllGlobals();
});

describe("SyntaxPicker", () => {
  it("portals the listbox and supports conventional keyboard selection and focus restoration", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const changes: string[] = [];
    const host = mount(
      <div style={{ overflow: "hidden" }}>
        <SyntaxPicker
          label="Text expression syntax"
          value="Literal"
          descriptors={[
            { type: "Literal", displayName: "Literal", editingMode: "literal" },
            { type: "JavaScript", displayName: "JavaScript", editingMode: "text" },
            { type: "Liquid", displayName: "Liquid", editingMode: "text" }
          ]}
          disabled={false}
          onChange={value => changes.push(value)}
        />
      </div>
    );
    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    trigger.click();
    await nextFrame();

    const listbox = document.body.querySelector<HTMLElement>("[role='listbox']");
    expect(listbox).not.toBeNull();
    expect(host.contains(listbox)).toBe(false);
    expect(document.activeElement?.textContent).toBe("Literal");

    press("End");
    expect(document.activeElement?.textContent).toBe("Liquid");
    press("ArrowUp");
    expect(document.activeElement?.textContent).toBe("JavaScript");
    press("Enter");

    expect(changes).toEqual(["JavaScript"]);
    expect(document.body.querySelector("[role='listbox']")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("closes on Escape and restores focus to its trigger", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const host = mount(
      <SyntaxPicker
        label="Text expression syntax"
        value="JavaScript"
        descriptors={[
          { type: "Literal", displayName: "Literal", editingMode: "literal" },
          { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
        ]}
        disabled={false}
        onChange={() => {}}
      />
    );
    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    flushSync(() => trigger.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    await nextFrame();
    expect(document.activeElement?.textContent).toBe("JavaScript");

    press("Escape");
    expect(document.body.querySelector("[role='listbox']")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });
});

function mount(node: React.ReactElement): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(node));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function press(key: string) {
  flushSync(() => document.activeElement?.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
}
