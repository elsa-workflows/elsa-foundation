import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import { AgentSessionIndicator, summarizeAgentSessions, type AgentSessionIndicatorSession } from "../AgentSessionIndicator";

describe("AgentSessionIndicator", () => {
  it("rolls child prompts, approvals, artifacts, and states into the parent indicator", () => {
    const sessions: AgentSessionIndicatorSession[] = [{
      id: "agt_parent",
      title: "Build extension",
      status: "background",
      pendingProposals: 1,
      pendingArtifacts: 2,
      childSessions: [{
        id: "agt_child_01",
        title: "Repair workflow",
        status: "waiting",
        pendingPrompts: 1,
        pendingProposals: 1
      }, {
        id: "agt_child_02",
        title: "Check diagnostics",
        status: "blocked"
      }]
    }];

    expect(summarizeAgentSessions(sessions)).toEqual({
      total: 3,
      activeOrBackground: 1,
      waiting: 1,
      blocked: 1,
      failed: 0,
      pendingProposals: 2,
      pendingArtifacts: 2,
      pendingPrompts: 1,
      childSessions: 2
    });
  });

  it("renders visible pending state chips", () => {
    const { container, unmount } = render(
      <AgentSessionIndicator sessions={[{
        id: "agt_parent",
        title: "Build extension",
        status: "background",
        pendingProposals: 1,
        pendingArtifacts: 1,
        childSessions: [{ id: "agt_child", title: "Ask", status: "waiting", pendingPrompts: 1 }]
      }]} />
    );

    expect(container.textContent).toContain("1 waiting");
    expect(container.textContent).toContain("1 proposal");
    expect(container.textContent).toContain("1 artifact");
    expect(container.textContent).toContain("1 prompt");
    expect(container.textContent).toContain("1 child");
    expect(container.querySelector(".agent-session-indicator")?.getAttribute("aria-label")).toContain("pending proposals");

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
