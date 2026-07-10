import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { findPublishedEquivalent } from "../workflow-editor/editorHelpers";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";
import type { WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";

const definitionId = "definition-1";

function executable(overrides: Partial<WorkflowExecutableSummary> = {}): WorkflowExecutableSummary {
  return {
    artifactId: "artifact-1",
    artifactVersion: "2.0.0",
    artifactHash: "hash-1",
    definitionId,
    definitionVersionId: "version-2",
    createdAt: "2026-07-01T00:00:00Z",
    publishedAt: "2026-07-01T00:00:00Z",
    rootActivityType: "Elsa.Activities.Sequence.Activities.Sequence",
    rootActivityVersion: "1.0.0",
    nodeCount: 3,
    resumeTargetCount: 0,
    ...overrides
  };
}

function testRun(overrides: Partial<WorkflowTestRunView> = {}): WorkflowTestRunView {
  return {
    testRunId: "test-run-1",
    definitionId,
    definitionVersionId: "draft",
    artifactId: "artifact-1",
    workflowExecutionId: "execution-1",
    status: "Running",
    ...overrides
  };
}

describe("findPublishedEquivalent", () => {
  it("matches a published executable of the definition with the test run's artifact id", () => {
    const match = executable();
    const others = [executable({ artifactId: "artifact-0", definitionVersionId: "version-1" })];

    expect(findPublishedEquivalent("artifact-1", [...others, match], definitionId)).toBe(match);
  });

  it("matches through sourceId when the definition id lives on the reference source", () => {
    const match = executable({ definitionId: "other", sourceId: definitionId });

    expect(findPublishedEquivalent("artifact-1", [match], definitionId)).toBe(match);
  });

  it("ignores executables of other definitions", () => {
    expect(findPublishedEquivalent("artifact-1", [executable({ definitionId: "other" })], definitionId)).toBeNull();
  });

  it("ignores rows without a publish timestamp (e.g. the test run's own reference)", () => {
    expect(findPublishedEquivalent("artifact-1", [executable({ publishedAt: null })], definitionId)).toBeNull();
  });

  it("resolves to no signal when the test run carries no artifact id", () => {
    expect(findPublishedEquivalent(null, [executable()], definitionId)).toBeNull();
    expect(findPublishedEquivalent(undefined, [executable()], definitionId)).toBeNull();
  });
});

describe("WorkflowRuntimePanel equivalence caption", () => {
  let active: { root: Root; container: HTMLElement } | null = null;

  afterEach(() => {
    if (active) {
      flushSync(() => active!.root.unmount());
      active.container.remove();
      active = null;
    }
  });

  function render(ui: React.ReactElement) {
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(ui));
    active = { root, container };
    return container;
  }

  it("surfaces the behavioral-identity message when the test run resolves to a published artifact", () => {
    const container = render(
      <WorkflowRuntimePanel testRun={testRun()} publishedEquivalent={executable()} onOpenRun={() => {}} />
    );

    expect(container.querySelector(".wf-runtime-equivalence")?.textContent)
      .toContain("Current draft is behaviorally identical to published v2.0.0");
  });

  it("shows no equivalence message without a published match", () => {
    const container = render(
      <WorkflowRuntimePanel testRun={testRun()} publishedEquivalent={null} onOpenRun={() => {}} />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });

  it("suppresses the caption on a rejected test run even when the artifact id matches", () => {
    const container = render(
      <WorkflowRuntimePanel
        testRun={testRun({ status: "Rejected", reason: "Reference gate closed" })}
        publishedEquivalent={executable()}
        onOpenRun={() => {}}
      />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });

  it("drops a stale signal computed for an earlier test run", () => {
    const container = render(
      <WorkflowRuntimePanel
        testRun={testRun({ artifactId: "artifact-2" })}
        publishedEquivalent={executable({ artifactId: "artifact-1" })}
        onOpenRun={() => {}}
      />
    );

    expect(container.querySelector(".wf-runtime-equivalence")).toBeNull();
  });
});
