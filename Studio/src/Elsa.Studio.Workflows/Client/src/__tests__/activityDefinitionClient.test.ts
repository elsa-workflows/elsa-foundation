import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getActivityDefinition, getActivityDefinitionDraftSummary, getActivityDefinitionVersionSummary, listActivityDefinitionDrafts, listActivityDefinitions, listActivityDefinitionVersions } from "../api/activityDesign";
import { clearApiCapabilityCache } from "../api/capabilities";

afterEach(clearApiCapabilityCache);

describe("Activity Definition management client", () => {
  it("uses only advertised bounded management relations", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities();
      if (url === "/design/activities/drafts/draft%2F1") return { draftId: "draft/1", definitionId: "definition/1", revision: 2, sourceVersionId: null, status: "Active", provider: { providerKey: "visual-graph", schemaVersion: "1" }, updatedAt: "2026-07-17T10:00:00Z", presentationLabel: null };
      if (url === "/design/activities/versions/version%2F1") return { definition: { definitionId: "definition/1", recommendedVersionId: "version/1" }, versionId: "version/1", version: "1.0.0", lifecycle: "Active", provider: { providerKey: "visual-graph", schemaVersion: "1" }, publishedAt: "2026-07-17T10:00:00Z" };
      return page();
    });
    const context = { baseUrl: "test://activity-definition-client", http: { getJson } } as unknown as StudioEndpointContext;

    await listActivityDefinitions(context, { limit: 25, cursor: "cursor/2", search: "invoice", authority: "Design", providerKey: "visual-graph" });
    await getActivityDefinition(context, "definition/1");
    await listActivityDefinitionDrafts(context, { definitionId: "definition/1", limit: 10, cursor: "draft-cursor" });
    await listActivityDefinitionVersions(context, { definitionId: "definition/1", limit: 10, cursor: "version-cursor" });
    await getActivityDefinitionDraftSummary(context, "definition/1", "draft/1");
    await getActivityDefinitionVersionSummary(context, "definition/1", "version/1");

    expect(getJson.mock.calls.map(([url]) => url)).toEqual([
      "/capabilities",
      "/design/activities/definitions?limit=25&sort=identity-asc&cursor=cursor%2F2&search=invoice&authority=Design&providerKey=visual-graph",
      "/design/activities/definitions/definition%2F1",
      "/design/activities/definitions/definition%2F1/drafts?limit=10&sort=identity-asc&cursor=draft-cursor",
      "/design/activities/definitions/definition%2F1/versions?limit=10&sort=identity-asc&cursor=version-cursor",
      "/design/activities/drafts/draft%2F1",
      "/design/activities/versions/version%2F1"
    ]);
  });

  it("fails before a domain request when the collection relation is absent", async () => {
    const getJson = vi.fn(async () => ({ capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [] }] }));
    const context = { baseUrl: "test://activity-definition-client-missing", http: { getJson } } as unknown as StudioEndpointContext;

    await expect(listActivityDefinitions(context, { limit: 25 })).rejects.toMatchObject({ name: "ApiCapabilityUnavailableError", relation: "activity-definitions" });
    expect(getJson).toHaveBeenCalledTimes(1);
  });
});

function capabilities() {
  return { capabilities: [{ id: "elsa.api.activity-design", contractVersion: "1", links: [
    { rel: "activity-definitions", href: "design/activities/definitions" },
    { rel: "activity-definition", href: "design/activities/definitions/{definitionId}", templated: true },
    { rel: "activity-definition-drafts", href: "design/activities/definitions/{definitionId}/drafts", templated: true },
    { rel: "activity-definition-draft", href: "design/activities/drafts/{draftId}", templated: true },
    { rel: "activity-definition-versions", href: "design/activities/definitions/{definitionId}/versions", templated: true },
    { rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }
  ] }] };
}

function page() { return { items: [], count: 0, totalCount: 0, hasMore: false, continuation: null, snapshot: { snapshotId: "snapshot-1", asOf: "2026-07-17T10:00:00Z" } }; }
