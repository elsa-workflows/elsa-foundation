import { describe, expect, it } from "vitest";
import type { WorkflowExecutableReference } from "../workflowTypes";
import { findPublishedRunReference } from "../workflow-editor/useExecutableWorkflowRun";

describe("published executable run source selection", () => {
  it("falls back to source-reference ID order when publication dates are invalid", () => {
    const references = [reference("ref-z"), reference("ref-a")];

    expect(findPublishedRunReference(references)?.sourceReferenceId).toBe("ref-a");
  });
});

function reference(sourceReferenceId: string): WorkflowExecutableReference {
  return {
    sourceReferenceId,
    artifactId: "artifact-1",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    artifactVersion: "1.0.0",
    createdAt: "not-a-date",
    publishedAt: "also-not-a-date",
    scope: "Published"
  };
}
