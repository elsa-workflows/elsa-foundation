import { useMemo } from "react";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowDefinitions } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions";
import type { WorkflowDefinitionSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type DefinitionFixtureOverrides = Pick<WorkflowDefinitionSummary, "id" | "name" | "description">
  & Partial<Omit<WorkflowDefinitionSummary, "id" | "name" | "description">>;

function definition(overrides: DefinitionFixtureOverrides): WorkflowDefinitionSummary {
  return {
    createdAt: "2026-07-19T00:00:00Z",
    lastModifiedAt: "2026-07-19T00:00:00Z",
    draftId: null,
    versionCount: 1,
    ...overrides
  };
}

const definitions = [
  definition({
    id: "definition-draft-base-current",
    name: "Published policy workflow with a long title to exercise the definition table grid and action column alignment",
    description: "Published version 2.0.0 with a draft based on saved version 3.0.0.",
    draftId: "draft-policy",
    draftSourceVersionId: "version-3",
    draftSourceVersion: "3.0.0",
    latestVersionId: "version-3",
    latestVersion: "3.0.0",
    publishedVersionId: "version-2",
    publishedVersion: "2.0.0",
    publicationState: "published" as const,
    versionCount: 3
  }),
  definition({
    id: "definition-published-no-draft",
    name: "Published workflow without a draft",
    description: "Latest saved version 3.0.0, published version 2.0.0.",
    draftId: null,
    latestVersionId: "version-3",
    latestVersion: "3.0.0",
    publishedVersionId: "version-2",
    publishedVersion: "2.0.0",
    publicationState: "published" as const,
    versionCount: 3
  }),
  definition({
    id: "definition-draft-only",
    name: "Draft only workflow",
    description: "A new workflow with no saved or published version.",
    draftId: "draft-new-workflow",
    draftSourceVersionId: null,
    draftSourceVersion: null,
    latestVersionId: null,
    latestVersion: null,
    publishedVersionId: null,
    publishedVersion: null,
    publicationState: "draft" as const,
    versionCount: 0
  }),
  definition({
    id: "definition-retracted",
    name: "Retracted workflow with a newer saved version",
    description: "Publication was retracted after version 4.0.0; version 5.0.0 remains saved.",
    draftId: null,
    latestVersionId: "version-5",
    latestVersion: "5.0.0",
    publishedVersionId: "version-4",
    publishedVersion: "4.0.0",
    publicationState: "retracted" as const,
    versionCount: 5
  }),
  definition({
    id: "definition-design-only",
    name: "Design only workflow",
    description: "The host does not provide publication lifecycle data.",
    draftId: null,
    latestVersionId: "version-1",
    latestVersion: "1.0.0",
    versionCount: 1
  }),
  definition({
    id: "definition-retired-deleted",
    name: "Retired workflow in the deleted list",
    description: "This row has a different set of row actions.",
    deletedAt: "2026-07-26T00:00:00Z",
    draftId: null,
    latestVersionId: "version-6",
    latestVersion: "6.0.0",
    publishedVersionId: "version-4",
    publishedVersion: "4.0.0",
    publicationState: "retired" as const,
    versionCount: 6
  })
];

export function DefinitionVersionsFixture() {
  const context = useMemo(() => ({
    baseUrl: "definition-versions-browser",
    http: {
      getJson: async (url: string) => {
        if (url === "/capabilities") return { capabilities: [{
          id: "elsa.api.workflow-design",
          contractVersion: "1",
          links: [{ rel: "workflow-definitions-page", href: "browser/definition-pages" }]
        }] };
        if (url.startsWith("/browser/definition-pages")) {
          const state = new URL(url, window.location.origin).searchParams.get("state");
          return {
            items: state === "deleted"
              ? definitions.filter(definition => definition.deletedAt)
              : state === "all"
                ? definitions
                : definitions.filter(definition => !definition.deletedAt),
            nextContinuationToken: null
          };
        }
        throw new Error(`Unexpected definition-versions fixture request: ${url}`);
      }
    }
  }) as unknown as StudioEndpointContext, []);
  const ai = useMemo(() => ({
    promptActions: { list: () => [] },
    dispatchPrompt: () => undefined
  }) as unknown as StudioAiContributionApi, []);

  return <main className="wf-editor browser-fixture">
    <h1>Workflow definitions</h1>
    <WorkflowDefinitions context={context} ai={ai} onOpen={() => undefined} />
  </main>;
}
