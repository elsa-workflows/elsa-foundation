import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { WorkflowDefinitionSummary } from "../workflowTypes";
import { LatestDefinitionVersion, PublishedDefinitionVersion } from "../workflow-editor/DefinitionVersionCells";

const definition: WorkflowDefinitionSummary = {
  id: "quote", name: "Customer quote", createdAt: "", lastModifiedAt: "", versionCount: 3,
  latestVersion: "3.0.0", latestVersionId: "v3", publicationState: "published",
  publishedVersion: "2.0.0", publishedVersionId: "v2"
};

function cells(overrides: Partial<WorkflowDefinitionSummary> = {}) {
  const value = { ...definition, ...overrides };
  return {
    latest: renderToStaticMarkup(<LatestDefinitionVersion definition={value} />),
    published: renderToStaticMarkup(<PublishedDefinitionVersion definition={value} />)
  };
}

describe("definition version cells", () => {
  it("distinguishes latest saved from an older active publication", () => {
    const result = cells();
    expect(result.latest).toContain("3.0.0");
    expect(result.latest).not.toContain("2.0.0");
    expect(result.published).toContain("2.0.0");
    expect(result.published).not.toContain("3.0.0");
  });

  it("shows the actual draft source without inventing a new version", () => {
    const result = cells({ draftId: "draft", draftSourceVersion: "1.0.0" });
    expect(result.latest).toMatch(/<details(?:\s[^>]*)?>/);
    expect(result.latest).not.toMatch(/<details\b[^>]*\bopen(?:\s|=|>)/);
    expect(result.latest).toContain(">Draft<");
    expect(result.latest).toContain("Draft based on version 1.0.0.");
    expect(result.latest).toContain("3.0.0");
    expect(result.latest).toContain("Latest saved version is 3.0.0");
    expect(result.latest).not.toContain("4.0.0");
    expect(result.published).toContain("2.0.0");
    expect(result.published).not.toContain("1.0.0");
  });

  it("does not repeat the draft source when it is also the latest saved version", () => {
    const result = cells({ draftId: "draft", draftSourceVersion: "3.0.0" });
    expect(result.latest).toContain("Draft based on saved version 3.0.0.");
    expect(result.latest).not.toContain("Latest saved");
  });

  it("shows a first unversioned draft as not published", () => {
    const result = cells({ draftId: "draft", draftSourceVersion: "0.9.0", latestVersion: null, publicationState: "draft", publishedVersion: null });
    expect(result.latest).toContain(">Draft<");
    expect(result.latest).toContain("Draft based on saved version 0.9.0.");
    expect(result.latest).not.toContain("wf-definition-version-number");
    expect(result.latest).not.toContain("1.0.0");
    expect(result.published).toContain("Not published");
  });

  it.each(["retracted", "retired"] as const)("does not label a %s release as published", state => {
    const result = cells({ publicationState: state });
    expect(result.published.toLowerCase()).toContain(state);
    expect(result.published).not.toContain("2.0.0");
  });

  it.each([undefined, null, "published"] as const)("keeps missing publication evidence unavailable (%s)", state => {
    expect(cells({ publicationState: state, publishedVersion: null }).published).toContain("Unavailable");
  });
});
