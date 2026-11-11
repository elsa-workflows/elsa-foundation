import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";

// A stored activity output must stay readable and clearable when the chooser chunk cannot load, as the workflow lazy
// boundary does for other deferred surfaces: no blank row and no unhandled rejection.
vi.mock("../ActivityDataChooser", () => {
  throw new Error("Failed to fetch dynamically imported module");
});

const { activityResultReferenceContribution } = await import("../activityResultReferenceContribution");

let cleanup: (() => void) | null = null;
afterEach(() => { cleanup?.(); cleanup = null; vi.restoreAllMocks(); });

describe("activity output reference without its editor chunk", () => {
  it("keeps the stored reference visible with Reload page and Clear", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const onChange = vi.fn();
    const Surface = activityResultReferenceContribution.surfaces.inline!;
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    cleanup = () => { flushSync(() => root.unmount()); container.remove(); };
    const props = {
      descriptor: { name: "Text", typeName: "System.String" },
      syntax: "ActivityResult",
      value: { producerNodeId: "producer", projectionKey: "Result", isOptional: false },
      context: { syntax: "ActivityResult", surface: "inline", descriptor: { name: "Text", typeName: "System.String" }, activity: { nodeId: "consumer" }, expressionDescriptors: [] },
      onChange
    } as unknown as StudioExpressionEditorProps;
    flushSync(() => root.render(<Surface {...props} />));

    await vi.waitFor(() => expect(container.querySelector("[role='alert']")).not.toBeNull());
    expect(container.textContent).toContain("producer › Result");
    const buttons = [...container.querySelectorAll<HTMLButtonElement>("button")];
    expect(buttons.map(button => button.textContent)).toEqual(["Reload page", "Clear"]);
    flushSync(() => buttons[1]!.click());
    expect(onChange).toHaveBeenCalledWith(null);
  });
});
