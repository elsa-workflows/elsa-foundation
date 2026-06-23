import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { AgentProposalReview } from "../AgentProposalReview";

describe("AgentProposalReview", () => {
  it("requires explicit proposal approval before execution", () => {
    const onApprove = vi.fn();
    const onDeny = vi.fn();
    const onExecute = vi.fn();
    const { container, unmount } = render(
      <AgentProposalReview
        proposals={[{
          id: "prop_01",
          title: "Add timeout branch",
          summary: "Adds stalled approval handling.",
          risk: "review-required",
          status: "awaiting-approval",
          revision: "rev_01",
          operations: [{ op: "add-activity" }],
          rollback: "Reject proposal."
        }]}
        onApprove={onApprove}
        onDeny={onDeny}
        onExecute={onExecute}
      />
    );

    const buttons = Array.from(container.querySelectorAll("button"));
    expect(container.textContent).toContain("Add timeout branch");
    expect(buttons.map(button => button.textContent?.trim())).toEqual(["Approve", "Deny", "Execute"]);
    expect(buttons[2].disabled).toBe(true);

    buttons[0].click();
    buttons[1].click();

    expect(onApprove).toHaveBeenCalledTimes(1);
    expect(onDeny).toHaveBeenCalledTimes(1);
    expect(onExecute).not.toHaveBeenCalled();
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
