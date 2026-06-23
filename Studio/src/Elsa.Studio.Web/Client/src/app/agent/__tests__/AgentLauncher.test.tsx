import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { AgentLauncher } from "../AgentLauncher";

describe("AgentLauncher", () => {
  it("announces expanded state and invokes the shell callback", () => {
    const onClick = vi.fn();
    const { container, unmount } = render(<AgentLauncher open={false} onClick={onClick} />);
    const button = container.querySelector("button")!;

    expect(button.getAttribute("aria-expanded")).toBe("false");
    expect(button.textContent).toContain("Weaver");

    button.click();

    expect(onClick).toHaveBeenCalledTimes(1);
    unmount();
  });
});

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
