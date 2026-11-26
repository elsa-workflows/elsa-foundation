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
  it.each([
    ["Literal", "literal", "lucide-type"],
    ["JavaScript", "text", "lucide-code-xml"],
    ["Liquid", "text", "lucide-code-xml"],
    ["Secret", "reference", "lucide-key-round"],
    ["Variable", "reference", "lucide-braces"],
    ["Object", "literal", "lucide-braces"],
    ["Connection", "reference", "lucide-cable"]
  ] as const)("shows a compact %s icon and announces the current mode", (type, editingMode, iconClass) => {
    const host = mount(
      <SyntaxPicker
        label="Text expression syntax"
        value={type}
        descriptors={[{ type, displayName: type, editingMode }]}
        disabled={false}
        variant="inline"
        onChange={() => {}}
      />
    );

    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    expect(trigger.getAttribute("aria-label")).toBe(`Text expression syntax: ${type}`);
    expect(trigger.title).toBe(type);
    expect(trigger.querySelector("svg")?.classList.contains(iconClass)).toBe(true);
  });

  it("renders a compact mode icon in the property-heading variant", () => {
    const host = mount(
      <SyntaxPicker
        label="Authorization expression syntax"
        value="Secret"
        descriptors={[{ type: "Secret", displayName: "Secret", editingMode: "reference" }]}
        disabled={false}
        variant="compact"
        onChange={() => {}}
      />
    );

    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    expect(host.querySelector(".wf-syntax-picker.compact")).not.toBeNull();
    expect(host.querySelector(".wf-syntax-picker.inline")).toBeNull();
    expect(trigger.getAttribute("aria-label")).toBe("Authorization expression syntax: Secret");
    expect(trigger.title).toBe("Secret");
    expect(trigger.querySelector("svg")?.classList.contains("lucide-key-round")).toBe(true);
  });

  it("keeps the compact trigger disabled when the property is read-only", () => {
    const host = mount(
      <SyntaxPicker
        label="Text expression syntax"
        value="Literal"
        descriptors={[{ type: "Literal", displayName: "Literal", editingMode: "literal" }]}
        disabled
        variant="compact"
        onChange={() => {}}
      />
    );

    expect(host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.disabled).toBe(true);
  });

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

  it("skips unavailable options for Arrow, Home, and End navigation", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const host = mount(
      <SyntaxPicker
        label="Text expression syntax"
        value="Literal"
        descriptors={[
          { type: "Literal", displayName: "Literal", editingMode: "literal" },
          { type: "Variable", displayName: "Variable", editingMode: "reference" },
          { type: "Liquid", displayName: "Liquid", editingMode: "text" }
        ]}
        getUnavailableReason={descriptor => descriptor.type === "Variable" ? "Variable editor unavailable." : null}
        disabled={false}
        onChange={() => {}}
      />
    );

    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    trigger.click();
    await nextFrame();
    press("ArrowUp");
    expect(document.activeElement?.textContent).toBe("Liquid");
    press("ArrowDown");
    expect(document.activeElement?.textContent).toBe("Literal");
    press("End");
    expect(document.activeElement?.textContent).toBe("Liquid");
    press("Home");
    expect(document.activeElement?.textContent).toBe("Literal");

    const unavailable = [...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(option => option.textContent?.includes("Variable"));
    expect(unavailable?.disabled).toBe(true);
    expect(unavailable?.textContent).toContain("Variable editor unavailable.");
  });

  it("keeps focus on the trigger when every option is unavailable", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const changes: string[] = [];
    const host = mount(
      <SyntaxPicker
        label="Expression syntax"
        value="Variable"
        descriptors={[
          { type: "Variable", displayName: "Variable", editingMode: "reference" },
          { type: "Secret", displayName: "Secret", editingMode: "reference" }
        ]}
        getUnavailableReason={() => "Editor unavailable."}
        disabled={false}
        onChange={value => changes.push(value)}
      />
    );

    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    trigger.focus();
    trigger.click();
    await nextFrame();
    expect(document.activeElement).toBe(trigger);
    press("ArrowDown");
    press("Enter");
    expect(changes).toEqual([]);
    expect([...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")].every(option => option.disabled)).toBe(true);

    press("Escape");
    expect(document.body.querySelector("[role='listbox']")).toBeNull();
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    await nextFrame();
    press("Tab");
    expect(document.body.querySelector("[role='listbox']")).toBeNull();
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
