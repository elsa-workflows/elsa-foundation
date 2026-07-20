import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ConversionControl } from "../ActivityPropertiesPanel";
import type { WrappedActivityInputValue } from "../activityProperties";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.unstubAllGlobals();
});

const profiles = [
  { id: "elsa.json", version: "1" },
  { id: "elsa.xml", version: "1" }
];

function wrappedValue(overrides: Partial<WrappedActivityInputValue> = {}): WrappedActivityInputValue {
  return { typeName: "System.String", expression: { type: "Literal", value: '{"name":"Grace"}' }, ...overrides };
}

describe("ConversionControl", () => {
  it("defaults to Auto and captions the inferred source → destination contract", () => {
    const host = mount(
      <ConversionControl
        inputLabel="Value"
        targetTypeName="System.String"
        wrapped={wrappedValue()}
        profiles={profiles}
        disabled={false}
        onChange={() => {}}
      />
    );

    const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
    expect(trigger.textContent).toBe("Auto");
    expect(host.querySelector(".wf-conversion-caption")?.textContent).toBe("Text → String");
    expect(host.querySelector(".wf-conversion-profile")).toBeNull();
  });

  it("selecting JSON writes an authored request and updates the caption to formatted content", async () => {
    stubResizeObserver();
    const changes: WrappedActivityInputValue[] = [];
    const host = mount(
      <Harness initial={wrappedValue()} onChange={next => changes.push(next)} />
    );

    await selectMode(host, "JSON");

    expect(changes.at(-1)?.conversion).toEqual({ mode: "json" });
    expect(host.querySelector(".wf-conversion-caption")?.textContent).toBe("JSON content → String");
  });

  it("selecting Auto on a plain request clears the authored conversion entirely", async () => {
    stubResizeObserver();
    const changes: WrappedActivityInputValue[] = [];
    const host = mount(
      <Harness initial={wrappedValue({ conversion: { mode: "json" } })} onChange={next => changes.push(next)} />
    );

    await selectMode(host, "Auto");

    expect(changes.at(-1)).toBeDefined();
    expect("conversion" in changes.at(-1)!).toBe(false);
  });

  it("shows a loaded request authored elsewhere, mode read case-insensitively", () => {
    const host = mount(
      <ConversionControl
        inputLabel="Value"
        targetTypeName="System.String"
        wrapped={wrappedValue({ conversion: { mode: "Xml" } })}
        profiles={profiles}
        disabled={false}
        onChange={() => {}}
      />
    );

    expect(host.querySelector(".wf-syntax-picker-trigger")?.textContent).toBe("XML");
  });

  it("Profile mode exposes id/version entry with known-profile suggestions and version prefill", async () => {
    stubResizeObserver();
    const changes: WrappedActivityInputValue[] = [];
    const host = mount(
      <Harness initial={wrappedValue()} onChange={next => changes.push(next)} />
    );

    await selectMode(host, "Profile");

    const idInput = host.querySelector<HTMLInputElement>("input[aria-label='Value conversion profile id']")!;
    const versionInput = host.querySelector<HTMLInputElement>("input[aria-label='Value conversion profile version']")!;
    expect(idInput).not.toBeNull();
    expect([...host.querySelectorAll("datalist option")].map(option => option.getAttribute("value")))
      .toEqual(["elsa.json", "elsa.xml"]);

    setInputValue(idInput, "elsa.json");
    expect(changes.at(-1)?.conversion).toEqual({ mode: "profile", profile: { id: "elsa.json", version: "1" } });

    setInputValue(versionInput, "2");
    expect(changes.at(-1)?.conversion).toEqual({ mode: "profile", profile: { id: "elsa.json", version: "2" } });
  });
});

// Re-renders with the latest wrapped value so multi-step interactions observe their own writes.
function Harness({ initial, onChange }: { initial: WrappedActivityInputValue; onChange(next: WrappedActivityInputValue): void }) {
  const [wrapped, setWrapped] = React.useState(initial);
  return (
    <ConversionControl
      inputLabel="Value"
      targetTypeName="System.String"
      wrapped={wrapped}
      profiles={profiles}
      disabled={false}
      onChange={next => {
        setWrapped(next);
        onChange(next);
      }}
    />
  );
}

async function selectMode(host: HTMLElement, label: string) {
  const trigger = host.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")!;
  flushSync(() => trigger.click());
  await nextFrame();
  const option = [...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent?.startsWith(label));
  expect(option, `mode option ${label}`).toBeDefined();
  flushSync(() => option!.click());
}

function setInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
  flushSync(() => {
    setter.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function stubResizeObserver() {
  vi.stubGlobal("ResizeObserver", class {
    observe() {}
    disconnect() {}
  });
}

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
