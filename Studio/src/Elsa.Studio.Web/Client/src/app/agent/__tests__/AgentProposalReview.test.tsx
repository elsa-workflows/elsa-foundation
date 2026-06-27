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
    expect(buttons.map(button => button.textContent?.trim())).toEqual(["Approve", "Deny", "Apply"]);
    expect(buttons[2].disabled).toBe(true);

    buttons[0].click();
    buttons[1].click();

    expect(onApprove).toHaveBeenCalledTimes(1);
    expect(onDeny).toHaveBeenCalledTimes(1);
    expect(onExecute).not.toHaveBeenCalled();
    unmount();
  });

  it("applies approved resource-target proposals without workflow operations", () => {
    const onApprove = vi.fn();
    const onDeny = vi.fn();
    const onExecute = vi.fn();
    const { container, unmount } = render(
      <AgentProposalReview
        proposals={[{
          id: "prop_02",
          title: "Enable package feed",
          summary: "Applies a package feed setting to the Studio environment.",
          risk: "review-required",
          status: "approved",
          revision: "rev_02",
          resourceTarget: {
            resourceType: "package-feed",
            resourceId: "nuget-prod",
            displayName: "Production NuGet feed",
            moduleId: "Elsa.Studio.PackageFeeds",
            summary: "Changes Studio package feed availability."
          },
          audit: {
            state: "awaiting-execution",
            actor: "studio-user"
          }
        }]}
        onApprove={onApprove}
        onDeny={onDeny}
        onExecute={onExecute}
      />
    );

    expect(container.textContent).toContain("Production NuGet feed");
    expect(container.textContent).toContain("package-feed");
    expect(container.textContent).toContain("nuget-prod");
    expect(container.textContent).toContain("awaiting execution");

    clickButton(container, "Apply");

    expect(onApprove).not.toHaveBeenCalled();
    expect(onDeny).not.toHaveBeenCalled();
    expect(onExecute).toHaveBeenCalledTimes(1);
    unmount();
  });

  it("shows disabled, loading, error, and audit state while blocking controls", () => {
    const onApprove = vi.fn();
    const onDeny = vi.fn();
    const onExecute = vi.fn();
    const { container, unmount } = render(
      <AgentProposalReview
        proposals={[{
          id: "prop_03",
          title: "Rotate secret",
          summary: "Rotates an extension secret.",
          risk: "admin",
          status: "awaiting-approval",
          revision: "rev_03",
          resourceTarget: {
            resourceType: "extension-secret",
            resourceId: "stripe-api-key",
            displayName: "Stripe API key"
          },
          audit: {
            state: "pending",
            outcome: "Waiting for administrator approval."
          },
          isLoading: true,
          error: "Policy service is unavailable.",
          disabledReason: "Administrator approval is required before this proposal can be applied."
        }]}
        onApprove={onApprove}
        onDeny={onDeny}
        onExecute={onExecute}
      />
    );

    const buttons = Array.from(container.querySelectorAll("button"));
    expect(container.textContent).toContain("Stripe API key");
    expect(container.textContent).toContain("Policy service is unavailable.");
    expect(container.textContent).toContain("Administrator approval is required");
    expect(container.textContent).toContain("Loading review state.");
    expect(container.textContent).toContain("Waiting for administrator approval.");
    expect(buttons.every(button => button.disabled)).toBe(true);

    buttons.forEach(button => button.click());

    expect(onApprove).not.toHaveBeenCalled();
    expect(onDeny).not.toHaveBeenCalled();
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

function clickButton(container: HTMLElement, text: string) {
  const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent?.includes(text));
  if (!button) {
    throw new Error(`Button not found: ${text}`);
  }

  button.click();
}
