import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  StudioActivityDescriptor,
  StudioActivityInputDescriptor,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import type { ActivityNode } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

const expressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
];

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  vi.unstubAllGlobals();
});

function renderPanel(
  inputs: StudioActivityInputDescriptor[],
  options: { activity?: ActivityNode; onChange?(activity: ActivityNode): void } = {}
) {
  stubResizeObserver();
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const descriptor: StudioActivityDescriptor = {
    typeName: "TestActivity",
    inputs,
    outputs: [],
    ports: []
  };
  const initialActivity = options.activity ?? activity();

  function Harness() {
    const [currentActivity, setCurrentActivity] = React.useState(initialActivity);
    const handleChange = (nextActivity: ActivityNode) => {
      options.onChange?.(nextActivity);
      setCurrentActivity(nextActivity);
    };
    return (
      <ActivityPropertiesPanel
        activity={currentActivity}
        descriptor={descriptor}
        editors={[]}
        expressionEditors={[]}
        expressionDescriptors={expressionDescriptors}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        onChange={handleChange}
      />
    );
  }

  flushSync(() => root.render(<Harness />));
  active = { root, container };
  return container;
}

function activity(overrides: Record<string, unknown> = {}): ActivityNode {
  return {
    nodeId: "node-1",
    activityVersionId: "activity-1",
    inputs: [],
    outputs: [],
    method: { typeName: "System.String", expression: { type: "Literal", value: "GET" } },
    ...overrides
  };
}

function methodInput(overrides: Partial<StudioActivityInputDescriptor> = {}): StudioActivityInputDescriptor {
  return { name: "Method", displayName: "Method", typeName: "System.String", isWrapped: true, ...overrides };
}

function toggle(container: HTMLElement): HTMLButtonElement {
  return container.querySelector<HTMLButtonElement>(".wf-conversion-toggle")!;
}

async function selectConversionMode(container: HTMLElement, label: string) {
  const trigger = container.querySelector<HTMLButtonElement>(".wf-conversion-control .wf-syntax-picker-trigger")!;
  flushSync(() => trigger.click());
  await nextFrame();
  const option = [...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")]
    .find(candidate => candidate.textContent?.startsWith(label));
  expect(option, `mode option ${label}`).toBeDefined();
  flushSync(() => option!.click());
}

function stubResizeObserver() {
  vi.stubGlobal("ResizeObserver", class {
    observe() {}
    disconnect() {}
  });
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

describe("conversion disclosure on property rows", () => {
  it("hides the conversion control at the Auto default behind an icon affordance", () => {
    const container = renderPanel([methodInput()]);

    expect(container.querySelector(".wf-conversion-control")).toBeNull();
    expect(container.querySelector(".wf-conversion-chip")).toBeNull();
    const button = toggle(container);
    expect(button).not.toBeNull();
    expect(button.getAttribute("aria-expanded")).toBe("false");
    expect(button.title).toBe("Conversion: Auto (Text → String)");
    expect(button.classList.contains("authored")).toBe(false);
  });

  it("renders no affordance at all for non-wrapped inputs", () => {
    const container = renderPanel([methodInput({ isWrapped: false })], {
      activity: activity({ method: "GET" })
    });

    expect(container.querySelector(".wf-conversion-toggle")).toBeNull();
    expect(container.querySelector(".wf-conversion-control")).toBeNull();
  });

  it("reveals the full control on toggle and moves focus to its mode picker", async () => {
    const container = renderPanel([methodInput()]);

    flushSync(() => toggle(container).click());
    await nextFrame();

    expect(toggle(container).getAttribute("aria-expanded")).toBe("true");
    const control = container.querySelector(".wf-conversion-control")!;
    expect(control).not.toBeNull();
    expect(document.activeElement).toBe(control.querySelector(".wf-syntax-picker-trigger"));
  });

  it("keeps an authored conversion visible as a chip and opens the control from it", () => {
    const container = renderPanel([methodInput()], {
      activity: activity({
        method: {
          typeName: "System.String",
          expression: { type: "Literal", value: "GET" },
          conversion: { mode: "json" }
        }
      })
    });

    expect(container.querySelector(".wf-conversion-control")).toBeNull();
    expect(toggle(container).classList.contains("authored")).toBe(true);
    const chip = container.querySelector<HTMLButtonElement>(".wf-conversion-chip")!;
    expect(chip.textContent).toBe("JSON · JSON content → String");

    flushSync(() => chip.click());

    expect(container.querySelector(".wf-conversion-control")).not.toBeNull();
    expect(container.querySelector(".wf-conversion-chip")).toBeNull();
  });

  it("clears the request when returning to Auto but stays open until the toggle collapses it", async () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([methodInput()], {
      onChange: next => changes.push(next),
      activity: activity({
        method: {
          typeName: "System.String",
          expression: { type: "Literal", value: "GET" },
          conversion: { mode: "json" }
        }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-conversion-chip")!.click());
    await selectConversionMode(container, "Auto");

    const method = changes.at(-1)?.method as Record<string, unknown>;
    expect("conversion" in method).toBe(false);
    // Reverting to Auto must not yank the control away mid-interaction.
    expect(container.querySelector(".wf-conversion-control")).not.toBeNull();

    flushSync(() => toggle(container).click());
    await nextFrame();

    expect(container.querySelector(".wf-conversion-control")).toBeNull();
    expect(container.querySelector(".wf-conversion-chip")).toBeNull();
    expect(document.activeElement).toBe(toggle(container));
  });

  it("collapses on Escape and returns focus to the icon affordance", async () => {
    const container = renderPanel([methodInput()]);

    flushSync(() => toggle(container).click());
    await nextFrame();
    const trigger = container.querySelector<HTMLButtonElement>(".wf-conversion-control .wf-syntax-picker-trigger")!;
    flushSync(() => trigger.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    await nextFrame();

    expect(container.querySelector(".wf-conversion-control")).toBeNull();
    expect(document.activeElement).toBe(toggle(container));
  });

  it("still opens read-only so authors can inspect the setting, with editing disabled", async () => {
    const container = renderPanel([methodInput({ isReadOnly: true })]);

    flushSync(() => toggle(container).click());
    await nextFrame();

    const trigger = container.querySelector<HTMLButtonElement>(".wf-conversion-control .wf-syntax-picker-trigger")!;
    expect(trigger.disabled).toBe(true);
  });

  it("exposes the full conversion control in the expanded property editor", async () => {
    const changes: ActivityNode[] = [];
    const container = renderPanel([methodInput()], {
      onChange: next => changes.push(next),
      activity: activity({
        method: { typeName: "System.String", expression: { type: "JavaScript", value: "input.method" } }
      })
    });

    flushSync(() => container.querySelector<HTMLButtonElement>("button[aria-label='Open expanded Method editor']")!.click());
    const dialog = container.querySelector<HTMLElement>(".wf-property-editor-dialog")!;
    expect(dialog.querySelector(".wf-conversion-control")).not.toBeNull();

    const trigger = dialog.querySelector<HTMLButtonElement>(".wf-conversion-control .wf-syntax-picker-trigger")!;
    flushSync(() => trigger.click());
    await nextFrame();
    const option = [...document.body.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(candidate => candidate.textContent?.startsWith("JSON"));
    expect(option, "mode option JSON").toBeDefined();
    flushSync(() => option!.click());

    expect((changes.at(-1)?.method as Record<string, unknown>).conversion).toEqual({ mode: "json" });
  });
});
