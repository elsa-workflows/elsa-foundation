import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { WeaverComposer } from "../WeaverComposer";

describe("WeaverComposer", () => {
  it("submits typed input and clears the field", () => {
    const onSubmit = vi.fn();
    const { container, unmount } = render(<WeaverComposer busy={false} followups={[]} onSubmit={onSubmit} onStop={() => {}} onRemoveFollowup={() => {}} />);
    const textarea = container.querySelector("textarea")!;

    typeInto(textarea, "Explain this workflow");
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-send")!.click());

    expect(onSubmit).toHaveBeenCalledWith("Explain this workflow");
    expect(textarea.value).toBe("");

    unmount();
  });

  it("shows a Stop button while streaming and never disables steering input", () => {
    const onStop = vi.fn();
    const { container, unmount } = render(<WeaverComposer busy={true} followups={[]} onSubmit={() => {}} onStop={onStop} onRemoveFollowup={() => {}} />);

    expect(container.querySelector(".weaver-send")).toBeNull();
    expect(container.querySelector("textarea")!.disabled).toBe(false);
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-stop")!.click());

    expect(onStop).toHaveBeenCalledTimes(1);

    unmount();
  });

  it("renders queued follow-ups and removes them", () => {
    const onRemoveFollowup = vi.fn();
    const { container, unmount } = render(
      <WeaverComposer busy={true} followups={[{ id: "f1", message: "Also rename it" }]} onSubmit={() => {}} onStop={() => {}} onRemoveFollowup={onRemoveFollowup} />
    );

    expect(container.textContent).toContain("Also rename it");
    flushSync(() => container.querySelector<HTMLButtonElement>(".weaver-followup-chip button")!.click());

    expect(onRemoveFollowup).toHaveBeenCalledWith("f1");

    unmount();
  });
});

function typeInto(textarea: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, "value")!.set!;
  flushSync(() => {
    setter.call(textarea, value);
    textarea.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function render(element: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(element));
  return {
    container,
    unmount: () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}
