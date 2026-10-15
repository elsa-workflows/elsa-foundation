import { describe, expect, it } from "vitest";
import { resolveDefinitionPublicationStatus } from "../workflow-editor/WorkflowDefinitions";

describe("definition publication status", () => {
  it("treats a never-published working copy as Draft, not Published", () => {
    expect(resolveDefinitionPublicationStatus({
      draftId: "draft-1",
      latestVersionId: null,
      latestVersion: null,
      versionCount: 0
    })).toBe("draft");
  });

  it("does not treat draftId alone as the list status — a published workflow still has a draft", () => {
    expect(resolveDefinitionPublicationStatus({
      draftId: "draft-1",
      latestVersionId: "version-1",
      latestVersion: "1.0.0",
      versionCount: 1
    })).toBe("published-with-draft");
  });

  it("shows Published when a version exists and the draft has been discarded", () => {
    expect(resolveDefinitionPublicationStatus({
      draftId: null,
      latestVersionId: "version-1",
      latestVersion: "1.0.0",
      versionCount: 1
    })).toBe("published");
  });

  it("prefers Deleted over publication when the definition is soft-deleted", () => {
    expect(resolveDefinitionPublicationStatus({
      draftId: "draft-1",
      latestVersionId: "version-1",
      latestVersion: "1.0.0",
      versionCount: 1,
      deletedAt: "2026-10-14T10:00:00Z"
    })).toBe("deleted");
  });
});
